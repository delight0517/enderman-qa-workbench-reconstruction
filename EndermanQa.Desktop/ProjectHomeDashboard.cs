using System;
using System.Windows;
using System.Windows.Controls;
using EndermanQa.Desktop.Modules;
using EndermanQa.Desktop.Persistence;
using EndermanQa.Desktop.Results;

namespace EndermanQa.Desktop;

/// <summary>Owns the home-page QA overview presentation. It reads the active results view and never writes QA data.</summary>
internal sealed class ProjectHomeDashboard : IModuleV1
{
    private readonly SuiteResultsView _results;
    private readonly Action _openResults;
    private readonly Action _exportPortfolio;
    private readonly Action _openFolders;
    private readonly Action _editProject;
    private readonly StackPanel _projectPage;
    private readonly string _sourceHash;
    private readonly TextBlock _projectName;
    private readonly TextBlock _projectTarget;
    private readonly TextBlock _projectIntroduction;
    private readonly TextBlock _progress;
    private readonly TextBlock _followUp;
    private readonly TextBlock _pass;
    private readonly TextBlock _attempted;
    private readonly TextBlock _passDetail;
    private readonly TextBlock _followUpDetail;
    private string? _followUpResourceKey;
    private string? _restoredProjectName;
    private Grid? _layout;
    private bool _initialized;
    private bool _disposed;

    public string ModuleId => "qa.project-brief";
    public string Version => "1.0.0";
    public string SourceHash => _sourceHash;

