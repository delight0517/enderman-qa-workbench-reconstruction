using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Shapes;
using EndermanQa.Desktop;

namespace EndermanQa.Desktop.Results;

internal static class SuiteResultsPresentation
{
    public static void ApplyExportActionButton(Button button, bool primary)
    {
        ArgumentNullException.ThrowIfNull(button);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(primary ? "Theme.Accent" : "Theme.Surface")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Brushes.White : new DynamicResourceExtension("Theme.TextStrong")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(primary ? "Theme.Accent" : "Theme.Border")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateRoundedButtonTemplate()));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(primary ? "Theme.TextAccent" : "Theme.AccentMuted")));
        style.Triggers.Add(hover);
        button.Style = style;
    }

    public static void Apply(SuiteResultsView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.SetResourceReference(Control.BackgroundProperty, "Theme.Canvas");
        view.SummaryText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondary");
        view.SummaryText.FontSize = 13;
        view.SummaryText.Margin = new Thickness(0, 5, 0, 0);

        var quietButton = new Style(typeof(Button));
        quietButton.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Surface")));
        quietButton.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextStrong")));
        quietButton.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Border")));
        quietButton.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        quietButton.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
        quietButton.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        quietButton.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        quietButton.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        quietButton.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0)));
        quietButton.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        quietButton.Setters.Add(new Setter(Control.TemplateProperty, CreateRoundedButtonTemplate()));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMuted")));
        quietButton.Triggers.Add(hover);

        foreach (var button in new[] { view.OpenWorkbookButton, view.ExportSuiteButton, view.OpenSuiteFolderButton })
            button.Style = quietButton;
        view.OpenWorkbookButton.Content = "기존 Excel 열기";
        view.ExportSuiteButton.Content = "QA 포트폴리오 내보내기";
        view.OpenSuiteFolderButton.Content = "결과 폴더 열기";

        var primaryButton = new Style(typeof(Button), quietButton);
        primaryButton.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Accent")));
        primaryButton.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        primaryButton.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent")));
        var primaryHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        primaryHover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.TextAccent")));
        primaryButton.Triggers.Add(primaryHover);
        view.RefreshButton.Style = primaryButton;

        view.SearchBox.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        view.SearchBox.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        view.SearchBox.SetResourceReference(Control.BorderBrushProperty, "Theme.Border");
        view.SearchBox.BorderThickness = new Thickness(1);
        view.SearchBox.Padding = new Thickness(10, 6, 10, 6);
        view.SearchBox.ToolTip = "ID, 케이스 이름, 상태, 판정 또는 관측 내용으로 검색";
        AutomationProperties.SetName(view.SearchBox, "QA 케이스 검색");
        SharedControlPresentation.ApplyComboBox(view.StatusFilter);

        view.CasesGrid.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        view.CasesGrid.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        view.CasesGrid.BorderThickness = new Thickness(0);
        view.CasesGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        view.CasesGrid.RowHeight = 36;
        view.CasesGrid.AlternationCount = 2;
        view.CasesGrid.SizeChanged += (_, _) => ApplyCaseColumnLayout(view.CasesGrid);
        view.CasesGrid.Loaded += (_, _) => ApplyCaseColumnLayout(view.CasesGrid);
        ApplyCaseColumnLayout(view.CasesGrid);

        var header = new Style(typeof(DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.SurfaceInset")));
        header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextSecondary")));
        header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        header.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Border")));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        view.CasesGrid.ColumnHeaderStyle = header;

        var row = new Style(typeof(DataGridRow));
        row.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Surface")));
        row.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextPrimary")));
        row.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Border")));
        row.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        var alternate = new Trigger { Property = ItemsControl.AlternationIndexProperty, Value = 1 };
        alternate.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.SurfaceAlt")));
        row.Triggers.Add(alternate);
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMutedAlt")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextStrong")));
        row.Triggers.Add(selected);
        view.CasesGrid.RowStyle = row;

        if (view.CasesGrid.Columns.Count > 2 && view.CasesGrid.Columns[2] is DataGridTextColumn statusColumn)
        {
            var statusText = new Style(typeof(TextBlock));
            statusText.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            statusText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            var passStatus = new DataTrigger { Binding = new Binding("Status"), Value = "executed-pass" };
            passStatus.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.Positive")));
            statusText.Triggers.Add(passStatus);
            var partialStatus = new DataTrigger { Binding = new Binding("Status"), Value = "executed-partial" };
            partialStatus.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.Warning")));
            statusText.Triggers.Add(partialStatus);
            var inconclusiveStatus = new DataTrigger { Binding = new Binding("Status"), Value = "anomaly-unconfirmed" };
            inconclusiveStatus.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextAccent")));
            statusText.Triggers.Add(inconclusiveStatus);
            statusColumn.ElementStyle = statusText;
        }
        if (view.CasesGrid.Columns.Count > 3 && view.CasesGrid.Columns[3] is DataGridTextColumn caseTitleColumn)
        {
            var titleText = new Style(typeof(TextBlock));
            titleText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            titleText.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
            titleText.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Title")));
            caseTitleColumn.ElementStyle = titleText;
        }
        var cell = new Style(typeof(DataGridCell));
        cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
        cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cell.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMutedAlt")));
        selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextStrong")));
        cell.Triggers.Add(selectedCell);
        var selectedRowCells = new DataTrigger
        {
            Binding = new Binding(nameof(DataGridRow.IsSelected))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
            },
            Value = true
        };
        selectedRowCells.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMutedAlt")));
        selectedRowCells.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextStrong")));
        cell.Triggers.Add(selectedRowCells);
        view.CasesGrid.CellStyle = cell;
    }

    internal static void ApplyCaseColumnLayout(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.Columns.Count < 4) return;
        var width = grid.ActualWidth;
        if (width > 0 && width < 430)
        {
            grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            grid.Columns[0].Width = new DataGridLength(52);
            grid.Columns[0].MinWidth = 52;
            grid.Columns[1].Width = new DataGridLength(58);
            grid.Columns[1].MinWidth = 58;
            grid.Columns[2].Width = new DataGridLength(92);
            grid.Columns[2].MinWidth = 92;
            grid.Columns[3].MinWidth = 60;
            grid.Columns[3].Width = new DataGridLength(Math.Max(60, width - 52 - 58 - 92 - SystemParameters.VerticalScrollBarWidth - 8));
            return;
        }
        grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        grid.Columns[0].Width = new DataGridLength(58);
        grid.Columns[0].MinWidth = 58;
        grid.Columns[1].Width = new DataGridLength(82);
        grid.Columns[1].MinWidth = 78;
        grid.Columns[2].Width = new DataGridLength(148);
        grid.Columns[2].MinWidth = 132;
        grid.Columns[3].MinWidth = 150;
        grid.Columns[3].Width = grid.ActualWidth > 0
            ? new DataGridLength(Math.Max(grid.Columns[3].MinWidth, grid.ActualWidth - 58 - 82 - 148 - SystemParameters.VerticalScrollBarWidth))
            : new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    private static ControlTemplate CreateRoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        chrome.SetBinding(Border.BackgroundProperty, TemplateProperty("Background"));
        chrome.SetBinding(Border.BorderBrushProperty, TemplateProperty("BorderBrush"));
        chrome.SetBinding(Border.BorderThicknessProperty, TemplateProperty("BorderThickness"));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, TemplateProperty("Content"));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateProperty("ContentTemplate"));
        content.SetBinding(ContentPresenter.ContentStringFormatProperty, TemplateProperty("ContentStringFormat"));
        content.SetBinding(FrameworkElement.MarginProperty, TemplateProperty("Padding"));
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, TemplateProperty("HorizontalContentAlignment"));
        content.SetBinding(FrameworkElement.VerticalAlignmentProperty, TemplateProperty("VerticalContentAlignment"));
        content.SetBinding(TextBlock.ForegroundProperty, TemplateProperty("Foreground"));
        chrome.AppendChild(content);
        template.VisualTree = chrome;

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "Chrome"));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.48, "Chrome"));
        template.Triggers.Add(disabled);
        return template;
    }

    private static Binding TemplateProperty(string path) => new(path)
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
    };

}
