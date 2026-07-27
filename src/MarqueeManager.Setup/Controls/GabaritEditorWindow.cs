using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Edits the GENERAL template of a surface (its fanart+gradient+logo recipe applied
/// to every system/game of that surface). It configures the runtime's existing
/// CompositionTemplate recipe — no new compositor. The per-system render happens
/// through that renderer once wired to the "Générée" link.
/// </summary>
public sealed class GabaritEditorWindow : Window
{
    public GabaritEditorWindow(string pluginRoot, string surfaceId, string scope, int targetWidth, int targetHeight)
    {
        var store = new GabaritStore(pluginRoot);
        var gabarit = store.Load(surfaceId, scope);
        var scopeWord = scope == GabaritStore.GameScope ? L.T("jeux", "games") : L.T("systèmes", "systems");

        Title = L.T("Gabarit général", "General template");
        Width = 540;
        Height = 460;
        Background = Ui.Background;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(Ui.Title(L.T("Gabarit général", "General template")));
        panel.Children.Add(Ui.Subtitle(L.T(
            $"Surface {surfaceId} — {targetWidth}×{targetHeight}. Cette recette s'applique à TOUS les {scopeWord} de cette surface (fanart + dégradé + logo, par système).",
            $"Surface {surfaceId} — {targetWidth}×{targetHeight}. This recipe applies to ALL {scopeWord} of this surface (fanart + gradient + logo, per system).")));

        // background
        panel.Children.Add(Ui.SectionHeader(L.T("Fond", "Background")));
        var background = Ui.ComboBox(240);
        background.Items.Add(new ComboBoxItem { Content = L.T("Fanart du système (cadré cover)", "System fanart (cover)"), Tag = "fanart" });
        background.Items.Add(new ComboBoxItem { Content = L.T("Fond noir", "Black"), Tag = "black" });
        background.SelectedIndex = gabarit.Background.Equals("black", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        panel.Children.Add(background);

        // gradient
        var gradient = Ui.CheckBox(L.T("Dégradé de lisibilité sous le logo", "Readability gradient under the logo"), gabarit.Gradient);
        gradient.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(gradient);

        // logo budget
        panel.Children.Add(Ui.SectionHeader(L.T("Taille maximale du logo (% de la surface)", "Max logo size (% of the surface)")));
        var logoRow = new WrapPanel();
        var widthLabel = Ui.MutedLabel(L.T("Largeur", "Width"));
        widthLabel.Margin = new Thickness(0, 0, 6, 0);
        widthLabel.VerticalAlignment = VerticalAlignment.Center;
        logoRow.Children.Add(widthLabel);
        var widthBox = Ui.TextBox(((int)Math.Round(gabarit.LogoMaxWidth * 100)).ToString(), 60);
        logoRow.Children.Add(widthBox);
        var heightLabel = Ui.MutedLabel(L.T("Hauteur", "Height"));
        heightLabel.Margin = new Thickness(14, 0, 6, 0);
        heightLabel.VerticalAlignment = VerticalAlignment.Center;
        logoRow.Children.Add(heightLabel);
        var heightBox = Ui.TextBox(((int)Math.Round(gabarit.LogoMaxHeight * 100)).ToString(), 60);
        logoRow.Children.Add(heightBox);
        panel.Children.Add(logoRow);
        panel.Children.Add(Ui.MutedLabel(L.T(
            "Le logo est toujours centré dans une zone sûre ; ces valeurs bornent sa taille max.",
            "The logo is always centered in a safe zone; these values cap its maximum size.")));

        var status = Ui.MutedLabel("");
        status.Margin = new Thickness(0, 8, 0, 0);

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(Ui.Button(L.T($"Enregistrer pour tous les {scopeWord} de cette surface", $"Save for all {scopeWord} of this surface"), (_, _) =>
        {
            var def = new GabaritDefinition(
                (background.SelectedItem as ComboBoxItem)?.Tag as string ?? "fanart",
                gradient.IsChecked == true,
                ParsePercent(widthBox.Text, gabarit.LogoMaxWidth),
                ParsePercent(heightBox.Text, gabarit.LogoMaxHeight));
            store.Save(surfaceId, scope, def);
            DialogResult = true;
            Close();
        }, primary: true));
        actions.Children.Add(Ui.Button(L.T("Annuler", "Cancel"), (_, _) => { DialogResult = false; Close(); }));
        panel.Children.Add(actions);
        panel.Children.Add(status);

        Content = panel;
    }

    private static double ParsePercent(string text, double fallback)
        => int.TryParse(text.Trim().TrimEnd('%', ' '), out var value) && value is > 0 and <= 100
            ? value / 100.0
            : fallback;
}
