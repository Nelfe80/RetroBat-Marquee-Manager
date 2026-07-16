using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// "Mes effets" — the effect composer. An effect is a NAMED, reusable stack of
/// sequenced actions: each action has its kind (tint / flash / shake / strobe /
/// sprites / my webm-gif media), its parameters and its start delay. Two actions
/// at delay 0 play together ("red veil + shake + explosions"); staggered delays
/// make a sequence ("red flash THEN a swarm of sprites"). Saved to
/// media\effects\library.json — games then allocate a signal to an effect name.
/// </summary>
public sealed class EffectComposerWindow : Window
{
    private static readonly (string Key, string Fr, string En)[] Kinds =
    {
        ("tint", "Voile coloré", "Colored veil"),
        ("flash", "Flash", "Flash"),
        ("pulse", "Impulsion", "Pulse"),
        ("shake", "Secousse", "Shake"),
        ("strobe", "Stroboscope", "Strobe"),
        ("sprite", "Nuée de sprites", "Sprite swarm"),
        ("media", "Mon média (webm/gif)", "My media (webm/gif)"),
        ("blackout", "Extinction", "Blackout"),
        ("powercycle", "Rallumage", "Power cycle")
    };

    private static readonly (string Key, string Fr, string En)[] Motions =
    {
        ("pop", "Apparition", "Pop"),
        ("fall", "Chute", "Fall"),
        ("rise", "Montée", "Rise"),
        ("cross", "Traversée", "Cross")
    };

    private readonly EffectsLibraryStore _store;
    private readonly Dictionary<string, List<EffectRule>> _effects;
    private readonly string _spritesDir;
    private readonly string _userMediaDir;

