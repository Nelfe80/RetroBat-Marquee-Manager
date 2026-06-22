using RetroBatMarqueeManager.Core.Interfaces;
using System.Runtime.InteropServices;

namespace RetroBatMarqueeManager.Infrastructure.Installation
{
    /// <summary>
    /// EN: Service to manage auto-start options (Windows/RetroBat)
    /// FR: Service pour gérer les options de démarrage automatique (Windows/RetroBat)
    /// </summary>
    public class AutoStartService
    {
        private readonly IConfigService _config;
        private readonly ILogger<AutoStartService> _logger;

        public AutoStartService(IConfigService config, ILogger<AutoStartService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public void ConfigureAutoStart()
        {
            try
            {
                var mode = _config.GetSetting("AutoStart", "false").ToLowerInvariant();
                var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RetroBatMarqueeManager.exe");

                // Clean up existing entries (Registry, Shortcuts, Scheduled Tasks)
                try
                {
                    // 1. Registry
                    const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                    const string RegistryValueName = "RetroBatMarqueeManager";
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                    if (key?.GetValue(RegistryValueName) != null)
                    {
                        key.DeleteValue(RegistryValueName);
                        _logger.LogInformation("Removed Windows registry auto-start entry.");
                    }

                    // 2. Scheduled Task (cleanup previous attempts)
                    DeleteScheduledTask("RetroBatMarqueeManager");
                }
                catch { }

                // Clean up shortcuts
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var lnkPath = Path.Combine(startupFolder, "RetroBatMarqueeManager.lnk");
                if (File.Exists(lnkPath)) File.Delete(lnkPath);

                // Apply new configuration
                switch (mode)
                {
                    case "windows":
                        // EN: Use Scheduled Task - attempting user-level task
                        CreateScheduledTask("RetroBatMarqueeManager");
                        break;

                    case "retrobat":
                        var retroBatStartPath = Path.Combine(_config.RetroBatPath, "emulationstation", ".emulationstation", "scripts", "start");
                        var retroBatStartScript = Path.Combine(retroBatStartPath, "StartRetroBatMarqueeManager.bat");
                        CreateRetroBatStartupScript(retroBatStartPath, retroBatStartScript);
                        break;

                    case "false":
                    default:
                        _logger.LogInformation("AutoStart disabled.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in ConfigureAutoStart: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Create Windows Scheduled Task using XML import (more robust than CLI args)
        /// FR: Créer Tâche Planifiée via XML import (plus robuste que args CLI)
        /// </summary>
        private void CreateScheduledTask(string taskName)
        {
            try
            {
                var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RetroBatMarqueeManager.exe");
                var xmlPath = Path.Combine(Path.GetTempPath(), "RetroBatMarqueeManager_Task.xml");
                var logPath = Path.Combine(Path.GetTempPath(), "RetroBatMarqueeManager_SchTasks.log");
                
                // EN: Generate Task XML definition for standard user execution at logon
                // FR: Générer définition XML pour exécution utilisateur standard au login
                // xmlns is required for valid schema
                var taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Auto-start RetroBat Marquee Manager</Description>
    <Author>{Environment.UserName}</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{Environment.UserDomainName}\{Environment.UserName}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
    </Exec>
  </Actions>
</Task>";

                File.WriteAllText(xmlPath, taskXml);

                // Command: schtasks /Create /TN "Name" /XML "file.xml" /F
                var commandArg = $"/Create /TN \"{taskName}\" /XML \"{xmlPath}\" /F";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = commandArg,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                {
                    _logger.LogError("Failed to start schtasks.exe process.");
                    return;
                }
                proc.WaitForExit();
                
                if (proc.ExitCode == 0)
                {
                    _logger.LogInformation($"Successfully created Scheduled Task: {taskName}");
                }
                else
                {
                    var error = proc.StandardError.ReadToEnd();
                    _logger.LogError($"Failed to create Scheduled Task via XML. ExitCode: {proc.ExitCode}, Error: {error}");
                }
                
                // Cleanup XML file
                try { if(File.Exists(xmlPath)) File.Delete(xmlPath); } catch {}
                try { if(File.Exists(logPath)) File.Delete(logPath); } catch {}
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating XML Scheduled Task: {ex.Message}");
            }
        }

        private void DeleteScheduledTask(string taskName)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                }
            }
            catch { }
        }

        private void CreateRetroBatStartupScript(string startDir, string scriptPath)
        {
             try
            {
                Directory.CreateDirectory(startDir);
                var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RetroBatMarqueeManager.exe");
                var batchContent = $"@echo off\r\nstart \"\" \"{exePath}\"";
                File.WriteAllText(scriptPath, batchContent);
                _logger.LogInformation($"Created RetroBat startup script: {scriptPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating RetroBat script: {ex.Message}");
            }
        }
    }
}
