using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using EndermanQa.Desktop.Integrations;
using EndermanQa.Desktop.Evidence;
using EndermanQa.Desktop.Results;

namespace EndermanQa.Desktop;

/// <summary>Composes the project workspace shell around existing feature tabs without owning their state.</summary>
internal sealed class ProjectWorkspaceShell
{
    private readonly TabControl _stages;
    private readonly Grid _root;
    private readonly Grid _chatColumn;
    private readonly Border _chatBody;
    private readonly TextBlock _chatTitle;
    private readonly TabControl _rightTabs;
    private readonly TabItem _chatTab;
    private readonly TabItem _evidenceTab;
    private readonly ListBox _evidenceFiles;
    private readonly EvidencePreviewControl _evidencePreview;
    private readonly string _evidenceRoot;
    private readonly string _dataRoot;
    private readonly Button _chatToggle;
    private readonly TextBlock _projectName;
    private readonly TreeView _navigation;
    private bool _chatVisible = true;
    private bool _updatingEvidenceItems;

    private ProjectWorkspaceShell(TabControl stages, Grid root, Grid chatColumn, Border chatBody, TextBlock chatTitle, TabControl rightTabs, TabItem chatTab, TabItem evidenceTab, ListBox evidenceFiles, EvidencePreviewControl evidencePreview, string evidenceRoot, Button chatToggle, TextBlock projectName, TreeView navigation)
    {
        _stages = stages;
        _root = root;
        _chatColumn = chatColumn;
        _chatBody = chatBody;
        _chatTitle = chatTitle;
        _rightTabs = rightTabs;
        _chatTab = chatTab;
        _evidenceTab = evidenceTab;
        _evidenceFiles = evidenceFiles;
        _evidencePreview = evidencePreview;
        _evidenceRoot = Path.GetFullPath(evidenceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _dataRoot = Path.GetFullPath(Path.Combine(evidenceRoot, ".."));
        _chatToggle = chatToggle;
        _projectName = projectName;
        _navigation = navigation;
    }

    public static ProjectWorkspaceShell Attach(
        TabControl stages,
        OnlyChatEmbeddedView onlyChat,
        string projectName,
        string evidenceRoot,
        Action openProject,
        Action openProjectFolders)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(onlyChat);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentNullException.ThrowIfNull(openProject);
        ArgumentNullException.ThrowIfNull(openProjectFolders);
        if (stages.Parent is not Grid parent)
            throw new InvalidOperationException("Project workspace must be hosted in the main stage grid.");

        var hostIndex = parent.Children.IndexOf(stages);
        var row = Grid.GetRow(stages);
        var column = Grid.GetColumn(stages);
        var rowSpan = Grid.GetRowSpan(stages);
        var columnSpan = Grid.GetColumnSpan(stages);
        var onlyChatTab = stages.Items.OfType<TabItem>().FirstOrDefault(item => item.Tag?.ToString() == "only-chat");
        if (onlyChatTab is not null)
        {
            onlyChatTab.Content = null;
            stages.Items.Remove(onlyChatTab);
        }

        var root = new Grid { Margin = new Thickness(16, 8, 16, 8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(206) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310), MinWidth = 270 });

