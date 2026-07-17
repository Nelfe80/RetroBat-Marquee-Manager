using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Per-system source priorities, per CATEGORY (marquee / topper / dmd): ordered
/// chain of sources the runtime walks (first existing wins). Owns the user drop
/// folders (drag & drop straight onto the card, alias-resolved names, sidecar
/// reindex) and the batch pre-generation of templated compositions so ES
/// navigation never waits on a render.
/// </summary>
public sealed class PrioritiesCard : UserControl
{
    private static readonly (string Key, string Fr, string En)[] SourceLabels =
    {
        ("composition", "Composition manuelle", "Manual composition"),
        ("user", "Mon dossier", "My folder"),
        ("marquee", "Marquee scrapé", "Scraped marquee"),
        ("screenmarquee", "Screen-marquee", "Screen-marquee"),
        ("generated", "Généré (APIExpose)", "Generated (APIExpose)"),
        ("logo", "Logo (wheel)", "Logo (wheel)"),
        ("fanart", "Fanart", "Fanart"),
        ("topper", "Topper scrapé", "Scraped topper"),
        ("animations", "Animations dmd*.gif", "dmd*.gif animations"),
        ("still", "dmd.png", "dmd.png")
    };

    private readonly string _pluginRoot;
    private readonly CompositionAssignments _assignments;
    private readonly GameMediaCatalog _media;
    private readonly GameIdentityIndex _identity;

    private readonly ComboBox _category = Ui.ComboBox(150);
    private readonly ComboBox _scope = Ui.ComboBox(190);
    private readonly StackPanel _chainPanel = new();
    private readonly TextBlock _coverage = Ui.MutedLabel("");
    private readonly StackPanel _testPanel = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private List<string> _chain = new();

    public PrioritiesCard(string pluginRoot, GameMediaCatalog media, GameIdentityIndex identity)
    {
        _pluginRoot = pluginRoot;
        _assignments = new CompositionAssignments(pluginRoot);
        _media = media;
        _identity = identity;

        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Priorités par système", "Per-system priorities")));
        card.Children.Add(Ui.MutedLabel(L.T(
            "Pour chaque catégorie puis chaque système, l'ordre des sources : la première disponible s'affiche. "
            + "Déposez des fichiers (glisser-déposer ici) dans « Mon dossier » — noms résolus par alias.",
            "For each category then each system, the source order: the first available one is displayed. "
            + "Drop files here (drag & drop) into “My folder” — names resolve through aliases.")));

        var pickers = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (key, fr, en) in new[]
                 {
                     ("marquee", "Marquee", "Marquee"), ("topper", "Topper", "Topper"), ("dmd", "DMD", "DMD")
                 })
        {
            _category.Items.Add(new ComboBoxItem { Content = L.T(fr, en), Tag = key });
        }
        _category.SelectedIndex = 0;
        _category.SelectionChanged += (_, _) => ReloadChain();
        pickers.Children.Add(_category);

        _scope.Items.Add(new ComboBoxItem { Content = L.T("Global (tous systèmes)", "Global (all systems)"), Tag = "" });
        foreach (var system in media.ListSystems())
            _scope.Items.Add(new ComboBoxItem { Content = system, Tag = system });
        _scope.SelectedIndex = 0;
        _scope.SelectionChanged += (_, _) => ReloadChain();
        pickers.Children.Add(_scope);
        card.Children.Add(pickers);

        card.Children.Add(_coverage);
        card.Children.Add(_chainPanel);

        // ---- grouped actions: Chaîne / Mon dossier / Pré-génération ----
        static StackPanel Group(Panel host, string title)
        {
            var header = Ui.MutedLabel(title, 10);
            header.FontWeight = FontWeights.Bold;
            header.Margin = new Thickness(0, 10, 0, 2);
            host.Children.Add(header);
            var body = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            host.Children.Add(body);
            return body;
        }

