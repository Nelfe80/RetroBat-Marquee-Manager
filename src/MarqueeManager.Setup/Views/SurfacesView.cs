using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Assigns each logical surface (marquee, topper, iccard, dmd, lcd) to a Windows
/// screen, a content stream and an optional zone (*Bounds). Mirrors the [Screens]
/// section of config.ini and writes it back without touching the comments.
/// </summary>
public sealed class SurfacesView : UserControl
{
    private static readonly string[] Contents = { "marquee", "topper", "iccard", "dmd", "lcd" };

    private sealed record SurfaceDef(string Key, string Display, string Hint);

    private static readonly SurfaceDef[] Surfaces =
    {
        new("Marquee", "Marquee",
            L.T("Le bandeau lumineux principal au-dessus de l'écran de jeu.",
                "The main light strip above the game screen.")),
        new("Topper", "Topper",
            L.T("Écran décoratif au sommet de la borne : fanart, visuels promotionnels.",
                "Decorative screen at the top of the cabinet: fanart, promo visuals.")),
        new("IcCard", "Instruction card",
            L.T("Carte de contrôles / how-to-play, souvent tactile et proche du joueur.",
                "Controls / how-to-play card, often touch-capable and close to the player.")),
        new("Dmd", L.T("DMD virtuel (fenêtre WPF)", "Virtual DMD (WPF window)"),
            L.T("Fenêtre DMD à l'écran. -1 ne désactive PAS le DMD physique (onglet DMD).",
                "On-screen DMD window. -1 does NOT disable the physical DMD (DMD tab).")),
        new("Lcd", "LCD panel",
            L.T("Surface d'appoint : layouts MAME, panel de contrôles, aides.",
                "Extra surface: MAME layouts, control panel, player aids."))
    };

    private sealed class SurfaceRow
    {
        public SurfaceDef Def = null!;
        public ComboBox Screen = null!;
        public ComboBox Content = null!;
        public CheckBox Fullscreen = null!;
        public TextBox Bounds = null!;
        public string? PreservedMultiValue;

        public int SelectedScreen()
        {
            if (Screen.SelectedItem is ComboBoxItem { Tag: int index })
            {
                return index;
            }

            return -1;
        }

        public bool IsMulti => Screen.SelectedItem is ComboBoxItem { Tag: string };
    }

    private readonly string _pluginRoot;
    private readonly List<SurfaceRow> _rows = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private IReadOnlyList<ScreenInfo> _screens;

