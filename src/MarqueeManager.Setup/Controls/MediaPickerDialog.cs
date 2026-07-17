using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>One selectable media candidate: where it comes from + its file.</summary>
public sealed record MediaCandidate(string SourceLabel, string Path);

/// <summary>
/// "Which fanart?" — a modal grid of every available candidate of a media kind,
/// grouped by source (APIExpose library, downloaded files…), thumbnails decoded
/// off the UI thread. Click one to pick it.
/// </summary>
public sealed class MediaPickerDialog : Window
{
    public string? SelectedPath { get; private set; }

    public MediaPickerDialog(string kindLabel, IReadOnlyList<MediaCandidate> candidates)
    {
        Title = L.T($"Choisir : {kindLabel}", $"Pick: {kindLabel}");
        Width = 760;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(Ui.Subtitle(L.T("Cliquez sur le média à utiliser.", "Click the media to use.")));

        if (candidates.Count == 0)
        {
            panel.Children.Add(Ui.Label(L.T(
                "Aucun média de ce type — récupérez-en via « Récupérer des médias en ligne ».",
                "No media of this kind — fetch some via “Fetch media online”.")));
        }

        foreach (var group in candidates.GroupBy(c => c.SourceLabel))
        {
            panel.Children.Add(Ui.SectionHeader(group.Key));
            var grid = new WrapPanel();
            foreach (var candidate in group)
            {
                grid.Children.Add(Thumb(candidate));
            }
            panel.Children.Add(grid);
        }

        var close = Ui.Button(L.T("Annuler", "Cancel"), (_, _) => DialogResult = false);
        close.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(close);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement Thumb(MediaCandidate candidate)
    {
        var host = new StackPanel { Width = 168, Margin = new Thickness(0, 0, 10, 10) };
        var border = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 96,
            Cursor = Cursors.Hand
        };
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(4) };
        border.Child = image;
        _ = Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(candidate.Path);
                bitmap.DecodePixelWidth = 330;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                Dispatcher.BeginInvoke(() => image.Source = bitmap);
            }
            catch
            {
                Dispatcher.BeginInvoke(() => border.Child = Ui.MutedLabel(L.T("aperçu impossible", "no preview")));
            }
        });
        border.MouseLeftButtonDown += (_, _) =>
        {
            SelectedPath = candidate.Path;
            DialogResult = true;
        };
        host.Children.Add(border);
        var name = Ui.MutedLabel(Path.GetFileName(candidate.Path), 10);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextAlignment = TextAlignment.Center;
        host.Children.Add(name);
        return host;
    }
}
