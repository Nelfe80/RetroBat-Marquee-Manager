using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// "Gestion des effets pendant la partie" — the game's ingame effects. The .MEM
/// signals show as readable "Quand X alors Y" rows on two columns with
/// provenance badges; clicking one (or "Lier un effet à un signal") opens the
/// dedicated <see cref="EffectBindingDialog"/>. The card also carries the
/// per-game policy, the "Mes effets" library access, the link to the .MEM file,
/// the full default table (unfiltered by genre) and the live ws/ingame monitor.
/// Overrides are written sparse to overrides\effects\ through
/// <see cref="EffectsOverrideStore"/> — the runtime reloads them per game.
/// </summary>
public sealed class EffectsCard : UserControl, IDisposable
{
    private static readonly (string Key, string Fr, string En)[] Kinds =
    {
        ("flash", "Flash coloré", "Colored flash"),
        ("pulse", "Impulsion", "Pulse"),
        ("tint", "Voile coloré", "Colored veil"),
        ("shake", "Secousse", "Shake"),
        ("strobe", "Stroboscope", "Strobe"),
        ("blackout", "Extinction", "Blackout"),
        ("powercycle", "Rallumage", "Power cycle"),
        ("sprite", "Nuée de sprites", "Sprite swarm")
    };

    private readonly string _pluginRoot;
    private readonly string _system;
    private readonly string _rom;
    private readonly IReadOnlyList<MemSignal> _signals;
    private readonly IReadOnlyList<string> _genreSlugs;
    private readonly EffectsOverrideStore _store;
    private readonly EffectsLibraryReader _library;
    private readonly string _spritesDir;
    private readonly string _apiUrl;

    private readonly UniformGrid _rows = new() { Columns = 2 };
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private string _scope = "game"; // game | system | genre:<slug>

    private IngameMonitor? _monitor;
    private Button? _monitorButton;
    private TextBlock? _monitorStatus;
    private ListBox? _monitorList;

