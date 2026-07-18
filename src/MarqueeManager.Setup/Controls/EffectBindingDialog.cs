using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// "Link an effect to a game signal" — the dedicated binding editor (RetroCreator
/// flows-mode logic): pick the MEM signal, then what it triggers — a simple
/// effect (flash, veil, sprite swarm with size/growth/placement…) or one of the
/// named effects of "Mes effets" — preview it, save. One binding per dialog.
/// </summary>
public sealed class EffectBindingDialog : Window
{
    private static readonly (string Key, string Fr, string En)[] Kinds =
    {
        ("flash", "Flash coloré", "Colored flash"),
        ("tint", "Voile coloré", "Colored veil"),
        ("pulse", "Impulsion", "Pulse"),
        ("shake", "Secousse", "Shake"),
        ("strobe", "Stroboscope", "Strobe"),
        ("sprite", "Nuée de sprites", "Sprite swarm"),
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

    private static readonly (string Key, string Fr, string En)[] Placements =
    {
        ("random", "Au hasard", "Random"),
        ("center", "Au centre", "Centered"),
        ("spread", "Espacés régulièrement", "Evenly spaced")
    };

    private readonly string _pluginRoot;
    private readonly string _spritesDir;
    private readonly IReadOnlyList<MemSignal> _signals;
    private readonly Func<Dictionary<string, EffectRule>> _loadRules;
    private readonly Action<Dictionary<string, EffectRule>> _saveRules;
    private readonly Func<MemSignal, (EffectRule? Rule, EffectOrigin Origin, string Detail)> _resolve;
    private readonly string _scopeLabel;

    private readonly StackPanel _editorHost = new();
    private readonly EffectPreview _preview = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly List<System.Windows.Threading.DispatcherTimer> _previewTimers = new();
    private MemSignal? _signal;
    private EffectRule _draft = new();

    /// <summary>True when a binding was saved or removed — the caller refreshes.</summary>
    public bool Changed { get; private set; }

    public EffectBindingDialog(string pluginRoot, string spritesDir, IReadOnlyList<MemSignal> signals,
        MemSignal? initialSignal,
        Func<Dictionary<string, EffectRule>> loadRules,
        Action<Dictionary<string, EffectRule>> saveRules,
        Func<MemSignal, (EffectRule? Rule, EffectOrigin Origin, string Detail)> resolve,
        string scopeLabel)
    {
        _pluginRoot = pluginRoot;
        _spritesDir = spritesDir;
        _signals = signals;
        _loadRules = loadRules;
        _saveRules = saveRules;
        _resolve = resolve;
        _scopeLabel = scopeLabel;

        Title = L.T("Lier un effet à un signal du jeu", "Link an effect to a game signal");
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var root = new StackPanel { Margin = new Thickness(16) };

        // ===== WHEN: the signal =====
        root.Children.Add(Ui.SectionHeader(L.T("QUAND ce signal tire…", "WHEN this signal fires…")));
        var signalPicker = Ui.ComboBox(420);
        foreach (var signal in signals)
        {
            var item = new ComboBoxItem
            {
                Content = signal.Action + (signal.Description.Length > 0 ? $" — {signal.Description}" : ""),
                Tag = signal
            };
            signalPicker.Items.Add(item);
            if (ReferenceEquals(signal, initialSignal)) signalPicker.SelectedItem = item;
        }
        if (signalPicker.SelectedItem == null && signalPicker.Items.Count > 0) signalPicker.SelectedIndex = 0;
        signalPicker.SelectionChanged += (_, _) =>
        {
            if ((signalPicker.SelectedItem as ComboBoxItem)?.Tag is MemSignal signal) SetSignal(signal);
        };
        root.Children.Add(signalPicker);

        // ===== THEN: the effect =====
        root.Children.Add(Ui.SectionHeader(L.T("ALORS jouer…", "THEN play…")));
        root.Children.Add(_editorHost);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("▶ Tester", "▶ Preview"), (_, _) => PlayDraft()));
        actions.Children.Add(Ui.Button(L.T("Enregistrer ce lien", "Save this binding"), (_, _) => Save(), primary: true));
        actions.Children.Add(Ui.Button(L.T("Revenir au défaut", "Back to default"), (_, _) => RemoveBinding()));
        actions.Children.Add(Ui.Button(L.T("Fermer", "Close"), (_, _) => Close()));
        root.Children.Add(actions);

        _preview.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(_preview);
        _status.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(_status);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closed += (_, _) => StopPreview();