    public SurfacesView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        _screens = ScreenProbe.Detect();
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Clear();
        _status.Text = "";
        (_status.Parent as Panel)?.Children.Remove(_status);
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));

        var page = new StackPanel();
        page.Children.Add(Ui.Title("Surfaces"));
        page.Children.Add(Ui.Subtitle(L.T(
            "Chaque surface s'affiche sur un écran Windows, montre un flux APIExpose, et peut se limiter à une zone "
            + "de l'écran (plusieurs surfaces peuvent partager un même écran vertical). "
            + "« Tester la zone » affiche une mire à l'emplacement exact avant d'enregistrer.",
            "Each surface appears on a Windows screen, shows an APIExpose stream, and can be restricted to a zone "
            + "of the screen (several surfaces can share one vertical display). "
            + "\"Test the zone\" shows a pattern at the exact spot before saving.")));

        foreach (var def in Surfaces)
        {
            page.Children.Add(BuildSurfaceCard(def, ini));
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer dans config.ini", "Save to config.ini"), OnSave, primary: true));
        actions.Children.Add(Ui.Button(L.T("Recharger", "Reload"), (_, _) =>
        {
            _screens = ScreenProbe.Detect();
            Rebuild();
        }));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
    }

    private Border BuildSurfaceCard(SurfaceDef def, IniFile ini)
    {
        var row = new SurfaceRow { Def = def };
        _rows.Add(row);

        var panel = new StackPanel();
        var title = Ui.Label(def.Display, 14);
        title.FontWeight = FontWeights.Bold;
        panel.Children.Add(title);
        panel.Children.Add(Ui.MutedLabel(def.Hint));

        // screen selector
        row.Screen = Ui.ComboBox(280);
        var rawScreen = ini.Get("Screens", def.Key + "Screen", "-1");
        FillScreenCombo(row, rawScreen);
        panel.Children.Add(Ui.Row(L.T("Écran", "Screen"), row.Screen));

        // content selector
        row.Content = Ui.ComboBox(280);
        var currentContent = ini.Get("Screens", def.Key + "Content", def.Key.ToLowerInvariant());
        foreach (var content in Contents)
        {
            row.Content.Items.Add(new ComboBoxItem { Content = ContentDisplay(content), Tag = content });
        }

        row.Content.SelectedIndex = Math.Max(0, Array.FindIndex(Contents,
            c => c.Equals(currentContent, StringComparison.OrdinalIgnoreCase)));
        panel.Children.Add(Ui.Row(L.T("Contenu affiché", "Displayed content"), row.Content, L.T("flux APIExpose", "APIExpose stream")));

        // bounds
        var boundsRaw = ini.Get("Screens", def.Key + "Bounds", "");
        row.Fullscreen = Ui.CheckBox(L.T("Plein écran", "Fullscreen"), string.IsNullOrWhiteSpace(boundsRaw));
        row.Bounds = Ui.TextBox(boundsRaw, 180);
        var boundsLine = new StackPanel { Orientation = Orientation.Horizontal };
        boundsLine.Children.Add(row.Bounds);
        boundsLine.Children.Add(Ui.Button(L.T("Tester la zone", "Test the zone"), (_, _) => TestZone(row)));
        var boundsGrid = Ui.Row(L.T("Zone (x,y,largeur,hauteur)", "Zone (x,y,width,height)"), boundsLine, L.T("vide = plein écran", "empty = fullscreen"));
        panel.Children.Add(Ui.Row("", row.Fullscreen));
        panel.Children.Add(boundsGrid);

        void SyncBoundsEnabled()
        {
            var zone = row.Fullscreen.IsChecked != true;
            row.Bounds.IsEnabled = zone;
            boundsGrid.Opacity = zone ? 1.0 : 0.45;
        }

        row.Fullscreen.Checked += (_, _) => SyncBoundsEnabled();
        row.Fullscreen.Unchecked += (_, _) => SyncBoundsEnabled();
        SyncBoundsEnabled();

        return Ui.Card(panel);
    }

    private void FillScreenCombo(SurfaceRow row, string rawValue)
    {
        row.Screen.Items.Clear();
        row.Screen.Items.Add(new ComboBoxItem { Content = L.T("Désactivé", "Disabled"), Tag = -1 });
        foreach (var screen in _screens)
        {
            row.Screen.Items.Add(new ComboBoxItem
            {
                Content = L.T("ÉCRAN", "SCREEN") + $" {screen.Index} — {screen.Bounds.Width}x{screen.Bounds.Height}"
                          + (screen.Primary ? L.T(" (principal)", " (primary)") : "")
                          + (screen.Touch == TouchSupport.Touch ? L.T(" (tactile)", " (touch)") : ""),
                Tag = screen.Index
            });
        }

        // the runtime accepts comma-separated indices (duplicated surface); preserve them
        if (rawValue.Contains(','))
        {
            row.PreservedMultiValue = rawValue;
            row.Screen.Items.Add(new ComboBoxItem
            {
                Content = L.T("Multi-écrans : ", "Multi-screen: ") + rawValue + L.T(" (conservé)", " (kept)"),
                Tag = rawValue
            });
            row.Screen.SelectedIndex = row.Screen.Items.Count - 1;
            return;
        }

        var index = int.TryParse(rawValue, out var parsed) ? parsed : -1;
        var match = row.Screen.Items.Cast<ComboBoxItem>().ToList()
            .FindIndex(item => item.Tag is int tag && tag == index);
        if (match < 0 && index >= 0)
        {
            // configured screen currently absent (marquee powered off?): keep the value
            // instead of silently falling back to "Disabled" and losing it on save
            row.Screen.Items.Add(new ComboBoxItem
            {
                Content = L.T("ÉCRAN", "SCREEN") + $" {index} — "
                          + L.T("non détecté actuellement (éteint ?), valeur conservée",
                              "not detected right now (powered off?), value kept"),
                Tag = index
            });
            match = row.Screen.Items.Count - 1;
        }

        row.Screen.SelectedIndex = Math.Max(0, match);
    }

    private static string ContentDisplay(string content) => content switch
    {
        "marquee" => L.T("marquee — bandeau du jeu", "marquee — game strip"),
        "topper" => L.T("topper — fanart / visuel dédié", "topper — fanart / dedicated visual"),
        "iccard" => "iccard — instruction card",
        "dmd" => L.T("dmd — DMD virtuel", "dmd — virtual DMD"),
        "lcd" => L.T("lcd — panel / layout MAME", "lcd — panel / MAME layout"),
        _ => content
    };

    private void TestZone(SurfaceRow row)
    {
        var screenIndex = row.SelectedScreen();
        if (row.IsMulti)
        {
            _status.Text = L.T("Zone non testable sur une affectation multi-écrans (modifiez config.ini à la main pour ce cas).",
                "Zone not testable on a multi-screen assignment (edit config.ini by hand for this case).");
            return;
        }

        if (screenIndex < 0 || screenIndex >= _screens.Count)
        {
            _status.Text = $"{row.Def.Display}" + L.T(" : choisissez d'abord un écran.", ": pick a screen first.");
            return;
        }

        var screen = _screens[screenIndex];
        int x = screen.Bounds.X, y = screen.Bounds.Y, w = screen.Bounds.Width, h = screen.Bounds.Height;
        if (row.Fullscreen.IsChecked != true)
        {
            if (!TryParseBounds(row.Bounds.Text, out var b))
            {
                _status.Text = $"{row.Def.Display}" + L.T(" : zone invalide, attendu x,y,largeur,hauteur (ex. 0,0,1920,360).",
                    ": invalid zone, expected x,y,width,height (e.g. 0,0,1920,360).");
                return;
            }

            x += b.x;
            y += b.y;
            w = Math.Min(b.w, screen.Bounds.Width - b.x);
            h = Math.Min(b.h, screen.Bounds.Height - b.y);
        }

        new TestPatternWindow(row.Def.Display, x, y, w, h).Show();
        _status.Text = $"{row.Def.Display}" + L.T($" : mire affichée sur l'écran {screenIndex}. Cliquez dessus pour la fermer.",
            $": pattern shown on screen {screenIndex}. Click it to close.");
    }

    private static bool TryParseBounds(string raw, out (int x, int y, int w, int h) bounds)
    {
        bounds = default;
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], out values[i]))
            {
                return false;
            }
        }

        if (values[2] <= 0 || values[3] <= 0 || values[0] < 0 || values[1] < 0)
        {
            return false;
        }

        bounds = (values[0], values[1], values[2], values[3]);
        return true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var zones = new List<(string surface, int screen, (int x, int y, int w, int h) rect)>();

        foreach (var row in _rows)
        {
            if (row.IsMulti)
            {
                continue;
            }

            var screenIndex = row.SelectedScreen();
            if (screenIndex < 0)
            {
                continue;
            }

            if (screenIndex >= _screens.Count)
            {
                // screen currently absent (powered off): keep the assignment untouched
                warnings.Add($"{row.Def.Display}" + L.T($" : l'écran {screenIndex} n'est pas détecté en ce moment (éteint ?), l'affectation est conservée telle quelle.",
                    $": screen {screenIndex} is not detected right now (powered off?), the assignment is kept as is."));
                continue;
            }

            var screen = _screens[screenIndex];
            var rect = (x: 0, y: 0, w: screen.Bounds.Width, h: screen.Bounds.Height);
            if (row.Fullscreen.IsChecked != true)
            {
                if (!TryParseBounds(row.Bounds.Text, out rect))
                {
                    errors.Add($"{row.Def.Display}" + L.T($" : zone invalide « {row.Bounds.Text} » (attendu x,y,largeur,hauteur).",
                        $": invalid zone \"{row.Bounds.Text}\" (expected x,y,width,height)."));
                    continue;
                }

                if (rect.x >= screen.Bounds.Width || rect.y >= screen.Bounds.Height)
                {
                    errors.Add($"{row.Def.Display}" + L.T($" : la zone démarre hors de l'écran {screenIndex} ({screen.Bounds.Width}x{screen.Bounds.Height}).",
                        $": the zone starts outside screen {screenIndex} ({screen.Bounds.Width}x{screen.Bounds.Height})."));
                    continue;
                }

                if (rect.x + rect.w > screen.Bounds.Width || rect.y + rect.h > screen.Bounds.Height)
                {
                    warnings.Add($"{row.Def.Display}" + L.T($" : la zone dépasse l'écran {screenIndex}, elle sera rognée par le runtime.",
                        $": the zone overflows screen {screenIndex}, the runtime will crop it."));
                }
            }

            foreach (var other in zones.Where(z => z.screen == screenIndex))
            {
                if (other.rect == rect)
                {
                    warnings.Add(L.T($"{row.Def.Display} et {other.surface} occupent exactement la même zone de l'écran {screenIndex}.",
                        $"{row.Def.Display} and {other.surface} occupy exactly the same zone of screen {screenIndex}."));
                }
                else if (Overlaps(other.rect, rect))
                {
                    warnings.Add(L.T($"{row.Def.Display} chevauche {other.surface} sur l'écran {screenIndex}.",
                        $"{row.Def.Display} overlaps {other.surface} on screen {screenIndex}."));
                }
            }

            zones.Add((row.Def.Display, screenIndex, rect));
        }

        if (errors.Count > 0)
        {
            _status.Text = L.T("Rien n'a été écrit :", "Nothing was written:") + Environment.NewLine + string.Join(Environment.NewLine, errors);
            return;
        }

        if (warnings.Count > 0)
        {
            var confirm = MessageBox.Show(
                string.Join(Environment.NewLine, warnings) + Environment.NewLine + Environment.NewLine
                + L.T("Enregistrer quand même ?", "Save anyway?"),
                "MarqueeManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                _status.Text = L.T("Enregistrement annulé.", "Save cancelled.");
                return;
            }
        }

        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        foreach (var row in _rows)
        {
            var screenValue = row.IsMulti ? row.PreservedMultiValue! : row.SelectedScreen().ToString();
            ini.Set("Screens", row.Def.Key + "Screen", screenValue);

            if (row.Content.SelectedItem is ComboBoxItem { Tag: string content })
            {
                ini.Set("Screens", row.Def.Key + "Content", content);
            }

            if (row.Fullscreen.IsChecked == true || string.IsNullOrWhiteSpace(row.Bounds.Text))
            {
                ini.CommentOut("Screens", row.Def.Key + "Bounds");
            }
            else
            {
                ini.Set("Screens", row.Def.Key + "Bounds", row.Bounds.Text.Trim());
            }
        }

        ini.Save();

        var message = L.T("config.ini enregistré (sauvegarde .bak créée).", "config.ini saved (.bak backup created).");
        if (MarqueeManagerProcess.IsRunning())
        {
            var restart = MessageBox.Show(
                L.T("MarqueeManager tourne encore avec l'ancienne configuration. Le redémarrer maintenant ?",
                    "MarqueeManager is still running with the old configuration. Restart it now?"),
                "MarqueeManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (restart == MessageBoxResult.Yes)
            {
                MarqueeManagerProcess.Stop();
                message += MarqueeManagerProcess.Start(_pluginRoot)
                    ? L.T(" MarqueeManager redémarré.", " MarqueeManager restarted.")
                    : L.T(" Impossible de relancer MarqueeManager.exe.", " Could not restart MarqueeManager.exe.");
            }
        }

        _status.Text = message;
    }

    private static bool Overlaps((int x, int y, int w, int h) a, (int x, int y, int w, int h) b)
        => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
}
