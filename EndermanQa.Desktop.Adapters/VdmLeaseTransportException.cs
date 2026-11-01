using System;
using System.IO;

namespace EndermanQa.Desktop.Adapters;

internal sealed class VdmLeaseTransportException : IOException
{
	internal string Action { get; }

	internal bool RequestMayHaveReachedProvider { get; }

	public VdmLeaseTransportException(string action, bool requestMayHaveReachedProvider, Exception innerException) : base($"VDM lease {action} exchange failed{(requestMayHaveReachedProvider ? "; provider outcome is unknown" : " before request delivery")}.", innerException)
	{
		Action = action;
		RequestMayHaveReachedProvider = requestMayHaveReachedProvider;
	}
}
