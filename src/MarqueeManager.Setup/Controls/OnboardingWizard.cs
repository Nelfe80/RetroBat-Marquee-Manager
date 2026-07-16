using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// First-launch wizard — three steps, under three minutes to a working marquee:
/// 1) "we detected N screens" (identification), 2) one pre-checked type proposal
/// per secondary screen (ScreenProbe suggestion), 3) done — zero-config surfaces
/// written, runtime started. Skippable ("Configure later") and relaunchable from
/// the Home view. Never shows again once completed or skipped (state\setup.ini).
/// </summary>
public sealed class OnboardingWizard : Window
{
    private const string DoneFlag = "OnboardingDone";

    private static readonly (string Key, string Fr, string En)[] Types =
    {
        ("marquee", "Marquee", "Marquee"),
        ("topper", "Topper", "Topper"),
        ("iccard", "Instruction card", "Instruction card"),
        ("dmd", "DMD virtuel", "Virtual DMD"),
        ("mixed-vertical", "Vertical mixte (marquee + jeu + IC)", "Mixed vertical (marquee + game + IC)"),
        ("game", "Écran de jeu (RetroBat)", "Game screen (RetroBat)"),
        ("skip", "Ne rien afficher dessus", "Show nothing on it")
    };

    private readonly string _pluginRoot;
    private readonly IReadOnlyList<ScreenInfo> _screens;
    private readonly Dictionary<int, string> _choices = new();
    private readonly ContentControl _stage = new();
    private readonly TextBlock _stepLabel;
    private int _step;

    public static bool ShouldRun(string pluginRoot)
        => SetupPrefs.Read(pluginRoot, DoneFlag, "") != "1"
           && !File.Exists(Path.Combine(pluginRoot, "state", "surfaces.json"));

    public OnboardingWizard(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        _screens = ScreenProbe.Detect();

        Title = L.T("Bienvenue dans Marquee Manager", "Welcome to Marquee Manager");
        Width = 760;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Background;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(22) };
        _stepLabel = Ui.MutedLabel("", 11);
        DockPanel.SetDock(_stepLabel, Dock.Top);
        root.Children.Add(_stepLabel);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var later = Ui.Button(L.T("Configurer plus tard", "Configure later"), (_, _) =>
        {
            SetupPrefs.Write(_pluginRoot, DoneFlag, "1");
            DialogResult = false;
        });
        buttons.Children.Add(later);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var back = Ui.Button(L.T("← Précédent", "← Back"), (_, _) => Go(_step - 1));
        var next = Ui.Button("", (_, _) => Next(), primary: true);
        right.Children.Add(back);
        right.Children.Add(next);
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(right);
        root.Children.Add(buttons);
        root.Children.Add(_stage);
        Content = root;

