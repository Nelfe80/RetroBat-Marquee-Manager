using System.Diagnostics;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Infrastructure.Processes
{
    public class SystemProcessService : IProcessService
    {
        private readonly ILogger<SystemProcessService> _logger;

        public SystemProcessService(ILogger<SystemProcessService> logger)
        {
            _logger = logger;
        }

        public async Task RunProcessAsync(string fileName, string arguments, string? workingDirectory = null)
        {
             await Task.Run(() => StartProcess(fileName, arguments, workingDirectory));
        }

        public void StartProcess(string fileName, string arguments, string? workingDirectory = null, bool waitForExit = true)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = !waitForExit, // Pour MPV (shell=True en Python), utiliser le shell
                    CreateNoWindow = true
                };

                // Si on utilise le shell (MPV), on ne peut pas rediriger
                if (waitForExit)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.UseShellExecute = false; // Nécessaire pour redirection
                }

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    psi.WorkingDirectory = workingDirectory;
                    // Env Vars pour ImageMagick seulement si on attend (convert.exe)
                    if (waitForExit) 
                    {
                        psi.EnvironmentVariables["MAGICK_HOME"] = workingDirectory;
                        psi.EnvironmentVariables["MAGICK_CONFIGURE_PATH"] = workingDirectory;
                        psi.EnvironmentVariables["MAGICK_CODER_MODULE_PATH"] = workingDirectory;
                    }
                }

                var process = Process.Start(psi);
                if (process != null && waitForExit)
                {
                    process.WaitForExit(10000); // 10 sec timeout pour ImageMagick
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start process {fileName}: {ex.Message}");
            }
        }
        
        // Méthode spéciale pour MPV : capture stderr/stdout et ne bloque pas
        public void StartProcessWithLogging(string fileName, string arguments, string? workingDirectory = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false, // Nécessaire pour rediriger
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false, // Afficher la fenêtre MPV
                    WorkingDirectory = workingDirectory
                };

                var process = Process.Start(psi);
                if (process != null)
                {
                    process.EnableRaisingEvents = true;
                    
                    // Lire stderr de manière asynchrone
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            // Filter noisy MPV logs
                            // Added "V: " pattern based on user report: "MPV stdout: V: 00:00:00 / 00:00:00 (0%)"
                            // Filter noisy MPV logs
                            // Added "V: ", "AV: ", "A-V:" pattern based on user report: "MPV stdout: AV: 00:00:05 / 00:00:09 (55%) A-V:  0.000"
                            if (e.Data.Contains("No media data") || e.Data.Contains("(Paused)") || e.Data.StartsWith("V: ") || e.Data.Contains("AV:") || e.Data.Contains("A-V:")) return;
                            _logger.LogError($"MPV stderr: {e.Data}");
                        }
                    };
                    
                    // Lire stdout aussi (MPV peut écrire des erreurs sur stdout)
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            // Filter noisy MPV logs
                            // Filter noisy MPV logs
                            if (e.Data.Contains("No media data") || e.Data.Contains("(Paused)") || e.Data.Trim().StartsWith("V:") || e.Data.Contains("AV:") || e.Data.Contains("A-V:")) return;
                            _logger.LogInformation($"MPV stdout: {e.Data}");
                        }
                    };
                    
                    // Logger quand MPV se termine
                    process.Exited += (sender, e) =>
                    {
                        var p = sender as Process;
                        _logger.LogWarning($"MPV process exited with code {p?.ExitCode}");
                    };
                    
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    
                    _logger.LogInformation($"MPV process started with PID {process.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start process {fileName}: {ex.Message}");
            }
        }

        // ArgumentList Overload for handling special chars safely
        public void StartProcess(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, bool waitForExit = true)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = !waitForExit, // Default false usually preferred for ArgumentList
                    CreateNoWindow = true
                };

                // ArgumentList requires UseShellExecute = false
                if (arguments != null)
                {
                    foreach (var arg in arguments) psi.ArgumentList.Add(arg);
                    psi.UseShellExecute = false; 
                }

                if (waitForExit)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.UseShellExecute = false;
                }

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    psi.WorkingDirectory = workingDirectory;
                    if (waitForExit) 
                    {
                        psi.EnvironmentVariables["MAGICK_HOME"] = workingDirectory;
                        psi.EnvironmentVariables["MAGICK_CONFIGURE_PATH"] = workingDirectory;
                        psi.EnvironmentVariables["MAGICK_CODER_MODULE_PATH"] = workingDirectory;
                    }
                }

                var process = Process.Start(psi);
                if (process != null && waitForExit)
                {
                    process.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start process {fileName}: {ex.Message}");
            }
        }

        // ArgumentList Overload for MPV logging
        public void StartProcessWithLogging(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false, // Required for redirect & ArgumentList
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    WorkingDirectory = workingDirectory
                };
                
                if (arguments != null)
                {
                    foreach (var arg in arguments) psi.ArgumentList.Add(arg);
                }

                var process = Process.Start(psi);
                if (process != null)
                {
                    process.EnableRaisingEvents = true;
                    
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            if (e.Data.Contains("No media data") || e.Data.Contains("(Paused)") || e.Data.StartsWith("V: ") || e.Data.Contains("AV:") || e.Data.Contains("A-V:")) return;
                            _logger.LogError($"MPV stderr: {e.Data}");
                        }
                    };
                    
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            if (e.Data.Contains("No media data") || e.Data.Contains("(Paused)") || e.Data.Trim().StartsWith("V:") || e.Data.Contains("AV:") || e.Data.Contains("A-V:")) return;
                            _logger.LogInformation($"MPV stdout: {e.Data}");
                        }
                    };
                    
                    process.Exited += (sender, e) =>
                    {
                        var p = sender as Process;
                        _logger.LogWarning($"MPV process exited with code {p?.ExitCode}");
                    };
                    
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    
                    _logger.LogInformation($"MPV process started with PID {process.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start process {fileName}: {ex.Message}");
            }
        }

        public bool IsProcessRunning(string processName)
        {
            return Process.GetProcessesByName(processName).Any();
        }

        public void KillProcess(string processName)
        {
            try 
            {
                // Basic .NET kill first (often sufficient)
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    try { p.Kill(); } catch { }
                }

                // Force kill via taskkill (Windows only) for stubborn processes or zombies
                if (OperatingSystem.IsWindows())
                {
                    // Assuming processName is without extension (e.g. "mpv")
                    // If it doesn't match, we might need ".exe"
                    var exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : $"{processName}.exe";
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /IM {exeName} /T", // /T kills tree
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi)?.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                 _logger.LogWarning($"KillProcess error for {processName}: {ex.Message}");
            }
        }

        // Feature: Refocus Game Window
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        public bool FocusProcess(string processName)
        {
            if (!OperatingSystem.IsWindows()) return false;
            
            try
            {
                // Handle extension if provided
                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    processName = Path.GetFileNameWithoutExtension(processName);

                var processes = Process.GetProcessesByName(processName);
                if (processes.Any())
                {
                    var p = processes.First();
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        // Restore if minimized
                        ShowWindow(p.MainWindowHandle, SW_RESTORE);
                        // Bring to front
                        bool result = SetForegroundWindow(p.MainWindowHandle);
                        _logger.LogInformation($"Refocusing process '{processName}' (PID:{p.Id}) success: {result}");
                        return result;
                    }
                }
            }
            catch(Exception ex) 
            {
                _logger.LogError($"Error focusing process {processName}: {ex.Message}");
            }
            return false;
        }
    }
}
