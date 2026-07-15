using System.Diagnostics;
using System.IO;
using System.Windows;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;
using MarqueeManager.Setup.Views;

namespace MarqueeManager.Setup;

/// <summary>
/// Shell: sidebar navigation between the configuration views. Views are
/// rebuilt on each navigation so they always reflect the current config.ini —
/// the file stays the single source of truth, there is no in-memory model.
/// Views owning live resources (WebSocket monitor…) implement IDisposable and
/// are disposed before the host swaps content.
/// </summary>
public partial class MainWindow : Window
{
    private readonly string _pluginRoot;

    public MainWindow()
    {
        _pluginRoot = PluginPaths.FindPluginRoot();
        Ui.Initialize(_pluginRoot);
        L.Initialize(_pluginRoot);

        InitializeComponent();
        TryLowerProcessPriority();
        LoadBrandIcon();
        ApplyShellTexts();
        UpdateThemeGlyph();
        SourceInitialized += (_, _) => TryEnableDarkTitleBar();
        Closed += (_, _) => DisposeActiveView();

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
            (NavHome, "setup-home"),
            (NavScreens, "setup-screens"),
            (NavSurfaces, "setup-surfaces"),
            (NavDmd, "setup-dmd"),
            (NavTouch, "setup-touch"),
            (NavGames, "setup-games"),
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

        DisposeActiveView();
        ContentHost.Content = true switch
        {
            _ when NavScreens.IsChecked == true => new ScreensView(_pluginRoot),
            _ when NavSurfaces.IsChecked == true => new SurfacesView(_pluginRoot),
            _ when NavDmd.IsChecked == true => new DmdView(_pluginRoot),
            _ when NavTouch.IsChecked == true => new TouchView(_pluginRoot),
            _ when NavGames.IsChecked == true => new GamesView(_pluginRoot),
            _ when NavOptions.IsChecked == true => new OptionsView(_pluginRoot),
            _ => (object)new HomeView(_pluginRoot, NavigateTo)
        };
        UpdateFooter();
    }

    private void NavigateTo(string view)
    {
        var nav = view switch
        {
            "screens" => NavScreens,
            "surfaces" => NavSurfaces,
            "dmd" => NavDmd,
            "touch" => NavTouch,
            "games" => NavGames,
            "options" => NavOptions,
            _ => NavHome
        };
        nav.IsChecked = true;
    }

    private void DisposeActiveView()
    {
        if (ContentHost?.Content is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        Ui.Toggle(_pluginRoot);
        TryEnableDarkTitleBar();
        UpdateThemeGlyph();
        UpdateFooter();
        ShowCurrent();
    }

    /// <summary>FR ↔ EN switch: persisted, shell relabelled, active view rebuilt.</summary>
    private void LangToggle_Click(object sender, RoutedEventArgs e)
    {
        L.Toggle(_pluginRoot);
        ApplyShellTexts();
        UpdateThemeGlyph();
        UpdateFooter();
        ShowCurrent();
    }

    private void ApplyShellTexts()
    {
        NavHome.Content = L.T("Accueil", "Home");
        NavScreens.Content = L.T("Écrans", "Screens");
        NavSurfaces.Content = L.T("Surfaces", "Surfaces");
        NavDmd.Content = L.T("DMD physique", "Physical DMD");
        NavTouch.Content = L.T("IC card tactile", "Touch IC card");
        NavGames.Content = L.T("Mes jeux", "My games");
        NavOptions.Content = L.T("Options", "Options");
        LangToggle.Content = L.French ? "EN" : "FR";
        LangToggle.ToolTip = L.T("Switch to English", "Passer en français");
    }

    private void UpdateThemeGlyph()
    {
        // sun proposes the light theme, moon proposes the dark one
        ThemeToggle.Content = Ui.IsLight ? "" : "";
        ThemeToggle.ToolTip = Ui.IsLight
            ? L.T("Passer en thème sombre", "Switch to the dark theme")
            : L.T("Passer en thème clair", "Switch to the light theme");
    }

    private void UpdateFooter()
    {
        var configPath = PluginPaths.ConfigPath(_pluginRoot);
        FooterInfo.Text = (File.Exists(configPath)
                ? L.T("config.ini trouvé", "config.ini found")
                : L.T("config.ini introuvable !", "config.ini not found!"))
            + "\n" + (MarqueeManagerProcess.IsRunning()
                ? L.T("MarqueeManager : en cours", "MarqueeManager: running")
                : L.T("MarqueeManager : arrêté", "MarqueeManager: stopped"))
            + $"\n{_pluginRoot}";
    }

    /// <summary>Native caption color (DWMWA_USE_IMMERSIVE_DARK_MODE), so the title
    /// bar follows the app theme instead of staying white.</summary>
    private void TryEnableDarkTitleBar()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var enabled = Ui.IsLight ? 0 : 1;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // cosmetic
        }
    }

    /// <summary>Brand icon (images\icon.png) shown top-left and used as window icon.</summary>
    private void LoadBrandIcon()
    {
        try
        {
            var path = Path.Combine(_pluginRoot, "images", "icon.png");
            if (!File.Exists(path))
            {
                return;
            }

            var brand = new System.Windows.Media.Imaging.BitmapImage();
            brand.BeginInit();
            brand.UriSource = new Uri(path);
            brand.DecodePixelWidth = 128;
            brand.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            brand.EndInit();
            brand.Freeze();
            BrandIconHost.Background = new System.Windows.Media.ImageBrush(brand) { Stretch = System.Windows.Media.Stretch.UniformToFill };
            Icon = brand;
        }
        catch
        {
            // cosmetic: the placeholder square stays
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
