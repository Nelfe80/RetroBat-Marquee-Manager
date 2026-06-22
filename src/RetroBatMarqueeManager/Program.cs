using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Configuration;
using RetroBatMarqueeManager.Infrastructure.Processes;
using RetroBatMarqueeManager.Application.Services;
using RetroBatMarqueeManager.Application.Workflows;
using RetroBatMarqueeManager.Infrastructure.UI;

public class Program
{
    // Windows API for hiding console window
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    
    private static IHost? _host;
    private static TrayIconService? _trayService;

    [STAThread]
    public static async Task Main(string[] args)
    {
        // Set High DPI Mode for UI elements (if any)
        // This requires a reference to System.Windows.Forms
        // Application.SetHighDpiMode(HighDpiMode.SystemAware); // Uncomment if System.Windows.Forms is used and needed

        const string PipeName = "RetroBatMarqueeManagerPipe";

        // --- CLIENT MODE (Send IPC Command) ---
        if (args.Length > 0 && args[0].StartsWith("-"))
        {
            try
            {
                var eventName = args[0].TrimStart('-');
                var eventArgs = string.Join("|", args.Skip(1));
                var message = $"{eventName}|{eventArgs}";

                using var client = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out);
                await client.ConnectAsync(2000);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                await writer.WriteAsync(message);
                return;
            }
            catch
            {
                return;
            }
        }

        // --- SERVER MODE ---

        // Support Windows-1252 encoding
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Kill any previous instance of this process before acquiring the mutex
        var currentId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var currentName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        foreach (var old in System.Diagnostics.Process.GetProcessesByName(currentName))
        {
            if (old.Id == currentId) continue;
            try
            {
                Console.WriteLine($"[INFO] Killing previous instance PID={old.Id}...");
                old.Kill();
                old.WaitForExit(3000);
            }
            catch { }
        }

