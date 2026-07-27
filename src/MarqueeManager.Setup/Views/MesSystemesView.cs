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
        var ini = IniFile.Load(PluginPaths.ConfigPath(pluginRoot));
        var identity = new GameIdentityIndex(pluginRoot,
            ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"));

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Mes systèmes", "My systems")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Par système : quelles sources s'affichent et dans quel ordre, quel template automatique, votre dossier de médias, et la pré-génération de masse.",
            "Per system: which sources display and in what order, which automatic template, your media folder, and bulk pre-generation.")));

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
        composeCard.Children.Add(Ui.SectionHeader(L.T("Mon marquee", "My marquee")));
        composeCard.Children.Add(Ui.MutedLabel(L.T(
            "Le marquee affiché quand un SYSTÈME est sélectionné dans ES. Créez-le visuellement (logo, marquee généré, fanart du thème, textes) — il prime sur le rendu automatique.",
            "The marquee shown when a SYSTEM is selected in ES. Create it visually (logo, generated marquee, theme fanart, texts) — it overrides the automatic render.")));
        var systemRow = new WrapPanel { Margin = new System.Windows.Thickness(0, 4, 0, 0) };
        var systemPicker = Ui.ComboBox(200);
        // nothing preselected; only systems with at least one INSTALLED game;
        // mame/fbneo stay listed (they carry their own chains and creations)
        systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = L.T("- sélectionner -", "- select -"), Tag = null });
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

        // the whole "Mon marquee" body only shows once a system is picked
        var body = new StackPanel { Visibility = System.Windows.Visibility.Collapsed };
        var preview = new System.Windows.Controls.Image
        {
            MaxHeight = 100,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new System.Windows.Thickness(0, 6, 0, 0)
        };
        var previewCaption = Ui.MutedLabel("");
        body.Children.Add(preview);
        body.Children.Add(previewCaption);
        var resolutionBody = new StackPanel();

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

        var surfaceRow = new WrapPanel { Margin = new System.Windows.Thickness(0, 6, 0, 0) };
        var surfaceLabel = Ui.MutedLabel(L.T("Surface :", "Surface:"));
        surfaceLabel.Margin = new System.Windows.Thickness(0, 0, 6, 0);
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
        string? SelectedSurface() => (surfacePicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        SurfaceModel? SurfaceOf(string? id) => surfaces.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        static string CategoryOf(SurfaceModel surface) => surface.Category.ToLowerInvariant() switch
        {
            "topper" => "toppers",
            "dmd-virtual" => "dmd",
            _ => "marquees"
        };

        System.Windows.Controls.Button deleteButton = null!;
        System.Windows.Controls.Button openButton = null!;
        openButton = Ui.Button(L.T("Ouvrir l'interface de création graphique", "Open the graphic creation interface"), (_, _) =>
        {
            if (SelectedSystem() is not { } system) return;
            var window = new GameComposerWindow(pluginRoot, "systems", system, system, SystemAssets(pluginRoot, system), SelectedSurface())
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            window.ShowDialog();
            Refresh();
        }, primary: true);
        surfaceRow.Children.Add(openButton);

        deleteButton = Ui.Button(L.T("Supprimer la création de cette surface", "Delete this surface's creation"), (_, _) =>
        {
            if (SelectedSystem() is not { } system || SurfaceOf(SelectedSurface()) is not { } surface) return;
            // the creation may live per-surface OR at the category level
            // (pre-per-surface saves): delete BOTH or it keeps coming back
            new MarqueeProjectStore(pluginRoot, CategoryOf(surface), surface.Id).Delete("systems", system);
            new MarqueeProjectStore(pluginRoot, CategoryOf(surface)).Delete("systems", system);
            Refresh();
        });
        surfaceRow.Children.Add(deleteButton);
        body.Children.Add(surfaceRow);
        body.Children.Add(showSuspended);
        composeCard.Children.Add(body);

        void Refresh()
        {
            UpdateResolution();
            // a suspended surface (ignored screen) is not an active edit target (§5)
            openButton.IsEnabled = SurfaceOf(SelectedSurface()) is { } activeSurface && !IsSuspended(activeSurface);
            var system = SelectedSystem();
            body.Visibility = system == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            preview.Source = null;
            if (system == null) return;

            // the SELECTED surface's creation wins, then the shared category
            // file, then the generated marquee — like the runtime
            string? path = null;
            if (SurfaceOf(SelectedSurface()) is { } previewSurface)
            {
                var surfaceStore = new MarqueeProjectStore(pluginRoot, CategoryOf(previewSurface), previewSurface.Id);
                if (surfaceStore.HasComposition("systems", system)) path = surfaceStore.PngPath("systems", system);
            }
            path ??= media.CurrentSystemMarquee(pluginRoot, system);
            previewCaption.Text = path == null
                ? L.T("Aucun marquee système pour l'instant.", "No system marquee yet.")
                : System.IO.Path.GetFileName(path).StartsWith("generated", StringComparison.OrdinalIgnoreCase)
                    ? L.T("Affiché actuellement : marquee généré.", "Currently displayed: generated marquee.")
                    : L.T("Affiché actuellement : votre création graphique.", "Currently displayed: your graphic creation.");
            if (path != null)
            {
                try
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.DecodePixelWidth = 640;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    preview.Source = bitmap;
                }
                catch
                {
                    // unreadable image: caption only
                }
            }

            deleteButton.Visibility = SurfaceOf(SelectedSurface()) is { } surface
                && (new MarqueeProjectStore(pluginRoot, CategoryOf(surface), surface.Id).HasComposition("systems", system)
                    || new MarqueeProjectStore(pluginRoot, CategoryOf(surface)).HasComposition("systems", system))
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        // The VISIBLE slice of the refonte: run the shared resolver for the picked
        // system on the picked surface and show what wins, its dimensional fit and
        // the full traced chain — the same decision the runtime will make.
        void UpdateResolution()
        {
            resolutionBody.Children.Clear();
            var system = SelectedSystem();
            var surface = SurfaceOf(SelectedSurface());
            if (system == null || surface == null)
            {
                resolutionBody.Children.Add(Ui.MutedLabel(L.T("Sélectionnez un système et une surface.", "Select a system and a surface.")));
                return;
            }

            var ctx = engine.SystemContext(surface, detectedScreens, system);
            var result = engine.Resolve(ctx);
            var media = result.Media;

            // 1 — what actually displays
            resolutionBody.Children.Add(Ui.MutedLabel($"{L.T("Surface", "Surface")} : {surface.Id} — {result.Target.Width}×{result.Target.Height}"));
            resolutionBody.Children.Add(BuildAdaptedPreview(result));
            var source = media.Source == ResolutionSource.Neutral
                ? L.T("Affiché : fond neutre", "Displayed: neutral background")
                : $"{L.T("Affiché", "Displayed")} : {ResolutionText.Link(media.Source)}";
            var sourceLabel = Ui.Label(source, 13);
            sourceLabel.FontWeight = System.Windows.FontWeights.Bold;
            resolutionBody.Children.Add(sourceLabel);
            if (media.OriginalSize is { } src)
            {
                var dims = $"{src.Width}×{src.Height} → {result.Target.Width}×{result.Target.Height} · {ResolutionText.Status(result.Dimensions.Status)}";
                if (result.Dimensions.CropY > 0) dims += $" · {L.T("crop vertical", "vertical crop")} {result.Dimensions.CropY * 100:0.#}%";
                if (result.Dimensions.CropX > 0) dims += $" · {L.T("crop horizontal", "horizontal crop")} {result.Dimensions.CropX * 100:0.#}%";
                if (result.Dimensions.HighMagnification) dims += $" · {L.T("agrandissement", "magnified")} ×{result.Dimensions.Magnification:0.#}";
                resolutionBody.Children.Add(Ui.MutedLabel(dims));
            }

            // 2 — how it's decided: one row per link, ✓ on the winner, "Utiliser" toggles
            resolutionBody.Children.Add(Ui.SectionHeader(L.T("Comment c'est décidé   ( ✓ = ce qui s'applique )", "How it's decided   ( ✓ = what applies )")));
            foreach (var link in engine.DescribeChain(ctx))
                resolutionBody.Children.Add(BuildLinkRow(ctx, link));

            if (engine.HasOverride(ctx))
            {
                var reset = Ui.Button(L.T("Rétablir les réglages de la surface", "Reset to surface settings"), (_, _) =>
                {
                    engine.ResetTarget(ctx);
                    Refresh();
                });
                reset.Margin = new System.Windows.Thickness(0, 6, 0, 0);
                resolutionBody.Children.Add(reset);
            }
        }

        // One source block: [Utiliser] checkbox + ✓/○ winner mark + name + state.
        System.Windows.FrameworkElement BuildLinkRow(ResolutionContext ctx, ChainLink link)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new System.Windows.Thickness(0, 3, 0, 0) };

            var use = Ui.CheckBox(L.T("Utiliser", "Use"), link.Enabled);
            use.Width = 78;
            use.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            use.Checked += (_, _) => { engine.SetSourceEnabled(ctx, link.Kind, true); Refresh(); };
            use.Unchecked += (_, _) => { engine.SetSourceEnabled(ctx, link.Kind, false); Refresh(); };
            row.Children.Add(use);

            var mark = new TextBlock
            {
                Text = link.IsWinner ? "✓" : "○",
                Foreground = link.IsWinner ? Ui.Ok : Ui.Muted,
                FontWeight = System.Windows.FontWeights.Bold,
                Width = 20,
                TextAlignment = System.Windows.TextAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            row.Children.Add(mark);

            var state = !link.Enabled ? L.T("désactivée", "disabled")
                : link.IsWinner ? L.T("utilisée", "used")
                : link.Present ? L.T("disponible (une source au-dessus prime)", "available (a source above wins)")
                : L.T("absente", "absent");
            var name = Ui.Label($"{ResolutionText.Link(link.Source)} — {state}", 12);
            name.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            if (link.IsWinner) name.FontWeight = System.Windows.FontWeights.Bold;
            else if (!link.Enabled || !link.Present) name.Foreground = Ui.Muted;
            row.Children.Add(name);

            return row;
        }

        showSuspended.Checked += (_, _) => { RebuildSurfacePicker(); Refresh(); };
        showSuspended.Unchecked += (_, _) => { RebuildSurfacePicker(); Refresh(); };
        RebuildSurfacePicker();
        systemPicker.SelectionChanged += (_, _) => Refresh();
        surfacePicker.SelectionChanged += (_, _) => Refresh();
        Refresh();
        page.Children.Add(Ui.Card(composeCard));

        var resolutionCard = new StackPanel();
        resolutionCard.Children.Add(Ui.SectionHeader(L.T("Résolution (aperçu du moteur partagé)", "Resolution (shared engine preview)")));
        resolutionCard.Children.Add(Ui.MutedLabel(L.T(
            "Ce que le runtime afficherait pour ce système sur la surface choisie, et pourquoi. Aperçu seulement — rien n'est généré.",
            "What the runtime would show for this system on the chosen surface, and why. Preview only — nothing is generated.")));
        resolutionCard.Children.Add(resolutionBody);
        page.Children.Add(Ui.Card(resolutionCard));

        var templates = new StackPanel();
        templates.Children.Add(Ui.SectionHeader(L.T("Templates de composition", "Composition templates")));
        templates.Children.Add(Ui.MutedLabel(L.T(
            "4 gabarits automatiques (fanart + gradient selon la luminance + logo) : 1920×360, 1280×400, 920×360 et vertical 1080×1920. "
            + "Affectez-les dans les priorités (« Template … ») : chaque jeu du système reçoit sa composition, rendue en tâche de fond ou pré-générée en masse.",
            "4 automatic recipes (fanart + luminance-driven gradient + logo): 1920×360, 1280×400, 920×360 and vertical 1080×1920. "
            + "Assign them in the priorities (“Template …”): every game of the system gets its composition, rendered in the background or pre-generated in bulk.")));
        page.Children.Add(Ui.Card(templates));

        page.Children.Add(Ui.Card(new PrioritiesCard(pluginRoot, media, identity)));

        Content = Ui.Page(page);
    }

    /// <summary>A thumbnail at the SURFACE ratio showing the resolved media framed
    /// exactly as the shared fit decided — crop clipped, letterbox on the neutral
    /// background: what will actually appear on that surface.</summary>
    private static System.Windows.FrameworkElement BuildAdaptedPreview(PreviewResult result)
    {
        double scale = Math.Min(360.0 / result.Target.Width, 220.0 / result.Target.Height);
        double boxW = Math.Floor(Math.Max(40, result.Target.Width * scale));
        double boxH = Math.Floor(Math.Max(20, result.Target.Height * scale));

        var canvas = new System.Windows.Controls.Canvas { Width = boxW, Height = boxH, ClipToBounds = true };
        var media = result.Media;
        if (media.Fit is { } fit && media.EffectivePath is { } path && System.IO.File.Exists(path))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = Math.Max(8, (int)(fit.TargetRect.Width * scale));
                bitmap.EndInit();
                bitmap.Freeze();
                // exact rect from the homothetic fit; Stretch.Fill maps the whole
                // image into it WITHOUT distortion because the rect keeps its aspect
                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Stretch = System.Windows.Media.Stretch.Fill,
                    Width = fit.TargetRect.Width * scale,
                    Height = fit.TargetRect.Height * scale
                };
                System.Windows.Controls.Canvas.SetLeft(image, fit.TargetRect.X * scale);
                System.Windows.Controls.Canvas.SetTop(image, fit.TargetRect.Y * scale);
                canvas.Children.Add(image);
            }
            catch
            {
                // unreadable media: the neutral box stands on its own
            }
        }

        return new System.Windows.Controls.Border
        {
            Width = boxW,
            Height = boxH,
            Background = System.Windows.Media.Brushes.Black, // the neutral background
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new System.Windows.Thickness(1),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new System.Windows.Thickness(0, 2, 0, 6),
            Child = canvas,
            ClipToBounds = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
    }

    /// <summary>System-level media: theme logo (wheel), generated marquee/DMD, fanart when present.</summary>
    private static IReadOnlyList<GameAsset> SystemAssets(string pluginRoot, string system)
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems", system));
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
