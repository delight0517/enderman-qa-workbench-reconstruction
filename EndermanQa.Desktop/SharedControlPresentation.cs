using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EndermanQa.Desktop;

/// <summary>Version 1 shared, stateless theme styling for native shell controls.</summary>
internal static class SharedControlPresentation
{
    public const int ContractVersion = 1;

    public static void ApplyComboBox(ComboBox comboBox)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        comboBox.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        comboBox.SetResourceReference(Control.BorderBrushProperty, "Theme.Border");
        comboBox.BorderThickness = new Thickness(1);
        comboBox.MinHeight = 38;
        comboBox.MaxDropDownHeight = 320;
        comboBox.Template = CreateComboBoxTemplate();

        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.Surface")));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextPrimary")));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMuted")));
        itemStyle.Triggers.Add(hover);
        var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentMutedAlt")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextStrong")));
        itemStyle.Triggers.Add(selected);
        comboBox.ItemContainerStyle = itemStyle;
    }

    public static void ApplyButton(Button button, bool primary)
    {
        ArgumentNullException.ThrowIfNull(button);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(primary ? "Theme.Accent" : "Theme.Surface")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(primary ? "Theme.Surface" : "Theme.TextStrong")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(primary ? "Theme.Accent" : "Theme.Border")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 14, 8)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 38d));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateButtonTemplate()));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(primary ? "Theme.TextAccent" : "Theme.AccentMuted")));
        style.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.48));
        style.Triggers.Add(disabled);
        button.Style = style;
    }

    public static void ApplyTextBox(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        textBox.SetResourceReference(Control.BackgroundProperty, "Theme.Surface");
        textBox.SetResourceReference(Control.ForegroundProperty, "Theme.TextPrimary");
        textBox.SetResourceReference(Control.BorderBrushProperty, "Theme.Border");
        textBox.BorderThickness = new Thickness(1);
        textBox.Padding = new Thickness(10, 7, 10, 7);
        textBox.MinHeight = 38;
    }

    private static ControlTemplate CreateButtonTemplate()
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
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Theme.TextAccent"), "Chrome"));
        template.Triggers.Add(focus);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "Chrome"));
        template.Triggers.Add(pressed);
        return template;
    }

    private static ControlTemplate CreateComboBoxTemplate()
    {
        var template = new ControlTemplate(typeof(ComboBox));
        var root = new FrameworkElementFactory(typeof(Grid));
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        chrome.SetBinding(Border.BackgroundProperty, TemplateProperty("Background"));
        chrome.SetBinding(Border.BorderBrushProperty, TemplateProperty("BorderBrush"));
        chrome.SetBinding(Border.BorderThicknessProperty, TemplateProperty("BorderThickness"));
        root.AppendChild(chrome);

        var selectedContent = new FrameworkElementFactory(typeof(ContentPresenter), "ContentSite");
        selectedContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        selectedContent.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 3, 38, 3));
        selectedContent.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        selectedContent.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectionBoxItemTemplate") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        selectedContent.SetBinding(ContentPresenter.ContentStringFormatProperty, new Binding("SelectionBoxItemStringFormat") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        selectedContent.SetBinding(TextBlock.ForegroundProperty, TemplateProperty("Foreground"));
        root.AppendChild(selectedContent);

        var toggleStyle = new Style(typeof(ToggleButton));
        toggleStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        toggleStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        toggleStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Theme.TextSecondary")));
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var arrow = new FrameworkElementFactory(typeof(Path));
        arrow.SetValue(Path.DataProperty, Geometry.Parse("M 1 2 L 5 6 L 9 2"));
        arrow.SetValue(Shape.StrokeThicknessProperty, 1.7d);
        arrow.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        arrow.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        arrow.SetBinding(Shape.StrokeProperty, TemplateProperty("Foreground"));
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        toggleTemplate.VisualTree = arrow;
        toggleStyle.Setters.Add(new Setter(Control.TemplateProperty, toggleTemplate));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton), "PART_DropDownToggle");
        toggle.SetValue(FrameworkElement.WidthProperty, 34d);
        toggle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        toggle.SetValue(FrameworkElement.FocusableProperty, false);
        toggle.SetValue(FrameworkElement.StyleProperty, toggleStyle);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
        root.AppendChild(toggle);

        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.StaysOpenProperty, false);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
        popup.SetBinding(Popup.PlacementTargetProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        popup.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 4, 0, 0));
        popupBorder.SetResourceReference(Border.BackgroundProperty, "Theme.Surface");
        popupBorder.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent"), "Chrome"));
        template.Triggers.Add(focus);
        template.VisualTree = root;
        return template;
    }

    private static Binding TemplateProperty(string path) => new(path)
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
    };
}
