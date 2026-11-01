using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EndermanQa.Desktop.Results;

/// <summary>Versioned lifecycle boundary for the compiled QA-results presentation.</summary>
internal interface IWpfPresentationModuleV1<TState> : IDisposable
{
    string ModuleId { get; }
    int ApiVersion { get; }
    string Version { get; }
    string PolicyHash { get; }
    bool SupportsHotReload { get; }
    string ApplyBoundary { get; }
    bool CanReplace(out string reason);
    TState CaptureState();
}

internal sealed record SuiteResultsLayoutModuleState(
    SuiteResultsViewState ViewState,
    string ActivePane,
    double DetailsVerticalOffset,
    int SchemaVersion = 1);

internal sealed class SuiteResultsHeaderLayout : IDisposable
{
    private readonly Action _detachHandlers;
    private bool _disposed;

    public Border Element { get; }

    public SuiteResultsHeaderLayout(Border element, Action detachHandlers)
    {
        Element = element;
        _detachHandlers = detachHandlers;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _detachHandlers();
        _disposed = true;
    }
}

internal sealed class SuiteResultsLayoutModuleV1 : IWpfPresentationModuleV1<SuiteResultsLayoutModuleState>
{
    private readonly SuiteResultsView _view;
    private SuiteResultsLayoutSession? _session;
    private Button? _excelButton;
    private Button? _jiraButton;
    private bool _includeLegacyExports = true;
    private bool _disposed;
    private SuiteResultsPresentationProfileV1 _profile = SuiteResultsPresentationProfileV1.Default;
    private string _profileHash = SuiteResultsPresentationProfileV1.DefaultHash;
    private string _lastReloadStatus = "기본 프로필 적용됨";
    private string? _profilePath;
    private FileSystemWatcher? _profileWatcher;
    private DispatcherTimer? _profileDebounce;
    private long _watchGeneration;

    public string ModuleId => "qa.results.presentation";
    public int ApiVersion => 1;
    public string Version => _profile.Version;
    public string PolicyHash => _profileHash;
    public SuiteResultsPresentationProfileV1 Profile => _profile;
    public bool SupportsHotReload => true;
    public string ApplyBoundary => "Versioned JSON presentation profile reloads live; changes to compiled layout structure require a candidate build and controlled restart.";
    public string Status => $"{ModuleId} v{Version} · {(_profileWatcher is null ? "감시 꺼짐" : "실시간 적용 감시 중")} · {_lastReloadStatus}";
    public string DisplayStatus => $"표시 모듈 v{Version} · {(_profileWatcher is null ? "감시 꺼짐" : "실시간 적용 감시 중")}";
    public string SourcePath => _profilePath ?? string.Empty;
    public event Action<string>? StatusChanged;