    public EffectsCard(string pluginRoot, string system, string rom,
        IReadOnlyList<MemSignal> signals, string? genreLabels, string? genreIds, string apiUrl,
        string? memPath = null)
    {
        _pluginRoot = pluginRoot;
        _system = system;
        _rom = rom;
        _signals = signals;
        _apiUrl = apiUrl;
        _store = new EffectsOverrideStore(pluginRoot);
        _library = new EffectsLibraryReader(pluginRoot);
        _genreSlugs = _library.ResolveGenreSlugs(genreLabels, genreIds);
        _spritesDir = Path.Combine(pluginRoot, "resources", "sprites");

        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Gestion des effets pendant la partie", "Ingame effects management")));

        // policy (per game) + library access
        var policyRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        var policyLabel = Ui.MutedLabel(L.T("Pour ce jeu :", "For this game:"));
        policyLabel.Margin = new Thickness(0, 0, 6, 0);
        policyRow.Children.Add(policyLabel);
        var policyPicker = Ui.ComboBox(300);
        policyPicker.Items.Add(new ComboBoxItem { Content = L.T("Hériter des effets par défaut (genre/système)", "Inherit the default effects (genre/system)"), Tag = "inherit" });
        policyPicker.Items.Add(new ComboBoxItem { Content = L.T("Uniquement mes effets (défauts coupés)", "Only my effects (defaults muted)"), Tag = "custom-only" });
        policyPicker.Items.Add(new ComboBoxItem { Content = L.T("Tout désactiver", "Disable everything"), Tag = "off" });
        var currentPolicy = _store.LoadPolicy(system, rom);
        policyPicker.SelectedIndex = currentPolicy switch { "custom-only" => 1, "off" => 2, _ => 0 };
        policyPicker.SelectionChanged += (_, _) =>
        {
            if ((policyPicker.SelectedItem as ComboBoxItem)?.Tag is string policy)
            {
                _store.SavePolicy(_system, _rom, policy);
                _status.Text = policy switch
                {
                    "custom-only" => L.T("Seuls les signaux que vous avez liés réagiront.", "Only the signals you linked will react."),
                    "off" => L.T("Aucun effet MEM ne jouera sur ce jeu.", "No MEM effect will play on this game."),
                    _ => L.T("Défauts genre/système + vos réglages.", "Genre/system defaults + your tweaks.")
                };
                _status.Foreground = Ui.Muted;
            }
        };
        policyRow.Children.Add(policyPicker);
        policyRow.Children.Add(Ui.Button(L.T("Mes effets…", "My effects…"), (_, _) =>
        {
            new EffectComposerWindow(_pluginRoot) { Owner = Window.GetWindow(this) }.ShowDialog();
            RebuildRows();
        }));
        card.Children.Add(policyRow);

        if (_genreSlugs.Count > 0)
        {
            card.Children.Add(Ui.MutedLabel(L.T(
                $"Genre détecté : {string.Join(", ", _genreSlugs)} — les défauts genrés s'appliquent déjà.",
                $"Detected genre: {string.Join(", ", _genreSlugs)} — genre defaults already apply.")));
        }

        // write scope
        var scopeRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        var scopeLabel = Ui.MutedLabel(L.T("Mes réglages s'appliquent à :", "My tweaks apply to:"));
        scopeLabel.Margin = new Thickness(0, 0, 6, 0);
        scopeRow.Children.Add(scopeLabel);
        var scopePicker = Ui.ComboBox(230);
        scopePicker.Items.Add(new ComboBoxItem { Content = L.T("ce jeu uniquement", "this game only"), Tag = "game" });
        scopePicker.Items.Add(new ComboBoxItem { Content = L.T($"tout le système {system}", $"the whole {system} system"), Tag = "system" });
        foreach (var slug in _genreSlugs)
        {
            scopePicker.Items.Add(new ComboBoxItem
            {
                Content = L.T($"tous les jeux {slug}", $"every {slug} game"),
                Tag = "genre:" + slug
            });
        }
        scopePicker.SelectedIndex = 0;
        scopePicker.SelectionChanged += (_, _) =>
        {
            if ((scopePicker.SelectedItem as ComboBoxItem)?.Tag is string scope)
            {
                _scope = scope;
                RebuildRows();
            }
        };
        scopeRow.Children.Add(scopePicker);
        scopeRow.Children.Add(Ui.Button(L.T("Lier un effet à un signal…", "Link an effect to a signal…"), (_, _) =>
            OpenBinding(_signals.FirstOrDefault()), primary: _signals.Count > 0));
        card.Children.Add(scopeRow);

        if (signals.Count == 0)
        {
            var none = Ui.Label(L.T(
                "⚠ Ce jeu n'a PAS de définition .MEM : aucun signal sémantique n'est disponible, donc aucun effet piloté par le jeu. "
                + "Le moniteur live reste utilisable pour vérifier.",
                "⚠ This game has NO .MEM definition: no semantic signal is available, so no game-driven effect. "
                + "The live monitor still works to double-check."));
            none.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(none);
        }
        else
        {
            card.Children.Add(Ui.MutedLabel(L.T(
                $"{signals.Count} signaux — cliquez une ligne pour lier/modifier son effet.",
                $"{signals.Count} signals — click a row to link/edit its effect.")));
        }

        // the .MEM file behind all this — never a black box
        if (memPath is { Length: > 0 })
        {
            var link = Ui.MutedLabel(L.T($"Fichier .MEM : {memPath}", $".MEM file: {memPath}"), 11);
            link.TextDecorations = TextDecorations.Underline;
            link.Cursor = Cursors.Hand;
            link.TextWrapping = TextWrapping.Wrap;
            link.MouseLeftButtonDown += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{memPath}\"");
                }
                catch
                {
                    // explorer unavailable
                }
            };
            card.Children.Add(link);
        }

        card.Children.Add(_rows);
        BuildDefaultsSection(card);
        BuildMonitorSection(card);
        _status.TextWrapping = TextWrapping.Wrap;
        card.Children.Add(_status);
        Content = card;

        RebuildRows();
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        _monitor = null;
    }

    // ================= rows =================

    private Dictionary<string, EffectRule> LoadScopeRules() => _scope switch
    {
        "system" => _store.LoadSystem(_system),
        var s when s.StartsWith("genre:") => _store.LoadGenre(s["genre:".Length..]),
        _ => _store.LoadGame(_system, _rom)
    };

    private void SaveScopeRules(Dictionary<string, EffectRule> rules)
    {
        switch (_scope)
        {
            case "system":
                _store.SaveSystem(_system, rules);
                break;
            case var s when s.StartsWith("genre:"):
                _store.SaveGenre(s["genre:".Length..], rules);
                break;
            default:
                _store.SaveGame(_system, _rom, rules);
                break;
        }
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        foreach (var signal in _signals)
        {
            _rows.Children.Add(BuildRow(signal));
        }
    }

    /// <summary>The rule currently winning for this signal + its provenance.</summary>
    private (EffectRule? Rule, EffectOrigin Origin, string Detail) ResolveCurrent(MemSignal signal)
    {
        var game = _store.LoadGame(_system, _rom);
        if (game.TryGetValue(signal.Action, out var g)) return (g, EffectOrigin.Game, L.T("réglé pour ce jeu", "set for this game"));
        var system = _store.LoadSystem(_system);
        if (system.TryGetValue(signal.Action, out var s)) return (s, EffectOrigin.System, L.T($"réglé pour {_system}", $"set for {_system}"));
        foreach (var slug in _genreSlugs)
        {
            var genre = _store.LoadGenre(slug);
            if (genre.TryGetValue(signal.Action, out var ge)) return (ge, EffectOrigin.GenreOverride, L.T($"réglé pour le genre {slug}", $"set for the {slug} genre"));
        }
        if (_library.FindDefault(signal.Action, signal.Family, _genreSlugs) is { } def)
        {
            return def.GenreScoped
                ? (def.Rule, EffectOrigin.GenreDefault, L.T($"défaut genre ({_genreSlugs.FirstOrDefault()})", $"genre default ({_genreSlugs.FirstOrDefault()})"))
                : (def.Rule, EffectOrigin.Default, L.T("défaut", "default"));
        }
        return (null, EffectOrigin.None, L.T("aucun effet", "no effect"));
    }

    private FrameworkElement BuildRow(MemSignal signal)
    {
        var (rule, origin, detail) = ResolveCurrent(signal);

        var header = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // status dot: gray = no effect, orange = default effect, green = custom
        var statusColor = origin switch
        {
            EffectOrigin.Game or EffectOrigin.System or EffectOrigin.GenreOverride => Color.FromRgb(0x4C, 0xC9, 0x6E),
            EffectOrigin.GenreDefault or EffectOrigin.Default => Color.FromRgb(0xFF, 0xB3, 0x00),
            _ => Color.FromRgb(0x6A, 0x6A, 0x7A)
        };
        if (rule is { Off: true }) statusColor = Color.FromRgb(0x6A, 0x6A, 0x7A);

        // sentence: ● Quand ACTION (desc) → alors [effet]
        var sentence = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2) };
        sentence.Inlines.Add(new System.Windows.Documents.InlineUIContainer(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(statusColor),
            Margin = new Thickness(0, 0, 7, 0)
        })
        { BaselineAlignment = System.Windows.BaselineAlignment.Center });
        sentence.Inlines.Add(new System.Windows.Documents.Run(L.T("Quand ", "When ")) { Foreground = Ui.Muted });
        sentence.Inlines.Add(new System.Windows.Documents.Run(signal.Action)
        { Foreground = Ui.Foreground, FontWeight = FontWeights.Bold });
        if (signal.Description.Length > 0)
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run($" — {signal.Description}") { Foreground = Ui.Muted, FontSize = 11 });
        }
        sentence.Inlines.Add(new System.Windows.Documents.Run(L.T("  alors  ", "  then  ")) { Foreground = Ui.Muted });
        if (rule == null)
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run(L.T("rien", "nothing")) { Foreground = Ui.Muted, FontStyle = FontStyles.Italic });
        }
        else if (rule.Off)
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run(L.T("silencieux (désactivé)", "silenced (off)")) { Foreground = Ui.Muted, FontStyle = FontStyles.Italic });
        }
        else if (rule.EffectRef is { Length: > 0 })
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run("✦ " + rule.EffectRef) { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
        }
        else if (rule.Actions is { Count: > 0 })
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run(L.T($"séquence ({rule.Actions.Count} actions)", $"sequence ({rule.Actions.Count} actions)")) { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
        }
        else
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run(KindLabel(rule.Kind)) { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
            if (rule.Sprite is { Length: > 0 })
            {
                sentence.Inlines.Add(new System.Windows.Documents.Run($" + {rule.Sprite}") { Foreground = Ui.Muted, FontSize = 11 });
            }
        }
        header.Children.Add(sentence);

        // right side: provenance badge (the status dot at line start carries the color logic)
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Badge(detail, origin));
        Grid.SetColumn(right, 1);
        header.Children.Add(right);

        header.MouseLeftButtonDown += (_, _) => OpenBinding(signal);

        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 10, 4),
            Child = header
        };
    }

    private void OpenBinding(MemSignal? signal)
    {
        if (_signals.Count == 0)
        {
            _status.Text = L.T("Aucun signal .MEM à lier sur ce jeu.", "No .MEM signal to link on this game.");
            _status.Foreground = Ui.Error;
            return;
        }
        var dialog = new EffectBindingDialog(_pluginRoot, _spritesDir, _signals, signal,
            LoadScopeRules, SaveScopeRules, ResolveCurrent, ScopeLabel())
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
        if (dialog.Changed) RebuildRows();
    }

    private Border Badge(string text, EffectOrigin origin)
    {
        var accent = origin is EffectOrigin.Game or EffectOrigin.System or EffectOrigin.GenreOverride;
        return new Border
        {
            Background = accent ? Ui.Accent : Ui.Brush(Color.FromRgb(0x2E, 0x2E, 0x44)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = accent ? Ui.Brush(Color.FromRgb(0x18, 0x12, 0x06)) : Ui.Muted
            }
        };
    }

    private string ScopeLabel() => _scope switch
    {
        "system" => L.T($"système {_system}", $"{_system} system"),
        var s when s.StartsWith("genre:") => L.T($"genre {s["genre:".Length..]}", $"{s["genre:".Length..]} genre"),
        _ => L.T("ce jeu", "this game")
    };

    // ================= full defaults table =================

    /// <summary>"Voir tous les effets" — the whole default library, including the
    /// rules other genres get, so the filter is never a mystery.</summary>
    private void BuildDefaultsSection(StackPanel card)
    {
        var defaults = _library.ListDefaults();
        if (defaults.Count == 0) return;

        var body = new StackPanel();
        foreach (var (match, genres, effect) in defaults)
        {
            var line = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            line.Inlines.Add(new System.Windows.Documents.Run(match) { Foreground = Ui.Foreground, FontWeight = FontWeights.SemiBold });
            line.Inlines.Add(new System.Windows.Documents.Run($" → {KindLabel(effect.Kind)}"
                + (effect.Sprite is { Length: > 0 } ? $" + {effect.Sprite}" : "")) { Foreground = Ui.Accent });
            if (genres != null)
            {
                var applies = _genreSlugs.Any(slug => genres.Contains(slug, StringComparison.OrdinalIgnoreCase));
                line.Inlines.Add(new System.Windows.Documents.Run($"   [{genres}]"
                    + (applies ? "" : L.T(" — autre genre, inactif ici", " — other genre, inactive here")))
                { Foreground = Ui.Muted });
            }
            body.Children.Add(line);
        }

        card.Children.Add(new Expander
        {
            Header = new TextBlock
            {
                Text = L.T($"Voir tous les effets par défaut ({defaults.Count}, tous genres)",
                    $"See every default effect ({defaults.Count}, all genres)"),
                Foreground = Ui.Muted,
                FontSize = 12
            },
            Content = body,
            IsExpanded = false,
            Margin = new Thickness(0, 6, 0, 0)
        });
    }

    // ================= live monitor =================

    private void BuildMonitorSection(StackPanel card)
    {
        card.Children.Add(Ui.SectionHeader(L.T("Moniteur live", "Live monitor")));
        card.Children.Add(Ui.MutedLabel(L.T(
            "Lancez le jeu et jouez : les signaux qui tirent s'affichent ici en direct. Cliquez-en un pour régler son effet.",
            "Launch the game and play: firing signals show up here live. Click one to tune its effect.")));

        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        _monitorButton = Ui.Button(L.T("Démarrer l'écoute", "Start listening"), (_, _) => ToggleMonitor());
        row.Children.Add(_monitorButton);
        _monitorStatus = Ui.MutedLabel("");
        row.Children.Add(_monitorStatus);
        card.Children.Add(row);

        _monitorList = new ListBox { MaxHeight = 170, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _monitorList.SelectionChanged += (_, _) =>
        {
            if (_monitorList.SelectedItem is ListBoxItem { Tag: string action } && action.Length > 0)
            {
                OpenBinding(_signals.FirstOrDefault(s => s.Action.Equals(action, StringComparison.OrdinalIgnoreCase)));
            }
        };
        card.Children.Add(_monitorList);
    }

    private void ToggleMonitor()
    {
        if (_monitor != null)
        {
            _monitor.Dispose();
            _monitor = null;
            _monitorButton!.Content = L.T("Démarrer l'écoute", "Start listening");
            _monitorStatus!.Text = "";
            return;
        }

        _monitor = new IngameMonitor(_apiUrl);
        _monitorButton!.Content = L.T("Arrêter l'écoute", "Stop listening");
        _monitorList!.Visibility = Visibility.Visible;
        _monitorStatus!.Text = L.T("connexion…", "connecting…");
        _monitor.ConnectedChanged += connected => Dispatcher.BeginInvoke(() =>
        {
            if (_monitorStatus != null)
            {
                _monitorStatus.Text = connected
                    ? L.T("à l'écoute de ws/ingame", "listening on ws/ingame")
                    : L.T("déconnecté — nouvelle tentative…", "disconnected — retrying…");
            }
        });
        _monitor.EventReceived += evt => Dispatcher.BeginInvoke(() =>
        {
            if (_monitorList == null)
            {
                return;
            }

            _monitorList.Items.Insert(0, new ListBoxItem
            {
                Content = $"{evt.At:HH:mm:ss}  {evt.Action}" + (evt.Family.Length > 0 ? $"  ({evt.Family})" : ""),
                Tag = evt.Action,
                FontSize = 11
            });
            while (_monitorList.Items.Count > 50)
            {
                _monitorList.Items.RemoveAt(_monitorList.Items.Count - 1);
            }
        });
    }

    // ================= helpers =================

    private static string KindLabel(string kind)
        => Kinds.FirstOrDefault(k => k.Key.Equals(kind, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } match
            ? L.T(match.Fr, match.En)
            : kind;

    private static Brush SafeBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return Brushes.Gray;
        }
    }
}
