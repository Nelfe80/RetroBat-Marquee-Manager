using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Infrastructure.Configuration;
using RetroBatMarqueeManager.Application.Services;

namespace RetroBatMarqueeManager.Infrastructure.UI
{
    /// <summary>
    /// EN: Service to manage system tray icon and menu
    /// FR: Service pour gérer l'icône de la barre système et son menu
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly IniConfigService _config;
        private readonly ScreenScraperService _screenScraper;
        private readonly ILogger<TrayIconService> _logger;
        private NotifyIcon? _trayIcon;
        private readonly CancellationTokenSource _cts = new();

        public TrayIconService(IniConfigService config, ScreenScraperService screenScraper, ILogger<TrayIconService> logger)
        {
            _config = config;
            _screenScraper = screenScraper;
            _logger = logger;
        }

        public void Initialize(Action onExit)
        {
            _logger.LogInformation("TrayIconService.Initialize() called");
            _logger.LogInformation($"MinimizeToTray setting: {_config.MinimizeToTray}");

            if (!_config.MinimizeToTray)
            {
                _logger.LogWarning("MinimizeToTray is false, skipping tray icon initialization");
                return;
            }

            try
            {
                _logger.LogInformation("Creating NotifyIcon...");
                
                // Try to load icon from file first
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico");
                Icon? iconToUse = null;
                
                if (File.Exists(iconPath))
                {
                    try
                    {
                        iconToUse = new Icon(iconPath);
                        _logger.LogInformation($"Loaded custom icon from {iconPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to load custom icon: {ex.Message}, using system icon");
                        iconToUse = SystemIcons.Application;
                    }
                }
                else
                {
                    _logger.LogWarning($"Icon file not found at {iconPath}, using system icon");
                    iconToUse = SystemIcons.Application;
                }
                
                // Create tray icon - ALWAYS use a valid icon
                _trayIcon = new NotifyIcon
                {
                    Icon = iconToUse,
                    Text = "RetroBat Marquee Manager",
                    Visible = true
                };
                
                _logger.LogInformation("NotifyIcon created with icon");

                // Localization Helper
                bool IsFrench() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase);

                // Create context menu
                _logger.LogInformation("Creating context menu...");
                var contextMenu = new ContextMenuStrip();
                
                var aboutItem = new ToolStripMenuItem("RetroBat Marquee Manager");
                aboutItem.Enabled = false;
                contextMenu.Items.Add(aboutItem);
                
                // Open Config
                var txtOpenConfig = IsFrench() ? "Ouvrir Configuration" : "Open Config";
                var configItem = new ToolStripMenuItem(txtOpenConfig);
                configItem.Click += (s, e) =>
                {
                     try
                     {
                         // EN: Launch the Configuration Menu instead of opening config.ini text file
                         // FR: Lancer le menu de configuration au lieu d'ouvrir le fichier texte config.ini
                         // Target the Launcher executable explicitly
                         var launcherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RetroBatMarqueeManager.exe");
                         
                         if (File.Exists(launcherPath))
                         {
                              Process.Start(new ProcessStartInfo(launcherPath, "-menu") { UseShellExecute = true });
                         }
                         else
                         {
                              // Fallback if launcher not found (should not happen)
                              Process.Start(new ProcessStartInfo(_config.ConfigPath) { UseShellExecute = true });
                         }
                     }
                     catch (Exception ex)
                     {
                         _logger.LogError($"Failed to open config menu: {ex.Message}");
                     }
                };
                contextMenu.Items.Add(configItem);

                // Clear Cache
                var txtClearCache = IsFrench() ? "Vider le Cache" : "Clear Cache";
                var clearCacheItem = new ToolStripMenuItem(txtClearCache);
                clearCacheItem.Click += (s, e) =>
                {
                     try
                     {
                         var cachePath = _config.CachePath;
                         _logger.LogInformation($"Attempting to clear cache at: {cachePath}");

                         if (Directory.Exists(cachePath))
                         {
                             // Robust delete: Delete files individually to handle locks gracefully (best effort)
                             var files = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
                             int deletedCount = 0;
                             int diffCount = 0;

                             foreach (var file in files)
                             {
                                 try 
                                 {
                                     File.Delete(file);
                                     deletedCount++;
                                 }
                                 catch (IOException) { diffCount++; /* Locked file, likely MPV */ }
                                 catch (Exception) { diffCount++; }
                             }

                             // Try to cleanup empty directories
                             try 
                             {
                                 var dirs = Directory.GetDirectories(cachePath, "*", SearchOption.AllDirectories)
                                                     .OrderByDescending(d => d.Length); // Deepest first
                                 foreach (var d in dirs)
                                 {
                                     try { Directory.Delete(d); } catch { }
                                 }
                             }
                             catch { }

                              // Clear offsets.json as requested
                              try 
                              {
                                   var offsetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offsets.json");
                                   if (File.Exists(offsetsPath))
                                   {
                                       File.Delete(offsetsPath);
                                       deletedCount++;
                                       _logger.LogInformation("Deleted offsets.json");
                                   }

                                   var videoOffsetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "video_offsets.json");
                                   if (File.Exists(videoOffsetsPath))
                                   {
                                       File.Delete(videoOffsetsPath);
                                       deletedCount++;
                                       _logger.LogInformation("Deleted video_offsets.json");
                                   }
                              }
                              catch (Exception ex)
                              {
                                  _logger.LogError($"Failed to delete offsets.json: {ex.Message}");
                                  diffCount++;
                              }

                              // Clear persistent failed scraps cache
                              try
                              {
                                  _screenScraper.ClearFailedScraps();
                              }
                              catch (Exception ex)
                              {
                                  _logger.LogError($"Failed to clear failed scraps cache: {ex.Message}");
                              }

                             var title = "RetroBat Marquee Manager";
                             string msg;
                             
                             if (diffCount > 0)
                             {
                                  msg = IsFrench() 
                                     ? $"Cache vid\u00e9 partiellement ({deletedCount} fichiers). {diffCount} fichiers verrouill\u00e9s." 
                                     : $"Cache partially cleared ({deletedCount} files). {diffCount} files locked.";
                                  _trayIcon!.ShowBalloonTip(3000, title, msg, ToolTipIcon.Warning);
                             }
                             else
                             {
                                  msg = IsFrench() ? "Cache vid\u00e9 avec succ\u00e8s !" : "Cache cleared successfully!";
                                  _trayIcon!.ShowBalloonTip(3000, title, msg, ToolTipIcon.Info);
                             }
                         }
                         else
                         {
                             // Ensure it exists if missing
                             Directory.CreateDirectory(cachePath);
                             var title = "RetroBat Marquee Manager";
                             var msg = IsFrench() ? "Le cache est d\u00e9j\u00e0 vide." : "Cache is already empty.";
                             _trayIcon!.ShowBalloonTip(3000, title, msg, ToolTipIcon.Info);
                         }
                     }
                     catch (Exception ex)
                     {
                         _logger.LogError($"Failed to clear cache: {ex.Message}");
                         var title = IsFrench() ? "Erreur" : "Error";
                         var msg = IsFrench() ? "Impossible de vider le cache." : "Failed to clear cache.";
                         _trayIcon!.ShowBalloonTip(3000, title, msg, ToolTipIcon.Error);
                     }
                };
                contextMenu.Items.Add(clearCacheItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                // Restart
                var txtRestart = IsFrench() ? "Red\u00e9marrer" : "Restart";
                var restartItem = new ToolStripMenuItem(txtRestart);
                restartItem.Click += (s, e) =>
                {
                     _logger.LogInformation("Restart requested via Tray Icon");
                     var appPath = Environment.ProcessPath;
                     var appDir = AppDomain.CurrentDomain.BaseDirectory;
                     
                     if (!string.IsNullOrEmpty(appPath))
                     {
                         // Use cmd.exe to wait 2 seconds before restarting to release the Mutex
                         // Ensure Working Directory is set so config files are found
                         var startInfo = new ProcessStartInfo
                         {
                             FileName = "cmd.exe",
                             Arguments = $"/c timeout /t 2 /nobreak & start \"\" /d \"{appDir}\" \"{appPath}\"",
                             UseShellExecute = false,
                             CreateNoWindow = true,
                             WorkingDirectory = appDir
                         };
                         
                         Process.Start(startInfo);
                         onExit(); 
                         Environment.Exit(0);
                     }
                };
                contextMenu.Items.Add(restartItem);

                contextMenu.Items.Add(new ToolStripSeparator());
                
                var txtExit = IsFrench() ? "Quitter" : "Exit";
                var exitItem = new ToolStripMenuItem(txtExit);
                exitItem.Click += (s, e) => 
                {
                    _logger.LogInformation("Exit menu item clicked");
                    onExit();
                };
                contextMenu.Items.Add(exitItem);

                _trayIcon.ContextMenuStrip = contextMenu;
                _logger.LogInformation("Context menu created and attached");

                _logger.LogInformation("System tray icon initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize system tray icon: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                // Create minimal tray icon even on error to prevent app from closing
                try
                {
                    _trayIcon = new NotifyIcon
                    {
                        Icon = SystemIcons.Application,
                        Text = "RetroBat Marquee Manager (Error)",
                        Visible = true
                    };
                    _logger.LogWarning("Created minimal tray icon after error");
                }
                catch
                {
                    _logger.LogError("Failed to create even minimal tray icon");
                }
            }
        }

        public void RunMessageLoop()
        {
            _logger.LogInformation("RunMessageLoop() called");
            
            // ALWAYS run the message loop, even if tray icon failed
            // This prevents the application from exiting immediately
            _logger.LogInformation("Starting Windows Forms message loop...");
            System.Windows.Forms.Application.Run();
            _logger.LogInformation("Windows Forms message loop exited");
        }

        public void Dispose()
        {
            _logger.LogInformation("TrayIconService.Dispose() called");
            _cts.Cancel();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _logger.LogInformation("Tray icon disposed");
            }
            System.Windows.Forms.Application.Exit();
        }
    }
}
