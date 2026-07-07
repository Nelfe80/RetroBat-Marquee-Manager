using System.Diagnostics;
using System.IO;
using System.Windows;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;
using MarqueeManager.Setup.Views;

namespace MarqueeManager.Setup;

/// <summary>
/// Shell: sidebar navigation between the five configuration views. Views are
/// rebuilt on each navigation so they always reflect the current config.ini —
/// the file stays the single source of truth, there is no in-memory model.
/// </summary>
public partial class MainWindow : Window
{
    private readonly string _pluginRoot;

    public MainWindow()
    {
        InitializeComponent();
        TryLowerProcessPriority();

        NavScreens.Content = L.T("Écrans", "Screens");
        NavSurfaces.Content = L.T("Surfaces", "Surfaces");
        NavDmd.Content = L.T("DMD physique", "Physical DMD");
        NavTouch.Content = L.T("IC card tactile", "Touch IC card");
        NavOptions.Content = L.T("Options", "Options");

        _pluginRoot = PluginPaths.FindPluginRoot();
        UpdateFooter();
        ShowCurrent();

        // documentation mode: `--screenshots <dir>` renders every tab to PNG and exits.
        // Used to keep the wiki illustrations in sync with the real UI.
        var args = Environment.GetCommandLineArgs();
        var shotIndex = Array.IndexOf(args, "--screenshots");
        if (shotIndex >= 0 && shotIndex + 1 < args.Length)
        {
            Loaded += (_, _) => _ = CaptureAllTabsAsync(args[shotIndex + 1]);
        }
    }

    private async System.Threading.Tasks.Task CaptureAllTabsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var tabs = new (System.Windows.Controls.RadioButton Nav, string Name)[]
        {
            (NavScreens, "setup-screens"),
            (NavSurfaces, "setup-surfaces"),
            (NavDmd, "setup-dmd"),
            (NavTouch, "setup-touch"),
            (NavOptions, "setup-options")
        };
        foreach (var (nav, name) in tabs)
        {
            nav.IsChecked = true;
            await System.Threading.Tasks.Task.Delay(1200); // async probes settle
            SaveScreenshot(directory, name);
        }

        Close();
    }

    private void SaveScreenshot(string directory, string name)
    {
        if (Content is not FrameworkElement root || root.ActualWidth <= 0)
        {
            return;
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || ContentHost != null)
        {
            ShowCurrent();
        }
    }

    private void ShowCurrent()
    {
        if (ContentHost == null)
        {
            return;
        }

        ContentHost.Content = true switch
        {
            _ when NavSurfaces.IsChecked == true => new SurfacesView(_pluginRoot),
            _ when NavDmd.IsChecked == true => new DmdView(_pluginRoot),
            _ when NavTouch.IsChecked == true => new TouchView(_pluginRoot),
            _ when NavOptions.IsChecked == true => new OptionsView(_pluginRoot),
            _ => (object)new ScreensView(_pluginRoot)
        };
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        var configPath = PluginPaths.ConfigPath(_pluginRoot);
        FooterInfo.Text = (File.Exists(configPath)
                ? L.T("config.ini trouvé", "config.ini found")
                : L.T("config.ini introuvable !", "config.ini not found!"))
            + $"\n{_pluginRoot}"
            + "\n\n" + (MarqueeManagerProcess.IsRunning()
                ? L.T("MarqueeManager : en cours", "MarqueeManager: running")
                : L.T("MarqueeManager : arrêté", "MarqueeManager: stopped"));
    }

    private static void TryLowerProcessPriority()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            current.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // cosmetic
        }
    }
}
