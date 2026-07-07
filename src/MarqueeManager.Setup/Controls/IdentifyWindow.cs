using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarqueeManager.Setup.Detection;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Full-screen "ÉCRAN n" badge shown on every display so the user can match Windows
/// indices to physical screens without guessing. Auto-closes after a few seconds.
/// </summary>
public sealed class IdentifyWindow : Window
{
    private readonly ScreenInfo _screen;

    private IdentifyWindow(ScreenInfo screen)
    {
        _screen = screen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(215, 10, 10, 18));

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = $"ÉCRAN {screen.Index}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
            FontSize = Math.Max(48, Math.Min(screen.Bounds.Width, screen.Bounds.Height) / 4.0),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{screen.Bounds.Width}x{screen.Bounds.Height}"
                   + (screen.Primary ? " · principal" : "")
                   + (screen.Touch == TouchSupport.Touch ? " · tactile" : ""),
            Foreground = Brushes.White,
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });
        Content = panel;

        SourceInitialized += (_, _) => NativePlacement.Place(
            this, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        MouseDown += (_, _) => Close();
    }

    /// <summary>Shows the badge on each screen and closes them after <paramref name="seconds"/>.</summary>
    public static void ShowAll(IReadOnlyList<ScreenInfo> screens, int seconds = 5)
    {
        var windows = screens.Select(screen => new IdentifyWindow(screen)).ToList();
        foreach (var window in windows)
        {
            window.Show();
        }

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(seconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var window in windows)
            {
                try
                {
                    window.Close();
                }
                catch
                {
                    // already closed by click
                }
            }
        };
        timer.Start();
    }
}