        if ((signalPicker.SelectedItem as ComboBoxItem)?.Tag is MemSignal first) SetSignal(first);
    }

    // ================= editor =================

    private void SetSignal(MemSignal signal)
    {
        _signal = signal;
        var (current, _, detail) = _resolve(signal);
        _draft = current?.Clone() ?? new EffectRule();
        _status.Text = L.T($"Comportement actuel : {detail}.", $"Current behavior: {detail}.");
        _status.Foreground = Ui.Muted;
        RenderEditor();
    }

    private void RenderEditor()
    {
        _editorHost.Children.Clear();
        var draft = _draft;

        // mode
        var modeRow = new WrapPanel();
        var modePicker = Ui.ComboBox(190);
        modePicker.Items.Add(new ComboBoxItem { Content = L.T("Effet simple", "Simple effect"), Tag = "simple" });
        modePicker.Items.Add(new ComboBoxItem { Content = L.T("Un de mes effets", "One of my effects"), Tag = "library" });
        modePicker.SelectedIndex = draft.EffectRef is { Length: > 0 } ? 1 : 0;
        modeRow.Children.Add(modePicker);
        _editorHost.Children.Add(modeRow);

        var simple = new StackPanel();
        var library = new StackPanel();

        // ----- library mode -----
        var namePicker = Ui.ComboBox(280);
        void FillNames()
        {
            namePicker.Items.Clear();
            foreach (var name in new EffectsLibraryStore(_pluginRoot).LoadOrSeed().Keys
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ComboBoxItem { Content = name, Tag = name };
                namePicker.Items.Add(item);
                if (name.Equals(draft.EffectRef, StringComparison.OrdinalIgnoreCase)) namePicker.SelectedItem = item;
            }
            if (namePicker.SelectedItem == null && namePicker.Items.Count > 0) namePicker.SelectedIndex = 0;
            if ((namePicker.SelectedItem as ComboBoxItem)?.Tag is string first) draft.EffectRef ??= first;
        }
        namePicker.SelectionChanged += (_, _) =>
        {
            if ((namePicker.SelectedItem as ComboBoxItem)?.Tag is string name) draft.EffectRef = name;
        };
        var libraryRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        libraryRow.Children.Add(namePicker);
        libraryRow.Children.Add(Ui.Button(L.T("Composer / gérer…", "Compose / manage…"), (_, _) =>
        {
            var composer = new EffectComposerWindow(_pluginRoot, pickMode: true) { Owner = this };
            if (composer.ShowDialog() == true && composer.SelectedEffectName is { } picked) draft.EffectRef = picked;
            FillNames();
        }));
        library.Children.Add(libraryRow);

        // ----- simple mode -----
        var line1 = new WrapPanel();
        PickerFor(line1, L.T("Effet", "Effect"), Kinds, draft.Kind, v =>
        {
            draft.Kind = v;
            RenderEditor(); // sprite params show/hide with the kind
        });
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
        line1.Children.Add(Ui.ColorPalette(colorBox));
        NumberFor(line1, L.T("Durée (ms)", "Duration (ms)"), draft.DurationMs, v => draft.DurationMs = v);
        simple.Children.Add(line1);

        if (draft.Kind == "sprite")
        {
            var line2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var spritePicker = Ui.ComboBox(170);
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
                // sprites folder missing
            }
            if (spritePicker.SelectedItem == null && spritePicker.Items.Count > 0) spritePicker.SelectedIndex = 0;
            if (draft.Sprite == null && (spritePicker.SelectedItem as ComboBoxItem)?.Tag is string firstSprite)
                draft.Sprite = firstSprite;
            spritePicker.SelectionChanged += (_, _) =>
            {
                if ((spritePicker.SelectedItem as ComboBoxItem)?.Tag is string name) draft.Sprite = name;
            };
            var spriteLabel = Ui.MutedLabel("Sprite");
            spriteLabel.Margin = new Thickness(0, 0, 6, 0);
            line2.Children.Add(spriteLabel);
            line2.Children.Add(spritePicker);
            NumberFor(line2, L.T("Nombre", "Count"), draft.Count, v => draft.Count = Math.Clamp(v, 1, 8), 64);
            PickerFor(line2, "Motion", Motions, draft.Motion, v => draft.Motion = v);
            simple.Children.Add(line2);

            // size / growth / placement — the pixel-art knobs
            var line2b = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var scalePicker = Ui.ComboBox(110);
            foreach (var percent in new[] { 100, 150, 200, 300, 400, 500, 750, 1000 })
            {
                var item = new ComboBoxItem { Content = percent + " %", Tag = percent };
                scalePicker.Items.Add(item);
                if (Math.Abs((draft.Scale <= 0 ? 1.0 : draft.Scale) * 100 - percent) < 1) scalePicker.SelectedItem = item;
            }
            if (scalePicker.SelectedItem == null) scalePicker.SelectedIndex = 0;
            scalePicker.SelectionChanged += (_, _) =>
            {
                if ((scalePicker.SelectedItem as ComboBoxItem)?.Tag is int percent)
                    draft.Scale = percent == 100 ? 0 : percent / 100.0; // 0 = default (omitted from JSON)
            };
            var scaleLabel = Ui.MutedLabel(L.T("Taille (pixels nets ≥ 150 %)", "Size (crisp pixels ≥ 150 %)"));
            scaleLabel.Margin = new Thickness(0, 0, 6, 0);
            line2b.Children.Add(scaleLabel);
            line2b.Children.Add(scalePicker);
            var growBox = Ui.CheckBox(L.T("Grossit pendant l'effet", "Grows during the effect"), draft.Grow);
            growBox.Checked += (_, _) => draft.Grow = true;
            growBox.Unchecked += (_, _) => draft.Grow = false;
            line2b.Children.Add(growBox);
            PickerFor(line2b, L.T("Position", "Position"), Placements, draft.Placement ?? "random",
                v => draft.Placement = v == "random" ? null : v);
            simple.Children.Add(line2b);
        }

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
        NumberFor(line3, L.T("Anti-rafale (ms)", "Cooldown (ms)"), draft.ThrottleMs, v => draft.ThrottleMs = v);
        var offBox = Ui.CheckBox(L.T("Désactiver ce signal", "Silence this signal"), draft.Off);
        offBox.Checked += (_, _) => draft.Off = true;
        offBox.Unchecked += (_, _) => draft.Off = false;
        line3.Children.Add(offBox);
        simple.Children.Add(line3);

        void ApplyMode()
        {
            var isLibrary = (modePicker.SelectedItem as ComboBoxItem)?.Tag as string == "library";
            simple.Visibility = isLibrary ? Visibility.Collapsed : Visibility.Visible;
            library.Visibility = isLibrary ? Visibility.Visible : Visibility.Collapsed;
            if (isLibrary) FillNames();
            else draft.EffectRef = null;
        }
        modePicker.SelectionChanged += (_, _) => ApplyMode();
        _editorHost.Children.Add(simple);
        _editorHost.Children.Add(library);
        ApplyMode();
    }

    // ================= actions =================

    private void Save()
    {
        if (_signal == null) return;
        var rules = _loadRules();
        rules[_signal.Action] = _draft;
        _saveRules(rules);
        Changed = true;
        _status.Text = L.T($"✔ {_signal.Action} enregistré ({_scopeLabel}). Le runtime applique au prochain changement de jeu.",
            $"✔ {_signal.Action} saved ({_scopeLabel}). The runtime applies it on the next game change.");
        _status.Foreground = Ui.Ok;
    }

    private void RemoveBinding()
    {
        if (_signal == null) return;
        var rules = _loadRules();
        if (rules.Remove(_signal.Action))
        {
            _saveRules(rules);
            Changed = true;
        }
        SetSignal(_signal);
        _status.Text = L.T($"{_signal.Action} : réglage {_scopeLabel} retiré — le défaut reprend.",
            $"{_signal.Action}: {_scopeLabel} tweak removed — the default takes over.");
        _status.Foreground = Ui.Muted;
    }

    /// <summary>Preview: named effects replay their whole timed stack (media
    /// actions excluded), simple rules play directly.</summary>
    private void PlayDraft()
    {
        StopPreview();
        var actions = _draft.EffectRef is { Length: > 0 }
            ? new EffectsLibraryStore(_pluginRoot).Load().TryGetValue(_draft.EffectRef, out var stack)
                ? stack : new List<EffectRule>()
            : _draft.Actions is { Count: > 0 } ? _draft.Actions : new List<EffectRule> { _draft };
        foreach (var action in actions.Where(a => a.Media is not { Length: > 0 }))
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, action.DelayMs))
            };
            var frozen = action;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _preview.Play(frozen, _spritesDir);
            };
            timer.Start();
            _previewTimers.Add(timer);
        }
    }

    private void StopPreview()
    {
        foreach (var timer in _previewTimers) timer.Stop();
        _previewTimers.Clear();
        _preview.Stop();
    }

    // ================= helpers =================

    private static void PickerFor(Panel host, string label, (string Key, string Fr, string En)[] choices,
        string selected, Action<string> onChange)
    {
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 6, 0);
        host.Children.Add(text);
        var picker = Ui.ComboBox(160);
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
    }

    private static void NumberFor(Panel host, string label, int value, Action<int> onChange, double width = 60)
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
