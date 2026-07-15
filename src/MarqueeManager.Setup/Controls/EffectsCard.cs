using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// "Quand [signal] alors [effet]" card (RetroCreator Flow Builder UX): the game's
/// .MEM signals as readable sentences, an inline editor with a live preview band,
/// provenance badges (game / system / genre / library default), and a live tap
/// on ws/ingame to discover signals by playing. Overrides are written sparse to
/// overrides\effects\ through <see cref="EffectsOverrideStore"/> — the runtime
/// reloads them at every game change.
/// </summary>
public sealed class EffectsCard : UserControl, IDisposable
{
    private static readonly (string Key, string Fr, string En)[] Kinds =
    {
        ("flash", "Flash coloré", "Colored flash"),
        ("pulse", "Impulsion", "Pulse"),
        ("tint", "Teinte soutenue", "Sustained tint"),
        ("shake", "Secousse", "Shake"),
        ("strobe", "Stroboscope", "Strobe"),
        ("blackout", "Extinction", "Blackout"),
        ("powercycle", "Rallumage", "Power cycle"),
        ("sprite", "Sprites seuls", "Sprites only")
    };

    private static readonly (string Key, string Fr, string En)[] Motions =
    {
        ("pop", "Apparition", "Pop"),
        ("fall", "Chute", "Fall"),
        ("rise", "Montée", "Rise"),
        ("cross", "Traversée", "Cross")
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

    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private string _scope = "game"; // game | system | genre:<slug>
    private string? _expandedAction;

    private IngameMonitor? _monitor;
    private Button? _monitorButton;
    private TextBlock? _monitorStatus;
    private ListBox? _monitorList;

    public EffectsCard(string pluginRoot, string system, string rom,
        IReadOnlyList<MemSignal> signals, string? genreLabels, string? genreIds, string apiUrl)
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
        card.Children.Add(Ui.SectionHeader(L.T("Effets lumière (signaux du jeu)", "Light effects (game signals)")));

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
        card.Children.Add(scopeRow);

        if (signals.Count == 0)
        {
            card.Children.Add(Ui.Label(L.T(
                "Ce jeu n'a pas de définition .MEM : aucun signal sémantique n'est disponible. Le moniteur live reste utilisable.",
                "This game has no .MEM definition: no semantic signal is available. The live monitor still works.")));
        }
        card.Children.Add(_rows);
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

        var host = new StackPanel();
        var header = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // sentence: Quand ACTION (desc) → alors [effet]
        var sentence = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2) };
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
        else
        {
            sentence.Inlines.Add(new System.Windows.Documents.Run(KindLabel(rule.Kind)) { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
            if (rule.Sprite is { Length: > 0 })
            {
                sentence.Inlines.Add(new System.Windows.Documents.Run($" + {rule.Sprite}") { Foreground = Ui.Muted, FontSize = 11 });
            }
        }
        header.Children.Add(sentence);

        // right side: color dot + provenance badge
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (rule is { Off: false } && rule.Kind is not ("sprite" or "shake" or "blackout"))
        {
            right.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = SafeBrush(rule.Color),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        right.Children.Add(Badge(detail, origin));
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        host.Children.Add(header);

        StackPanel? editor = null;
        if (_expandedAction == signal.Action)
        {
            editor = BuildEditor(signal, rule);
            host.Children.Add(editor);
        }

        header.MouseLeftButtonDown += (_, _) =>
        {
            _expandedAction = _expandedAction == signal.Action ? null : signal.Action;
            RebuildRows();
        };

        return new Border
        {
            Background = editor != null ? Ui.Brush(Color.FromRgb(0x16, 0x16, 0x20)) : Brushes.Transparent,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 6, 4),
            Child = host
        };
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

    // ================= inline editor =================

    private StackPanel BuildEditor(MemSignal signal, EffectRule? current)
    {
        var draft = current == null
            ? new EffectRule()
            : new EffectRule
            {
                Kind = current.Kind,
                Color = current.Color,
                DurationMs = current.DurationMs,
                Dip = current.Dip,
                Sprite = current.Sprite,
                Count = current.Count,
                Motion = current.Motion,
                ThrottleMs = current.ThrottleMs,
                Off = current.Off
            };

        var editor = new StackPanel { Margin = new Thickness(12, 6, 0, 8) };
        var preview = new EffectPreview();

        var line1 = new WrapPanel();
        var kind = PickerFor(line1, L.T("Effet", "Effect"), Kinds, draft.Kind, v => draft.Kind = v);
        var colorBox = Ui.TextBox(draft.Color, 80);
        var swatch = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
            Background = SafeBrush(draft.Color), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        colorBox.TextChanged += (_, _) =>
        {
            draft.Color = colorBox.Text.Trim();
            swatch.Background = SafeBrush(draft.Color);
        };
        var colorLabel = Ui.MutedLabel(L.T("Couleur", "Color"));
        colorLabel.Margin = new Thickness(0, 0, 6, 0);
        line1.Children.Add(colorLabel);
        line1.Children.Add(colorBox);
        line1.Children.Add(swatch);
        var durationBox = NumberFor(line1, L.T("Durée (ms)", "Duration (ms)"), draft.DurationMs, v => draft.DurationMs = v);
        editor.Children.Add(line1);

        var line2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var spritePicker = Ui.ComboBox(170);
        spritePicker.Items.Add(new ComboBoxItem { Content = L.T("(aucun sprite)", "(no sprite)"), Tag = "" });
        try
        {
            foreach (var gif in Directory.EnumerateFiles(_spritesDir, "*.gif").OrderBy(f => f))
            {
                var name = Path.GetFileName(gif);
                var item = new ComboBoxItem { Content = name, Tag = name };
                spritePicker.Items.Add(item);
                if (name.Equals(draft.Sprite, StringComparison.OrdinalIgnoreCase)) spritePicker.SelectedItem = item;
            }
        }
        catch
        {
            // sprites folder missing: picker stays empty
        }
        if (spritePicker.SelectedItem == null) spritePicker.SelectedIndex = 0;
        spritePicker.SelectionChanged += (_, _) =>
        {
            var tag = (spritePicker.SelectedItem as ComboBoxItem)?.Tag as string;
            draft.Sprite = string.IsNullOrEmpty(tag) ? null : tag;
        };
        var spriteLabel = Ui.MutedLabel("Sprite");
        spriteLabel.Margin = new Thickness(0, 0, 6, 0);
        line2.Children.Add(spriteLabel);
        line2.Children.Add(spritePicker);
        var countBox = NumberFor(line2, L.T("Nombre", "Count"), draft.Count, v => draft.Count = Math.Clamp(v, 1, 8), 40);
        PickerFor(line2, "Motion", Motions, draft.Motion, v => draft.Motion = v);
        editor.Children.Add(line2);

        var line3 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var dipSlider = new Slider
        {
            Minimum = 0, Maximum = 1, Value = Math.Clamp(draft.Dip, 0, 1),
            Width = 110, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
        };
        dipSlider.ValueChanged += (_, args) => draft.Dip = Math.Round(args.NewValue, 2);
        var dipLabel = Ui.MutedLabel(L.T("Creux lumière", "Light dip"));
        dipLabel.Margin = new Thickness(0, 0, 6, 0);
        line3.Children.Add(dipLabel);
        line3.Children.Add(dipSlider);
        var throttleBox = NumberFor(line3, L.T("Anti-rafale (ms)", "Cooldown (ms)"), draft.ThrottleMs, v => draft.ThrottleMs = v);
        var offBox = Ui.CheckBox(L.T("Désactiver ce signal", "Silence this signal"), draft.Off);
        offBox.Checked += (_, _) => draft.Off = true;
        offBox.Unchecked += (_, _) => draft.Off = false;
        line3.Children.Add(offBox);
        editor.Children.Add(line3);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("▶ Tester l'effet", "▶ Preview the effect"), (_, _) => preview.Play(draft, _spritesDir)));
        actions.Children.Add(Ui.Button(L.T("Enregistrer", "Save"), (_, _) =>
        {
            var rules = LoadScopeRules();
            rules[signal.Action] = draft;
            SaveScopeRules(rules);
            _status.Text = L.T($"{signal.Action} enregistré ({ScopeLabel()}). Le runtime applique au prochain changement de jeu.",
                $"{signal.Action} saved ({ScopeLabel()}). The runtime applies it on the next game change.");
            _status.Foreground = Ui.Ok;
            RebuildRows();
        }, primary: true));
        var scopeRules = LoadScopeRules();
        if (scopeRules.ContainsKey(signal.Action))
        {
            actions.Children.Add(Ui.Button(L.T("Revenir au défaut", "Back to default"), (_, _) =>
            {
                var rules = LoadScopeRules();
                rules.Remove(signal.Action);
                SaveScopeRules(rules);
                _status.Text = L.T($"{signal.Action} : réglage {ScopeLabel()} retiré.", $"{signal.Action}: {ScopeLabel()} tweak removed.");
                _status.Foreground = Ui.Muted;
                RebuildRows();
            }));
        }
        editor.Children.Add(actions);
        editor.Children.Add(preview);
        preview.Margin = new Thickness(0, 8, 0, 0);

        // first look: replay what the signal currently does
        Dispatcher.BeginInvoke(() => preview.Play(draft, _spritesDir), System.Windows.Threading.DispatcherPriority.Background);
        return editor;
    }

    private string ScopeLabel() => _scope switch
    {
        "system" => L.T($"système {_system}", $"{_system} system"),
        var s when s.StartsWith("genre:") => L.T($"genre {s["genre:".Length..]}", $"{s["genre:".Length..]} genre"),
        _ => L.T("ce jeu", "this game")
    };

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
                _expandedAction = action;
                RebuildRows();
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

    private ComboBox PickerFor(Panel host, string label, (string Key, string Fr, string En)[] choices,
        string selected, Action<string> onChange)
    {
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 6, 0);
        host.Children.Add(text);
        var picker = Ui.ComboBox(150);
        foreach (var (key, fr, en) in choices)
        {
            var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
            picker.Items.Add(item);
            if (key.Equals(selected, StringComparison.OrdinalIgnoreCase)) picker.SelectedItem = item;
        }
        if (picker.SelectedItem == null) picker.SelectedIndex = 0;
        picker.SelectionChanged += (_, _) =>
        {
            if ((picker.SelectedItem as ComboBoxItem)?.Tag is string key) onChange(key);
        };
        host.Children.Add(picker);
        return picker;
    }

    private static TextBox NumberFor(Panel host, string label, int value, Action<int> onChange, double width = 60)
    {
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 6, 0);
        host.Children.Add(text);
        var box = Ui.TextBox(value.ToString(), width);
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), out var parsed) && parsed >= 0) onChange(parsed);
        };
        host.Children.Add(box);
        return box;
    }

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