    public ProjectHomeDashboard(StackPanel projectPage, SuiteResultsView results, Action openResults, Action exportPortfolio, Action openFolders, Action editProject, string sourceHash)
    {
        _projectPage = projectPage ?? throw new ArgumentNullException(nameof(projectPage));
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _openResults = openResults ?? throw new ArgumentNullException(nameof(openResults));
        _exportPortfolio = exportPortfolio ?? throw new ArgumentNullException(nameof(exportPortfolio));
        _openFolders = openFolders ?? throw new ArgumentNullException(nameof(openFolders));
        _editProject = editProject ?? throw new ArgumentNullException(nameof(editProject));
        _sourceHash = sourceHash ?? throw new ArgumentNullException(nameof(sourceHash));
        _projectName = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold };
        _projectTarget = Label("대상 정보가 없습니다.", "Theme.TextSecondary", 12, FontWeights.Medium);
        _projectIntroduction = Label("프로젝트 소개를 추가하면 QA 케이스보다 먼저 목적과 범위를 확인할 수 있습니다.", "Theme.TextSecondary", 13, FontWeights.Normal);
        _progress = MetricValue();
        _followUp = MetricValue();
        _pass = MetricValue();
        _attempted = MetricValue();
        _passDetail = MetricDetail();
        _followUpDetail = MetricDetail();
    }

    public object CaptureState()
    {
        var activeProjectId = _projectName.Text;
        return new ProjectBriefModuleStateV1(activeProjectId);
    }

    public void Initialize(object? state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;

        _projectPage.MaxWidth = 1180;
        _projectPage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _restoredProjectName = (state as ProjectBriefModuleStateV1)?.ActiveProjectName;

        _layout = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var moduleLabel = Label("프로젝트 브리프", "Theme.TextAccent", 12, FontWeights.SemiBold);
        moduleLabel.ToolTip = $"{ModuleId} · API 1 · v{Version} · SHA-256 {SourceHash} · 코드 교체는 후보 빌드와 앱 재시작이 필요합니다.";
        _layout.Children.Add(BuildOverviewCard(moduleLabel));
        var workflow = BuildWorkflowCard(_openFolders, _openResults, _exportPortfolio, _editProject);
        Grid.SetColumn(workflow, 1);
        _layout.Children.Add(workflow);

        _projectPage.Children.Insert(Math.Min(2, _projectPage.Children.Count), _layout);
        _results.SummaryUpdated += Results_SummaryUpdated;
        _initialized = true;
        Update(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _results.SummaryUpdated -= Results_SummaryUpdated;
        if (_layout is not null) _projectPage.Children.Remove(_layout);
        _layout = null;
        _initialized = false;
    }

    public void Update(QaProjectDefinition? activeProject)
    {
        if (!_initialized) return;
        var projectName = activeProject?.Name ?? _restoredProjectName;
        _projectName.Text = string.IsNullOrWhiteSpace(projectName) ? "프로젝트를 선택하세요" : projectName;
        _projectTarget.Text = string.IsNullOrWhiteSpace(activeProject?.GameTarget) ? "대상 정보가 없습니다." : $"QA 대상 · {activeProject.GameTarget}";
        _projectIntroduction.Text = string.IsNullOrWhiteSpace(activeProject?.Description)
            ? "프로젝트 소개를 추가하면 QA 케이스보다 먼저 목적과 범위를 확인할 수 있습니다."
            : activeProject.Description.Trim();

        var counts = _results.CurrentSummaryCounts;
        _progress.Text = counts.Total == 0 ? "—" : $"{counts.Attempted} / {counts.Total}";
        _attempted.Text = $"{counts.Attempted}건";
        _pass.Text = $"{counts.FullPass + counts.Partial}건";
        _passDetail.Text = $"완전 {counts.FullPass} · 부분 {counts.Partial}";
        _followUp.Text = (counts.Inconclusive + counts.NotRun).ToString("N0");
        _followUpDetail.Text = $"미확정 {counts.Inconclusive} · 미실행 {counts.NotRun}";
        string followUpResourceKey = counts.Inconclusive + counts.NotRun > 0 ? "Theme.Warning" : "Theme.Positive";
        if (!string.Equals(_followUpResourceKey, followUpResourceKey, StringComparison.Ordinal))
        {
            _followUp.SetResourceReference(TextBlock.ForegroundProperty, followUpResourceKey);
            _followUpResourceKey = followUpResourceKey;
        }
    }

    private void Results_SummaryUpdated(object? sender, EventArgs e) => Update(null);

    private Border BuildOverviewCard(TextBlock moduleLabel)
    {
        var content = new StackPanel();
        content.Children.Add(moduleLabel);
        content.Children.Add(_projectName);
        content.Children.Add(_projectTarget);
        content.Children.Add(_projectIntroduction);

        content.Children.Add(Label("QA 진행 방향", "Theme.TextStrong", 14, FontWeights.SemiBold, new Thickness(0, 15, 0, 5)));
        content.Children.Add(Label("대상·범위 확인  →  100개 케이스 검토  →  판정과 증거 확인  →  포트폴리오 내보내기", "Theme.TextSecondary", 12, FontWeights.Normal));
        content.Children.Add(Label("프로젝트 설명과 대상은 프로젝트 설정에서 수정할 수 있습니다.", "Theme.TextSecondary", 11, FontWeights.Normal, new Thickness(0, 4, 0, 12)));

        var metrics = new WrapPanel { Orientation = Orientation.Horizontal };
        metrics.Children.Add(Metric("진행", _progress, "Theme.AccentMuted"));
        metrics.Children.Add(Metric("실행", _attempted, "Theme.SurfaceAlt"));
        metrics.Children.Add(Metric("Pass 판정", _pass, _passDetail, "Theme.SurfaceAlt"));
        metrics.Children.Add(Metric("추가 확인", _followUp, _followUpDetail, "Theme.AccentMuted"));
        content.Children.Add(metrics);

        var open = new Button
        {
            Content = "QA 결과 열기",
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(0, 13, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        open.SetResourceReference(Control.BackgroundProperty, "Theme.Accent");
        open.SetResourceReference(Control.ForegroundProperty, "Theme.Surface");
        open.Click += (_, _) => _openResults();
        content.Children.Add(open);
        return Card(content, new Thickness(0, 0, 8, 0));
    }

    private static Border BuildWorkflowCard(Action openFolders, Action openResults, Action exportPortfolio, Action editProject)
    {
        var content = new StackPanel();
        content.Children.Add(Label("프로젝트 작업", "Theme.TextStrong", 16, FontWeights.SemiBold));
        content.Children.Add(Label("원하는 위치로 바로 이동하세요.", "Theme.TextSecondary", 12, FontWeights.Normal, new Thickness(0, 3, 0, 11)));
        content.Children.Add(QuickAction("프로젝트 폴더 열기", "프로젝트 파일과 산출물 찾아보기", openFolders, primary: false));
        content.Children.Add(QuickAction("프로젝트 소개·대상 수정", "프로젝트 소개와 QA 대상 편집", editProject, primary: false));
        content.Children.Add(QuickAction("QA 결과 열기", "남은 케이스와 증거 확인", openResults, primary: false));
        content.Children.Add(QuickAction("QA 포트폴리오 내보내기", "README 제출 안내를 반영해 묶기", exportPortfolio, primary: true));
        return Card(content, new Thickness(8, 0, 0, 0));
    }

    private static Button QuickAction(string title, string detail, Action action, bool primary)
    {
        var button = new Button { Content = title, ToolTip = detail, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
        button.SetResourceReference(Control.BackgroundProperty, primary ? "Theme.Accent" : "Theme.SurfaceAlt");
        button.SetResourceReference(Control.ForegroundProperty, primary ? "Theme.Surface" : "Theme.TextStrong");
        button.Click += (_, _) => action();
        return button;
    }

    private static Border Metric(string label, TextBlock value, TextBlock? detail, string background)
    {
        var stack = new StackPanel();
        stack.Children.Add(Label(label, "Theme.TextSecondary", 11, FontWeights.Medium));
        stack.Children.Add(value);
        if (detail is not null) stack.Children.Add(detail);
        var card = new Border
        {
            Child = stack,
            MinWidth = 112,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(7)
        };
        card.SetResourceReference(Border.BackgroundProperty, background);
        return card;
    }

    private static Border Metric(string label, TextBlock value, string background) => Metric(label, value, null, background);

    private static Border Card(UIElement content, Thickness margin)
    {
        var card = new Border
        {
            Child = content,
            Padding = new Thickness(18),
            Margin = margin,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Theme.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        return card;
    }

    private static TextBlock Label(string text, string color, double size, FontWeight weight, Thickness? margin = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Margin = margin ?? new Thickness(0), TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, color);
        return label;
    }

    private static TextBlock MetricValue()
    {
        var value = new TextBlock { FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) };
        value.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextStrong");
        return value;
    }

    private static TextBlock MetricDetail()
    {
        var detail = new TextBlock { FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.NoWrap };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondary");
        return detail;
    }

}

internal sealed record ProjectBriefModuleStateV1(string ActiveProjectName);
