using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;

namespace RetroBatMarqueeManager.Launcher
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // EN: Ultra-early logging to diagnose startup issues
            // FR: Logging très précoce pour diagnostiquer problèmes de démarrage
            try
            {
                var startupLog = Path.Combine(Path.GetTempPath(), "RetroBatMarqueeManager_LauncherStartup.log");
                File.AppendAllText(startupLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Launcher started with {args.Length} args\n");
            }
            catch { /* Ignore logging errors */ }
            
            // 1. Locate Application
            var appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RetroBatMarqueeManager.App.exe");
            if (!File.Exists(appPath))
            {
                LogLauncher($"FATAL ERROR: Application not found at {appPath}");
                return;
            }
            
            // EN: Handle -menu argument OR First Run (missing config)
            // FR: Gérer argument -menu OU Premier Lancement (config absente)
            var configPathCheck = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            bool isFirstRun = !File.Exists(configPathCheck);
            bool isMenuArg = args.Length > 0 && args[0].Equals("-menu", StringComparison.OrdinalIgnoreCase);

            if (isMenuArg || isFirstRun)
            {
                // EN: If First Run, start Watchdog (Service) in parallel to generate config
                // FR: Si Premier Lancement, lancer Watchdog (Service) en parallèle pour générer config
                if (isFirstRun)
                {
                    Task.Run(() => RunWatchdog(appPath, args));
                }

                // EN: Prevent multiple instances of config menu
                // FR: Empêcher les instances multiples du menu de configuration
                bool createdNew;
                using (var mutex = new System.Threading.Mutex(true, "RetroBatMarqueeManager_ConfigMenu_Mutex", out createdNew))
                {
                    if (!createdNew)
                    {
                        // Already running - find and focus
                        Process current = Process.GetCurrentProcess();
                        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                        {
                            if (process.Id != current.Id)
                            {
                                NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SW_RESTORE);
                                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                                return;
                            }
                        }
                        return;
                    }

                    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    // Open with First Run flag if applicable
                    Application.Run(new Forms.ConfigMenuForm(configPath, isFirstRun));
                }
                return; // Exit after closing config form
            }
            
            // 2. Check for .NET 8 Desktop Runtime
            if (!IsNet8DesktopInstalled())
            {
                // EN: In headless mode (no explorer.exe), MessageBox won't work - log to file instead
                // FR: En mode headless (pas d'explorer.exe), MessageBox ne marche pas - log dans fichier
                LogLauncher("ERROR: .NET 8 Desktop Runtime not found");
                
                // Try to open help page (will fail silently in headless)
                var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Install_DotNet8.html");
                if (File.Exists(htmlPath))
                {
                    try { Process.Start(htmlPath); }
                    catch { /* Headless mode - ignore */ }
                }
                else
                {
                    try { Process.Start("https://dotnet.microsoft.com/en-us/download/dotnet/8.0"); }
                    catch { /* Headless mode - ignore */ }
                }
                return; // Exit Launcher
            }

            // 3. Normal Startup - Run Watchdog Blocking
            RunWatchdog(appPath, args);
        }

        static void RunWatchdog(string appPath, string[] args)
        {
            // 3. Watchdog Loop - Monitor and Auto-Restart on Crash
            // EN: Intelligent crash monitoring with consecutive crash limit
            // FR: Monitoring intelligent des crashs avec limite de crashs consécutifs
            int consecutiveCrashes = 0;
            DateTime lastSuccessfulStart = DateTime.Now;
            const int MAX_CONSECUTIVE_CRASHES = 3;
            const int RESET_WINDOW_MINUTES = 5;

            LogLauncher($"Starting watchdog for {Path.GetFileName(appPath)}");

            while (true)
            {
                LogLauncher($"Launch attempt #{consecutiveCrashes + 1} (consecutive crashes: {consecutiveCrashes})");
                
                var startTime = DateTime.Now;
                int exitCode = LaunchAndMonitor(appPath, args);
                var runDuration = DateTime.Now - startTime;

                LogLauncher($"Process exited with code {exitCode} after {runDuration.TotalSeconds:F1}s");

                // Check if graceful shutdown (exit code 0 or marker file exists)
                if (IsGracefulShutdown(exitCode))
                {
                    LogLauncher("Graceful shutdown detected - stopping watchdog");
                    break; // Stop launcher
                }

                // Crash detected
                consecutiveCrashes++;
                LogLauncher($"Crash detected (total consecutive: {consecutiveCrashes})");

                // Reset counter if app ran successfully for long enough
                if (runDuration.TotalMinutes >= RESET_WINDOW_MINUTES)
                {
                    LogLauncher($"App ran for {runDuration.TotalMinutes:F1} minutes - resetting crash counter");
                    consecutiveCrashes = 1; // Reset to 1 (current crash counts)
                }

                // Check crash limit
                if (consecutiveCrashes >= MAX_CONSECUTIVE_CRASHES)
                {
                    LogLauncher($"Crash limit reached ({MAX_CONSECUTIVE_CRASHES}) - stopping auto-restart");
                    MessageBox.Show(
                        $"L'application a crashé {MAX_CONSECUTIVE_CRASHES} fois consécutivement.\n\n" +
                        "Le redémarrage automatique a été désactivé.\n" +
                        "Veuillez consulter les logs (debug.log) pour diagnostiquer le problème.",
                        "Limite de Crashs Atteinte",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    break; // Stop auto-restart
                }

                // Exponential backoff before restart
                int delayMs = 2000 * consecutiveCrashes; // 2s, 4s, 6s
                LogLauncher($"Waiting {delayMs}ms before restart...");
                System.Threading.Thread.Sleep(delayMs);

                lastSuccessfulStart = DateTime.Now;
            }

            LogLauncher("Watchdog stopped");
        }

        static bool ShouldMinimize()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (File.Exists(configPath))
                {
                    var lines = File.ReadAllLines(configPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;
                        
                        var parts = trimmed.Split('=');
                        if (parts.Length >= 2)
                        {
                            if (parts[0].Trim().Equals("MinimizeToTray", StringComparison.OrdinalIgnoreCase))
                            {
                                return parts[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        static string EscapeArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            // If it contains spaces or quotes, wrap in quotes and escape existing quotes
            if (arg.Contains(" ") || arg.Contains("\"") || arg.Contains("\t"))
            {
                // Escape existing quotes with backslash
                // And wrap the whole thing in quotes
                return "\"" + arg.Replace("\"", "\\\"") + "\""; 
            }
            return arg;
        }

        /// <summary>
        /// EN: Launch application and monitor until exit
        /// FR: Lancer l'application et surveiller jusqu'à la fin
        /// </summary>
        static int LaunchAndMonitor(string appPath, string[] args)
        {
            try
            {
                // Pass through arguments with proper quoting for spaces
                var escapedArgs = args.Select(a => EscapeArgument(a));
                var argumentsString = string.Join(" ", escapedArgs);

                // Check if --tray argument is present (for silent startup)
                bool isTrayMode = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
                bool shouldHide = isTrayMode || ShouldMinimize();

                var startInfo = new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = argumentsString,
                    UseShellExecute = false,
                    CreateNoWindow = shouldHide, // Hide console if --tray or configured in config.ini
                    WindowStyle = shouldHide ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal // Hide window completely
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        LogLauncher("ERROR: Failed to start process");
                        return -1;
                    }

                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                LogLauncher($"ERROR launching app: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// EN: Check if shutdown was graceful (exit code 0 or marker file present)
        /// FR: Vérifier si l'arrêt était gracieux (code 0 ou fichier marker présent)
        /// </summary>
        static bool IsGracefulShutdown(int exitCode)
        {
            // Check exit code
            if (exitCode == 0)
                return true;

            // Check for marker file created by EmergencyCleanup
            var markerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".graceful_exit");
            if (File.Exists(markerPath))
            {
                try
                {
                    File.Delete(markerPath); // Cleanup marker
                }
                catch { }
                return true;
            }

            return false;
        }

        /// <summary>
        /// EN: Simple logging to launcher.log file
        /// FR: Logging simple dans le fichier launcher.log
        /// </summary>
        static void LogLauncher(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log");
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n";
                File.AppendAllText(logPath, logMessage);
            }
            catch
            {
                // Silent fail - logging is non-critical
            }
        }

        static bool IsNet8DesktopInstalled()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-runtimes",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (output.Contains("Microsoft.WindowsDesktop.App 8."))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        internal const int SW_RESTORE = 9;
    }
}
