using System;
using System.Linq;
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
            || parts[8] is not Border nextGuidance
            || parts[9] is not Button nextButton || !Equals(nextButton.Content?.ToString(), "다음: AI 작업 설명"))
            throw new InvalidOperationException("Project/game form controls do not match the v1 contract; no controls were detached.");

        stack.Children.Clear();
        var layout = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } } };
        var header = new StackPanel();
        var projectLabel = (TextBlock)parts[0];
        projectLabel.Margin = new Thickness(0, 0, 0, 3);
        window.StageProjectName.Margin = new Thickness(0, 0, 0, 3);
        projectButton.HorizontalAlignment = HorizontalAlignment.Left;
        projectButton.Margin = new Thickness(0, 8, 0, 0);
        header.Children.Add(projectLabel);
        header.Children.Add(window.StageProjectName);
        header.Children.Add(projectButton);
        layout.Children.Add(header);

        separator.Margin = new Thickness(0, 14, 0, 14);
        Grid.SetRow(separator, 1);
        layout.Children.Add(separator);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 320 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        Grid.SetRow(body, 2);
        layout.Children.Add(body);

        var targetPanel = new StackPanel();
        targetPanel.Children.Add(SectionHeading("게임 설정"));
        var targetLabel = (TextBlock)parts[4];
        targetLabel.Margin = new Thickness(0, 14, 0, 5);
        gamePicker.Width = double.NaN;
        gamePicker.MinWidth = 250;
        gamePicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        SharedControlPresentation.ApplyComboBox(gamePicker);
        targetPanel.Children.Add(targetLabel);
        targetPanel.Children.Add(gamePicker);
        versionLabel.Margin = new Thickness(0, 14, 0, 5);
        versionMemo.Width = double.NaN;
        versionMemo.MinWidth = 250;
        versionMemo.HorizontalAlignment = HorizontalAlignment.Stretch;
        SharedControlPresentation.ApplyTextBox(versionMemo);
        targetPanel.Children.Add(versionLabel);
        targetPanel.Children.Add(versionMemo);
        var targetCard = Card(targetPanel, new Thickness(16), new Thickness(0, 0, 8, 0));
        body.Children.Add(targetCard);

        var nextPanel = new StackPanel();
        nextPanel.Children.Add(SectionHeading("다음 단계"));
        nextGuidance.Margin = new Thickness(0, 14, 0, 0);
        nextPanel.Children.Add(nextGuidance);
        nextButton.HorizontalAlignment = HorizontalAlignment.Left;
        nextButton.Margin = new Thickness(0, 14, 0, 0);
        SharedControlPresentation.ApplyButton(projectButton, primary: false);
        SharedControlPresentation.ApplyButton(nextButton, primary: true);
        nextPanel.Children.Add(nextButton);
        var nextCard = Card(nextPanel, new Thickness(16), new Thickness(8, 0, 0, 0));
        Grid.SetColumn(nextCard, 1);
        body.Children.Add(nextCard);

        card.Child = layout;
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