        // -- CHAÎNE : modifier, valider, vérifier --
        var chainGroup = Group(card, L.T("CHAÎNE", "CHAIN"));
        var addRow = new WrapPanel();
        var addPicker = Ui.ComboBox(230);
        addRow.Children.Add(addPicker);
        addRow.Children.Add(Ui.Button(L.T("Ajouter la source", "Add source"), (_, _) =>
        {
            if ((addPicker.SelectedItem as ComboBoxItem)?.Tag is string source && !_chain.Contains(source))
            {
                _chain.Add(source);
                RenderChain();
            }
        }));
        addRow.Children.Add(Ui.Button(L.T("Revenir au défaut", "Back to default"), (_, _) =>
        {
            _assignments.SetChain(Category(), ScopeSystem(), null);
            _assignments.Save();
            ReloadChain();
        }));
        chainGroup.Children.Add(addRow);
        var chainActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        chainActions.Children.Add(Ui.Button(L.T("Enregistrer les priorités", "Save priorities"), (_, _) =>
        {
            _assignments.SetChain(Category(), ScopeSystem(), _chain);
            _assignments.Save();
            _status.Text = L.T("Priorités enregistrées — appliquées à la prochaine sélection.",
                "Priorities saved — applied at the next selection.");
            _status.Foreground = Ui.Ok;
            RefreshCoverage();
        }, primary: true));
        chainActions.Children.Add(Ui.Button(L.T("Tester la chaîne", "Test the chain"), (_, _) => _ = TestChainAsync()));
        chainGroup.Children.Add(chainActions);
        // the test log lives INSIDE the chain block, right under its buttons
        chainGroup.Children.Add(_testPanel);
        _category.SelectionChanged += (_, _) => FillAddPicker(addPicker);
        FillAddPicker(addPicker);

        // -- MON DOSSIER : les médias personnels du système --
        var folderGroup = Group(card, L.T("MON DOSSIER", "MY FOLDER"));
        var notice = Ui.MutedLabel(L.T(
            "Déposez ici vos médias (images PNG/JPG ou vidéos MP4) : un fichier par jeu, nommé comme la rom "
            + "(« mslug.png »), comme le titre du jeu (« Metal Slug (World).png ») ou n'importe quel alias — "
            + "le nom est résolu automatiquement. Ils passent devant les autres sources dès que « Mon dossier » est dans la chaîne.",
            "Drop your media here (PNG/JPG images or MP4 videos): one file per game, named after the rom "
            + "(“mslug.png”), the game title (“Metal Slug (World).png”) or any alias — "
            + "the name resolves automatically. They outrank other sources as soon as “My folder” is in the chain."));
        notice.TextWrapping = TextWrapping.Wrap;
        folderGroup.Children.Add(notice);
        var folderRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        folderRow.Children.Add(Ui.Button(L.T("Ouvrir mon dossier", "Open my folder"), (_, _) => OpenUserFolder()));
        folderRow.Children.Add(Ui.Button(L.T("Réindexer (alias)", "Reindex (aliases)"), (_, _) => _ = ReindexAsync()));
        folderRow.Children.Add(Ui.MutedLabel(L.T("(ou glissez-déposez vos fichiers directement sur cette carte)",
            "(or drag & drop your files straight onto this card)")));
        folderGroup.Children.Add(folderRow);

        // -- PRÉ-GÉNÉRATION : les templates rendus d'avance --
        var pregenGroup = Group(card, L.T("PRÉ-GÉNÉRATION DES TEMPLATES", "TEMPLATE PRE-GENERATION"));
        var pregenNotice = Ui.MutedLabel(L.T(
            "Un « Template » est une composition automatique (fanart + gradient + logo) rendue pour CHAQUE jeu du système. "
            + "Pour l'utiliser, ajoutez « Template … » dans la chaîne ci-dessus (le gabarit fixe le format : 1920×360, 1280×400, 920×360 ou vertical). "
            + "Normalement le rendu se fait au premier affichage ; pré-générer rend TOUT d'avance pour une navigation ES instantanée.",
            "A “Template” is an automatic composition (fanart + gradient + logo) rendered for EVERY game of the system. "
            + "To use it, add “Template …” to the chain above (the recipe sets the format: 1920×360, 1280×400, 920×360 or vertical). "
            + "Rendering normally happens on first display; pre-generating renders EVERYTHING ahead for instant ES navigation."));
        pregenNotice.TextWrapping = TextWrapping.Wrap;
        pregenGroup.Children.Add(pregenNotice);
        var pregen = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        pregen.Children.Add(Ui.Button(L.T("Pré-générer ce système", "Pre-generate this system"), (_, _) => _ = PregenerateAsync(ScopeSystem())));
        pregen.Children.Add(Ui.Button(L.T("Pré-générer tous les systèmes affectés", "Pre-generate every assigned system"), (_, _) => _ = PregenerateAsync(null)));
        pregenGroup.Children.Add(pregen);