        // Mutex for Single Instance
        const string mutexName = "Global\\RetroBatMarqueeManager_Mutex";
        bool createdNew;
        using (var mutex = new System.Threading.Mutex(true, mutexName, out createdNew))
        {
            if (!createdNew)
            {
                // Already running (race condition: another instance started in parallel)
                return;
            }

            // Pre-load config to check for settings
            var configService = new IniConfigService(null); // Logger is null initially

            // EN: Log Rotation Logic (Max 2 files, 1.5MB)
            // FR: Logique de rotation des logs (Max 2 fichiers, 1.5MB)
            if (configService.LogToFile)
            {
                try 
                {
                    var logPath = configService.LogFilePath;
                    if (File.Exists(logPath))
                    {
                        var info = new FileInfo(logPath);
                        // 1.5 MB = 1.5 * 1024 * 1024 = 1,572,864 bytes
                        if (info.Length > 1_572_864)
                        {
                            var oldPath = logPath + ".old";
                            if (File.Exists(oldPath))
                            {
                                File.Delete(oldPath);
                            }
                            File.Move(logPath, oldPath);
                            Console.WriteLine($"[INFO] Log file rotated: {logPath} -> {oldPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to rotate log file: {ex.Message}");
                }
            }

            // Hide console window if MinimizeToTray is enabled
            if (configService.MinimizeToTray)
            {
                var handle = GetConsoleWindow();
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_HIDE);
                }
            }

            // EN: Register emergency cleanup handlers for crash/forced kill scenarios
            // FR: Enregistrer les handlers de nettoyage d'urgence pour crash/kill forcé
            RegisterEmergencyCleanupHandlers();
    
            _host = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureLogging((hostContext, logging) => 
                {
                    if (configService.LogToFile)
                    {
                        var logPath = configService.LogFilePath;
                        logging.AddProvider(new RetroBatMarqueeManager.Infrastructure.Logging.SimpleFileLoggerProvider(logPath));
                    }
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Core / Infrastructure
                    services.AddSingleton<IConfigService>(configService); // Use existing instance (interface)
                    services.AddSingleton<IniConfigService>(configService); // Also register concrete type for TrayIconService
                    services.AddSingleton<IDmdConfigService, DmdConfigService>();
                    services.AddSingleton<IEsSettingsService>(sp => new EsSettingsService(configService.RetroBatPath, sp.GetRequiredService<ILogger<EsSettingsService>>()));
                    services.AddSingleton<IProcessService, SystemProcessService>();
                    services.AddSingleton<MpvController>(); // Register MPV Controller
                    services.AddSingleton<RetroBatMarqueeManager.Infrastructure.Native.DmdDeviceWrapper>(); // Native DMD Wrapper
                    
                    // Application / Services
                    services.AddSingleton<IDmdService, DmdService>();
                    services.AddSingleton<IMarqueeFileFinder, MarqueeFileFinderService>();
                    services.AddSingleton<ImageConversionService>();
                    services.AddSingleton<MarqueeWorkflow>();
                    services.AddSingleton<OffsetStorageService>(); // Persistence (Images composées / Composed Images)
                    services.AddSingleton<VideoOffsetStorageService>(); // Persistence (Vidéos / Videos)
                    services.AddSingleton<IOverlayTemplateService, OverlayTemplateService>();
                    services.AddSingleton<VideoMarqueeService>(); // Video Generation Service
                    services.AddSingleton<IInputService, RetroBatMarqueeManager.Infrastructure.Input.KeyboardInputService>(); // Input
                    services.AddSingleton<RetroBatMarqueeManager.Infrastructure.Installation.ScriptInstallerService>(); // Auto-Install Scripts
                    services.AddSingleton<RetroBatMarqueeManager.Infrastructure.Installation.ScriptInstallerService>(); // Auto-Install Scripts
                    services.AddSingleton<RetroBatMarqueeManager.Infrastructure.Installation.AutoStartService>(); // Auto-Start Management
                    // Scrapers registration
                    services.AddSingleton<ScreenScraperService>(); // Concrete for TrayIcon
                    services.AddSingleton<IScraperService>(sp => sp.GetRequiredService<ScreenScraperService>()); // Interface map
                    
                    services.AddSingleton<ArcadeItaliaScraperService>();
                    services.AddSingleton<IScraperService>(sp => sp.GetRequiredService<ArcadeItaliaScraperService>());

                    services.AddSingleton<IScraperManager, ScraperManager>();

                    // EN: RetroAchievements (API Client + Service) / FR: RetroAchievements (Client API + Service)
                    services.AddSingleton<RetroBatMarqueeManager.Infrastructure.Api.RetroAchievementsApiClient>(sp =>
                    {
                        var httpClient = new HttpClient();
                        var config = sp.GetRequiredService<IConfigService>();
                        var esSettings = sp.GetRequiredService<IEsSettingsService>();
                        var logger = sp.GetRequiredService<ILogger<RetroBatMarqueeManager.Infrastructure.Api.RetroAchievementsApiClient>>();
                        return new RetroBatMarqueeManager.Infrastructure.Api.RetroAchievementsApiClient(httpClient, config, esSettings, logger);
                    });
                    
                    // EN: Register as Singleton first so it can be injected into MarqueeWorkflow
                    // FR: Enregistrer comme Singleton d'abord pour qu'il puisse être injecté dans MarqueeWorkflow
                    services.AddSingleton<RetroBatMarqueeManager.Application.Services.RetroAchievementsService>();
                    
                    // EN: Then register as HostedService (same instance)
                    // FR: Puis enregistrer comme HostedService (même instance)
                    services.AddHostedService<RetroBatMarqueeManager.Application.Services.RetroAchievementsService>(sp => 
                        sp.GetRequiredService<RetroBatMarqueeManager.Application.Services.RetroAchievementsService>());

                    
                    // EN: Only register TrayIconService if explorer.exe is running (not in custom shell)
                    // FR: Enregistrer TrayIconService seulement si explorer.exe tourne (pas en shell personnalisé)
                    if (IsExplorerRunning())
                    {
                        services.AddSingleton<TrayIconService>(); // System Tray
                    }
                    else
                    {
                        Console.WriteLine("[INFO] explorer.exe not detected - running in headless mode (no systray)");
                    }
                    
                    // Hosted Service
                    services.AddHostedService<Worker>();
                    services.AddHostedService<RetroBatMonitorService>(); // Auto-Shutdown Monitor
                    services.AddHostedService<RetroBatMarqueeManager.Application.Services.WebSocketListenerService>();
                })
                .Build();
    
            // Auto-install EmulationStation scripts on first run
            try
            {
                var installer = _host.Services.GetRequiredService<RetroBatMarqueeManager.Infrastructure.Installation.ScriptInstallerService>();
                installer.InstallScriptsIfNeeded();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Installer failed: {ex.Message}");
            }

            // Configure AutoStart
            try
            {
                var autoStart = _host.Services.GetRequiredService<RetroBatMarqueeManager.Infrastructure.Installation.AutoStartService>();
                autoStart.ConfigureAutoStart();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] AutoStart failed: {ex.Message}");
            }
            Console.WriteLine("[DEBUG] AutoStart sequence finished.");