    public SuiteResultsLayoutModuleV1(SuiteResultsView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool CanReplace(out string reason)
    {
        ThrowIfDisposed();
        if (_view.HasPendingPresentationWork)
        {
            reason = "결과 새로고침·내보내기·상세 조회가 끝난 뒤 화면 모듈을 바꿀 수 있습니다.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public SuiteResultsLayoutModuleState CaptureState()
    {
        ThrowIfDisposed();
        return new SuiteResultsLayoutModuleState(
            _view.SnapshotState(),
            _session?.CaptureActivePane() ?? "list",
            _session?.CaptureDetailsVerticalOffset() ?? 0);
    }

    public void Initialize(SuiteResultsLayoutModuleState? state = null)
    {
        ThrowIfDisposed();
        if (!CanReplace(out var reason)) throw new InvalidOperationException(reason);
        if (state is not null && state.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported results-layout state schema {state.SchemaVersion}.");

        var next = SuiteResultsLayout.Apply(
            _view,
            _profile,
            _excelButton,
            _jiraButton,
            _includeLegacyExports,
            state?.ActivePane,
            state?.DetailsVerticalOffset ?? 0);

        var previous = _session;
        _session = next;
        if (state is not null)
        {
            _view.RestoreState(state.ViewState);
            _session.Restore(state.ActivePane, state.DetailsVerticalOffset);
        }
        previous?.Dispose();
    }

    public void StartWatching(string appDataRoot)
    {
        ThrowIfDisposed();
        StopWatching();
        (SuiteResultsPresentationProfileV1 Profile, string Sha256, string Path) loaded;
        try
        {
            loaded = SuiteResultsPresentationProfileV1.LoadOrCreate(appDataRoot);
            _profilePath = loaded.Path;
            bool applied = ApplyProfile(loaded.Profile, loaded.Sha256, "초기 프로필 로드");
            bool pending = !applied && _lastReloadStatus.StartsWith("대기 중", StringComparison.Ordinal);
            if (!applied && !pending)
                throw new InvalidOperationException(_lastReloadStatus);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            _lastReloadStatus = "감시 시작 실패 · 기본 정책 유지 · " + ex.Message;
            RaiseStatusChanged();
            throw;
        }

        string directory = Path.GetDirectoryName(loaded.Path)!;
        long generation = ++_watchGeneration;
        _profileDebounce = new DispatcherTimer(DispatcherPriority.Background, _view.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _profileDebounce.Tick += (_, _) => ReloadProfile(generation);
        _profileWatcher = new FileSystemWatcher(directory, Path.GetFileName(loaded.Path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false
        };
        FileSystemEventHandler changed = (_, _) => QueueProfileReload(generation);
        RenamedEventHandler renamed = (_, _) => QueueProfileReload(generation);
        ErrorEventHandler error = (_, e) => QueueWatcherFailure(generation, e.GetException());
        _profileWatcher.Changed += changed;
        _profileWatcher.Created += changed;
        _profileWatcher.Renamed += renamed;
        _profileWatcher.Error += error;
        _profileWatcher.EnableRaisingEvents = true;
        _lastReloadStatus = _profileHash.Equals(loaded.Sha256, StringComparison.OrdinalIgnoreCase)
            ? "기본 프로필 로드됨"
            : "작업 완료 후 프로필 적용 대기 중";
        RaiseStatusChanged();
        if (!_profileHash.Equals(loaded.Sha256, StringComparison.OrdinalIgnoreCase))
            _profileDebounce.Start();
    }

    public bool ApplyProfile(SuiteResultsPresentationProfileV1 candidate, string expectedHash, string reason = "프로필 적용")
    {
        ThrowIfDisposed();
        try
        {
            candidate.Validate();
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Presentation profile hash is invalid.");
            if (_profilePath is not null)
            {
                string appDataRoot = Path.GetDirectoryName(Path.GetDirectoryName(_profilePath)!)!;
                var current = SuiteResultsPresentationProfileV1.LoadOrCreate(appDataRoot);
                if (!string.Equals(current.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase) || current.Profile != candidate)
                    throw new InvalidDataException("Presentation profile changed after it was read; stale candidate rejected.");
            }
            if (!CanReplace(out var busyReason))
            {
                _lastReloadStatus = "대기 중 · " + busyReason;
                RaiseStatusChanged();
                return false;
            }

            SuiteResultsLayoutModuleState state = CaptureState();
            SuiteResultsPresentationProfileV1 previousProfile = _profile;
            string previousHash = _profileHash;
            SuiteResultsLayoutSession? previousSession = _session;
            SuiteResultsLayoutSession? next = null;
            try
            {
                next = SuiteResultsLayout.Apply(_view, candidate, _excelButton, _jiraButton, _includeLegacyExports, state.ActivePane, state.DetailsVerticalOffset);
                _view.RestoreState(state.ViewState);
                next.Restore(state.ActivePane, state.DetailsVerticalOffset);
                _session = next;
                _profile = candidate;
                _profileHash = expectedHash.ToUpperInvariant();
                next = null;
                try
                {
                    previousSession?.Dispose();
                    _lastReloadStatus = $"{reason} 성공 · SHA-256 {_profileHash}";
                }
                catch (Exception cleanupError)
                {
                    _lastReloadStatus = $"새 프로필 적용됨 · 이전 핸들 정리 실패 · {cleanupError.Message} · SHA-256 {_profileHash}";
                }
                RaiseStatusChanged();
                return true;
            }
            catch (Exception applyError)
            {
                next?.Dispose();
                _profile = previousProfile;
                _profileHash = previousHash;
                try
                {
                    var restored = SuiteResultsLayout.Apply(_view, previousProfile, _excelButton, _jiraButton, _includeLegacyExports, state.ActivePane, state.DetailsVerticalOffset);
                    _view.RestoreState(state.ViewState);
                    restored.Restore(state.ActivePane, state.DetailsVerticalOffset);
                    _session = restored;
                    try { previousSession?.Dispose(); } catch { }
                    _lastReloadStatus = "적용 실패 · 이전 화면 복구됨 · " + applyError.Message;
                }
                catch (Exception restoreError)
                {
                    _lastReloadStatus = "적용 실패 · 이전 정책 복구도 실패함 · " + applyError.Message + " / " + restoreError.Message;
                }
                RaiseStatusChanged();
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            _lastReloadStatus = "후보 거부 · 이전 모듈 유지 · " + ex.Message;
            RaiseStatusChanged();
            return false;
        }
    }

    private void QueueProfileReload(long generation)
    {
        if (_disposed) return;
        _view.Dispatcher.BeginInvoke((Action)(() =>
        {
            if (_disposed || generation != _watchGeneration || _profileDebounce is null) return;
            _profileDebounce.Stop();
            _profileDebounce.Start();
        }));
    }

    private void QueueWatcherFailure(long generation, Exception error)
    {
        if (_disposed) return;
        _view.Dispatcher.BeginInvoke((Action)(() =>
        {
            if (_disposed || generation != _watchGeneration) return;
            _lastReloadStatus = "파일 감시 오류 · 이전 모듈 유지 · " + error.Message;
            RaiseStatusChanged();
        }));
    }

    private void ReloadProfile(long generation)
    {
        _profileDebounce?.Stop();
        if (_disposed || generation != _watchGeneration || _profilePath is null) return;
        if (_view.HasPendingPresentationWork)
        {
            _lastReloadStatus = "작업 완료 후 프로필 적용 대기 중";
            RaiseStatusChanged();
            _profileDebounce?.Start();
            return;
        }
        try
        {
            var loaded = SuiteResultsPresentationProfileV1.LoadOrCreate(Path.GetDirectoryName(Path.GetDirectoryName(_profilePath)!)!);
            if (string.Equals(loaded.Sha256, _profileHash, StringComparison.OrdinalIgnoreCase))
            {
                _lastReloadStatus = "파일 변경 감지 · 동일 해시 · 현재 모듈 유지";
                RaiseStatusChanged();
                return;
            }
            ApplyProfile(loaded.Profile, loaded.Sha256, "실시간 프로필 적용");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            _lastReloadStatus = "후보 거부 · 이전 모듈 유지 · " + ex.Message;
            RaiseStatusChanged();
        }
    }

    private void StopWatching()
    {
        ++_watchGeneration;
        if (_profileDebounce is not null)
        {
            _profileDebounce.Stop();
            _profileDebounce = null;
        }
        _profileWatcher?.Dispose();
        _profileWatcher = null;
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(Status);

    public void ConfigureExportButtons(Button? excelButton, Button? jiraButton, bool includeLegacyExports)
    {
        ThrowIfDisposed();
        if (!CanReplace(out var reason)) throw new InvalidOperationException(reason);
        var state = CaptureState();
        var oldExcel = _excelButton;
        var oldJira = _jiraButton;
        var oldLegacy = _includeLegacyExports;
        _excelButton = excelButton;
        _jiraButton = jiraButton;
        _includeLegacyExports = includeLegacyExports;
        try
        {
            Initialize(state);
        }
        catch
        {
            _excelButton = oldExcel;
            _jiraButton = oldJira;
            _includeLegacyExports = oldLegacy;
            Initialize(state);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopWatching();
        _session?.Dispose();
        _session = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SuiteResultsLayoutModuleV1));
    }
}

internal sealed class SuiteResultsLayoutSession : IDisposable
{
    private readonly TabControl _tabs;
    private readonly TabItem _listTab;
    private readonly TabItem _detailTab;
    private readonly ScrollViewer _detailsScroll;
    private readonly Action _detachHandlers;
    private string _preferredCompactPane;
    private bool _disposed;

    internal SuiteResultsLayoutSession(TabControl tabs, TabItem listTab, TabItem detailTab, ScrollViewer detailsScroll, string preferredCompactPane, Action detachHandlers)
    {
        _tabs = tabs;
        _listTab = listTab;
        _detailTab = detailTab;
        _detailsScroll = detailsScroll;
        _preferredCompactPane = preferredCompactPane;
        _detachHandlers = detachHandlers;
    }

    public string CaptureActivePane()
    {
        if (ReferenceEquals(_tabs.SelectedItem, _detailTab)) _preferredCompactPane = "details";
        else if (ReferenceEquals(_tabs.SelectedItem, _listTab)) _preferredCompactPane = "list";
        return _preferredCompactPane;
    }

    public double CaptureDetailsVerticalOffset() => _detailsScroll.VerticalOffset;

    public void Restore(string activePane, double detailsVerticalOffset)
    {
        if (_disposed) return;
        _preferredCompactPane = string.Equals(activePane, "details", StringComparison.OrdinalIgnoreCase) ? "details" : "list";
        _tabs.SelectedItem = _preferredCompactPane == "details" ? _detailTab : _listTab;
        _detailsScroll.ScrollToVerticalOffset(Math.Max(0, detailsVerticalOffset));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _detachHandlers();
        _disposed = true;
    }
}