        _status.TextWrapping = TextWrapping.Wrap;
        card.Children.Add(_status);
        Content = card;

        AllowDrop = true;
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };

        ReloadChain();
    }

    private string Category() => (_category.SelectedItem as ComboBoxItem)?.Tag as string ?? "marquee";
    private string? ScopeSystem()
    {
        var tag = (_scope.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return tag.Length == 0 ? null : tag;
    }

    private void FillAddPicker(ComboBox picker)
    {
        picker.Items.Clear();
        foreach (var source in CompositionAssignments.SourcesFor(Category()))
            picker.Items.Add(new ComboBoxItem { Content = LabelOf(source), Tag = source });
        if (Category() is "marquee" or "dmd")
        {
            foreach (var template in new[] { "h-1920x360", "h-1280x400", "h-920x360", "v-1080x1920" })
                picker.Items.Add(new ComboBoxItem { Content = L.T($"Template {template}", $"Template {template}"), Tag = "template:" + template });
        }
        picker.SelectedIndex = 0;
    }

    private void ReloadChain()
    {
        _chain = _assignments.ChainFor(Category(), ScopeSystem());
        RenderChain();
        RefreshCoverage();
        _testPanel.Children.Clear();
    }

    private void RenderChain()
    {
        _chainPanel.Children.Clear();
        var explicitChain = _assignments.HasExplicitChain(Category(), ScopeSystem());
        var origin = Ui.MutedLabel(explicitChain
            ? L.T("chaîne personnalisée :", "custom chain:")
            : L.T("chaîne héritée (défaut) :", "inherited chain (default):"));
        _chainPanel.Children.Add(origin);

        for (var i = 0; i < _chain.Count; i++)
        {
            var index = i;
            var source = _chain[i];

            // fixed-width columns: every badge and every button lines up
            var row = new Grid { Margin = new Thickness(8, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Background = Ui.Brush(Color.FromRgb(0x2E, 0x2E, 0x44)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = $"{index + 1}.  {LabelOf(source)}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Ui.Foreground,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            row.Children.Add(badge);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var up = Ui.Button("↑", (_, _) => Move(index, -1));
            up.Padding = new Thickness(8, 3, 8, 3);
            buttons.Children.Add(up);
            var down = Ui.Button("↓", (_, _) => Move(index, +1));
            down.Padding = new Thickness(8, 3, 8, 3);
            buttons.Children.Add(down);
            var remove = Ui.Button("✕", (_, _) =>
            {
                _chain.RemoveAt(index);
                RenderChain();
            });
            remove.Padding = new Thickness(8, 3, 8, 3);
            buttons.Children.Add(remove);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);

            _chainPanel.Children.Add(row);
        }
    }

    private void Move(int index, int direction)
    {
        var target = index + direction;
        if (target < 0 || target >= _chain.Count) return;
        (_chain[index], _chain[target]) = (_chain[target], _chain[index]);
        RenderChain();
    }

    private void RefreshCoverage()
    {
        var system = ScopeSystem();
        if (system == null)
        {
            _coverage.Text = "";
            return;
        }
        var (user, compositions) = _assignments.Coverage(Category(), system);
        _coverage.Text = L.T(
            $"Couverture {system} : mon dossier {user} fichier(s) · compositions {compositions}",
            $"{system} coverage: my folder {user} file(s) · compositions {compositions}");
    }

    // ================= folder actions =================

    private void OpenUserFolder()
    {
        var system = ScopeSystem();
        if (system == null)
        {
            _status.Text = L.T("Choisissez d'abord un système.", "Pick a system first.");
            _status.Foreground = Ui.Error;
            return;
        }
        var folder = _assignments.UserFolder(Category(), system);
        Directory.CreateDirectory(folder);
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // explorer unavailable
        }
    }

    private async Task ReindexAsync()
    {
        var system = ScopeSystem();
        if (system == null) return;
        _status.Text = L.T("Réindexation…", "Reindexing…");
        _status.Foreground = Ui.Muted;
        var category = Category();
        var count = await Task.Run(() => _assignments.ReindexUserFolder(category, system, _identity));
        _status.Text = L.T($"{count} fichier(s) indexé(s) (alias résolus, sidecar .index.json).",
            $"{count} file(s) indexed (aliases resolved, .index.json sidecar).");
        _status.Foreground = Ui.Ok;
        RefreshCoverage();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var system = ScopeSystem();
        if (system == null)
        {
            _status.Text = L.T("Choisissez un système avant de déposer des fichiers.", "Pick a system before dropping files.");
            _status.Foreground = Ui.Error;
            return;
        }
        var category = Category();
        _ = Task.Run(() =>
        {
            var imported = new List<string>();
            foreach (var file in files.Where(File.Exists))
            {
                try
                {
                    var rom = _assignments.ImportUserFile(category, system, file, _identity);
                    if (rom != null) imported.Add($"{Path.GetFileName(file)} → {rom}");
                }
                catch
                {
                    // unreadable file: skipped
                }
            }
            Dispatcher.BeginInvoke(() =>
            {
                _status.Text = imported.Count > 0
                    ? L.T("Importé : ", "Imported: ") + string.Join(" · ", imported)
                    : L.T("Aucun fichier importé.", "No file imported.");
                _status.Foreground = imported.Count > 0 ? Ui.Ok : Ui.Error;
                RefreshCoverage();
            });
        });
    }

    // ================= chain test (badge « source affichée ») =================

    private async Task TestChainAsync()
    {
        // "Global" tests the default chain on a cross-system sample — no more
        // "pick a system" dead end; results (and errors) land right under the button
        var system = ScopeSystem();
        var category = Category();
        var chain = _chain.ToList();
        _testPanel.Children.Clear();
        _testPanel.Children.Add(Ui.Spinner(L.T("Résolution d'un échantillon…", "Resolving a sample…")));

        var rows = await Task.Run(() =>
        {
            var games = _media.ListGames()
                .Where(g => system == null || g.System.Equals(system, StringComparison.OrdinalIgnoreCase))
                .Take(system == null ? 2000 : 400).ToList();
            var random = new Random();
            return games.OrderBy(_ => random.Next()).Take(5)
                .Select(g => (g.Rom, g.System, Source: ResolveBadge(category, chain, g.System, g.Rom)))
                .ToList();
        });

        _testPanel.Children.Clear();
        if (rows.Count == 0)
        {
            _testPanel.Children.Add(Ui.MutedLabel(L.T("Aucun jeu à tester dans ce périmètre.", "No game to test in this scope.")));
            return;
        }
        foreach (var (rom, gameSystem, source) in rows)
        {
            _testPanel.Children.Add(Ui.MutedLabel(
                $"• {rom}{(system == null ? $" ({gameSystem})" : "")} → "
                + L.T("source affichée : ", "displayed source: ") + source));
        }
    }

    /// <summary>Setup-side mirror of the runtime chain walk, on the media library
    /// files — enough to show WHICH source would win, without launching ES.</summary>
    private string ResolveBadge(string category, List<string> chain, string system, string rom)
    {
        foreach (var source in chain)
        {
            var exists = source switch
            {
                "composition" => File.Exists(_assignments.CompositionPath(category, system, rom)),
                "user" => UserFileExists(category, system, rom),
                _ when source.StartsWith("template:") => File.Exists(Path.Combine(_pluginRoot, "media", category + "s",
                    ".cache", Safe(system), Safe(rom) + "-" + Safe(source["template:".Length..]) + ".png")),
                "marquee" => LibraryFile(system, rom, @"artwork\marquee\marquee.png", @"artwork\marquee\marquee.jpg") != null,
                "screenmarquee" => LibraryFile(system, rom, @"artwork\marquee\screenmarquee.png") != null,
                "generated" => LibraryFile(system, rom, @"artwork\marquee\generated-marquee.png", @"artwork\marquee\generated-dmd.png") != null,
                "logo" => LibraryFile(system, rom, @"ui\wheels\wheel.png") != null,
                "fanart" => LibraryFile(system, rom, @"artwork\fanart.jpg", @"artwork\fanart.png") != null,
                "topper" => LibraryFile(system, rom, @"artwork\marquee\topper.png") != null,
                "animations" => Directory.Exists(Path.Combine(_media.GameRoot(system, rom), "artwork", "marquee"))
                                && Directory.EnumerateFiles(Path.Combine(_media.GameRoot(system, rom), "artwork", "marquee"), "dmd*.gif").Any(),
                "still" => LibraryFile(system, rom, @"artwork\marquee\dmd.png") != null,
                _ => false
            };
            if (exists) return LabelOf(source);
        }
        return L.T("rien (flux d'origine)", "nothing (stream default)");
    }

    private bool UserFileExists(string category, string system, string rom)
    {
        var folder = _assignments.UserFolder(category, system);
        if (!Directory.Exists(folder)) return false;
        if (Directory.EnumerateFiles(folder, Safe(rom) + ".*").Any()) return true;
        var sidecar = Path.Combine(folder, ".index.json");
        if (!File.Exists(sidecar)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sidecar));
            return doc.RootElement.EnumerateObject().Any(p =>
                p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(p.Value.GetString(), rom, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(folder, p.Name)));
        }
        catch
        {
            return false;
        }
    }

    private string? LibraryFile(string system, string rom, params string[] relatives)
    {
        foreach (var relative in relatives)
        {
            var path = Path.Combine(_media.GameRoot(system, rom), relative);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    // ================= batch pre-generation =================

    private async Task PregenerateAsync(string? system)
    {
        var exe = Path.Combine(_pluginRoot, "MarqueeManager.exe");
        if (!File.Exists(exe))
        {
            _status.Text = L.T("MarqueeManager.exe introuvable à la racine du plugin.", "MarqueeManager.exe not found at the plugin root.");
            _status.Foreground = Ui.Error;
            return;
        }

        _status.Text = L.T("Pré-génération en cours…", "Pre-generating…");
        _status.Foreground = Ui.Muted;
        var argument = system ?? "all";
        var result = await Task.Run(() =>
        {
            try
            {
                var info = new ProcessStartInfo(exe, $"--render-templates {argument}")
                {
                    WorkingDirectory = _pluginRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(info)!;
                string? line;
                var last = "";
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    last = line;
                    if (line.StartsWith("PROGRESS") || line.StartsWith("TOTAL"))
                    {
                        var display = line;
                        Dispatcher.BeginInvoke(() => _status.Text = L.T("Pré-génération : ", "Pre-generating: ") + display);
                    }
                }
                process.WaitForExit(600000);
                return last;
            }
            catch (Exception ex)
            {
                return "ERROR " + ex.Message;
            }
        });

        if (result.StartsWith("DONE"))
        {
            var parts = result.Split(' ');
            _status.Text = L.T(
                $"Pré-génération terminée : {parts.ElementAtOrDefault(1)} rendue(s), {parts.ElementAtOrDefault(3)} erreur(s). L'affichage ES est instantané.",
                $"Pre-generation finished: {parts.ElementAtOrDefault(1)} rendered, {parts.ElementAtOrDefault(3)} error(s). ES display is instant.");
            _status.Foreground = Ui.Ok;
        }
        else
        {
            _status.Text = L.T("Pré-génération : ", "Pre-generation: ") + result;
            _status.Foreground = result.StartsWith("ERROR") ? Ui.Error : Ui.Muted;
        }
    }

    private static string LabelOf(string source)
        => source.StartsWith("template:")
            ? "Template " + source["template:".Length..]
            : SourceLabels.FirstOrDefault(s => s.Key == source) is { Key.Length: > 0 } match
                ? L.T(match.Fr, match.En)
                : source;

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
