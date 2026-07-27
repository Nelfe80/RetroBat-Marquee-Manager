using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// The shared "Résolution" block card: one adapted preview of what displays on the
/// surface, then one row per chain link with a green ✓ on the winner and a
/// "Utiliser" toggle that PERSISTS an override for this exact target. Reused by
/// Mes systèmes (system scope) and Mes jeux (game scope) — same engine, same UI.
/// </summary>
public sealed class ResolutionCard : UserControl
{
    private readonly MediaResolutionPreview _engine;
    private readonly StackPanel _body = new();
    private ResolutionContext? _context;
    private Action? _compose;
    private Action? _onChanged;

    public ResolutionCard(MediaResolutionPreview engine)
    {
        _engine = engine;
        var panel = new StackPanel();
        panel.Children.Add(Ui.SectionHeader(L.T("Résolution (aperçu du moteur partagé)", "Resolution (shared engine preview)")));
        panel.Children.Add(Ui.MutedLabel(L.T(
            "Ce qui s'affiche sur cette surface, et pourquoi. « Utiliser » = source retenue ; ✓ = ce qui s'applique. Aperçu seulement — rien n'est généré.",
            "What shows on this surface, and why. 'Use' = kept source; ✓ = what applies. Preview only — nothing is generated.")));
        panel.Children.Add(_body);
        Content = Ui.Card(panel);
    }

    /// <summary>Point the card at a target (null blanks it). <paramref name="composePersonal"/>
    /// wires the "Composer" button on the personal block; <paramref name="onChanged"/> fires
    /// after an override toggle so the host can refresh siblings.</summary>
    public void Update(ResolutionContext? context, Action? composePersonal = null, Action? onChanged = null)
    {
        _context = context;
        _compose = composePersonal;
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

        var result = _engine.Resolve(ctx);
        var media = result.Media;

        _body.Children.Add(Ui.MutedLabel($"{L.T("Surface", "Surface")} : {ctx.SurfaceId} — {result.Target.Width}×{result.Target.Height}"));
        _body.Children.Add(BuildAdaptedPreview(result));

        var source = media.Source == ResolutionSource.Neutral
            ? L.T("Affiché : fond neutre", "Displayed: neutral background")
            : $"{L.T("Affiché", "Displayed")} : {ResolutionText.Link(media.Source)}";
        var sourceLabel = Ui.Label(source, 13);
        sourceLabel.FontWeight = FontWeights.Bold;
        _body.Children.Add(sourceLabel);

        if (media.OriginalSize is { } src)
        {
            var dims = $"{src.Width}×{src.Height} → {result.Target.Width}×{result.Target.Height} · {ResolutionText.Status(result.Dimensions.Status)}";
            if (result.Dimensions.CropY > 0) dims += $" · {L.T("crop vertical", "vertical crop")} {result.Dimensions.CropY * 100:0.#}%";
            if (result.Dimensions.CropX > 0) dims += $" · {L.T("crop horizontal", "horizontal crop")} {result.Dimensions.CropX * 100:0.#}%";
            if (result.Dimensions.HighMagnification) dims += $" · {L.T("agrandissement", "magnified")} ×{result.Dimensions.Magnification:0.#}";
            _body.Children.Add(Ui.MutedLabel(dims));
        }

        _body.Children.Add(Ui.SectionHeader(L.T("Comment c'est décidé   ( ✓ = ce qui s'applique )", "How it's decided   ( ✓ = what applies )")));
        foreach (var link in _engine.DescribeChain(ctx))
            _body.Children.Add(BuildLinkRow(ctx, link));

        if (_engine.HasOverride(ctx))
        {
            var reset = Ui.Button(L.T("Rétablir les réglages de la surface", "Reset to surface settings"), (_, _) =>
            {
                _engine.ResetTarget(ctx);
                Render();
                _onChanged?.Invoke();
            });
            reset.Margin = new Thickness(0, 6, 0, 0);
            _body.Children.Add(reset);
        }
    }

    private FrameworkElement BuildLinkRow(ResolutionContext ctx, ChainLink link)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };

        var use = Ui.CheckBox(L.T("Utiliser", "Use"), link.Enabled);
        use.Width = 78;
        use.VerticalAlignment = VerticalAlignment.Center;
        use.Checked += (_, _) => { _engine.SetSourceEnabled(ctx, link.Kind, true); Render(); _onChanged?.Invoke(); };
        use.Unchecked += (_, _) => { _engine.SetSourceEnabled(ctx, link.Kind, false); Render(); _onChanged?.Invoke(); };
        row.Children.Add(use);

        var mark = new TextBlock
        {
            Text = link.IsWinner ? "✓" : "○",
            Foreground = link.IsWinner ? Ui.Ok : Ui.Muted,
            FontWeight = FontWeights.Bold,
            Width = 20,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(mark);

        var state = !link.Enabled ? L.T("désactivée", "disabled")
            : link.IsWinner ? L.T("utilisée", "used")
            : link.Present ? L.T("disponible (une source au-dessus prime)", "available (a source above wins)")
            : L.T("absente", "absent");
        var name = Ui.Label($"{ResolutionText.Link(link.Source)} — {state}", 12);
        name.VerticalAlignment = VerticalAlignment.Center;
        if (link.IsWinner) name.FontWeight = FontWeights.Bold;
        else if (!link.Enabled || !link.Present) name.Foreground = Ui.Muted;
        row.Children.Add(name);

        // the personal block carries the composer entry point
        if (link.Kind == SourceKind.Personal && _compose is { } compose)
        {
            var composeButton = Ui.Button(L.T("Composer", "Compose"), (_, _) => compose());
            composeButton.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(composeButton);
        }
        return row;
    }

    /// <summary>Thumbnail at the SURFACE ratio, media framed exactly as the fit
    /// decided (crop clipped, letterbox on the neutral background).</summary>
    private static FrameworkElement BuildAdaptedPreview(PreviewResult result)
    {
        double scale = Math.Min(360.0 / result.Target.Width, 220.0 / result.Target.Height);
        double boxW = Math.Floor(Math.Max(40, result.Target.Width * scale));
        double boxH = Math.Floor(Math.Max(20, result.Target.Height * scale));

        var canvas = new Canvas { Width = boxW, Height = boxH, ClipToBounds = true };
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
                var image = new Image
                {
                    Source = bitmap,
                    Stretch = System.Windows.Media.Stretch.Fill,
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
            Background = System.Windows.Media.Brushes.Black,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 6),
            Child = canvas,
            ClipToBounds = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
    }
}