        var projectNameText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(projectName) ? "기본 작업 공간" : projectName,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10)
        };
        projectNameText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextStrong");

        var projectActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        projectActions.Children.Add(ActionButton("프로젝트 변경", openProject, primary: true));
        projectActions.Children.Add(ActionButton("폴더 탐색", openProjectFolders, primary: false));

        var navigation = BuildNavigation(stages);
        var sidebar = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new StackPanel
                    {
                        Children =
                        {
                            Caption("현재 프로젝트", "Theme.TextAccent"),
                            projectNameText,
                            projectActions,
                            new Separator { Margin = new Thickness(0, 0, 0, 10) }
                        }
                    },
                    navigation
                }
            }
        };
        ((DockPanel)sidebar.Child).SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        sidebar.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        DockPanel.SetDock(((DockPanel)sidebar.Child).Children[0], Dock.Top);

        var tabTemplate = new ControlTemplate(typeof(TabControl));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        tabTemplate.VisualTree = content;
        stages.Template = tabTemplate;
        stages.BorderThickness = new Thickness(0);
        stages.Padding = new Thickness(0);
        stages.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        stages.VerticalContentAlignment = VerticalAlignment.Stretch;
        stages.ClipToBounds = true;
        foreach (var stage in stages.Items.OfType<TabItem>())
        {
            stage.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            stage.VerticalContentAlignment = VerticalAlignment.Stretch;
            if (stage.Content is FrameworkElement featureView)
            {
                featureView.HorizontalAlignment = HorizontalAlignment.Stretch;
                featureView.VerticalAlignment = VerticalAlignment.Stretch;
                featureView.MinWidth = 0;
            }
        }
        stages.SetResourceReference(Control.BackgroundProperty, "Theme.Canvas");
        Grid.SetColumn(stages, 1);
        parent.Children.Remove(stages);
        root.Children.Add(sidebar);
        root.Children.Add(stages);

        var chatHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var chatTitle = new TextBlock { Text = "Only Chat", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        chatTitle.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextStrong");
        chatHeader.Children.Add(chatTitle);
        var chatToggle = ActionButton("접기", () => { }, primary: false);
        chatToggle.Margin = new Thickness(8, 0, 0, 0);
        chatToggle.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chatToggle, Dock.Right);
        chatHeader.Children.Add(chatToggle);

        var evidenceFiles = new ListBox
        {
            MaxHeight = 92,
            MinHeight = 42,
            Margin = new Thickness(6),
            BorderThickness = new Thickness(1),
            DisplayMemberPath = nameof(EvidenceChoice.DisplayName),
            ToolTip = "선택한 QA 케이스의 첨부 파일"
        };
        evidenceFiles.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        evidenceFiles.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        evidenceFiles.SetResourceReference(Control.BorderBrushProperty, "Theme.Border");
        var evidencePreview = new EvidencePreviewControl { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var evidenceLayout = new Grid { MinWidth = 0 };
        evidenceLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        evidenceLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        evidenceLayout.Children.Add(evidenceFiles);
        Grid.SetRow(evidencePreview, 1);
        evidenceLayout.Children.Add(evidencePreview);
        var chatTab = new TabItem { Header = "Only Chat", Content = onlyChat, MinWidth = 92 };
        var evidenceTab = new TabItem { Header = "첨부 증거", Content = evidenceLayout, MinWidth = 86 };
        var rightTabs = new TabControl { Items = { chatTab, evidenceTab }, SelectedItem = chatTab, MinWidth = 0, Padding = new Thickness(2) };
        rightTabs.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        rightTabs.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        var chatBody = new Border
        {
            Child = rightTabs,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        chatBody.SetResourceReference(Border.BackgroundProperty, "Theme.Surface");
        chatBody.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        chatBody.BorderThickness = new Thickness(1);

        var chatColumn = new Grid();
        chatColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chatColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        chatColumn.Children.Add(chatHeader);
        Grid.SetRow(chatBody, 1);
        chatColumn.Children.Add(chatBody);
        Grid.SetColumn(chatColumn, 2);
        root.Children.Add(chatColumn);

        var shell = new ProjectWorkspaceShell(stages, root, chatColumn, chatBody, chatTitle, rightTabs, chatTab, evidenceTab, evidenceFiles, evidencePreview, evidenceRoot, chatToggle, projectNameText, navigation);
        chatToggle.Click += (_, _) => shell.ToggleChat();
        rightTabs.SelectionChanged += (_, _) =>
        {
            if (rightTabs.SelectedItem is TabItem active)
                chatTitle.Text = active.Header?.ToString() ?? "작업 패널";
        };
        evidenceFiles.SelectionChanged += (_, _) =>
        {
            if (!shell._updatingEvidenceItems) _ = shell.LoadSelectedEvidenceAsync();
        };
        stages.SelectionChanged += (_, _) =>
        {
            shell.SyncNavigation(stages);
            shell.ConstrainSelectedContent();
        };
        stages.SizeChanged += (_, _) => shell.ConstrainSelectedContent();
        navigation.SelectedItemChanged += (_, e) =>
        {
            if (e.NewValue is TreeViewItem { Tag: string stageId })
                MainStageNavigation.Select(stages, stageId);
        };
        shell.SyncNavigation(stages);

        parent.Children.Remove(stages);
        parent.Children.Insert(Math.Min(hostIndex, parent.Children.Count), root);
        Grid.SetRow(root, row);
        Grid.SetColumn(root, column);
        Grid.SetRowSpan(root, rowSpan);
        Grid.SetColumnSpan(root, columnSpan);
        return shell;
    }

    public void UpdateProjectName(string? name)
    {
        _projectName.Text = string.IsNullOrWhiteSpace(name) ? "기본 작업 공간" : name;
    }

    public void ShowOnlyChat()
    {
        if (!_chatVisible) ToggleChat();
        _rightTabs.SelectedItem = _chatTab;
    }

    public async Task ShowEvidenceAsync(IReadOnlyList<SuiteEvidenceAttachment> attachments)
    {
        var choices = new List<EvidenceChoice>();
        foreach (var item in attachments)
        {
            if (!IsAllowedEvidence(item.Path)) continue;
            choices.Add(new EvidenceChoice(item.Path, item.ExpectedSha256, string.IsNullOrWhiteSpace(item.DisplayName) ? Path.GetFileName(item.Path) : item.DisplayName));
        }
        _updatingEvidenceItems = true;
        _evidenceFiles.ItemsSource = choices;
        _evidenceTab.Header = choices.Count > 0 ? $"첨부 증거 · {choices.Count}" : "첨부 증거";
        if (choices.Count == 0)
        {
            _updatingEvidenceItems = false;
            _evidencePreview.ShowUnavailable("선택한 케이스에 첨부된 이미지나 영상이 없습니다.");
            if (ReferenceEquals(_rightTabs.SelectedItem, _evidenceTab)) _rightTabs.SelectedItem = _chatTab;
            return;
        }
        if (!_chatVisible) ToggleChat();
        _rightTabs.SelectedItem = _evidenceTab;
        _evidenceFiles.SelectedIndex = 0;
        _updatingEvidenceItems = false;
        await LoadSelectedEvidenceAsync();
    }

    private async Task LoadSelectedEvidenceAsync()
    {
        if (_evidenceFiles.SelectedItem is not EvidenceChoice choice) return;
        try
        {
            if (!IsAllowedEvidence(choice.Path))
            {
                _evidencePreview.ShowUnavailable("프로젝트 증거 폴더 밖의 파일이거나 링크된 파일이라 표시하지 않았습니다.");
                return;
            }
            var hash = choice.ExpectedSha256;
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            {
                await using var stream = new FileStream(choice.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, CancellationToken.None));
            }
            var extension = Path.GetExtension(choice.Path).ToLowerInvariant();
            var mediaType = extension switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
                ".mp4" => "video/mp4", ".wmv" => "video/x-ms-wmv", ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime", ".mkv" => "video/x-matroska", ".webm" => "video/webm", _ => "application/octet-stream"
            };
            await _evidencePreview.ShowEvidenceAsync(choice.Path, mediaType, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _evidencePreview.ShowUnavailable("첨부 파일을 열지 못했습니다. " + ex.Message);
        }
    }

    private bool IsAllowedEvidence(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(_evidenceRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return false;
            if (Path.GetExtension(full).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".mp4" or ".wmv" or ".avi" or ".mov" or ".mkv" or ".webm")) return false;
            var current = _dataRoot;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            foreach (var part in Path.GetRelativePath(current, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private sealed record EvidenceChoice(string Path, string ExpectedSha256, string DisplayName);

    private void ToggleChat()
    {
        _chatVisible = !_chatVisible;
        _chatColumn.Visibility = Visibility.Visible;
        _chatBody.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
        _chatTitle.Visibility = _chatVisible ? Visibility.Visible : Visibility.Collapsed;
        _chatToggle.Content = _chatVisible ? "접기" : "열기";
        _chatToggle.ToolTip = _chatVisible ? "Only Chat 접기" : "Only Chat 다시 열기";
        _root.ColumnDefinitions[2].Width = _chatVisible ? new GridLength(310) : new GridLength(56);
        _chatColumn.RowDefinitions[1].Height = _chatVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _chatColumn.HorizontalAlignment = HorizontalAlignment.Stretch;
        _chatToggle.HorizontalAlignment = _chatVisible ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        _chatToggle.Margin = _chatVisible ? new Thickness(8, 0, 0, 0) : new Thickness(0);
    }

    private void SyncNavigation(TabControl stages)
    {
        var selectedId = (stages.SelectedItem as TabItem)?.Tag?.ToString();
        var selected = _navigation.Items.OfType<TreeViewItem>()
            .SelectMany(group => group.Items.OfType<TreeViewItem>())
            .FirstOrDefault(item => item.Tag?.ToString() == selectedId);
        if (selected is not null && !ReferenceEquals(_navigation.SelectedItem, selected))
            selected.IsSelected = true;
    }

    private void ConstrainSelectedContent()
    {
        if (_stages.ActualWidth <= 0 || _stages.SelectedContent is not FrameworkElement selected)
            return;
        var pending = new Stack<DependencyObject>();
        var visited = new HashSet<DependencyObject>();
        pending.Push(selected);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (current is FrameworkElement element)
            {
                element.MinWidth = 0;
                element.MaxWidth = _stages.ActualWidth;
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(current))
                if (child is DependencyObject logicalChild) pending.Push(logicalChild);
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                    pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    private static TreeView BuildNavigation(TabControl stages)
    {
        var tree = new TreeView { BorderThickness = new Thickness(0), Padding = new Thickness(0), MinWidth = 170 };
        tree.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        tree.ItemContainerStyle = new Style(typeof(TreeViewItem));
        tree.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 6, 7)));
        tree.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        tree.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        tree.ItemContainerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextPrimary")));

        AddGroup("프로젝트", ("프로젝트 개요", "project-game"), ("QA 결과", "qa-results"), ("프로젝트 폴더", "project-files"));
        AddGroup("작업", ("게임 · 매크로", "game-macros"), ("AI 작업", "ai-work"), ("Jira 제출", "jira-submit"));
        AddGroup("도구", ("도구 제작", "tool-design"), ("기타 도구", "tools"));
        return tree;

        void AddGroup(string title, params (string Label, string Id)[] items)
        {
            var group = new TreeViewItem { Header = title, IsExpanded = true, Focusable = false };
            group.SetResourceReference(Control.ForegroundProperty, "Theme.TextSecondary");
            foreach (var (label, id) in items)
            {
                var stage = stages.Items.OfType<TabItem>().FirstOrDefault(item => item.Tag?.ToString() == id);
                if (stage is null) continue;
                var item = new TreeViewItem
                {
                    Header = label,
                    Tag = id,
                    ToolTip = stage.ToolTip,
                };
                AutomationProperties.SetName(item, label);
                item.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
                group.Items.Add(item);
            }
            tree.Items.Add(group);
        }
    }

    private static Button ActionButton(string label, Action action, bool primary)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(Control.BackgroundProperty, primary ? "Theme.Accent" : "Theme.SurfaceAlt");
        button.SetResourceReference(Control.ForegroundProperty, primary ? "Theme.Surface" : "Theme.TextPrimary");
        button.SetResourceReference(Control.BorderBrushProperty, "Theme.Border");
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock Caption(string text, string resource)
    {
        var block = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        block.SetResourceReference(TextBlock.ForegroundProperty, resource);
        return block;
    }
}
