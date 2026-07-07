using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Processes;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Physical DMD configuration ([DMD] section): model, resolution, port, brightness,
/// USB tuning — plus the static inventory of the private DMD stack and an on-demand
/// test pattern through dmdext. The test is never automatic: a powered-off panel
/// must not block the setup.
/// </summary>
public sealed class DmdView : UserControl
{
    private static readonly (string Value, string Display)[] Models =
    {
        ("zedmd", "ZeDMD (128x32)"),
        ("zedmdhd", "ZeDMD HD (256x64)"),
        ("pin2dmd", "Pin2DMD"),
        ("pindmdv3", "PinDMD v3"),
        ("virtualdmd", "Virtual DMD (DmdDevice)")
    };

    private readonly string _pluginRoot;
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly CheckBox _enabled;
    private readonly ComboBox _model;
    private readonly ComboBox _resolution;
    private readonly ComboBox _port;
    private readonly CheckBox _optimize;
    private readonly Slider _brightness;
    private readonly TextBlock _brightnessValue = Ui.MutedLabel("");
    private readonly ComboBox _usbPackage;
    private readonly TextBox _minRefresh;
    private readonly TextBox _minBlockMs;
    private readonly TextBox _activeSystems;
    private Process? _testProcess;

    public DmdView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        var ini = IniFile.Load(PluginPaths.ConfigPath(pluginRoot));
        var probe = DmdProbe.Inspect(pluginRoot);

        var page = new StackPanel();
        page.Children.Add(Ui.Title("DMD physique"));
        page.Children.Add(Ui.Subtitle(
            "Configuration du vrai panneau DMD (ZeDMD, Pin2DMD…). Indépendant du DMD virtuel WPF : "
            + "DmdScreen=-1 dans l'onglet Surfaces ne coupe que la fenêtre à l'écran, pas ce panneau."));

        // detection
        page.Children.Add(Ui.SectionHeader("Détection"));
        var detectText = Ui.MutedLabel(probe.Describe(), 12);
        detectText.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(Ui.Card(detectText));

        // settings
        page.Children.Add(Ui.SectionHeader("Réglages"));
        var settings = new StackPanel();

        _enabled = Ui.CheckBox("Activer la pile DMD", ini.GetBool("DMD", "Enabled", false));
        settings.Children.Add(_enabled);

        _model = Ui.ComboBox(260);
        var currentModel = ini.Get("DMD", "Model", "zedmd");
        foreach (var (value, display) in Models)
        {
            _model.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        }

        var modelIndex = Array.FindIndex(Models, m => m.Value.Equals(currentModel, StringComparison.OrdinalIgnoreCase));
        _model.SelectedIndex = modelIndex >= 0 ? modelIndex : 0;
        settings.Children.Add(Ui.Row("Modèle", _model));

        _resolution = Ui.ComboBox(260);
        var currentRes = $"{ini.GetInt("DMD", "Width", 128)}x{ini.GetInt("DMD", "Height", 32)}";
        foreach (var res in new[] { "128x32", "256x64", "192x64", "128x16" })
        {
            _resolution.Items.Add(new ComboBoxItem { Content = res, Tag = res });
        }

        if (_resolution.Items.Cast<ComboBoxItem>().All(item => (string)item.Tag != currentRes))
        {
            _resolution.Items.Add(new ComboBoxItem { Content = currentRes + " (actuel)", Tag = currentRes });
        }

        _resolution.SelectedIndex = _resolution.Items.Cast<ComboBoxItem>().ToList()
            .FindIndex(item => (string)item.Tag == currentRes);
        settings.Children.Add(Ui.Row("Résolution", _resolution, "128x32 = ZeDMD standard, 256x64 = HD"));

        _port = Ui.ComboBox(260);
        _port.Items.Add(new ComboBoxItem { Content = "Auto-détection", Tag = "" });
        foreach (var portName in probe.SerialPorts)
        {
            _port.Items.Add(new ComboBoxItem { Content = portName, Tag = portName });
        }

        var currentPort = ini.Get("DMD", "ZeDmdPort", "");
        var portIndex = _port.Items.Cast<ComboBoxItem>().ToList()
            .FindIndex(item => ((string)item.Tag).Equals(currentPort, StringComparison.OrdinalIgnoreCase));
        if (portIndex < 0 && currentPort.Length > 0)
        {
            _port.Items.Add(new ComboBoxItem { Content = currentPort + " (configuré, absent)", Tag = currentPort });
            portIndex = _port.Items.Count - 1;
        }

        _port.SelectedIndex = Math.Max(0, portIndex);
        settings.Children.Add(Ui.Row("Port série ZeDMD", _port, "laisser en auto sauf conflit"));

        _optimize = Ui.CheckBox("Optimiser le ZeDMD à l'ouverture (calibration historique)",
            ini.GetBool("DMD", "OptimizeZeDmd", true));
        settings.Children.Add(_optimize);

