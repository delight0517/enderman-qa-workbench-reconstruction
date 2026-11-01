using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

internal sealed class VerifiedClientDisplayLease : IAsyncDisposable
{
	private readonly VdmLeaseV2PipeClient _transport;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private VdmLeaseV2Protocol.LeaseProof _proof;

	private WindowsDisplayPathMapper.ActiveOutput _activeOutput;

	private int _released;

	internal string DevicePath => _proof.DevicePath;

	internal WindowsDisplayPathMapper.ActiveOutput ActiveOutput => _activeOutput;

	internal string ProofId => _proof.LeaseId;

	internal Guid RunId => _proof.RunId;

	internal Guid ClientSessionId => _proof.ClientSessionId;

	internal string InstanceId => _proof.InstanceId;

	internal DateTimeOffset ExpiresAtUtc => _proof.ExpiresAtUtc;

	internal bool IsEligible
	{
		get
		{
			if (Volatile.Read(ref _released) == 0 && DevicePath.StartsWith("\\\\?\\DISPLAY#", StringComparison.OrdinalIgnoreCase) && ExpiresAtUtc > DateTimeOffset.UtcNow)
			{
				return VdmLeaseV2Protocol.RequiredScope.Length == _proof.Scope.Count;
			}
			return false;
		}
	}

	private VerifiedClientDisplayLease(VdmLeaseV2PipeClient transport, VdmLeaseV2Protocol.LeaseProof proof, WindowsDisplayPathMapper.ActiveOutput activeOutput)
	{
		_transport = transport;
		_proof = proof;
		_activeOutput = activeOutput;
	}

	internal static async Task<VerifiedClientDisplayLease> AcquireAsync(Guid runId, Guid clientSessionId, string instanceId, CancellationToken cancellationToken = default(CancellationToken))
	{
		VdmLeaseV2PipeClient transport = new VdmLeaseV2PipeClient();
		VdmLeaseV2Protocol.Request request = VdmLeaseV2Protocol.CreateAcquire(runId, Environment.ProcessId, clientSessionId, instanceId);
		byte[] array;
		try
		{
			array = await transport.SendOnceAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (VdmLeaseTransportException ex) when (ex.Action == "acquire" && ex.RequestMayHaveReachedProvider)
		{
			throw new InvalidOperationException("VDM lease acquisition outcome is unknown. Do not retry or launch a client; wait at least 121 seconds from the original request for expiry.", ex);
		}
		object obj = VdmLeaseV2Protocol.ParseReply(array, request, DateTimeOffset.UtcNow);
		if (obj is VdmLeaseV2Protocol.Denial denial)
		{
			throw new InvalidOperationException("VDM refused the display lease (" + denial.ErrorCode + "); client launch remains disabled.");
		}
		if (!(obj is VdmLeaseV2Protocol.LeaseProof leaseProof))
		{
			throw new InvalidDataException("VDM did not return an acquired lease proof.");
		}
		WindowsDisplayPathMapper.ActiveOutput activeOutput = WindowsDisplayPathMapper.ResolveUniqueLeasedPath(leaseProof.DevicePath, WindowsDisplayPathMapper.EnumerateActiveOutputs());
		return new VerifiedClientDisplayLease(transport, leaseProof, activeOutput);
	}

	internal async Task ValidateForSideEffectAsync(string requiredScope, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!VdmLeaseV2Protocol.RequiredScope.Contains<string>(requiredScope, StringComparer.Ordinal))
		{
			throw new ArgumentOutOfRangeException("requiredScope");
		}
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _released) != 0, this);
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			if (_proof.ExpiresAtUtc <= utcNow)
			{
				throw new InvalidOperationException("VDM lease has expired; do not launch, interact, or capture.");
			}
			if (!_proof.Scope.Contains<string>(requiredScope, StringComparer.Ordinal))
			{
				throw new InvalidOperationException("VDM lease does not authorize '" + requiredScope + "'.");
			}
			string action = ((_proof.ExpiresAtUtc - utcNow <= TimeSpan.FromSeconds(60.0)) ? "renew" : "validate");
			VdmLeaseV2Protocol.Request request = VdmLeaseV2Protocol.CreateLeaseAction(action, _proof.RunId, _proof.WorkbenchPid, _proof.ClientSessionId, _proof.InstanceId, _proof.LeaseId);
			object obj = VdmLeaseV2Protocol.ParseReply(await _transport.SendOnceAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), request, DateTimeOffset.UtcNow, _proof.RevocationGeneration);
			if (obj is VdmLeaseV2Protocol.Denial denial)
			{
				throw new InvalidOperationException("VDM lease validation failed (" + denial.ErrorCode + "); side effect is blocked.");
			}
			if (!(obj is VdmLeaseV2Protocol.LeaseProof leaseProof) || leaseProof.LeaseId != _proof.LeaseId || leaseProof.DevicePath != _proof.DevicePath || leaseProof.TopologyGeneration != _proof.TopologyGeneration || leaseProof.RunId != _proof.RunId || leaseProof.ClientSessionId != _proof.ClientSessionId || leaseProof.WorkbenchPid != _proof.WorkbenchPid || leaseProof.InstanceId != _proof.InstanceId)
			{
				throw new InvalidDataException("VDM lease identity or output changed during revalidation.");
			}
			WindowsDisplayPathMapper.ActiveOutput activeOutput = WindowsDisplayPathMapper.ResolveUniqueLeasedPath(leaseProof.DevicePath, WindowsDisplayPathMapper.EnumerateActiveOutputs());
			if (!activeOutput.DevicePath.Equals(_activeOutput.DevicePath, StringComparison.OrdinalIgnoreCase) || !activeOutput.GdiDeviceName.Equals(_activeOutput.GdiDeviceName, StringComparison.OrdinalIgnoreCase) || activeOutput.Left != _activeOutput.Left || activeOutput.Top != _activeOutput.Top || activeOutput.Right != _activeOutput.Right || activeOutput.Bottom != _activeOutput.Bottom)
			{
				throw new InvalidDataException("The VDM lease still exists but its active Windows output mapping changed.");
			}
			_proof = leaseProof;
		}
		finally
		{
			_gate.Release();
		}
	}

	internal async Task ReleaseAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				VdmLeaseV2Protocol.Request request = VdmLeaseV2Protocol.CreateLeaseAction("release", _proof.RunId, _proof.WorkbenchPid, _proof.ClientSessionId, _proof.InstanceId, _proof.LeaseId);
				VdmLeaseV2Protocol.Denial denial = VdmLeaseV2Protocol.ParseReply(await _transport.SendOnceAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), request, DateTimeOffset.UtcNow, _proof.RevocationGeneration) as VdmLeaseV2Protocol.Denial;
				bool flag = (object)denial != null;
				if (flag)
				{
					string errorCode = denial.ErrorCode;
					bool flag2 = ((errorCode == "RELEASED" || errorCode == "LEASE_EXPIRED") ? true : false);
					flag = flag2;
				}
				if (!flag)
				{
					throw new InvalidDataException("VDM did not confirm release of the exact lease.");
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(ReleaseAsync(CancellationToken.None));
	}
}
