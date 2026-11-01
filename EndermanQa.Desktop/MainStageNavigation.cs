using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace EndermanQa.Desktop;

internal static class MainStageNavigation
{
    private static readonly string[] StageIds =
    {
        "project-game", "game-macros", "ai-work", "project-files",
        "jira-submit", "tool-design", "only-chat", "tools"
    };

    public static bool Configure(TabControl mainStages, FrameworkElement suiteResultsContent)
    {
        ArgumentNullException.ThrowIfNull(mainStages);
        ArgumentNullException.ThrowIfNull(suiteResultsContent);

        var stageIndex = 0;
        foreach (var stage in mainStages.Items.OfType<TabItem>())
        {
            if (Equals(stage.Tag, "qa-results"))
            {
                continue;
            }
            if (stageIndex >= StageIds.Length)
            {
                break;
            }
            stage.Tag = StageIds[stageIndex++];
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project-game"] = "프로젝트 개요",
            ["game-macros"] = "게임 · 매크로",
            ["ai-work"] = "AI 작업",
            ["project-files"] = "프로젝트 파일",
            ["jira-submit"] = "Jira 제출",
            ["tool-design"] = "도구 제작",
            ["only-chat"] = "Only Chat",
            ["tools"] = "도구"
        };
        foreach (var stage in mainStages.Items.OfType<TabItem>())
        {
            if (stage.Tag is string id && labels.TryGetValue(id, out var label))
            {
                stage.Header = label;
                stage.ToolTip = label;
            }
        }
        DependencyObject? current = suiteResultsContent;
        while (current is not null && current is not TabItem)
        {
            current = LogicalTreeHelper.GetParent(current)
                ?? (current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : null)
                ?? (current as FrameworkElement)?.Parent;
        }

        if (current is not TabItem resultsTab)
        {
            return false;
        }

        if (ReferenceEquals(resultsTab.Parent, mainStages))
        {
            ApplyResultsIdentity(resultsTab);
            ApplyStagePresentation(mainStages);
            return true;
        }

        if (resultsTab.Parent is not TabControl sourceTabs)
        {
            return false;
        }

        sourceTabs.Items.Remove(resultsTab);
        ApplyResultsIdentity(resultsTab);
        resultsTab.ToolTip = "100개 테스트 케이스의 최신 판정, 실행 상태와 증거를 확인합니다.";
        mainStages.Items.Insert(Math.Min(1, mainStages.Items.Count), resultsTab);
        ApplyStagePresentation(mainStages);
        return true;
    }

    private static void ApplyStagePresentation(TabControl mainStages)
    {
        mainStages.SetResourceReference(Control.BackgroundProperty, "Theme.Canvas");
        mainStages.BorderThickness = new Thickness(0);
        mainStages.Padding = new Thickness(0);

        var stageStyle = new Style(typeof(TabItem));
        stageStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Surface")));
        stageStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextSecondary")));
        stageStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Border")));
        stageStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        stageStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 8, 13, 8)));
        stageStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
        stageStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 40d));
        stageStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        stageStyle.Setters.Add(new Setter(Control.TemplateProperty, CreateStageItemTemplate()));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMuted")));
        stageStyle.Triggers.Add(hover);

        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Surface")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextStrong")));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent")));
        selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 3)));
        stageStyle.Triggers.Add(selected);

        foreach (var stage in mainStages.Items.OfType<TabItem>())
            stage.Style = stageStyle;
    }

    private static ControlTemplate CreateStageItemTemplate()
    {
        var template = new ControlTemplate(typeof(TabItem));
        var chrome = new FrameworkElementFactory(typeof(Border), "StageChrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chrome.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        var header = new FrameworkElementFactory(typeof(ContentPresenter), "StageHeader");
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        header.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        header.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        chrome.AppendChild(header);
        template.VisualTree = chrome;

        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent"), "StageChrome"));
        template.Triggers.Add(focused);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.48, "StageChrome"));
        template.Triggers.Add(disabled);
        return template;
    }

    public static bool Select(TabControl mainStages, string stageId)
    {
        ArgumentNullException.ThrowIfNull(mainStages);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        var stage = mainStages.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), stageId, StringComparison.Ordinal));
        if (stage is null)
        {
            return false;
        }
        mainStages.SelectedItem = stage;
        return true;
    }

    private static void ApplyResultsIdentity(TabItem resultsTab)
    {
        resultsTab.Header = "QA 결과";
        resultsTab.Tag = "qa-results";
    }
}