            // Write to a file to prove we reached this point
            try
            {
                var debugInfo = $"Reached tray check at {DateTime.Now}\nMinimizeToTray={configService.MinimizeToTray}\n";
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), debugInfo);
            }
            catch { }

            // Debug logging to console (always visible)
            Console.WriteLine($"[DEBUG] MinimizeToTray setting: {configService.MinimizeToTray}");
            Console.WriteLine($"[DEBUG] About to check MinimizeToTray condition...");

            // Initialize System Tray if enabled
            if (configService.MinimizeToTray)
            {
                Console.WriteLine("[DEBUG] MinimizeToTray is TRUE - starting tray mode");
                
                // Hide Console Window
                var handle = GetConsoleWindow();
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_HIDE);
                    FreeConsole();
                }
                
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), 
                        "Entered tray mode branch\n");
                    
                    // Initialize Windows Forms BEFORE creating any Windows Forms objects
                    System.Windows.Forms.Application.EnableVisualStyles();
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    
                    // Start host services asynchronously (non-blocking)
                    await _host.StartAsync();
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), 
                        "Host started\n");

                    // Initialize System Tray (if available)
                    // EN: In headless mode (no explorer.exe), TrayIconService is not registered
                    // FR: En mode headless (pas d'explorer.exe), TrayIconService n'est pas enregistré
                    _trayService = _host.Services.GetService<TrayIconService>();
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), 
                        $"TrayService obtained: {(_trayService != null ? "Yes" : "No (headless)")}\n");
                    
                    if (_trayService != null)
                    {
                        _trayService.Initialize(OnExit);
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), 
                            "TrayService initialized\n");
                    }

                    // Run Windows Forms message loop on main thread (required for tray icon)
                    // EN: Only run message loop if tray icon exists (headless mode = no message loop)
                    // FR: Exécuter boucle de messages seulement si icone tray existe (headless = pas de boucle)
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), 
                        $"About to start message loop (tray exists: {(_trayService != null)})\n");
                    
                    if (_trayService != null)
                    {
                        // Tray mode: Run Windows Forms message loop (blocking until app exit)
                        System.Windows.Forms.Application.Run();
                    }
                    else
                    {
                        // Headless mode: Just wait for host to stop
                        Console.WriteLine("[INFO] Running in headless mode - waiting for shutdown signal");
                        await _host.WaitForShutdownAsync();
                    }

                    // When message loop exits, stop the host
                    await _host.StopAsync();
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray_error.txt"), 
                        $"Tray mode error: {ex.Message}\n{ex.StackTrace}");
                    Console.WriteLine($"[ERROR] Tray mode failed: {ex.Message}");
                    Console.WriteLine($"[ERROR] Tray mode failed: {ex.Message}");
                    Console.WriteLine($"[ERROR] Stack: {ex.StackTrace}");
                    try 
                    {
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), 
                            $"CRASH [TrayMode] {DateTime.Now}\n{ex.Message}\n{ex.StackTrace}");
                    } catch {}
                    
                    // Don't call _host.Run() here - it causes double-start!
                    // Just exit gracefully
                    Environment.Exit(1);
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] MinimizeToTray is FALSE - starting normal mode");
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), 
                    "Normal mode (MinimizeToTray=false)\n");
                // Run normally without tray
                _host.Run();
            }
        }
    }

    private static void OnExit()
    {
        try
        {
            // Stop MPV gracefully by killing the process
            var processService = _host?.Services.GetService<IProcessService>();
            if (processService != null)
            {
                processService.KillProcess("dmdext");
            }
        }
        catch (Exception ex)
        {
            try 
            {
               File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_checkpoint.txt"), $"Error stopping processes: {ex.Message}\n");
            } catch {}
            //Console.WriteLine($"[ERROR] Failed to stop MPV: {ex.Message}");
        }

        _trayService?.Dispose();
        _host?.StopAsync().Wait();
        System.Windows.Forms.Application.Exit();
        Environment.Exit(0);
    }

    /// <summary>
    /// EN: Register emergency cleanup handlers for crash, forced kill (Task Manager), or Ctrl+C scenarios
    /// FR: Enregistrer les handlers de nettoyage d'urgence pour crash, kill forcé (Task Manager), ou Ctrl+C
    /// </summary>
    private static void RegisterEmergencyCleanupHandlers()
    {
        // EN: Handle application crash or forced termination
        // FR: Gérer le crash de l'application ou l'arrêt forcé
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            try
            {
                EmergencyCleanup();
            }
            catch
            {
                // EN: Absolute last resort - cannot log
                // FR: Dernier recours absolu - impossible de logger
            }
        };

        // EN: Handle Ctrl+C in console mode
        // FR: Gérer Ctrl+C en mode console
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // EN: Prevent immediate exit / FR: Empêcher la sortie immédiate
            try
            {
                EmergencyCleanup();
                Environment.Exit(0);
            }
            catch
            {
                Environment.Exit(1);
            }
        };
    }

    /// <summary>
    /// EN: Emergency cleanup when normal shutdown handlers cannot run
    /// FR: Nettoyage d'urgence quand les handlers d'arrêt normaux ne peuvent pas s'exécuter
    /// </summary>
    private static void EmergencyCleanup()
    {
        try
        {
            // EN: Kill orphaned processes directly using System.Diagnostics
            // FR: Tuer les processus orphelins directement via System.Diagnostics
            var processesToKill = new[] { "mpv", "dmdext" };
            
            foreach (var processName in processesToKill)
            {
                try
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                    foreach (var proc in processes)
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(1000); // EN: Wait 1s max / FR: Attendre 1s max
                        }
                        catch { /* EN: Process already dead / FR: Processus déjà mort */ }
                    }
                }
                catch { /* EN: Process not found / FR: Processus introuvable */ }
            }

            // EN: Create marker file to signal graceful shutdown to launcher watchdog
            // FR: Créer un fichier marqueur pour signaler l'arrêt gracieux au watchdog launcher
            try
            {
                var markerPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".graceful_exit");
                System.IO.File.WriteAllText(markerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { /* EN: Marker file non-critical / FR: Fichier marqueur non-critique */ }
        }
        catch
        {
            // EN: Absolute last resort - cannot do anything
            // FR: Dernier recours absolu - impossible de faire quoi que ce soit
        }
    }
    
    /// <summary>
    /// EN: Check if explorer.exe is running (standard Windows shell)
    /// FR: Vérifier si explorer.exe tourne (shell Windows standard)
    /// </summary>
    private static bool IsExplorerRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("explorer").Length > 0;
        }
        catch
        {
            return false;
        }
    }

}
