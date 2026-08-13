using MarqueeManager.Compositions.Core.Composition;
using System.Windows.Controls;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Per-SYSTEM settings, LedManager style: composition templates, source priority
/// chains, user drop folder, coverage and bulk pre-generation. Extracted from
/// the former "Mes composants" library section.
/// </summary>
public sealed class MesSystemesView : UserControl
{
    public MesSystemesView(string pluginRoot)
    {
        var media = new GameMediaCatalog(pluginRoot);

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Mes systèmes", "My systems")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Par système : quelle source s'affiche (clique une carte), le gabarit général, votre dossier de médias, et la pré-génération de masse.",
            "Per system: which source shows (click a card), the general template, your media folder, and bulk pre-generation.")));

        if (!media.IsAvailable)
        {
            page.Children.Add(Ui.Card(Ui.Label(L.T(
                "La bibliothèque média d'APIExpose est introuvable (plugins\\APIExpose\\media).",
                "The APIExpose media library was not found (plugins\\APIExpose\\media)."))));
            Content = Ui.Page(page);
            return;
        }

        // shared resolution engine (lot 1 domain) fed by read-only Setup adapters:
        // the preview below shows exactly what the runtime would resolve.
        var assignments = new CompositionAssignments(pluginRoot);
        var detectedScreens = MarqueeManager.Setup.Detection.ScreenProbe.Detect();
        var engine = new MediaResolutionPreview(pluginRoot, media, assignments);

        // "Mon marquee" — same block as the game sheet: pick the SYSTEM, then
        // the displayed marquee, the surface picker, the creation entry point
        // and the per-surface deletion appear. Each creation is INDEPENDENT
        // per surface.
        var composeCard = new StackPanel();
        composeCard.Children.Add(Ui.SectionHeader(L.T("Système & surface", "System & surface")));
        composeCard.Children.Add(Ui.MutedLabel(L.T(
            "Choisis le système et la surface ; tout se règle dans les cartes ci-dessous (clique une carte pour l'utiliser, ou compose ta création).",
            "Pick the system and the surface; everything is set in the cards below (click a card to use it, or compose your creation).")));
        var systemRow = new WrapPanel { Margin = new System.Windows.Thickness(0, 4, 0, 0) };
        var systemLabel = Ui.MutedLabel(L.T("Système :", "System:"));
        systemLabel.Margin = new System.Windows.Thickness(0, 0, 6, 0);
        systemLabel.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        systemRow.Children.Add(systemLabel);
        var systemPicker = Ui.ComboBox(200);
        // nothing preselected; only systems with at least one INSTALLED game;
        // mame/fbneo stay listed (they carry their own chains and creations)
        systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = L.T("- sélectionner -", "- select -"), Tag = null });
        // the general template of systems has ALWAYS been one template for all of them —
        // it was simply edited from inside a system, which read as "this system's". It
        // now has the entry it deserves, above the separator.
        systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem
        {
            Content = L.T("Tous les systèmes", "All systems"), Tag = GabaritIdentity.AllSentinel
        });
        systemPicker.Items.Add(new System.Windows.Controls.Separator());
        var present = media.ListPresentRoms(pluginRoot);
        bool HasGames(string system) => GameMediaCatalog.ArcadeAliases.Contains(system)
            ? present.TryGetValue("arcade", out var arcade) && arcade.Count > 0
            : present.TryGetValue(system, out var roms) && roms.Count > 0;
        foreach (var system in media.ListSystems().Where(HasGames))
        {
            systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = system, Tag = system });
        }
        systemPicker.SelectedIndex = 0;
        systemRow.Children.Add(systemPicker);
        composeCard.Children.Add(systemRow);

        var resolutionCard = new ResolutionCard(engine);
        // "All systems" is a level of its own: the resolution card speaks about ONE
        // system's chain and draws nothing without a target, so that level needs its own
        // panel rather than an emptied card.
        var allSystemsHost = new StackPanel { Visibility = System.Windows.Visibility.Collapsed };

        var surfacesStore = new SurfacesStore(pluginRoot);
        var surfaces = surfacesStore.Load();
        // A surface whose screen(s) the user excluded from MarqueeManager is
        // SUSPENDED: not offered as an active target (spec §5) unless the user asks.
        var unmanagedScreens = surfacesStore.LoadScreens()
            .Where(s => !s.ManagedByMarqueeManager && s.WindowsIndex >= 0)
            .Select(s => s.WindowsIndex)
            .ToHashSet();
        bool IsSuspended(SurfaceModel surface)
            => surface.Screens.Count > 0 && surface.Screens.All(unmanagedScreens.Contains);

        var surfaceRow = systemRow; // system + surface share one line
        var surfaceLabel = Ui.MutedLabel(L.T("Surface :", "Surface:"));
        surfaceLabel.Margin = new System.Windows.Thickness(16, 0, 6, 0);
        surfaceLabel.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        surfaceRow.Children.Add(surfaceLabel);
        var surfacePicker = Ui.ComboBox(240);
        var showSuspended = Ui.CheckBox(L.T("Afficher les surfaces suspendues", "Show suspended surfaces"), false);
        showSuspended.Margin = new System.Windows.Thickness(0, 6, 0, 0);

        void RebuildSurfacePicker()
        {
            var previous = SelectedSurface();
            surfacePicker.Items.Clear();
            foreach (var surface in surfaces)
            {
                var suspended = IsSuspended(surface);
                if (suspended && showSuspended.IsChecked != true) continue;
                var dims = MediaResolutionPreview.TargetOf(surface, detectedScreens);
                var label = $"{surface.Id} ({surface.Category}) — {dims.Width}×{dims.Height}"
                            + (suspended ? L.T("  · suspendue", "  · suspended") : "");
                var item = new System.Windows.Controls.ComboBoxItem { Content = label, Tag = surface.Id };
                surfacePicker.Items.Add(item);
                if (surface.Id.Equals(previous, StringComparison.OrdinalIgnoreCase)) surfacePicker.SelectedItem = item;
            }
            if (surfacePicker.SelectedItem == null && surfacePicker.Items.Count > 0) surfacePicker.SelectedIndex = 0;
        }

        surfaceRow.Children.Add(surfacePicker);

        string? SelectedSystem() => (systemPicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        // a real system to resolve the template's assets and preview against
        string? SampleSystem() => systemPicker.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .Select(i => i.Tag as string)
            .FirstOrDefault(tag => tag is { Length: > 0 } && tag != GabaritIdentity.AllSentinel);
        string? SelectedSurface() => (surfacePicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        SurfaceModel? SurfaceOf(string? id) => surfaces.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        static string CategoryOf(SurfaceModel surface) => surface.Category.ToLowerInvariant() switch
        {
            "topper" => "toppers",
            "dmd-virtual" => "dmd",
            _ => "marquees"
        };

        composeCard.Children.Add(showSuspended);

        void Refresh() => UpdateResolution();

        // The template every system shares: what it produces on a sample system, and the
        // one button that composes it.
        void RenderAllSystemsLevel(string? sample, SurfaceModel? surface)
        {
            allSystemsHost.Children.Clear();
            var panel = new StackPanel();
            panel.Children.Add(Ui.SectionHeader(L.T("Tous les systèmes", "All systems")));
            panel.Children.Add(Ui.MutedLabel(L.T(
                "Une seule mise en page, appliquée à chaque système avec SES médias. Choisissez un système ci-dessus pour ses réglages propres.",
                "One layout, applied to every system with ITS media. Pick a system above for its own settings.")));
            if (surface == null || sample == null)
            {
                panel.Children.Add(Ui.MutedLabel(L.T("Sélectionnez une surface.", "Select a surface.")));
                allSystemsHost.Children.Add(Ui.Card(panel));
                return;
            }

            var cat = CategoryOf(surface);
            var has = GabaritRenderer.HasGabarit(pluginRoot, cat, surface.Id, GabaritIdentity.SystemScope);
            panel.Children.Add(Ui.MutedLabel(has
                ? L.T("✓ Un gabarit existe pour tous les systèmes.", "✓ A template exists for all systems.")
                : L.T("Aucun gabarit — chaque système répond avec ses propres sources.",
                      "No template — each system answers with its own sources.")));

            if (has)
            {
                var dims = MediaResolutionPreview.TargetOf(surface, detectedScreens);
                var cache = GabaritRenderer.CachePath(pluginRoot, cat, surface.Id, sample);
                if (!System.IO.File.Exists(cache))
                {
                    GabaritRenderer.RenderSystem(pluginRoot, cat, surface.Id, sample,
                        dims.Width, dims.Height, SystemAssets(pluginRoot, sample));
                }
                if (System.IO.File.Exists(cache))
                {
                    panel.Children.Add(Ui.MutedLabel(L.T($"Aperçu (exemple : {sample})", $"Preview (sample: {sample})")));
                    panel.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = Ui.Preview(cache),
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        MaxHeight = 220,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        Margin = new System.Windows.Thickness(0, 4, 0, 4)
                    });
                }
            }

            var edit = Ui.Button(has
                ? L.T("Modifier le gabarit", "Edit the template")
                : L.T("Créer le gabarit", "Create the template"), (_, _) =>
            {
                new GameComposerWindow(pluginRoot, GabaritIdentity.SystemId, GabaritIdentity.SystemScope,
                    L.T($"Gabarit — tous les systèmes (aperçu : {sample})", $"Template — all systems (preview: {sample})"),
                    SystemAssets(pluginRoot, sample), surface.Id, gabaritMode: true,
                    sample: ("systems", sample))
                {
                    Owner = System.Windows.Window.GetWindow(this)
                }.ShowDialog();
                GabaritRenderer.InvalidateSurface(pluginRoot, cat, surface.Id);
                Refresh();
            }, primary: true);
            edit.Margin = new System.Windows.Thickness(0, 8, 0, 0);
            edit.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            panel.Children.Add(edit);
            allSystemsHost.Children.Add(Ui.Card(panel));
        }

        // Point the shared block card at the picked system + surface. The composer
        // and delete actions live on the "Ma création" card, not up here.
        void UpdateResolution()
        {
            var picked = SelectedSystem();
            var allSystems = picked == GabaritIdentity.AllSentinel;
            // "All systems" is a level, not a system: it edits the one template every
            // system already shares. A real system still has to supply the assets and
            // the preview, but none of THAT system's own settings are offered here.
            var system = allSystems ? SampleSystem() : picked;
            var surface = SurfaceOf(SelectedSurface());
            // render the surface's gabarit for THIS system once (cached) so the
            // "Générée" card reflects the general template
            if (system != null && surface != null)
            {
                var cat = CategoryOf(surface);
                if (GabaritRenderer.HasGabarit(pluginRoot, cat, surface.Id, GabaritIdentity.SystemScope)
                    && !System.IO.File.Exists(GabaritRenderer.CachePath(pluginRoot, cat, surface.Id, system)))
                {
                    var dims = MediaResolutionPreview.TargetOf(surface, detectedScreens);
                    GabaritRenderer.RenderSystem(pluginRoot, cat, surface.Id, system, dims.Width, dims.Height, SystemAssets(pluginRoot, system));
                }
            }
            ResolutionContext? ctx = (system != null && surface != null)
                ? engine.SystemContext(surface, detectedScreens, system)
                : null;
            allSystemsHost.Visibility = allSystems ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            resolutionCard.Visibility = allSystems ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            if (allSystems) RenderAllSystemsLevel(system, SurfaceOf(SelectedSurface()));
            resolutionCard.Update(allSystems ? null : ctx,
                composePersonal: allSystems ? null : () =>
                {
                    if (system == null) return;
                    var window = new GameComposerWindow(pluginRoot, "systems", system, system, SystemAssets(pluginRoot, system), SelectedSurface())
                    {
                        Owner = System.Windows.Window.GetWindow(this)
                    };
                    window.ShowDialog();
                    Refresh();
                },
                editGabarit: () =>
                {
                    if (system == null || surface == null) return;
                    // the general template = a composed layout (layers keyed by type)
                    // saved per surface; it resolves to each system's media at render.
                    // The selected system provides the assets for a concrete preview.
                    new GameComposerWindow(pluginRoot, GabaritIdentity.SystemId, GabaritIdentity.SystemScope,
                        L.T($"Gabarit — tous les systèmes (aperçu : {system})", $"Template — all systems (preview: {system})"),
                        SystemAssets(pluginRoot, system), surface.Id, gabaritMode: true,
                        sample: ("systems", system))
                    {
                        Owner = System.Windows.Window.GetWindow(this)
                    }.ShowDialog();
                    // the recipe changed → drop the cached renders so they regenerate
                    GabaritRenderer.InvalidateSurface(pluginRoot, CategoryOf(surface), surface.Id);
                    Refresh();
                },
                deletePersonal: allSystems ? null : () =>
                {
                    if (system == null || surface == null) return;
                    // the creation may live per-surface OR at the category level
                    new MarqueeProjectStore(pluginRoot, CategoryOf(surface), surface.Id).Delete("systems", system);
                    new MarqueeProjectStore(pluginRoot, CategoryOf(surface)).Delete("systems", system);
                    Refresh();
                });
        }

        showSuspended.Checked += (_, _) => { RebuildSurfacePicker(); Refresh(); };
        showSuspended.Unchecked += (_, _) => { RebuildSurfacePicker(); Refresh(); };
        RebuildSurfacePicker();
        systemPicker.SelectionChanged += (_, _) => Refresh();
        surfacePicker.SelectionChanged += (_, _) => Refresh();
        Refresh();

        // bulk warm-up: render the surface's system gabarit for EVERY listed system
        // at once (one layout, all systems) — same render as the lazy per-view path.
        var pregenRow = new WrapPanel { Margin = new System.Windows.Thickness(0, 10, 0, 0) };
        var pregenStatus = Ui.MutedLabel("");
        pregenStatus.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        pregenStatus.Margin = new System.Windows.Thickness(10, 0, 0, 0);
        pregenRow.Children.Add(Ui.Button(L.T("Pré-générer pour tous les systèmes", "Pre-generate for all systems"), (_, _) =>
        {
            var surface = SurfaceOf(SelectedSurface());
            if (surface == null) return;
            var cat = CategoryOf(surface);
            if (!GabaritRenderer.HasGabarit(pluginRoot, cat, surface.Id, GabaritIdentity.SystemScope))
            {
                pregenStatus.Text = L.T("Aucun gabarit sur cette surface — composez-le d'abord.",
                    "No gabarit on this surface — compose it first.");
                return;
            }
            var dims = MediaResolutionPreview.TargetOf(surface, detectedScreens);
            var allSystems = media.ListSystems().Where(HasGames).ToList();
            var done = 0;
            foreach (var sys in allSystems)
            {
                if (GabaritRenderer.RenderSystem(pluginRoot, cat, surface.Id, sys, dims.Width, dims.Height, SystemAssets(pluginRoot, sys)) != null)
                    done++;
            }
            pregenStatus.Text = L.T($"{done}/{allSystems.Count} systèmes générés pour « {surface.Id} ».",
                $"{done}/{allSystems.Count} systems generated for “{surface.Id}”.");
            Refresh();
        }));
        pregenRow.Children.Add(pregenStatus);
        composeCard.Children.Add(pregenRow);

        page.Children.Add(Ui.Card(composeCard));

        page.Children.Add(resolutionCard);
        page.Children.Add(allSystemsHost);

        // Priorités par système et Templates de composition sont ABSORBÉS par le
        // nouveau modèle : l'ordre est fixe et on choisit le gagnant en cliquant une
        // carte (activer/désactiver par lien) ; les recettes fanart+gradient+logo
        // sont désormais des gabarits éditables (« Modifier le gabarit général »).

        Content = Ui.Page(page);
    }

    /// <summary>System-level media: theme logo (wheel), generated marquee/DMD, fanart when present.</summary>
    private static IReadOnlyList<GameAsset> SystemAssets(string pluginRoot, string system)
    {
        // arcade family (mame, fbneo…): its media — wheel/logo, generated marquee,
        // fanart — lives under the canonical "arcade" folder, not under the frontend.
        var mediaSystem = GameMediaCatalog.ArcadeAliases.Contains(system) ? "arcade" : system;
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems", mediaSystem));
        var assets = new List<GameAsset>();
        void Add(string key, string fr, string en, params string[] relative)
        {
            foreach (var rel in relative)
            {
                var path = System.IO.Path.Combine(root, rel);
                if (System.IO.File.Exists(path))
                {
                    assets.Add(new GameAsset(key, L.T(fr, en), path));
                    return;
                }
            }
        }
        Add("fanart", "Fanart du système", "System fanart", @"artwork\fanart.jpg", @"artwork\fanart.png");
        Add("wheel", "Logo du système", "System logo", @"ui\wheels\wheel.png");
        Add("marquee", "Marquee généré", "Generated marquee", @"artwork\marquee\generated-system-marquee.png");
        Add("dmd", "DMD généré", "Generated DMD", @"artwork\marquee\generated-system-dmd.png");

        // system fanart: the ACTIVE ES THEME carries it (APIExpose's own cascade:
        // art/background/<system>.* etc. — carbon ships 338 of them). Same lookup
        // here, so the composer offers exactly what the runtime would show.
        if (assets.All(a => a.Key != "fanart") && ThemeSystemFanart(pluginRoot, system) is { } themeFanart)
        {
            assets.Insert(0, new GameAsset("fanart",
                L.T("Fanart du système (thème ES)", "System fanart (ES theme)"), themeFanart));
        }

        // very last resort: the first game fanart of the system
        if (assets.All(a => a.Key != "fanart"))
        {
            try
            {
                var games = System.IO.Path.Combine(root, "games");
                if (System.IO.Directory.Exists(games))
                {
                    foreach (var dir in System.IO.Directory.EnumerateDirectories(games).Take(60))
                    {
                        foreach (var candidate in new[] { @"artwork\fanart.jpg", @"artwork\fanart.png" })
                        {
                            var path = System.IO.Path.Combine(dir, candidate);
                            if (System.IO.File.Exists(path))
                            {
                                assets.Insert(0, new GameAsset("fanart",
                                    L.T($"Fanart (jeu : {System.IO.Path.GetFileName(dir)})",
                                        $"Fanart (game: {System.IO.Path.GetFileName(dir)})"), path));
                                return assets;
                            }
                        }
                    }
                }
            }
            catch
            {
                // no fallback fanart: the palette simply skips it
            }
        }
        return assets;
    }

    /// <summary>Mirror of APIExpose's theme fanart cascade for the ACTIVE theme
    /// (es_settings ThemeSet): &lt;theme&gt;\&lt;sys&gt;\art\background, &lt;theme&gt;\art\background,
    /// _systemmedia variants — first &lt;system&gt;.* image wins.</summary>
    private static string? ThemeSystemFanart(string pluginRoot, string system)
    {
        try
        {
            var esRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                pluginRoot, "..", "..", "emulationstation", ".emulationstation"));
            var settings = System.IO.Path.Combine(esRoot, "es_settings.cfg");
            if (!System.IO.File.Exists(settings)) return null;
            var themeSet = System.Xml.Linq.XDocument.Load(settings).Root?
                .Elements("string")
                .FirstOrDefault(e => (string?)e.Attribute("name") == "ThemeSet")
                ?.Attribute("value")?.Value;
            if (string.IsNullOrWhiteSpace(themeSet)) return null;
            var themeRoot = System.IO.Path.Combine(esRoot, "themes", themeSet);
            if (!System.IO.Directory.Exists(themeRoot)) return null;

            var names = GameMediaCatalog.ArcadeAliases.Contains(system)
                ? new[] { system, "arcade" }
                : new[] { system };
            foreach (var name in names)
            {
                foreach (var directory in new[]
                         {
                             System.IO.Path.Combine(themeRoot, name, "art", "background"),
                             System.IO.Path.Combine(themeRoot, name, "background"),
                             System.IO.Path.Combine(themeRoot, "art", "background"),
                             System.IO.Path.Combine(themeRoot, "_systemmedia", "fanartsyst"),
                             System.IO.Path.Combine(themeRoot, "_systemmedia", "background")
                         })
                {
                    if (!System.IO.Directory.Exists(directory)) continue;
                    var match = System.IO.Directory.EnumerateFiles(directory, name + ".*")
                        .FirstOrDefault(f => System.IO.Path.GetExtension(f).ToLowerInvariant()
                            is ".jpg" or ".jpeg" or ".png" or ".webp");
                    if (match != null) return match;
                }
            }
        }
        catch
        {
            // theme unreadable: no theme fanart
        }
        return null;
    }
}