        _backButton = back;
        _nextButton = next;
        Go(0);
    }

    private readonly Button _backButton;
    private readonly Button _nextButton;

    private void Next()
    {
        if (_step >= 2)
        {
            Finish();
            return;
        }
        Go(_step + 1);
    }

    private void Go(int step)
    {
        _step = Math.Clamp(step, 0, 2);
        _stepLabel.Text = L.T($"Étape {_step + 1} sur 3", $"Step {_step + 1} of 3");
        _backButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        _nextButton.Content = _step == 2
            ? L.T("Terminer — tout configurer", "Finish — configure everything")
            : L.T("Continuer →", "Continue →");
        _stage.Content = _step switch
        {
            0 => BuildStepScreens(),
            1 => BuildStepTypes(),
            _ => BuildStepSummary()
        };
    }

    // ===== step 1: detection =====

    private UIElement BuildStepScreens()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Title(L.T(
            $"Nous avons détecté {_screens.Count} écran(s)",
            $"We detected {_screens.Count} screen(s)")));
        panel.Children.Add(Ui.Subtitle(L.T(
            "Marquee Manager va afficher marquees, scores et effets lumière sur vos écrans secondaires. "
            + "Vérifions d'abord qui est qui.",
            "Marquee Manager will render marquees, scores and light effects on your secondary screens. "
            + "First, let's check who is who.")));

        foreach (var screen in _screens)
        {
            panel.Children.Add(Ui.Card(Ui.Label(screen.Describe(), 12), padding: 10));
        }
        var identify = Ui.Button(L.T("Identifier (numéros sur chaque écran)", "Identify (numbers on every screen)"),
            (_, _) => IdentifyWindow.ShowAll(_screens));
        identify.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(identify);
        return panel;
    }

    // ===== step 2: one type per secondary screen =====

    private UIElement BuildStepTypes()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Title(L.T("À quoi sert chaque écran ?", "What is each screen for?")));
        panel.Children.Add(Ui.Subtitle(L.T(
            "Nous avons pré-choisi d'après la forme de chaque écran (un bandeau 5:1 est probablement un marquee). "
            + "Corrigez si besoin — tout reste modifiable ensuite dans « Mon setup ».",
            "We pre-picked from each screen's shape (a 5:1 strip is probably a marquee). "
            + "Fix anything — everything stays editable later in “My setup”.")));

        foreach (var screen in _screens)
        {
            if (screen.Primary)
            {
                _choices[screen.Index] = "game";
                panel.Children.Add(Ui.Card(Ui.MutedLabel(L.T(
                    $"Écran {screen.Index} (principal) : RetroBat / EmulationStation — rien à faire.",
                    $"Screen {screen.Index} (primary): RetroBat / EmulationStation — nothing to do.")), padding: 10));
                continue;
            }

            var row = new WrapPanel();
            var label = Ui.Label(L.T(
                $"Écran {screen.Index} — {screen.Bounds.Width}×{screen.Bounds.Height} ({screen.Orientation})",
                $"Screen {screen.Index} — {screen.Bounds.Width}×{screen.Bounds.Height} ({screen.Orientation})"), 12);
            label.Width = 300;
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);

            var picker = Ui.ComboBox(280);
            var suggested = Suggest(screen);
            foreach (var (key, fr, en) in Types)
            {
                var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
                picker.Items.Add(item);
                if (key == suggested) picker.SelectedItem = item;
            }
            if (picker.SelectedItem == null) picker.SelectedIndex = 0;
            _choices[screen.Index] = suggested;
            picker.SelectionChanged += (_, _) =>
            {
                if ((picker.SelectedItem as ComboBoxItem)?.Tag is string type) _choices[screen.Index] = type;
            };
            row.Children.Add(picker);
            panel.Children.Add(Ui.Card(row, padding: 10));
        }
        return panel;
    }

    private static string Suggest(ScreenInfo screen)
    {
        if (screen.Ratio >= 3) return "marquee";
        if (screen.Bounds.Width < screen.Bounds.Height) return "mixed-vertical";
        if (screen.Touch == TouchSupport.Touch) return "iccard";
        if (screen.Ratio >= 2.2) return "dmd";
        return "marquee";
    }

    // ===== step 3: summary + apply =====

    private UIElement BuildStepSummary()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Title(L.T("Votre setup est prêt", "Your setup is ready")));
        panel.Children.Add(Ui.Subtitle(L.T(
            "En terminant : les surfaces et composants par défaut sont posés, une mire d'identification confirme chaque écran, "
            + "et le runtime démarre. Naviguez dans EmulationStation — vos marquees s'affichent.",
            "On finish: default surfaces and components are laid out, an identification pattern confirms every screen, "
            + "and the runtime starts. Browse EmulationStation — your marquees show up.")));

        foreach (var screen in _screens)
        {
            var type = _choices.TryGetValue(screen.Index, out var t) ? t : "skip";
            var typeLabel = Types.FirstOrDefault(x => x.Key == type);
            panel.Children.Add(Ui.Card(Ui.Label(
                L.T($"Écran {screen.Index} → {(typeLabel.Fr ?? type)}", $"Screen {screen.Index} → {(typeLabel.En ?? type)}"), 12),
                padding: 10));
        }
        panel.Children.Add(Ui.MutedLabel(L.T(
            "Pour aller plus loin ensuite : « Mon setup » (surfaces, compositions), « Mes systèmes » (sources), « Mes jeux » (effets).",
            "To go further later: “My setup” (surfaces, compositions), “My systems” (sources), “My games” (effects).")));
        return panel;
    }

    private void Finish()
    {
        var store = new SurfacesStore(_pluginRoot);
        var surfaces = store.Load();
        var plan = new List<ScreenModel>();

        foreach (var screen in _screens)
        {
            var type = _choices.TryGetValue(screen.Index, out var t) ? t : (screen.Primary ? "game" : "skip");
            plan.Add(new ScreenModel
            {
                Id = screen.DeviceName,
                Name = screen.Primary ? L.T("Écran RetroBat", "RetroBat screen") : $"{L.T("Écran", "Screen")} {screen.Index}",
                WindowsIndex = screen.Index,
                PhysicalX = screen.Bounds.X,
                PhysicalY = screen.Bounds.Y,
                Connected = true,
                Usage = type == "skip" ? "" : type
            });
            if (type is "skip" or "game") continue;
            SurfacesStore.ProvisionScreenType(surfaces, screen.Index, screen.Bounds.Width, screen.Bounds.Height, type);
        }

        store.Save(surfaces, plan);
        SetupPrefs.Write(_pluginRoot, DoneFlag, "1");

        IdentifyWindow.ShowAll(_screens, seconds: 4);
        if (MarqueeManagerProcess.IsRunning()) MarqueeManagerProcess.Stop();
        MarqueeManagerProcess.Start(_pluginRoot);
        DialogResult = true;
    }
}
