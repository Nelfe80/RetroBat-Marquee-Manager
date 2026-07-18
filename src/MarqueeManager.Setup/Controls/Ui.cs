using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarqueeManager.Setup.Config;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Theme engine + factory for the controls shared by every view (same design
/// language as LedManagerSetup, amber accent). The XAML shell consumes the token
/// brushes through DynamicResource, so <see cref="Apply"/> retints it live; the
/// code-built views route their colors through <see cref="Brush"/>/<see cref="Text"/>,
/// whose remap table translates the dark palette into the light one — views are
/// rebuilt on theme change. Preview/composer canvases deliberately stay dark in
/// both themes (<see cref="Viewport"/>). Choice persisted in state\setup.ini
/// [Setup] Theme — NOT in config.ini, which the runtime regenerates.
/// </summary>
public static class Ui
{
    public static bool IsLight { get; private set; }

    /// <summary>Dark plate for preview / composer canvases, identical in both themes.</summary>
    public static SolidColorBrush Viewport { get; } = Frozen(C(0x15, 0x15, 0x1F));

    // ---- theme-aware palette (properties: re-evaluated when views rebuild) ----
    public static Brush Background => Brush(C(0x0F, 0x0F, 0x17));
    public static Brush Panel => Brush(C(0x17, 0x17, 0x22));
    public static Brush PanelBorder => Brush(C(0x22, 0x22, 0x2F));
    public static Brush Foreground => Brush(C(0xE8, 0xE8, 0xF0));
    public static Brush Muted => Brush(C(0x8A, 0x8A, 0x9A));
    public static Brush Accent => Brush(C(0xFF, 0xB3, 0x00));
    public static Brush Ok => Brush(C(0x4C, 0xC9, 0x6E));
    public static Brush Error => Brush(C(0xE8, 0x5C, 0x5C));

    private static readonly Dictionary<Color, Color> LightMap = new()
    {
        [C(0x0F, 0x0F, 0x17)] = C(0xF1, 0xF2, 0xF7), // app background
        [C(0xE8, 0xE8, 0xF0)] = C(0x1E, 0x1E, 0x28), // primary text
        [C(0xB8, 0xB8, 0xC6)] = C(0x4A, 0x4A, 0x5A), // body text
        [C(0x8A, 0x8A, 0x9A)] = C(0x70, 0x70, 0x7E), // muted text
        [C(0x1D, 0x1D, 0x2A)] = C(0xFF, 0xFF, 0xFF), // panel surface
        [C(0x17, 0x17, 0x22)] = C(0xFF, 0xFF, 0xFF), // card surface
        [C(0x2E, 0x2E, 0x44)] = C(0xE7, 0xE8, 0xF2), // chip / badge surface
        [C(0x16, 0x16, 0x20)] = C(0xEF, 0xF0, 0xF6), // inset surface
        [C(0x1A, 0x1A, 0x26)] = C(0xFF, 0xFF, 0xFF), // field surface
        [C(0x10, 0x10, 0x18)] = C(0xFF, 0xFF, 0xFF), // legacy field surface
        [C(0x26, 0x26, 0x36)] = C(0xEA, 0xEB, 0xF3), // control surface
        [C(0x3A, 0x3A, 0x52)] = C(0xCE, 0xCF, 0xE0), // border
        [C(0x22, 0x22, 0x2F)] = C(0xE3, 0xE4, 0xEE), // hairline border
        [C(0xFF, 0xB3, 0x00)] = C(0xB8, 0x77, 0x00), // amber accent as text
        [C(0x4C, 0xC9, 0x6E)] = C(0x1F, 0xA8, 0x3C), // ok green
        [C(0xE8, 0x5C, 0x5C)] = C(0xC9, 0x3A, 0x3A), // error red
        [C(0xE8, 0x30, 0x30)] = C(0xD4, 0x2B, 0x2B), // red
        [C(0x30, 0x60, 0xE8)] = C(0x2B, 0x50, 0xD0), // blue
        [C(0x20, 0xE8, 0xE8)] = C(0x0F, 0x9E, 0xAE), // cyan
        [C(0xE8, 0x8A, 0x5A)] = C(0xC4, 0x62, 0x2F), // warning orange
    };

    private static readonly Dictionary<string, Color> DarkTokens = new()
    {
        ["AppBackground"] = C(0x0F, 0x0F, 0x17),
        ["SidebarBackground"] = C(0x0B, 0x0B, 0x12),
        ["PanelBackground"] = C(0x1D, 0x1D, 0x2A),
        ["CardBackground"] = C(0x17, 0x17, 0x22),
        ["AppForeground"] = C(0xE8, 0xE8, 0xF0),
        ["AppMuted"] = C(0x8A, 0x8A, 0x9A),
        ["ControlBackground"] = C(0x26, 0x26, 0x36),
        ["ControlHover"] = C(0x32, 0x32, 0x4A),
        ["ControlBorder"] = C(0x3A, 0x3A, 0x52),
        ["FieldBackground"] = C(0x1A, 0x1A, 0x26),
        ["HairlineBrush"] = C(0x22, 0x22, 0x2F),
        ["AccentSoft"] = C(0xFF, 0xCE, 0x55),
        ["NavHover"] = C(0x18, 0x18, 0x24),
        ["NavChecked"] = C(0x1E, 0x1E, 0x2E),
    };

    private static readonly Dictionary<string, Color> LightTokens = new()
    {
        ["AppBackground"] = C(0xF1, 0xF2, 0xF7),
        ["SidebarBackground"] = C(0xFA, 0xFB, 0xFE),
        ["PanelBackground"] = C(0xFF, 0xFF, 0xFF),
        ["CardBackground"] = C(0xFF, 0xFF, 0xFF),
        ["AppForeground"] = C(0x1E, 0x1E, 0x28),
        ["AppMuted"] = C(0x70, 0x70, 0x7E),
        ["ControlBackground"] = C(0xEA, 0xEB, 0xF3),
        ["ControlHover"] = C(0xDE, 0xDF, 0xEC),
        ["ControlBorder"] = C(0xCE, 0xCF, 0xE0),
        ["FieldBackground"] = C(0xFF, 0xFF, 0xFF),
        ["HairlineBrush"] = C(0xE3, 0xE4, 0xEE),
        ["AccentSoft"] = C(0xB8, 0x77, 0x00),
        ["NavHover"] = C(0xEC, 0xEC, 0xF4),
        ["NavChecked"] = C(0xFB, 0xF0, 0xD8),
    };

    public static void Initialize(string? pluginRoot)
    {
        IsLight = SetupPrefs.Read(pluginRoot, "Theme", "dark")
            .Equals("light", StringComparison.OrdinalIgnoreCase);
        Apply();
    }

    public static void Toggle(string? pluginRoot)
    {
        IsLight = !IsLight;
        Apply();
        SetupPrefs.Write(pluginRoot, "Theme", IsLight ? "light" : "dark");
    }

    /// <summary>Theme-aware brush for the code-built views (frozen).</summary>
    public static SolidColorBrush Brush(Color color) => Frozen(Remap(color));

    public static SolidColorBrush Text(byte r, byte g, byte b) => Brush(Color.FromRgb(r, g, b));

    public static Color Remap(Color color)
        => IsLight && LightMap.TryGetValue(color, out var mapped) ? mapped : color;

    private static void Apply()
    {
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, color) in IsLight ? LightTokens : DarkTokens)
        {
            resources[key] = Frozen(color);
        }
    }

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ================= factories shared by every view =================

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
        CornerRadius = new CornerRadius(10),
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
            FontSize = 12
        };
        if (primary)
        {
            // Application.Current is null under test harnesses — plain accent fallback
            if (System.Windows.Application.Current?.TryFindResource("AccentButton") is Style accent)
            {
                button.Style = accent;
            }
            else
            {
                button.FontWeight = FontWeights.Bold;
                button.Background = Accent;
                button.Foreground = Brush(Color.FromRgb(0x18, 0x12, 0x06));
            }
        }
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
        Margin = new Thickness(0, 2, 8, 2),
        HorizontalAlignment = HorizontalAlignment.Left
    };

    /// <summary>Classic marquee bulb / neon tints offered wherever a color is picked.</summary>
    public static readonly string[] PaletteColors =
    {
        "#ffd9a0", "#ffffff", "#ff3b30", "#ff9c57", "#ffd60a",
        "#34c759", "#32ade6", "#007aff", "#af52de", "#ff2d55"
    };

    /// <summary>Row of clickable color chips bound to a hex TextBox: a click writes
    /// the hex into the box (its TextChanged applies the color), the chip matching
    /// the current value stays outlined. The hex field remains the escape hatch.</summary>
    public static WrapPanel ColorPalette(TextBox colorBox)
    {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var chips = new List<Border>();
        void RefreshOutlines()
        {
            var current = colorBox.Text.Trim();
            foreach (var chip in chips)
            {
                var selected = (chip.Tag as string ?? "").Equals(current, StringComparison.OrdinalIgnoreCase);
                chip.BorderBrush = selected ? Accent : PanelBorder;
                chip.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }
        foreach (var hex in PaletteColors)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var chip = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
                Background = new System.Windows.Media.SolidColorBrush(color),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex,
                Tag = hex
            };
            var chosen = hex;
            chip.MouseLeftButtonDown += (_, e) =>
            {
                colorBox.Text = chosen;
                e.Handled = true;
            };
            chips.Add(chip);
            panel.Children.Add(chip);
        }
        colorBox.TextChanged += (_, _) => RefreshOutlines();
        RefreshOutlines();
        return panel;
    }

    /// <summary>Indeterminate progress row shown while a sheet loads off-thread.</summary>
    public static StackPanel Spinner(string text)
    {
        var host = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        host.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 220, HorizontalAlignment = HorizontalAlignment.Left });
        var label = MutedLabel(text);
        label.Margin = new Thickness(0, 6, 0, 0);
        host.Children.Add(label);
        return host;
    }

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
