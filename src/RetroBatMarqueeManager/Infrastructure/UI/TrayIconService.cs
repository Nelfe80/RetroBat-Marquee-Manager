using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using RetroBatMarqueeManager.Infrastructure.Configuration;

namespace RetroBatMarqueeManager.Infrastructure.UI;

public sealed class TrayIconService : IDisposable
{
    private readonly IniConfigService _config;
    private readonly ILogger<TrayIconService> _logger;
    private NotifyIcon? _icon;
    private ApplicationContext? _context;
    private Control? _dispatcher;

    public TrayIconService(IniConfigService config, ILogger<TrayIconService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Initialize(Action exit)
    {
        var french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase);
        var iconPath = Path.Combine(_config.BaseDirectory, "Resources", "icon.ico");
        Icon icon;
        try { icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application; }
        catch { icon = SystemIcons.Application; }

        // Keep a handle owned by the STA tray thread so shutdown requests coming
        // from hosted services can be marshalled back to the Windows Forms loop.
        _dispatcher = new Control();
        _ = _dispatcher.Handle;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"MarqueeManager {typeof(TrayIconService).Assembly.GetName().Version}") { Enabled = false });
        menu.Items.Add(Item(french ? "Ouvrir config.ini" : "Open config.ini", () => Open(_config.ConfigPath)));
        menu.Items.Add(Item(french ? "Ouvrir les logs" : "Open logs", () => Open(Path.GetDirectoryName(_config.LogFilePath) ?? _config.BaseDirectory)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(french ? "Quitter" : "Exit", exit));
        _icon = new NotifyIcon { Icon = icon, Text = "RetroBat MarqueeManager", Visible = true, ContextMenuStrip = menu };
    }

    private ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Tray action failed"); }
        };
        return item;
    }

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public void RunMessageLoop()
    {
        _context = new ApplicationContext();
        System.Windows.Forms.Application.Run(_context);
    }

    public void StopMessageLoop()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is { IsDisposed: false } && dispatcher.InvokeRequired)
        {
            try
            {
                dispatcher.BeginInvoke(new Action(StopMessageLoop));
                return;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Unable to marshal tray shutdown to its UI thread"); }
        }

        try { _context?.ExitThread(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Unable to exit tray application context"); }
        try { System.Windows.Forms.Application.ExitThread(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Unable to exit tray application"); }
    }

    public void Dispose()
    {
        if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
        _context?.Dispose();
        _dispatcher?.Dispose();
    }
}
