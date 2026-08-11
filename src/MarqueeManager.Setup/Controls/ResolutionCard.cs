using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// The shared "Résolution" card: dedicated SOURCE CARDS from the most general to
/// the most precise, each with its own framed preview and edit button. Clicking a
/// card FORCES that source for the selected target (persisted); the green ✓ marks
/// what is used. Reused by Mes systèmes (system) and Mes jeux (game).
/// </summary>
public sealed class ResolutionCard : UserControl
{
    private static readonly Dictionary<SourceKind, int> DisplayRank = new()
    {
        [SourceKind.Dynamic] = -1,    // the surface’s own layers, responsive: the base
        [SourceKind.Generated] = 0,   // general template, all systems
        [SourceKind.Personal] = 1,    // this system / this game
        [SourceKind.UserDrop] = 2,    // a raw file the user dropped in
        [SourceKind.Scraped] = 3,
        [SourceKind.Logo] = 4,
        [SourceKind.SystemFallback] = 5
    };

    private readonly MediaResolutionPreview _engine;
    private readonly StackPanel _body = new();
    private ResolutionContext? _context;
    private Action? _compose;
    private Action? _deletePersonal;
    private Action? _editGabarit;
    private Action? _onChanged;

    public ResolutionCard(MediaResolutionPreview engine)
    {
        _engine = engine;
        var panel = new StackPanel();
        panel.Children.Add(Ui.SectionHeader(L.T("Résolution — clique une carte pour l'utiliser", "Resolution — click a card to use it")));
        panel.Children.Add(Ui.MutedLabel(L.T(
            "Du plus général (gabarit) au plus précis (ta création). ✓ = ce qui s'affiche. Aperçu seulement — rien n'est généré.",
            "From the most general (template) to the most precise (your creation). ✓ = what shows. Preview only — nothing is generated.")));
        panel.Children.Add(_body);
        Content = Ui.Card(panel);
    }

    /// <summary>Point the card at a target (null blanks it). <paramref name="composePersonal"/>
    /// edits the personal creation; <paramref name="editGabarit"/> edits the general template
    /// (button hidden when null); <paramref name="onChanged"/> fires after a change.</summary>
    public void Update(ResolutionContext? context, Action? composePersonal = null, Action? editGabarit = null,
        Action? deletePersonal = null, Action? onChanged = null)
    {
        _context = context;
        _compose = composePersonal;
        _editGabarit = editGabarit;
        _deletePersonal = deletePersonal;
        _onChanged = onChanged;
        Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        if (_context is not { } ctx)
        {
            _body.Children.Add(Ui.MutedLabel(L.T("Sélectionnez une cible.", "Select a target.")));
            return;
        }

        var target = _engine.Resolve(ctx).Target;
        _body.Children.Add(Ui.MutedLabel($"{L.T("Surface", "Surface")} : {ctx.SurfaceId} — {target.Width}×{target.Height}"));

        foreach (var link in _engine.DescribeChain(ctx).OrderBy(l => DisplayRank.GetValueOrDefault(l.Kind, 9)))
            _body.Children.Add(BuildSourceCard(ctx, link, target));

        if (_engine.HasOverride(ctx))
        {
            var reset = Ui.Button(L.T("Automatique (rétablir la surface)", "Automatic (reset to surface)"), (_, _) =>
            {
                _engine.ResetTarget(ctx);
                Render();
                _onChanged?.Invoke();
            });
            reset.Margin = new Thickness(0, 8, 0, 0);
            _body.Children.Add(reset);
        }
    }

