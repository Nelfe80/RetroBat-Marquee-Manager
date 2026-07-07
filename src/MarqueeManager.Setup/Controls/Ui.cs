using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Small factory for the dark-themed controls shared by every view — the views are
/// built in code (same approach as LedManagerSetup) so the styling lives here.
/// </summary>
public static class Ui
{
    public static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1C));
    public static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x2A));
    public static readonly Brush PanelBorder = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x44));
    public static readonly Brush Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF0));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    public static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0x6E));
    public static readonly Brush Error = new SolidColorBrush(Color.FromRgb(0xE8, 0x5C, 0x5C));

    public static TextBlock Title(string text) => new()
    {
        Text = text,
        Foreground = Foreground,
        FontSize = 20,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    public static TextBlock Subtitle(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 14)
    };

    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        Foreground = Accent,
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 14, 0, 6)
    };

    public static TextBlock Label(string text, double size = 12) => new()
    {
        Text = text,
        Foreground = Foreground,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock MutedLabel(string text, double size = 11) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    public static Border Card(UIElement content, double padding = 14) => new()
    {
        Background = Panel,
        BorderBrush = PanelBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(padding),
        Margin = new Thickness(0, 0, 0, 10),
        Child = content
    };

    public static Button Button(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 12,
            FontWeight = primary ? FontWeights.Bold : FontWeights.Normal,
            Foreground = primary ? Brushes.Black : Foreground,
            Background = primary ? Accent : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3C)),
            BorderBrush = PanelBorder,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += onClick;
        return button;
    }

    public static CheckBox CheckBox(string text, bool isChecked) => new()
    {
        Content = text,
        IsChecked = isChecked,
        Foreground = Foreground,
        FontSize = 12,
        Margin = new Thickness(0, 3, 0, 3),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static ComboBox ComboBox(double width = 220) => new()
    {
        Width = width,
        FontSize = 12,
        Margin = new Thickness(0, 2, 8, 2),
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    public static TextBox TextBox(string text, double width = 220) => new()
    {
        Text = text,
        Width = width,
        FontSize = 12,
        Padding = new Thickness(6, 4, 6, 4),
        Margin = new Thickness(0, 2, 8, 2),
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x18)),
        Foreground = Foreground,
        BorderBrush = PanelBorder,
        CaretBrush = Foreground,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    public static ScrollViewer Page(StackPanel content)
    {
        content.Margin = new Thickness(24, 20, 24, 20);
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Background
        };
    }

    /// <summary>Two-column row: fixed-width label on the left, control on the right.</summary>
    public static Grid Row(string label, UIElement control, string? hint = null, double labelWidth = 250)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = Label(label);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        Grid.SetColumn((FrameworkElement)control, 1);
        grid.Children.Add(control);

        if (hint != null)
        {
            var hintText = MutedLabel(hint);
            hintText.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(hintText, 2);
            grid.Children.Add(hintText);
        }

        return grid;
    }
}