    private readonly ListBox _names = new() { MinWidth = 210, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _actionsPanel = new();
    private readonly EffectPreview _preview = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly List<DispatcherTimer> _previewTimers = new();
    private string? _current;

    /// <summary>Set when the caller wants to know which effect to allocate.</summary>
    public string? SelectedEffectName { get; private set; }

    public EffectComposerWindow(string pluginRoot, bool pickMode = false)
    {
        _store = new EffectsLibraryStore(pluginRoot);
        _effects = _store.LoadOrSeed();
        _spritesDir = Path.Combine(pluginRoot, "resources", "sprites");
        _userMediaDir = _store.UserMediaRoot;

        Title = L.T("Mes effets", "My effects");
        Width = 1050;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ===== left: effect list =====
        var left = new DockPanel();
        var leftHeader = Ui.SectionHeader(L.T("Bibliothèque", "Library"));
        DockPanel.SetDock(leftHeader, Dock.Top);
        left.Children.Add(leftHeader);
        var leftButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        leftButtons.Children.Add(Ui.Button(L.T("Nouvel effet", "New effect"), (_, _) => NewEffect(), primary: true));
        leftButtons.Children.Add(Ui.Button(L.T("Dupliquer", "Duplicate"), (_, _) => DuplicateEffect()));
        leftButtons.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) => DeleteEffect()));
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        _names.SelectionChanged += (_, _) =>
        {
            if (_names.SelectedItem is ListBoxItem { Tag: string name })
            {
                _current = name;
                RenderActions();
            }
        };
        left.Children.Add(_names);
        root.Children.Add(new Border
        {
            Background = Ui.Panel, BorderBrush = Ui.PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 10, 0),
            Child = left
        });

        // ===== right: sequence editor =====
        var right = new DockPanel();
        var bottom = new StackPanel();
        var bottomButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        bottomButtons.Children.Add(Ui.Button(L.T("▶ Prévisualiser la séquence", "▶ Preview the sequence"), (_, _) => PlaySequence()));
        bottomButtons.Children.Add(Ui.Button(L.T("Enregistrer la bibliothèque", "Save the library"), (_, _) => SaveLibrary(), primary: true));
        if (pickMode)
        {
            bottomButtons.Children.Add(Ui.Button(L.T("Utiliser cet effet", "Use this effect"), (_, _) =>
            {
                SaveLibrary();
                SelectedEffectName = _current;
                DialogResult = true;
            }, primary: true));
        }
        bottom.Children.Add(bottomButtons);
        _preview.Margin = new Thickness(0, 8, 0, 0);
        bottom.Children.Add(_preview);
        _status.TextWrapping = TextWrapping.Wrap;
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        right.Children.Add(bottom);

        var hint = Ui.MutedLabel(L.T(
            "Délai 0 = actions simultanées ; délais échelonnés = séquence. Déposez vos webm/gif/apng dans media\\effects\\user\\.",
            "Delay 0 = simultaneous actions; staggered delays = sequence. Drop your webm/gif/apng files in media\\effects\\user\\."));
        DockPanel.SetDock(hint, Dock.Top);
        right.Children.Add(hint);
        right.Children.Add(new ScrollViewer
        {
            Content = _actionsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 6, 0, 0)
        });
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        Content = root;
        Closed += (_, _) => StopPreview();
        RefreshNames();
        if (_names.Items.Count > 0) _names.SelectedIndex = 0;
    }

    // ================= library list =================

    private void RefreshNames()
    {
        _names.Items.Clear();
        foreach (var name in _effects.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListBoxItem { Content = name, Tag = name };
            _names.Items.Add(item);
            if (name.Equals(_current, StringComparison.OrdinalIgnoreCase)) _names.SelectedItem = item;
        }
    }

    private void NewEffect()
    {
        var name = UniqueName(L.T("Nouvel effet", "New effect"));
        _effects[name] = new List<EffectRule> { new() { Kind = "flash" } };
        _current = name;
        RefreshNames();
        RenderActions();
    }

    private void DuplicateEffect()
    {
        if (_current == null || !_effects.TryGetValue(_current, out var actions)) return;
        var name = UniqueName(_current);
        _effects[name] = actions.Select(a => a.Clone()).ToList();
        _current = name;
        RefreshNames();
        RenderActions();
    }

    private void DeleteEffect()
    {
        if (_current == null) return;
        if (MessageBox.Show(L.T($"Supprimer « {_current} » ?", $"Delete “{_current}”?"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _effects.Remove(_current);
        _current = _effects.Keys.FirstOrDefault();
        RefreshNames();
        RenderActions();
    }

    private string UniqueName(string stem)
    {
        var name = stem;
        var n = 2;
        while (_effects.ContainsKey(name)) name = $"{stem} ({n++})";
        return name;
    }

    // ================= sequence editor =================

    private void RenderActions()
    {
        _actionsPanel.Children.Clear();
        if (_current == null || !_effects.TryGetValue(_current, out var actions)) return;

        // rename
        var nameRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var nameLabel = Ui.MutedLabel(L.T("Nom :", "Name:"));
        nameLabel.Margin = new Thickness(0, 0, 6, 0);
        nameRow.Children.Add(nameLabel);
        var nameBox = Ui.TextBox(_current, 260);
        nameRow.Children.Add(nameBox);
        nameRow.Children.Add(Ui.Button(L.T("Renommer", "Rename"), (_, _) =>
        {
            var newName = nameBox.Text.Trim();
            if (newName.Length == 0 || newName == _current || _effects.ContainsKey(newName)) return;
            _effects.Remove(_current!);
            _effects[newName] = actions;
            _current = newName;
            RefreshNames();
            RenderActions();
        }));
        _actionsPanel.Children.Add(nameRow);

        for (var i = 0; i < actions.Count; i++)
        {
            _actionsPanel.Children.Add(BuildActionRow(actions, i));
        }

        var add = Ui.Button(L.T("+ Ajouter une action", "+ Add an action"), (_, _) =>
        {
            var lastDelay = actions.Count > 0 ? actions[^1].DelayMs : 0;
            actions.Add(new EffectRule { Kind = "sprite", Sprite = "star.gif", DelayMs = lastDelay });
            RenderActions();
        });
        add.Margin = new Thickness(0, 6, 0, 0);
        _actionsPanel.Children.Add(add);
    }

    private Border BuildActionRow(List<EffectRule> actions, int index)
    {
        var action = actions[index];
        var body = new StackPanel();

        var line1 = new WrapPanel();
        var displayKind = action.Media is { Length: > 0 } ? "media" : action.Kind;
        ComboBox? mediaPicker = null;
        ComboBox? spritePicker = null;

        var kindPicker = PickerFor(line1, L.T("Action", "Action"), Kinds, displayKind, kind =>
        {
            if (kind == "media")
            {
                action.Kind = "sprite"; // the runtime rides media on the overlay pipeline
                action.Media ??= "";
            }
            else
            {
                action.Kind = kind;
                action.Media = null;
                action.Fullscreen = false;
            }
            RenderActions();
        });

        var delayBox = NumberFor(line1, L.T("Départ (ms)", "Start (ms)"), action.DelayMs, v => action.DelayMs = v);
        var durationBox = NumberFor(line1, L.T("Durée (ms)", "Duration (ms)"), action.DurationMs, v => action.DurationMs = v);
        body.Children.Add(line1);

        var line2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (displayKind == "media")
        {
            mediaPicker = Ui.ComboBox(220);
            var files = _store.ListUserMedia();
            if (files.Count == 0)
            {
                mediaPicker.Items.Add(new ComboBoxItem
                {
                    Content = L.T("(déposez des fichiers dans media\\effects\\user)", "(drop files in media\\effects\\user)"),
                    Tag = ""
                });
            }
            foreach (var file in files)
            {
                var item = new ComboBoxItem { Content = file, Tag = file };
                mediaPicker.Items.Add(item);
                if (file.Equals(action.Media, StringComparison.OrdinalIgnoreCase)) mediaPicker.SelectedItem = item;
            }
            if (mediaPicker.SelectedItem == null) mediaPicker.SelectedIndex = 0;
            mediaPicker.SelectionChanged += (_, _) =>
            {
                if ((mediaPicker.SelectedItem as ComboBoxItem)?.Tag is string file && file.Length > 0)
                    action.Media = file;
            };
            var mediaLabel = Ui.MutedLabel(L.T("Fichier", "File"));
            mediaLabel.Margin = new Thickness(0, 0, 6, 0);
            line2.Children.Add(mediaLabel);
            line2.Children.Add(mediaPicker);
            var fullscreenBox = Ui.CheckBox(L.T("Plein écran (remplace le marquee)", "Fullscreen (replaces the marquee)"), action.Fullscreen);
            fullscreenBox.Checked += (_, _) => action.Fullscreen = true;
            fullscreenBox.Unchecked += (_, _) => action.Fullscreen = false;
            line2.Children.Add(fullscreenBox);
        }
        else
        {
            if (displayKind is "tint" or "flash" or "pulse" or "strobe")
            {
                var colorBox = Ui.TextBox(action.Color, 80);
                var swatch = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
                    Background = SafeBrush(action.Color), VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                colorBox.TextChanged += (_, _) =>
                {
                    action.Color = colorBox.Text.Trim();
                    swatch.Background = SafeBrush(action.Color);
                };
                var colorLabel = Ui.MutedLabel(L.T("Couleur", "Color"));
                colorLabel.Margin = new Thickness(0, 0, 6, 0);
                line2.Children.Add(colorLabel);
                line2.Children.Add(colorBox);
                line2.Children.Add(swatch);

                var dipSlider = new Slider
                {
                    Minimum = 0, Maximum = 1, Value = Math.Clamp(action.Dip, 0, 1),
                    Width = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
                };
                dipSlider.ValueChanged += (_, args) => action.Dip = Math.Round(args.NewValue, 2);
                var dipLabel = Ui.MutedLabel(L.T("Intensité/creux", "Intensity/dip"));
                dipLabel.Margin = new Thickness(0, 0, 6, 0);
                line2.Children.Add(dipLabel);
                line2.Children.Add(dipSlider);
            }
            if (displayKind == "sprite")
            {
                spritePicker = Ui.ComboBox(160);
                try
                {
                    foreach (var gif in Directory.EnumerateFiles(_spritesDir, "*.gif").OrderBy(f => f))
                    {
                        var name = Path.GetFileName(gif);
                        var item = new ComboBoxItem { Content = name, Tag = name };
                        spritePicker.Items.Add(item);
                        if (name.Equals(action.Sprite, StringComparison.OrdinalIgnoreCase)) spritePicker.SelectedItem = item;
                    }
                }
                catch
                {
                    // sprites folder missing
                }
                if (spritePicker.SelectedItem == null && spritePicker.Items.Count > 0) spritePicker.SelectedIndex = 0;
                spritePicker.SelectionChanged += (_, _) =>
                {
                    if ((spritePicker.SelectedItem as ComboBoxItem)?.Tag is string name) action.Sprite = name;
                };
                var spriteLabel = Ui.MutedLabel("Sprite");
                spriteLabel.Margin = new Thickness(0, 0, 6, 0);
                line2.Children.Add(spriteLabel);
                line2.Children.Add(spritePicker);
                NumberFor(line2, L.T("Nombre", "Count"), action.Count, v => action.Count = Math.Clamp(v, 1, 8), 40);
                PickerFor(line2, "Motion", Motions, action.Motion, v => action.Motion = v);
            }
        }
        if (line2.Children.Count > 0) body.Children.Add(line2);

        var tools = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (index > 0) tools.Children.Add(Ui.Button("↑", (_, _) =>
        {
            (actions[index - 1], actions[index]) = (actions[index], actions[index - 1]);
            RenderActions();
        }));
        if (index < actions.Count - 1) tools.Children.Add(Ui.Button("↓", (_, _) =>
        {
            (actions[index + 1], actions[index]) = (actions[index], actions[index + 1]);
            RenderActions();
        }));
        tools.Children.Add(Ui.Button(L.T("Retirer", "Remove"), (_, _) =>
        {
            actions.RemoveAt(index);
            RenderActions();
        }));
        body.Children.Add(tools);

        return new Border
        {
            Background = Ui.Brush(Color.FromRgb(0x16, 0x16, 0x20)),
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 3, 0, 3),
            Child = body
        };
    }

    // ================= preview: replays the whole timed stack =================

    private void PlaySequence()
    {
        StopPreview();
        if (_current == null || !_effects.TryGetValue(_current, out var actions) || actions.Count == 0) return;

        var mediaCount = 0;
        foreach (var action in actions)
        {
            if (action.Media is { Length: > 0 })
            {
                mediaCount++;
                continue; // the preview band cannot decode webm — noted below
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, action.DelayMs)) };
            var frozen = action;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _preview.Play(frozen, _spritesDir);
            };
            timer.Start();
            _previewTimers.Add(timer);
        }
        _status.Text = mediaCount > 0
            ? L.T($"Séquence rejouée ({mediaCount} action(s) média jouée(s) uniquement sur le marquee réel).",
                $"Sequence replayed ({mediaCount} media action(s) only play on the real marquee).")
            : L.T("Séquence rejouée.", "Sequence replayed.");
        _status.Foreground = Ui.Muted;
    }

    private void StopPreview()
    {
        foreach (var timer in _previewTimers) timer.Stop();
        _previewTimers.Clear();
        _preview.Stop();
    }

    private void SaveLibrary()
    {
        _store.Save(_effects);
        _status.Text = L.T($"Bibliothèque enregistrée ({_effects.Count} effet(s)) — {_store.LibraryPath}",
            $"Library saved ({_effects.Count} effect(s)) — {_store.LibraryPath}");
        _status.Foreground = Ui.Ok;
    }

    // ================= helpers =================

    private static ComboBox PickerFor(Panel host, string label, (string Key, string Fr, string En)[] choices,
        string selected, Action<string> onChange)
    {
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 6, 0);
        host.Children.Add(text);
        var picker = Ui.ComboBox(170);
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