        _brightness = new Slider
        {
            Minimum = -1,
            Maximum = 15,
            Value = ini.GetInt("DMD", "Brightness", -1),
            Width = 200,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _brightness.ValueChanged += (_, _) => UpdateBrightnessLabel();
        var brightnessLine = new StackPanel { Orientation = Orientation.Horizontal };
        brightnessLine.Children.Add(_brightness);
        brightnessLine.Children.Add(_brightnessValue);
        settings.Children.Add(Ui.Row("Luminosité", brightnessLine));
        UpdateBrightnessLabel();

        _usbPackage = Ui.ComboBox(260);
        foreach (var (value, display) in new[] { ("0", "Auto (512 en 128x32, 1024 en HD)"), ("512", "512"), ("1024", "1024") })
        {
            _usbPackage.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        }

        var usbCurrent = ini.Get("DMD", "UsbPackageSize", "0");
        var usbIndex = _usbPackage.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => (string)item.Tag == usbCurrent);
        if (usbIndex < 0)
        {
            _usbPackage.Items.Add(new ComboBoxItem { Content = usbCurrent + " (actuel)", Tag = usbCurrent });
            usbIndex = _usbPackage.Items.Count - 1;
        }

        _usbPackage.SelectedIndex = usbIndex;
        settings.Children.Add(Ui.Row("Paquets USB", _usbPackage, "augmenter si balayage/flicker"));

        _minRefresh = Ui.TextBox(ini.Get("DMD", "PanelMinRefreshRate", "0"), 80);
        settings.Children.Add(Ui.Row("Refresh minimal du panneau", _minRefresh, "0 = ne pas modifier"));

        _minBlockMs = Ui.TextBox(ini.Get("DMD", "MinimumBlockDisplayMs", "3000"), 80);
        settings.Children.Add(Ui.Row("Durée minimale d'un bloc (ms)", _minBlockMs, "anti-clignotement de la rotation"));

        _activeSystems = Ui.TextBox(ini.Get("DMD", "ActiveSystemsDMD", ""), 420);
        settings.Children.Add(Ui.Row("Systèmes qui gardent le DMD", _activeSystems, "pinballs : MarqueeManager leur laisse la main"));

        page.Children.Add(Ui.Card(settings));

        // actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button("Enregistrer dans config.ini", OnSave, primary: true));
        actions.Children.Add(Ui.Button("Afficher une mire sur le DMD", OnTestPattern));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
        Unloaded += (_, _) => StopTest();
    }

    private void UpdateBrightnessLabel()
    {
        var value = (int)Math.Round(_brightness.Value);
        _brightnessValue.Text = value < 0 ? "-1 (garder la valeur firmware)" : value.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_minRefresh.Text.Trim(), out var refresh) || refresh < 0)
        {
            _status.Text = "Refresh minimal invalide : entier ≥ 0 attendu.";
            return;
        }

        if (!int.TryParse(_minBlockMs.Text.Trim(), out var blockMs) || blockMs < 250)
        {
            _status.Text = "Durée minimale d'un bloc invalide : entier ≥ 250 attendu.";
            return;
        }

        var resolution = ((string)((ComboBoxItem)_resolution.SelectedItem).Tag).Split('x');

        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        ini.Set("DMD", "Enabled", (_enabled.IsChecked == true).ToString().ToLowerInvariant());
        ini.Set("DMD", "Model", (string)((ComboBoxItem)_model.SelectedItem).Tag);
        ini.Set("DMD", "Width", resolution[0]);
        ini.Set("DMD", "Height", resolution[1]);
        ini.Set("DMD", "ZeDmdPort", (string)((ComboBoxItem)_port.SelectedItem).Tag);
        ini.Set("DMD", "OptimizeZeDmd", (_optimize.IsChecked == true).ToString().ToLowerInvariant());
        ini.Set("DMD", "Brightness", ((int)Math.Round(_brightness.Value)).ToString());
        ini.Set("DMD", "UsbPackageSize", (string)((ComboBoxItem)_usbPackage.SelectedItem).Tag);
        ini.Set("DMD", "PanelMinRefreshRate", refresh.ToString());
        ini.Set("DMD", "MinimumBlockDisplayMs", blockMs.ToString());
        ini.Set("DMD", "ActiveSystemsDMD", _activeSystems.Text.Trim());
        ini.Save();

        _status.Text = "Section [DMD] enregistrée (sauvegarde .bak créée). Redémarrez MarqueeManager pour appliquer.";
    }

    /// <summary>
    /// Pushes dmdext's built-in test pattern to the physical panel. Requires the DMD
    /// to be powered and MarqueeManager stopped (it holds the device).
    /// </summary>
    private void OnTestPattern(object sender, RoutedEventArgs e)
    {
        var dmdext = Path.Combine(_pluginRoot, "tools", "dmd", "dmdext.exe");
        if (!File.Exists(dmdext))
        {
            _status.Text = "dmdext.exe introuvable dans tools\\dmd — impossible d'envoyer la mire.";
            return;
        }

        if (MarqueeManagerProcess.IsRunning())
        {
            var stop = MessageBox.Show(
                "MarqueeManager occupe le DMD. L'arrêter pour envoyer la mire ?",
                "MarqueeManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (stop != MessageBoxResult.Yes)
            {
                _status.Text = "Test annulé : MarqueeManager garde le DMD.";
                return;
            }

            MarqueeManagerProcess.Stop();
        }

        StopTest();
        try
        {
            _testProcess = Process.Start(new ProcessStartInfo(dmdext, "test --destination auto")
            {
                WorkingDirectory = Path.GetDirectoryName(dmdext)!,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            _status.Text = "Mire envoyée via dmdext (panneau allumé requis). Elle s'arrête toute seule dans 10 s.";

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                StopTest();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            _status.Text = "Échec du lancement de dmdext : " + ex.Message;
        }
    }

    private void StopTest()
    {
        try
        {
            if (_testProcess is { HasExited: false })
            {
                _testProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // already gone
        }
        finally
        {
            _testProcess?.Dispose();
            _testProcess = null;
        }
    }
}
