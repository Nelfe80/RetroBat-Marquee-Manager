using System.Diagnostics;
using System.IO;
using System.Windows;
using MarqueeManager.Setup.Config;
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

        _pluginRoot = PluginPaths.FindPluginRoot();
        UpdateFooter();
        ShowCurrent();
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
        FooterInfo.Text = (File.Exists(configPath) ? "config.ini trouvé" : "config.ini introuvable !")
            + $"\n{_pluginRoot}"
            + "\n\n" + (MarqueeManagerProcess.IsRunning()
                ? "MarqueeManager : en cours"
                : "MarqueeManager : arrêté");
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