    private FrameworkElement BuildSourceCard(ResolutionContext ctx, ChainLink link, PixelSize target)
    {
        var panel = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = link.IsWinner ? "✓" : "○",
            Foreground = link.IsWinner ? Ui.Ok : Ui.Muted,
            FontWeight = FontWeights.Bold,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center
        });
        var title = Ui.Label(CardTitle(ctx, link.Kind), 13);
        title.VerticalAlignment = VerticalAlignment.Center;
        if (link.IsWinner) title.FontWeight = FontWeights.Bold;
        titleRow.Children.Add(title);
        panel.Children.Add(titleRow);

        // every card shows a box at the surface ratio — greyed when the media is
        // absent — so the composition stays balanced
        var preview = BuildAdaptedPreview(link.Path, link.Fit, target);
        if (!link.Present) preview.Opacity = 0.4;
        panel.Children.Add(preview);
        if (!link.Present)
            panel.Children.Add(Ui.MutedLabel(L.T(
                "aucun média pour cette source — rien à afficher, non sélectionnable",
                "no media for this source — nothing to show, not selectable")));
        if (link.Kind == SourceKind.UserDrop)
        {
            // discoverability: tell the user exactly where to drop the file
            var hint = Ui.MutedLabel(L.T("Déposez votre image ici : ", "Drop your image here: ") + MediaResolutionPreview.DropTarget(ctx));
            hint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(hint);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        if (link.Kind == SourceKind.Personal && _compose is { } compose)
            actions.Children.Add(Ui.Button(link.Present ? L.T("Modifier", "Edit") : L.T("Composer", "Compose"), (_, _) => compose()));
        if (link.Kind == SourceKind.Personal && link.Present && _deletePersonal is { } delete)
            actions.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) => delete()));
        if (link.Kind == SourceKind.Generated && _editGabarit is { } editGabarit)
            actions.Children.Add(Ui.Button(L.T("Modifier le gabarit général", "Edit the general template"), (_, _) => editGabarit()));
        if (link.Kind == SourceKind.UserDrop)
            actions.Children.Add(Ui.Button(L.T("Ouvrir le dossier", "Open the folder"), (_, _) => _engine.OpenDropFolder(ctx)));
        if (actions.Children.Count > 0) panel.Children.Add(actions);

        var selectable = link.Present && link.Kind != SourceKind.SystemFallback;
        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 5, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent, // transparent still hit-tests → whole card clickable
            BorderBrush = link.IsWinner ? Ui.Ok : Ui.PanelBorder,
            BorderThickness = new Thickness(link.IsWinner ? 2 : 1),
            Cursor = selectable ? Cursors.Hand : Cursors.Arrow
        };
        if (selectable)
            border.MouseLeftButtonUp += (_, _) =>
            {
                _engine.SelectSource(ctx, link.Kind);
                Render();
                _onChanged?.Invoke();
            };
        return border;
    }

    private static string CardTitle(ResolutionContext ctx, SourceKind kind) => kind switch
    {
        SourceKind.Generated => ctx.Scope == MediaScope.Game
            ? L.T("Gabarit général — tous les jeux de ce système", "General template — all games of this system")
            : L.T("Gabarit général — tous les systèmes", "General template — all systems"),
        SourceKind.Personal => ctx.Scope == MediaScope.Game
            ? L.T("Ma création pour ce jeu", "My creation for this game")
            : L.T("Ma création pour ce système", "My creation for this system"),
        SourceKind.UserDrop => L.T("Mon dossier médias", "My media folder"),
        SourceKind.Scraped => L.T("Marquee scrapé", "Scraped marquee"),
        SourceKind.Logo => L.T("Logo mis en page", "Laid-out logo"),
        SourceKind.SystemFallback => L.T("Rendu du système", "System render"),
        SourceKind.Dynamic => L.T("Rendu dynamique — les calques de la surface",
                                  "Dynamic render — the surface's own layers"),
        _ => kind.ToString()
    };

    /// <summary>Thumbnail at the SURFACE ratio. When a media is given it is framed
    /// exactly as its fit decided (crop clipped, letterbox on the neutral
    /// background); otherwise an empty neutral box (kept for a balanced layout).</summary>
    private static FrameworkElement BuildAdaptedPreview(string? path, FitDecision? fit, PixelSize target)
    {
        double scale = Math.Min(320.0 / target.Width, 180.0 / target.Height);
        double boxW = Math.Floor(Math.Max(40, target.Width * scale));
        double boxH = Math.Floor(Math.Max(20, target.Height * scale));

        var canvas = new Canvas { Width = boxW, Height = boxH, ClipToBounds = true };
        if (path is not null && fit is not null && System.IO.File.Exists(path))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                // IgnoreImageCache so a creation JUST saved by the composer shows now
                bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = Math.Max(8, (int)(fit.TargetRect.Width * scale));
                bitmap.EndInit();
                bitmap.Freeze();
                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Fill,
                    Width = fit.TargetRect.Width * scale,
                    Height = fit.TargetRect.Height * scale
                };
                Canvas.SetLeft(image, fit.TargetRect.X * scale);
                Canvas.SetTop(image, fit.TargetRect.Y * scale);
                canvas.Children.Add(image);
            }
            catch
            {
                // unreadable media: the neutral box stands on its own
            }
        }

        return new Border
        {
            Width = boxW,
            Height = boxH,
            Background = Brushes.Black,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 2),
            Child = canvas,
            ClipToBounds = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false // clicks pass through to the card
        };
    }
}
