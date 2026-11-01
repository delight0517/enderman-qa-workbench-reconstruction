using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndermanQa.Desktop;

/// <summary>Version 1 home-card composition. Reuses original controls and event subscriptions.</summary>
internal static class ProjectGameSettingsLayout
{
    public const int ContractVersion = 1;

    public static void Apply(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var gamePicker = window.GameTargetPicker;
        var versionMemo = window.SelectedGameVersionBox;
        var stack = LogicalTreeHelper.GetParent(gamePicker) as StackPanel;
        var card = stack is null ? null : LogicalTreeHelper.GetParent(stack) as Border;
        if (stack is null || card is null || !ReferenceEquals(card.Child, stack) || stack.Children.Count != 10)
            throw new InvalidOperationException("Project/game form structure changed; no controls were detached.");

        var parts = stack.Children.Cast<UIElement>().ToArray();
        bool LabelAt(int index, string expected) => parts[index] is TextBlock label && string.Equals(label.Text, expected, StringComparison.Ordinal);
        if (!LabelAt(0, "QA 프로젝트")
            || !ReferenceEquals(parts[1], window.StageProjectName)
            || parts[2] is not Button projectButton || !Equals(projectButton.Content?.ToString(), "프로젝트 변경 또는 새 프로젝트 만들기")
            || parts[3] is not Separator separator
            || !LabelAt(4, "게임 대상")
            || !ReferenceEquals(parts[5], gamePicker)
            || parts[6] is not TextBlock versionLabel || !versionLabel.Text.StartsWith("버전 또는 실행 환경 메모", StringComparison.Ordinal)
            || !ReferenceEquals(parts[7], versionMemo)
            || parts[8] is not Border
            || parts[9] is not Button nextButton || !Equals(nextButton.Content?.ToString(), "다음: AI 작업 설명"))
            throw new InvalidOperationException("Project/game form controls do not match the v1 contract; no controls were detached.");

        stack.Children.Clear();
        var layout = new StackPanel();
        var projectLabel = (TextBlock)parts[0];
        projectLabel.Visibility = Visibility.Collapsed;
        window.StageProjectName.Visibility = Visibility.Collapsed;
        separator.Margin = new Thickness(0, 0, 0, 12);
        layout.Children.Add(separator);

        var targetPanel = new StackPanel();
        var targetLabel = (TextBlock)parts[4];
        targetLabel.Margin = new Thickness(0, 0, 0, 5);
        gamePicker.Width = double.NaN;
        gamePicker.MinWidth = 250;
        gamePicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        SharedControlPresentation.ApplyComboBox(gamePicker);
        targetPanel.Children.Add(targetLabel);
        targetPanel.Children.Add(gamePicker);
        versionLabel.Margin = new Thickness(0, 12, 0, 5);
        versionMemo.Width = double.NaN;
        versionMemo.MinWidth = 250;
        versionMemo.HorizontalAlignment = HorizontalAlignment.Stretch;
        SharedControlPresentation.ApplyTextBox(versionMemo);
        targetPanel.Children.Add(versionLabel);
        targetPanel.Children.Add(versionMemo);
        var targetCard = Card(targetPanel, new Thickness(16), new Thickness(0));
        layout.Children.Add(targetCard);

        var disclosureHeader = new StackPanel { Margin = new Thickness(2, 1, 2, 1) };
        disclosureHeader.Children.Add(SectionHeading("게임 대상 및 버전"));
        var disclosureHint = new TextBlock
        {
            Text = "QA 대상 게임과 실행 버전을 여기에서 조정하세요. 프로젝트 전환과 소개 수정은 프로젝트 홈에서 할 수 있습니다.",
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        disclosureHint.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextSecondary");
        disclosureHeader.Children.Add(disclosureHint);

        var expander = new Expander
        {
            Header = disclosureHeader,
            Content = layout,
            IsExpanded = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0)
        };
        card.Child = expander;
    }

    private static TextBlock SectionHeading(string text)
    {
        var heading = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextAccent");
        return heading;
    }

    private static Border Card(UIElement child, Thickness padding, Thickness margin)
    {
        var card = new Border
        {
            Child = child,
            Padding = padding,
            Margin = margin,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "Theme.SurfaceAlt");
        card.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        return card;
    }
}
