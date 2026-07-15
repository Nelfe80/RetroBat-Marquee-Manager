using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Pins the game's light profile: which bulb (technology/era from bulbs.xml) and
/// which cabinet profile (cabinets.xml) instead of the automatic grammar. Stored
/// in the game's overrides\effects file, section "lighting" — the runtime's
/// LightingLibraries.Resolve consults it before the grammar.
/// </summary>
public sealed class LightingProfileCard : UserControl
{
    private readonly EffectsOverrideStore _store;
    private readonly string _system;
    private readonly string _rom;
    private readonly ComboBox _bulbs = Ui.ComboBox(330);
    private readonly ComboBox _cabinets = Ui.ComboBox(330);
    private readonly TextBlock _status = Ui.MutedLabel("", 12);

    public LightingProfileCard(string pluginRoot, string system, string rom)
    {
        _store = new EffectsOverrideStore(pluginRoot);
        _system = system;
        _rom = rom;

        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Profil d'éclairage", "Light profile")));
        card.Children.Add(Ui.MutedLabel(L.T(
            "Par défaut le runtime choisit l'ampoule d'époque via sa grammaire (année, constructeur…). Épinglez ici un profil précis pour ce jeu.",
            "By default the runtime picks the period bulb through its grammar (year, manufacturer…). Pin an exact profile for this game here.")));

        var lighting = Path.Combine(pluginRoot, "resources", "lighting");
        var (savedBulb, savedCabinet) = _store.LoadLightingProfile(system, rom);

        _bulbs.Items.Add(new ComboBoxItem { Content = L.T("Automatique (grammaire)", "Automatic (grammar)"), Tag = "" });
        try
        {
            foreach (var bulb in XDocument.Load(Path.Combine(lighting, "bulbs.xml")).Descendants("bulb"))
            {
                var id = (string?)bulb.Attribute("id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var technology = (string?)bulb.Attribute("technology") ?? "";
                var item = new ComboBoxItem { Content = $"{Humanize(id)}  ·  {technology}", Tag = id };
                _bulbs.Items.Add(item);
                if (id.Equals(savedBulb, StringComparison.OrdinalIgnoreCase)) _bulbs.SelectedItem = item;
            }
        }
        catch
        {
            // missing library: automatic only
        }
        if (_bulbs.SelectedItem == null) _bulbs.SelectedIndex = 0;
        card.Children.Add(Ui.Row(L.T("Ampoule", "Bulb"), _bulbs));

        _cabinets.Items.Add(new ComboBoxItem { Content = L.T("Automatique (grammaire)", "Automatic (grammar)"), Tag = "" });
        try
        {
            foreach (var profile in XDocument.Load(Path.Combine(lighting, "cabinets.xml")).Descendants("profile"))
            {
                var id = (string?)profile.Attribute("id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var item = new ComboBoxItem { Content = Humanize(id), Tag = id };
                _cabinets.Items.Add(item);
                if (id.Equals(savedCabinet, StringComparison.OrdinalIgnoreCase)) _cabinets.SelectedItem = item;
            }
        }
        catch
        {
            // missing library: automatic only
        }
        if (_cabinets.SelectedItem == null) _cabinets.SelectedIndex = 0;
        card.Children.Add(Ui.Row(L.T("Profil de borne", "Cabinet profile"), _cabinets));

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer le profil", "Save the profile"), (_, _) => Save(), primary: true));
        card.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        card.Children.Add(_status);
        Content = card;
    }

    private void Save()
    {
        var bulb = (_bulbs.SelectedItem as ComboBoxItem)?.Tag as string;
        var cabinet = (_cabinets.SelectedItem as ComboBoxItem)?.Tag as string;
        _store.SaveLightingProfile(_system, _rom,
            string.IsNullOrEmpty(bulb) ? null : bulb,
            string.IsNullOrEmpty(cabinet) ? null : cabinet);
        _status.Text = string.IsNullOrEmpty(bulb) && string.IsNullOrEmpty(cabinet)
            ? L.T("Profil remis en automatique.", "Profile back to automatic.")
            : L.T("Profil épinglé pour ce jeu — appliqué à la prochaine scène.",
                "Profile pinned for this game — applied at the next scene.");
        _status.Foreground = Ui.Ok;
    }

    private static string Humanize(string id) => id.Replace('_', ' ');
}
