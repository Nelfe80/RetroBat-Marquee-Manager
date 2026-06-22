using System.Drawing;
using System.Windows.Forms;

namespace RetroBatMarqueeManager.Launcher.Helpers
{
    // EN: Custom renderer for MenuStrip to support dark theme
    // FR: Rendu personnalisé pour MenuStrip afin de supporter le thème sombre
    public class DarkThemeRenderer : ToolStripProfessionalRenderer
    {
        public DarkThemeRenderer() : base(new DarkThemeColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // EN: Force white text color for all items regardless of parent settings
            // FR: Forcer la couleur du texte en blanc pour tous les éléments
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    public class DarkThemeColorTable : ProfessionalColorTable
    {
        private Color _backColor = Color.FromArgb(45, 45, 48);
        private Color _selectedColor = Color.FromArgb(63, 63, 70);
        private Color _borderColor = Color.FromArgb(63, 63, 70);

        // EN: Selection and hover colors / FR: Couleurs de sélection et de survol
        public override Color MenuItemSelected => _selectedColor;
        public override Color MenuItemSelectedGradientBegin => _selectedColor;
        public override Color MenuItemSelectedGradientEnd => _selectedColor;
        
        public override Color MenuItemPressedGradientBegin => _selectedColor;
        public override Color MenuItemPressedGradientMiddle => _selectedColor;
        public override Color MenuItemPressedGradientEnd => _selectedColor;

        // EN: Dropdown menu background and border / FR: Fond et bordure du menu déroulant
        public override Color ToolStripDropDownBackground => _backColor;
        public override Color MenuBorder => _borderColor;
        public override Color MenuItemBorder => _borderColor;

        // EN: Image margin (icon area) background / FR: Fond de la marge d'image (zone d'icône)
        public override Color ImageMarginGradientBegin => _backColor;
        public override Color ImageMarginGradientMiddle => _backColor;
        public override Color ImageMarginGradientEnd => _backColor;

        // EN: Fix for separator and button selection / FR: Correction pour le séparateur et la sélection de bouton
        public override Color SeparatorDark => _borderColor;
        public override Color SeparatorLight => Color.FromArgb(80, 80, 80);
        public override Color ButtonSelectedGradientBegin => _selectedColor;
        public override Color ButtonSelectedGradientMiddle => _selectedColor;
        public override Color ButtonSelectedGradientEnd => _selectedColor;
        public override Color ButtonPressedGradientBegin => _selectedColor;
        public override Color ButtonPressedGradientMiddle => _selectedColor;
        public override Color ButtonPressedGradientEnd => _selectedColor;
    }
}
