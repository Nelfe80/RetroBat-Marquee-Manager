using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
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
        page.Children.Add(Ui.Title(L.T("DMD physique", "Physical DMD")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Configuration du vrai panneau DMD (ZeDMD, Pin2DMD…). Indépendant du DMD virtuel WPF : "
            + "DmdScreen=-1 dans l'onglet Surfaces ne coupe que la fenêtre à l'écran, pas ce panneau.",
            "Configuration of the real DMD panel (ZeDMD, Pin2DMD…). Independent from the virtual WPF DMD: "
            + "DmdScreen=-1 in the Surfaces tab only disables the on-screen window, not this panel.")));

        // detection
        page.Children.Add(Ui.SectionHeader(L.T("Détection", "Detection")));
        var detectText = Ui.MutedLabel(probe.Describe(), 12);
        detectText.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(Ui.Card(detectText));

        // settings
        page.Children.Add(Ui.SectionHeader(L.T("Réglages", "Settings")));
        var settings = new StackPanel();

        _enabled = Ui.CheckBox(L.T("Activer la pile DMD", "Enable the DMD stack"), ini.GetBool("DMD", "Enabled", false));
        settings.Children.Add(_enabled);

        _model = Ui.ComboBox(260);
        var currentModel = ini.Get("DMD", "Model", "zedmd");
        foreach (var (value, display) in Models)
        {
            _model.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        }

        var modelIndex = Array.FindIndex(Models, m => m.Value.Equals(currentModel, StringComparison.OrdinalIgnoreCase));
        _model.SelectedIndex = modelIndex >= 0 ? modelIndex : 0;
        settings.Children.Add(Ui.Row(L.T("Modèle", "Model"), _model));

        _resolution = Ui.ComboBox(260);
        var currentRes = $"{ini.GetInt("DMD", "Width", 128)}x{ini.GetInt("DMD", "Height", 32)}";
        foreach (var res in new[] { "128x32", "256x64", "192x64", "128x16" })
        {
            _resolution.Items.Add(new ComboBoxItem { Content = res, Tag = res });
        }

        if (_resolution.Items.Cast<ComboBoxItem>().All(item => (string)item.Tag != currentRes))
        {
            _resolution.Items.Add(new ComboBoxItem { Content = currentRes + L.T(" (actuel)", " (current)"), Tag = currentRes });
        }

        _resolution.SelectedIndex = _resolution.Items.Cast<ComboBoxItem>().ToList()
            .FindIndex(item => (string)item.Tag == currentRes);
        settings.Children.Add(Ui.Row(L.T("Résolution", "Resolution"), _resolution, L.T("128x32 = ZeDMD standard, 256x64 = HD", "128x32 = standard ZeDMD, 256x64 = HD")));

        _port = Ui.ComboBox(260);
        _port.Items.Add(new ComboBoxItem { Content = L.T("Auto-détection", "Auto-detect"), Tag = "" });
        foreach (var portName in probe.SerialPorts)
        {
            _port.Items.Add(new ComboBoxItem { Content = portName, Tag = portName });
        }

        var currentPort = ini.Get("DMD", "ZeDmdPort", "");
        var portIndex = _port.Items.Cast<ComboBoxItem>().ToList()
            .FindIndex(item => ((string)item.Tag).Equals(currentPort, StringComparison.OrdinalIgnoreCase));
        if (portIndex < 0 && currentPort.Length > 0)
        {
            _port.Items.Add(new ComboBoxItem { Content = currentPort + L.T(" (configuré, absent)", " (configured, absent)"), Tag = currentPort });
            portIndex = _port.Items.Count - 1;
        }

        _port.SelectedIndex = Math.Max(0, portIndex);
        settings.Children.Add(Ui.Row(L.T("Port série ZeDMD", "ZeDMD serial port"), _port, L.T("laisser en auto sauf conflit", "leave on auto unless it conflicts")));

        _optimize = Ui.CheckBox(L.T("Optimiser le ZeDMD à l'ouverture (calibration historique)", "Optimize the ZeDMD on open (legacy calibration)"),
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
        settings.Children.Add(Ui.Row(L.T("Luminosité", "Brightness"), brightnessLine));
        UpdateBrightnessLabel();

        _usbPackage = Ui.ComboBox(260);
        foreach (var (value, display) in new[] { ("0", L.T("Auto (512 en 128x32, 1024 en HD)", "Auto (512 at 128x32, 1024 in HD)")), ("512", "512"), ("1024", "1024") })
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
        settings.Children.Add(Ui.Row(L.T("Paquets USB", "USB packets"), _usbPackage, L.T("augmenter si balayage/flicker", "increase on sweep/flicker")));

        _minRefresh = Ui.TextBox(ini.Get("DMD", "PanelMinRefreshRate", "0"), 80);
        settings.Children.Add(Ui.Row(L.T("Refresh minimal du panneau", "Panel minimum refresh"), _minRefresh, L.T("0 = ne pas modifier", "0 = leave unchanged")));

        _minBlockMs = Ui.TextBox(ini.Get("DMD", "MinimumBlockDisplayMs", "3000"), 80);
        settings.Children.Add(Ui.Row(L.T("Durée minimale d'un bloc (ms)", "Minimum block duration (ms)"), _minBlockMs, L.T("anti-clignotement de la rotation", "anti-flicker for the rotation")));

        _activeSystems = Ui.TextBox(ini.Get("DMD", "ActiveSystemsDMD", ""), 420);
        settings.Children.Add(Ui.Row(L.T("Systèmes qui gardent le DMD", "Systems that keep the DMD"), _activeSystems, L.T("pinballs : MarqueeManager leur laisse la main", "pinballs: MarqueeManager hands the DMD over")));

        page.Children.Add(Ui.Card(settings));

        // actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer dans config.ini", "Save to config.ini"), OnSave, primary: true));
        actions.Children.Add(Ui.Button(L.T("Afficher une mire sur le DMD", "Show a pattern on the DMD"), OnTestPattern));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
        Unloaded += (_, _) => StopTest();
    }

    private void UpdateBrightnessLabel()
    {
        var value = (int)Math.Round(_brightness.Value);
        _brightnessValue.Text = value < 0 ? L.T("-1 (garder la valeur firmware)", "-1 (keep the firmware value)") : value.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_minRefresh.Text.Trim(), out var refresh) || refresh < 0)
        {
            _status.Text = L.T("Refresh minimal invalide : entier ≥ 0 attendu.", "Invalid minimum refresh: integer ≥ 0 expected.");
            return;
        }

        if (!int.TryParse(_minBlockMs.Text.Trim(), out var blockMs) || blockMs < 250)
        {
            _status.Text = L.T("Durée minimale d'un bloc invalide : entier ≥ 250 attendu.", "Invalid minimum block duration: integer ≥ 250 expected.");
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

        _status.Text = L.T("Section [DMD] enregistrée (sauvegarde .bak créée). Redémarrez MarqueeManager pour appliquer.",
            "[DMD] section saved (.bak backup created). Restart MarqueeManager to apply.");
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
            _status.Text = L.T("dmdext.exe introuvable dans tools\\dmd — impossible d'envoyer la mire.",
                "dmdext.exe not found in tools\\dmd — cannot send the pattern.");
            return;
        }

        if (MarqueeManagerProcess.IsRunning())
        {
            var stop = MessageBox.Show(
                L.T("MarqueeManager occupe le DMD. L'arrêter pour envoyer la mire ?",
                    "MarqueeManager holds the DMD. Stop it to send the pattern?"),
                "MarqueeManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (stop != MessageBoxResult.Yes)
            {
                _status.Text = L.T("Test annulé : MarqueeManager garde le DMD.", "Test cancelled: MarqueeManager keeps the DMD.");
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
            _status.Text = L.T("Mire envoyée via dmdext (panneau allumé requis). Elle s'arrête toute seule dans 10 s.",
                "Pattern sent through dmdext (panel must be powered). It stops by itself in 10 s.");

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
            _status.Text = L.T("Échec du lancement de dmdext : ", "dmdext failed to start: ") + ex.Message;
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
