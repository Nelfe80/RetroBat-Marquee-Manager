using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RetroBatMarqueeManager.Infrastructure.UI
{
    public class MarqueeForm : Form
    {
        public IntPtr RenderHandle => this.Handle;
        private int _targetScreen;
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public MarqueeForm(int screenNumber, Microsoft.Extensions.Logging.ILogger logger)
        {
            _targetScreen = screenNumber;
            _logger = logger;
            
            // Basic setup
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.ShowInTaskbar = false;
            this.TopMost = true; // --ontop
            this.StartPosition = FormStartPosition.Manual;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PositionWindow();
        }

        private void PositionWindow()
        {
            Screen[] screens = Screen.AllScreens;
            // Config usually passes "1", "2". If user says "1", they might mean the first one.
            // But we should verify if "screen 1" corresponds to Display1.
            // Config passed is 0-based index (0=Primary, 1=Secondary) according to config.ini comments
            int screenIndex = _targetScreen;
            if (screenIndex < 0) screenIndex = 0;
            if (screenIndex >= screens.Length) screenIndex = 0; // Fallback to primary

            var screen = screens[screenIndex];
            _logger.LogInformation($"[MarqueeForm] Targeting Screen Index: {screenIndex} (Config: {_targetScreen}). Found: {screens.Length} screens.");
            _logger.LogInformation($"[MarqueeForm] Screen Bounds: {screen.Bounds}");
            
            this.Location = screen.Bounds.Location;
            this.Size = screen.Bounds.Size;
            this.StartPosition = FormStartPosition.Manual;
            this.WindowState = FormWindowState.Normal; // Reset first
            this.Bounds = screen.Bounds; // Force bounds
            this.WindowState = FormWindowState.Maximized; // Ensure FULLSCREEN
        }
    }
}
