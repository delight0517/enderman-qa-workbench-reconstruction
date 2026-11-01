using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace EndermanQa.Desktop.Results;

internal static class SuiteResultsLayout
{
    public static SuiteResultsLayoutSession Apply(SuiteResultsView view, SuiteResultsPresentationProfileV1 profile, Button? excelExportButton = null, Button? jiraExportButton = null, bool includeLegacyExports = true, string? preferredCompactPane = null, double detailsVerticalOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();

        var controls = Controls(view, excelExportButton, jiraExportButton);
        foreach (var control in controls)
            EnsureSafelyDetachable(control);
        foreach (var control in controls)
            Detach(control);

        var root = new Grid { Margin = new Thickness(profile.PageMarginPx), MinHeight = 0, ClipToBounds = true };
        root.SetResourceReference(Panel.BackgroundProperty, "Theme.Canvas");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = BuildHeader(view, profile);
        root.Children.Add(header.Element);

        var body = new Grid { Margin = new Thickness(0, profile.BodyGapPx, 0, 0), MinHeight = 0, ClipToBounds = true };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 265 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 285 });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var caseList = BuildCaseList(view, includeLegacyExports ? excelExportButton : null, includeLegacyExports ? jiraExportButton : null);
        body.Children.Add(caseList);
        var details = BuildDetails(view, out var detailsScroll);
        Grid.SetColumn(details, 1);
        body.Children.Add(details);

        var compactLayout = false;
        var compactTabs = new TabControl { MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), TabStripPlacement = Dock.Top };
        compactTabs.SetResourceReference(Control.BackgroundProperty, "Theme.Canvas");
        compactTabs.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        compactTabs.ItemContainerStyle = BuildCompactTabStyle();
        var listTab = new TabItem { Header = "케이스 목록", MinWidth = 90 };
        var detailTab = new TabItem { Header = "선택 결과", MinWidth = 90 };
        compactTabs.Items.Add(listTab);
        compactTabs.Items.Add(detailTab);
        var activeCompactPane = string.Equals(preferredCompactPane, "details", StringComparison.OrdinalIgnoreCase)
            ? "details"
            : string.Equals(preferredCompactPane, "list", StringComparison.OrdinalIgnoreCase)
                ? "list"
                : view.SelectedCase is null ? "list" : "details";
        void ApplyResponsiveBody(double width)
        {
            var stageWidth = width > 0 ? width : AvailableStageWidth(view, width);
            // The workspace shell reserves fixed space for project navigation and the
            // right-side chat/evidence panel. Keep the results list and details in
            // separate tabs until the remaining stage is genuinely wide enough for both.
            var shouldStack = stageWidth > 0 && stageWidth < profile.CompactTabsThresholdPx;
            root.Margin = shouldStack ? new Thickness(Math.Max(4, profile.PageMarginPx - 6)) : new Thickness(profile.PageMarginPx);
            body.Margin = shouldStack ? new Thickness(0, Math.Max(4, profile.BodyGapPx - 6), 0, 0) : new Thickness(0, profile.BodyGapPx, 0, 0);
            var listTitle = caseList.Tag as TextBlock;
            if (listTitle is not null)
                listTitle.Visibility = shouldStack ? Visibility.Collapsed : Visibility.Visible;
            view.CasesGrid.RowHeight = shouldStack ? profile.CompactRowHeightPx : profile.RegularRowHeightPx;
            if (shouldStack == compactLayout) return;
            compactLayout = shouldStack;
            body.ColumnDefinitions.Clear();
            body.RowDefinitions.Clear();
            if (shouldStack)
            {
                body.Children.Remove(caseList);
                body.Children.Remove(details);
                listTab.Content = caseList;
                detailTab.Content = details;
                compactTabs.SelectedItem = activeCompactPane == "details" ? detailTab : listTab;
                body.Children.Add(compactTabs);
                caseList.Margin = new Thickness(0);
                details.Margin = new Thickness(0);
            }
            else
            {
                if (ReferenceEquals(compactTabs.SelectedItem, detailTab)) activeCompactPane = "details";
                else if (ReferenceEquals(compactTabs.SelectedItem, listTab)) activeCompactPane = "list";
                body.Children.Remove(compactTabs);
                listTab.Content = null;
                detailTab.Content = null;
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 265 });
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 285 });
                if (!body.Children.Contains(caseList)) body.Children.Add(caseList);
                if (!body.Children.Contains(details)) body.Children.Add(details);
                Grid.SetRow(caseList, 0);
                Grid.SetColumn(caseList, 0);
                Grid.SetRow(details, 0);
                Grid.SetColumn(details, 1);
                caseList.Margin = new Thickness(0, 0, 7, 0);
                details.Margin = new Thickness(7, 0, 0, 0);
            }
        }
        SizeChangedEventHandler bodySizeChanged = (_, e) => ApplyResponsiveBody(e.NewSize.Width);
        RoutedEventHandler bodyLoaded = (_, _) => ApplyResponsiveBody(body.ActualWidth);
        body.SizeChanged += bodySizeChanged;
        body.Loaded += bodyLoaded;

        view.NoticeText.Margin = new Thickness(4, 8, 4, 0);
        view.NoticeText.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(view.NoticeText, 2);
        root.Children.Add(view.NoticeText);
        view.Content = root;
        ApplyResponsiveBody(body.ActualWidth);
        if (view.SelectedCase is { } selectedCase && view.CasesGrid.Items.Contains(selectedCase))
        {
            view.CasesGrid.SelectedItem = selectedCase;
            view.CasesGrid.ScrollIntoView(selectedCase);
        }
        SuiteResultsPresentation.ApplyCaseColumnLayout(view.CasesGrid);
        detailsScroll.ScrollToVerticalOffset(Math.Max(0, detailsVerticalOffset));
        return new SuiteResultsLayoutSession(
            compactTabs,
            listTab,
            detailTab,
            detailsScroll,
            activeCompactPane,
            () =>
            {
                body.SizeChanged -= bodySizeChanged;
                body.Loaded -= bodyLoaded;
                header.Dispose();
            });
    }

    private static SuiteResultsHeaderLayout BuildHeader(SuiteResultsView view, SuiteResultsPresentationProfileV1 profile)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        var title = new TextBlock
        {
            Text = "100건 QA 실행 결과",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextStrong");
        heading.Children.Add(title);
        view.SummaryText.FontSize = 13;
        view.SummaryText.TextWrapping = TextWrapping.Wrap;
        view.SummaryText.Margin = new Thickness(0);
        heading.Children.Add(view.SummaryText);
        var moduleStatus = new TextBlock
        {
            Text = view.PresentationModule.DisplayStatus,
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        moduleStatus.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
        void UpdateModuleStatus(string _)
        {
            moduleStatus.Text = view.PresentationModule.DisplayStatus;
            moduleStatus.ToolTip = view.PresentationModule.Status + Environment.NewLine + "SHA-256 " + view.PresentationModule.PolicyHash + Environment.NewLine + view.PresentationModule.SourcePath;
        }
        view.PresentationModule.StatusChanged += UpdateModuleStatus;
        UpdateModuleStatus(string.Empty);
        heading.Children.Add(moduleStatus);
        var originalSummary = view.SummaryText.Text;
        var summaryIsCompact = false;
        void ApplySummaryDensity(double stageWidth)
        {
            var fullText = view.SummaryText.Text;
            if (stageWidth > 0 && stageWidth < profile.CompactTabsThresholdPx)
            {
                if (!summaryIsCompact)
                    originalSummary = fullText;
                var counts = view.CurrentSummaryCounts;
                view.SummaryText.Text = $"전체 {counts.Total} · 실행 {counts.Attempted} · 미실행 {counts.NotRun} · 확인 {counts.Inconclusive + counts.NotRun}{Environment.NewLine}" +
                    $"통과 {counts.FullPass} · 부분 {counts.Partial} · 실패 {counts.Failures} · 미확정 {counts.Inconclusive}";
                view.SummaryText.ToolTip = string.IsNullOrWhiteSpace(originalSummary) ? fullText : originalSummary;
                view.SummaryText.FontSize = 12;
                summaryIsCompact = true;
            }
            else
            {
                if (summaryIsCompact && !string.IsNullOrWhiteSpace(originalSummary)) view.SummaryText.Text = originalSummary;
                else originalSummary = fullText;
                view.SummaryText.ToolTip = null;
                view.SummaryText.FontSize = 13;
                summaryIsCompact = false;
            }
        }
        EventHandler summaryUpdated = (_, _) => ApplySummaryDensity(AvailableStageWidth(view, layout.ActualWidth));
        view.SummaryUpdated += summaryUpdated;
        layout.Children.Add(heading);

        var actions = new WrapPanel
        {
            Margin = new Thickness(-4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var button in new[] { view.OpenWorkbookButton, view.OpenSuiteFolderButton, view.RefreshButton })
            actions.Children.Add(button);
        Grid.SetColumn(actions, 1);
        layout.Children.Add(actions);

        var compactLayout = false;
        void ApplyResponsiveHeader(double width)
        {
            var stageWidth = AvailableStageWidth(view, width);
            ApplySummaryDensity(stageWidth);
            var shouldStack = stageWidth > 0 && stageWidth < profile.HeaderStackThresholdPx;
            if (shouldStack == compactLayout) return;
            compactLayout = shouldStack;
            layout.ColumnDefinitions.Clear();
            layout.RowDefinitions.Clear();
            if (shouldStack)
            {
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(heading, 0);
                Grid.SetColumn(heading, 0);
                Grid.SetRow(actions, 1);
                Grid.SetColumn(actions, 0);
                actions.Margin = new Thickness(0, 8, 0, 0);
                actions.HorizontalAlignment = HorizontalAlignment.Left;
                actions.Width = Math.Max(180, stageWidth - 72);
                layout.Width = Math.Max(180, stageWidth - 64);
            }
            else
            {
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(heading, 0);
                Grid.SetColumn(heading, 0);
                Grid.SetRow(actions, 0);
                Grid.SetColumn(actions, 1);
                actions.Margin = new Thickness(-4, 0, 0, 0);
                actions.HorizontalAlignment = HorizontalAlignment.Right;
                actions.Width = double.NaN;
                layout.Width = double.NaN;
            }
        }
        SizeChangedEventHandler headerSizeChanged = (_, e) => ApplyResponsiveHeader(e.NewSize.Width);
        RoutedEventHandler headerLoaded = (_, _) => ApplyResponsiveHeader(layout.ActualWidth);
        layout.SizeChanged += headerSizeChanged;
        layout.Loaded += headerLoaded;

        return new SuiteResultsHeaderLayout(
            Card(layout, new Thickness(16), new Thickness(0)),
            () =>
            {
                layout.SizeChanged -= headerSizeChanged;
                layout.Loaded -= headerLoaded;
                view.SummaryUpdated -= summaryUpdated;
                view.PresentationModule.StatusChanged -= UpdateModuleStatus;
            });
    }

    private static double AvailableStageWidth(FrameworkElement element, double fallback)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is TabControl stages && stages.ActualWidth > 0)
                return stages.ActualWidth;
            current = LogicalTreeHelper.GetParent(current)
                ?? (current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : null);
        }
        return fallback;
    }

    private static Border BuildCaseList(SuiteResultsView view, Button? excelExportButton, Button? jiraExportButton)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = SectionTitle("케이스 목록");
        title.Margin = new Thickness(2, 0, 2, 6);
        layout.Children.Add(title);

        view.SearchBox.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetRow(view.SearchBox, 1);
        layout.Children.Add(view.SearchBox);

        var filters = new Grid();
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        filters.Children.Add(view.StatusFilter);
        view.CasesGrid.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(view.CasesGrid, 1);
        filters.Children.Add(view.CasesGrid);
        Grid.SetRow(filters, 2);
        layout.Children.Add(filters);

        var exports = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0)
        };
        view.ExportSuiteButton.Margin = new Thickness(0, 0, 8, 0);
        exports.Children.Add(view.ExportSuiteButton);
        if (excelExportButton is not null)
        {
            excelExportButton.Margin = new Thickness(0, 0, 8, 0);
            exports.Children.Add(excelExportButton);
        }
        if (jiraExportButton is not null)
        {
            jiraExportButton.Margin = new Thickness(0);
            exports.Children.Add(jiraExportButton);
        }
        Grid.SetRow(exports, 3);
        layout.Children.Add(exports);

        var card = Card(layout, new Thickness(14, 6, 14, 6), new Thickness(0, 0, 7, 0));
        card.Tag = title;
        return card;
    }

    private static Border BuildDetails(SuiteResultsView view, out ScrollViewer scrollViewer)
    {
        var content = new StackPanel();
        view.DetailTitle.FontSize = 19;
        view.DetailTitle.Margin = new Thickness(0, 0, 0, 5);
        view.DetailTitle.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(view.DetailTitle);

        view.DetailStatus.Margin = new Thickness(0, 0, 0, 14);
        view.DetailStatus.Padding = new Thickness(9, 6, 9, 6);
        view.DetailStatus.TextWrapping = TextWrapping.Wrap;
        var status = new Border { CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 0, 12) };
        status.SetResourceReference(Border.BackgroundProperty, "Theme.AccentMuted");
        status.Child = view.DetailStatus;
        content.Children.Add(status);

        content.Children.Add(DetailSection("기대 행동", view.ExpectedText));
        content.Children.Add(DetailSection("실제 관측", view.ActualText));
        content.Children.Add(DetailSection("범위와 판정 근거", view.CoverageText));
        view.RepeatabilityText.Margin = new Thickness(1, -3, 1, 12);
        content.Children.Add(view.RepeatabilityText);

        var attemptsTitle = SectionTitle("재현 시도");
        attemptsTitle.Margin = new Thickness(1, 2, 1, 5);
        content.Children.Add(attemptsTitle);
        view.AttemptsGrid.Height = 112;
        view.AttemptsGrid.Margin = new Thickness(0, 0, 0, 14);
        content.Children.Add(view.AttemptsGrid);

        content.Children.Add(DetailSection("실행 원본 journal", view.JournalText));
        content.Children.Add(DetailSection("증거 파일 상태", view.EvidenceText));

        var scroll = new ScrollViewer
        {
            MinHeight = 0,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
        scrollViewer = scroll;
        return Card(scroll, new Thickness(16), new Thickness(7, 0, 0, 0));
    }

    private static Style BuildCompactTabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 7, 13, 7)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.SurfaceAlt")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextSecondary")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Border")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Surface")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextAccent")));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent")));
        selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Triggers.Add(selected);
        return style;
    }

    private static Border DetailSection(string label, FrameworkElement content)
    {
        var section = new StackPanel();
        var heading = SectionTitle(label);
        heading.Margin = new Thickness(1, 0, 1, 5);
        section.Children.Add(heading);

        if (content is TextBlock text)
        {
            text.Margin = new Thickness(0);
            text.TextWrapping = TextWrapping.Wrap;
        }
        else if (content is TextBox box)
        {
            box.Margin = new Thickness(0);
            box.MinHeight = 76;
        }
        section.Children.Add(content);
        return Card(section, new Thickness(11, 9, 11, 10), new Thickness(0, 0, 0, 10), "Theme.SurfaceAlt");
    }

    private static TextBlock SectionTitle(string text)
    {
        var title = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextAccent");
        return title;
    }

    private static Border Card(UIElement child, Thickness padding, Thickness margin, string background = "Theme.Surface")
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = padding,
            Margin = margin,
            BorderThickness = new Thickness(1),
            Child = child,
            SnapsToDevicePixels = true
        };
        card.SetResourceReference(Border.BackgroundProperty, background);
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        return card;
    }

    private static FrameworkElement[] Controls(SuiteResultsView view, Button? excelExportButton, Button? jiraExportButton)
    {
        var controls = new List<FrameworkElement>
        {
        view.SummaryText, view.OpenWorkbookButton, view.ExportSuiteButton, view.OpenSuiteFolderButton,
        view.RefreshButton, view.SearchBox, view.StatusFilter, view.CasesGrid, view.DetailTitle,
        view.DetailStatus, view.ExpectedText, view.ActualText, view.CoverageText, view.RepeatabilityText,
        view.AttemptsGrid, view.JournalText, view.EvidenceText, view.NoticeText
        };
        if (excelExportButton is not null)
            controls.Add(excelExportButton);
        if (jiraExportButton is not null)
            controls.Add(jiraExportButton);
        return controls.ToArray();
    }

    private static void Detach(FrameworkElement element)
    {
        var parent = GetParent(element);
        switch (parent)
        {
            case Panel panel when panel.Children.Contains(element):
                panel.Children.Remove(element);
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ItemsControl items when items.Items.Contains(element):
                items.Items.Remove(element);
                break;
            case null:
                break;
            default:
                throw new InvalidOperationException($"Cannot safely reparent {element.GetType().Name} from {parent.GetType().Name}.");
        }
    }

    private static void EnsureSafelyDetachable(FrameworkElement element)
    {
        var parent = GetParent(element);
        var canDetach = parent switch
        {
            Panel panel => panel.Children.Contains(element),
            ContentControl content => ReferenceEquals(content.Content, element),
            Decorator decorator => ReferenceEquals(decorator.Child, element),
            ItemsControl items => items.Items.Contains(element),
            null => true,
            _ => false
        };
        if (!canDetach)
        {
            var parentName = parent?.GetType().Name ?? "unknown parent";
            throw new InvalidOperationException($"Cannot safely reparent {element.GetType().Name} from {parentName}; no controls were detached.");
        }
    }

    private static DependencyObject? GetParent(FrameworkElement element) =>
        LogicalTreeHelper.GetParent(element)
        ?? element.Parent
        ?? VisualTreeHelper.GetParent(element);
}
