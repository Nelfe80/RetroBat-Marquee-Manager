using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;
using RetroBatMarqueeManager.Launcher.Helpers;

namespace RetroBatMarqueeManager.Launcher.Forms
{
    /// <summary>
    /// EN: Configuration UI Form for RetroBat Marquee Manager
    /// FR: Formulaire d'interface de configuration pour RetroBat Marquee Manager
    /// </summary>
    public partial class ConfigMenuForm : Form
    {
        private readonly ConfigManager _configManager;
        private readonly TranslationManager _translationManager;
        private Dictionary<string, Dictionary<string, string>> _config;
        private bool _isDirty = false; // EN: Track unsaved changes / FR: Suivi des modifications non sauvegardées
        private Button _currentTabButton;

        private bool _isFirstRun = false;

        public ConfigMenuForm(string configPath, bool isFirstRun = false)
        {
            InitializeComponent();
            this.menuStrip.Renderer = new DarkThemeRenderer();
            this.tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;

            // Manual event subscription with explicit delegate
            this.menu_tools_restart_app.Click += new System.EventHandler(this.menu_tools_restart_app_Click);

            
            _configManager = new ConfigManager(configPath);
            
            var languagesFolder = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, 
                "Languages"
            );
            _translationManager = new TranslationManager(languagesFolder);
            
            _currentTabButton = btnTabGeneral;
            _isFirstRun = isFirstRun;
        }

        // EN: Debug logging helper / FR: Helper de logging debug
        private void LogDebug(string message)
        {
            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_launcher.log");
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n";
                System.IO.File.AppendAllText(logPath, logMessage);
            }
            catch { /* Silent fail */ }
        }


        private void ConfigMenuForm_Load(object sender, EventArgs e)
        {
            // Hide tab headers at runtime (but visible in Designer for editing!)
            tabControl.Appearance = TabAppearance.FlatButtons;
            tabControl.ItemSize = new Size(0, 1);
            tabControl.SizeMode = TabSizeMode.Fixed;
            
            // Load language
            _translationManager.LoadLanguage("en");
            
            // Load configuration
            // Load configuration
            if (_isFirstRun)
            {
                // EN: First Run - Wait for service to generate config
                // FR: Premier Lancement - Attendre que le service génère la config
                this.UseWaitCursor = true;
                this.tabControl.Enabled = false; // Disable UI
                
                // Run background wait task
                Task.Run(() => 
                {
                    bool created = _configManager.WaitForConfigCreation();
                    
                    // Update UI on main thread
                    this.Invoke(new Action(() => 
                    {
                        this.UseWaitCursor = false;
                        this.tabControl.Enabled = true;
                        
                        try
                        {
                            if (created)
                            {
                                _config = _configManager.LoadConfig();
                                LoadConfigToForm();
                            }
                            else
                            {
                                MessageBox.Show(
                                    "Timeout waiting for configuration file generation.\nLoading default settings.",
                                    "Warning",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning
                                );
                                // Load empty config (will use defaults)
                                _config = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                                LoadConfigToForm();
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Error loading config: " + ex.Message);
                        }
                    }));
                });
            }
            else
            {
                try
                {
                    _config = _configManager.LoadConfig();
                    LoadConfigToForm();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Error loading configuration: " + ex.Message,
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            
            // Apply translations
            _translationManager.ApplyTranslations(this);
            


            // EN: Handle resizing for FlowLayoutPanels / FR: Gérer le redimensionnement pour les FlowLayoutPanels
            this.Shown += ConfigMenuForm_Shown;
            this.Resize += ConfigMenuForm_Resize;
        }

        private void ConfigMenuForm_Shown(object sender, EventArgs e)
        {
            // EN: Initial layout adjustment when form is fully shown
            // FR: Ajustement initial de la mise en page
            ConfigMenuForm_Resize(this, EventArgs.Empty);
        }

        private void ConfigMenuForm_Resize(object sender, EventArgs e)
        {
            AdjustFlowLayoutControls(flpGeneral);
            AdjustFlowLayoutControls(flpScraping);
            AdjustFlowLayoutControls(flpDMD);
            AdjustFlowLayoutControls(flpScreen);
            AdjustFlowLayoutControls(flpPinball);
            AdjustFlowLayoutControls(flpAdvanced);
        }

        private void AdjustFlowLayoutControls(FlowLayoutPanel flp)
        {
            if (flp == null) return;
            
            flp.SuspendLayout();
            // EN: Calculate width minus padding and scrollbar / FR: Calculer largeur moins padding et barre de défilement
            int newWidth = flp.ClientSize.Width - flp.Padding.Horizontal - 25;
            
            if (newWidth > 0)
            {
                foreach (Control c in flp.Controls)
                {
                    c.Width = newWidth;
                }
            }
            flp.ResumeLayout(true);
        
        }

        #region Tab Navigation

        private void btnTabGeneral_Click(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 0;
            HighlightTabButton(btnTabGeneral);
        }

        private void btnTabScraping_Click(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 1;
            HighlightTabButton(btnTabScraping);
        }

        private void btnTabDMD_Click(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 2;
            HighlightTabButton(btnTabDMD);
        }

        private void btnTabScreen_Click(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 3;
            HighlightTabButton(btnTabScreen);
        }

        private void btnTabPinball_Click(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 4;
            HighlightTabButton(btnTabPinball);
        }

        private void btnTabAdvanced_Click(object sender, EventArgs e)
        {
            tabControl.SelectedIndex = 5;
            HighlightTabButton(btnTabAdvanced);
        }

        private void HighlightTabButton(Button button)
        {
            // Reset previous button
            if (_currentTabButton != null)
            {
                _currentTabButton.BackColor = Color.FromArgb(45, 45, 48);
                _currentTabButton.Font = new Font("Segoe UI", 10F);
            }
            
            // Highlight new button
            button.BackColor = Color.FromArgb(0, 122, 204);
            button.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            
            _currentTabButton = button;
        }

        #endregion

        #region Menu Event Handlers

        private void menu_file_save_Click(object sender, EventArgs e)
        {
            SaveConfiguration();
        }

        private void menu_file_reload_Click(object sender, EventArgs e)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(
                    _translationManager.Translate("msg_unsaved_changes"),
                    "Confirm",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question
                );
                
                if (result == DialogResult.Cancel)
                    return;
                    
                if (result == DialogResult.Yes)
                    SaveConfiguration();
            }
            
            _config = _configManager.LoadConfig();
            LoadConfigToForm();
            _isDirty = false;
        }

        private void menu_file_exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void menu_tools_clear_cache_Click(object sender, EventArgs e)
        {
            try
            {
                // Access cache path via ConfigManager/Config
                // We re-load or use _configManager helper. Since we need the resolved path with logic from IniConfigService,
                // we'll manually construct the path or reuse logic if available.
                // In ConfigMenuForm, we use _config dictionary, so we have to manually reconstruct the path logic 
                // OR instantiate a temporary IniConfigService if possible, but that requires ILogger.
                // Simpler approach: Replicate the path logic relative to MarqueeImagePath.

                var marqueeImagePath = _configManager.GetValue(_config, "Settings", "MarqueeImagePath", "");
                if (string.IsNullOrEmpty(marqueeImagePath))
                {
                    // Fallback logic improved for portable/relative paths
                    // 1. Check if 'medias' exists relative to the launcher (plugin folder)
                    var localMedias = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
                    if (System.IO.Directory.Exists(localMedias))
                    {
                        marqueeImagePath = localMedias;
                    }
                    else
                    {
                        // 2. Fallback using RomsPath logic matching IniConfigService
                        var retroBatPath = txtRetroBatPath.Text;
                        if (string.IsNullOrEmpty(retroBatPath)) retroBatPath = @"C:\RetroBat"; // Default fallback
                        marqueeImagePath = System.IO.Path.Combine(retroBatPath, "plugins", "RetroBatMarqueeManager", "medias");
                    }
                }
                else
                {
                     // Use helper to resolve absolute path if needed
                     if (!System.IO.Path.IsPathRooted(marqueeImagePath))
                        marqueeImagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, marqueeImagePath);
                }

                var cachePath = System.IO.Path.Combine(marqueeImagePath, "_cache");
                
                if (MessageBox.Show(
                    _translationManager.Translate("msg_confirm_clear_cache") ?? $"Delete all files in?\n{cachePath}",
                    "Confirm Clear Cache",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                     if (System.IO.Directory.Exists(cachePath))
                     {
                         var files = System.IO.Directory.GetFiles(cachePath, "*.*", System.IO.SearchOption.AllDirectories);
                         int deletedCount = 0;
                         int diffCount = 0;

                         foreach (var file in files)
                         {
                             try 
                             {
                                 System.IO.File.Delete(file);
                                 deletedCount++;
                             }
                             catch { diffCount++; }
                         }

                         // Cleanup empty dirs
                         try 
                         {
                             var dirs = System.IO.Directory.GetDirectories(cachePath, "*", System.IO.SearchOption.AllDirectories)
                                                 .OrderByDescending(d => d.Length);
                             foreach (var d in dirs)
                             {
                                 try { System.IO.Directory.Delete(d); } catch { }
                             }
                         }
                         catch { }

                         // Clear offsets.json and video_offsets.json
                         try 
                         {
                              var offsetsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offsets.json");
                              if (System.IO.File.Exists(offsetsPath))
                              {
                                  System.IO.File.Delete(offsetsPath);
                                  deletedCount++;
                              }
                              
                              var videoOffsetsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "video_offsets.json");
                              if (System.IO.File.Exists(videoOffsetsPath))
                              {
                                  System.IO.File.Delete(videoOffsetsPath);
                                  deletedCount++;
                              }
                         }
                         catch {}

                         string msg = $"Cache cleared ({deletedCount} files).";
                         if (diffCount > 0) msg += $"\n{diffCount} files were locked/in-use.";
                         
                         MessageBox.Show(msg, "Result", MessageBoxButtons.OK, diffCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                     }
                     else
                     {
                         MessageBox.Show("Cache folder does not exist (empty).", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                     }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing cache: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void menu_tools_open_logs_Click(object sender, EventArgs e)
        {
            var logsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (System.IO.Directory.Exists(logsPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", logsPath);
            }
        }



        private void menu_help_readme_Click(object sender, EventArgs e)
        {
            // EN: Search for NOTICE.html in the application directory or parent
            // FR: Chercher NOTICE.html dans le répertoire de l'application ou parent
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var helpFile = System.IO.Path.Combine(baseDir, "NOTICE.html");
            
            // If not in Launcher/bin, check parent (root)
            if (!System.IO.File.Exists(helpFile))
            {
                var parentDir = System.IO.Directory.GetParent(baseDir);
                if (parentDir != null) parentDir = parentDir.Parent; // Move up twice
                if (parentDir != null) parentDir = parentDir.Parent; // Move up thrice if needed
                
                if (parentDir != null) helpFile = System.IO.Path.Combine(parentDir.FullName, "NOTICE.html");
            }

            if (System.IO.File.Exists(helpFile))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(helpFile) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Documentation file (NOTICE.html) not found. (EN)\nFichier de documentation (NOTICE.html) introuvable. (FR)", "Documentation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void menu_help_about_Click(object sender, EventArgs e)
        {
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).ProductVersion;
            MessageBox.Show(
                $"RetroBat Marquee Manager - Configuration\r\n" +
                $"Version {version}\r\n\r\n" +
                "© 2026 RetroBat Team",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        #endregion

        #region Button Event Handlers

        private void btn_save_Click(object sender, EventArgs e)
        {
            SaveConfiguration();
        }

        private void btn_cancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnEditDmdLayout_Click(object sender, EventArgs e)
        {
            OpenOverlayDesigner("dmd");
        }

        private void btnEditMpvLayout_Click(object sender, EventArgs e)
        {
            OpenOverlayDesigner("mpv");
        }

        private void OpenOverlayDesigner(string screenType)
        {
            var config = _configManager.LoadConfig();
            var templatePath = _configManager.GetValue(config, "Settings", "OverlayTemplatePath", @"overlays.json");
            
            // EN: Force root path if still pointing to config folder / FR: Forcer la racine si pointe encore vers config
            if (templatePath.StartsWith("config\\", StringComparison.OrdinalIgnoreCase))
                templatePath = templatePath.Substring(7);

            // Convert to absolute if relative
            if (!System.IO.Path.IsPathRooted(templatePath))
            {
                templatePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templatePath);
            }

            int w = 128, h = 32;
            if (screenType == "mpv")
            {
                if (!int.TryParse(txtMarqueeWidth.Text, out w)) w = 1920;
                if (!int.TryParse(txtMarqueeHeight.Text, out h)) h = 360;
            }
            else
            {
                if (!int.TryParse(txtDMDWidth.Text, out w)) w = 128;
                if (!int.TryParse(txtDMDHeight.Text, out h)) h = 32;
            }

            using (var designer = new OverlayDesignerForm(templatePath, screenType, w, h))
            {
                designer.ShowDialog(this);
            }
        }

        private void btnBrowseDMDExe_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                ofd.FileName = "dmdext.exe";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtDMDExePath.Text = ofd.FileName;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseDMDMedia_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select DMD Custom Games folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtDMDMediaPath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseSystemDMD_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select DMD Custom Systems folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtSystemCustomDMDPath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseDMDGameStart_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select DMD Game Start Media folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtDMDGameStartPath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseSystemCustomMarquee_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Custom Systems Marquee folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtSystemCustomMarqueePath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseGameCustomMarquee_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Custom Games Marquee folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtGameCustomMarqueePath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseGameStartMedia_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Game Start Media folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtGameStartMediaPath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        #endregion

        #region Configuration Load/Save

        private void LoadConfigToForm()
        {
            // EN: Load configuration values into form controls / FR: Charger les valeurs de configuration dans les contrôles du formulaire
            
            // GENERAL TAB - Core Paths
            var retroBatPathValue = _configManager.GetValue(_config, "Settings", "RetroBatPath", "");
            LogDebug($"[CONFIG] RetroBatPath raw value: '{retroBatPathValue}'");
            txtRetroBatPath.Text = retroBatPathValue;
            LogDebug($"[CONFIG] txtRetroBatPath.Text set to: '{txtRetroBatPath.Text}'");
            
            var romsPathValue = _configManager.GetValue(_config, "Settings", "RomsPath", "");
            LogDebug($"[CONFIG] RomsPath raw value: '{romsPathValue}'");
            txtRomsPath.Text = romsPathValue;
            LogDebug($"[CONFIG] txtRomsPath.Text set to: '{txtRomsPath.Text}'");
            
            txtIMPath.Text = _configManager.GetValue(_config, "Settings", "IMPath", "tools\\imagemagick\\convert.exe");
            
            // GENERAL TAB - Marquee Settings
            txtBackgroundColor.Text = _configManager.GetValue(_config, "Settings", "MarqueeBackgroundColor", "Black");
            chkCompose.Checked = _configManager.GetValue(_config, "Settings", "MarqueeCompose", "true") == "true";
            cboComposeMedia.SelectedItem = _configManager.GetValue(_config, "Settings", "ComposeMedia", "fanart");
            cboLayout.SelectedItem = _configManager.GetValue(_config, "Settings", "MarqueeLayout", "gradient-standard");
            chkAutoConvert.Checked = _configManager.GetValue(_config, "Settings", "MarqueeAutoConvert", "true") == "true";
            
            cboVideoGeneration.SelectedItem = _configManager.GetValue(_config, "Settings", "MarqueeVideoGeneration", "false");
            txtVideoFolder.Text = _configManager.GetValue(_config, "Settings", "GenerateMarqueeVideoFolder", "generated_videos");
            cboFfmpegHwEncoding.SelectedItem = _configManager.GetValue(_config, "Settings", "FfmpegHwEncoding", "");
            
            // Tooltips for new hardware acceleration options
            toolTipHint.SetToolTip(cboFfmpegHwEncoding, _translationManager.Translate("tip_ffmpeg_hw_encoding"));
            
            // GENERAL TAB - RetroAchievements
            chkRAEnable.Checked = _configManager.GetValue(_config, "Settings", "MarqueeRetroAchievements", "false") == "true";
            txtRAApiKey.Text = _configManager.GetValue(_config, "Settings", "RetroAchievementsWebApiKey", "");
            cboRADisplayTarget.SelectedItem = _configManager.GetValue(_config, "Settings", "MarqueeRetroAchievementsDisplayTarget", "both");
            txtRAOverlays.Text = _configManager.GetValue(_config, "Settings", "MarqueeRetroAchievementsOverlays", "score,badges,count,items,challenge");
            txtMpvRAOverlays.Text = _configManager.GetValue(_config, "Settings", "MpvRetroAchievementsOverlays", "");
            txtDmdRAOverlays.Text = _configManager.GetValue(_config, "Settings", "DmdRetroAchievementsOverlays", "");
            chk_ra_mpv_notifs.Checked = _configManager.GetValue(_config, "Settings", "MpvRetroAchievementsNotifications", "true") == "true";
            chk_ra_dmd_notifs.Checked = _configManager.GetValue(_config, "Settings", "DmdRetroAchievementsNotifications", "true") == "true";
            
            // GENERAL TAB - User Interface
            chkMinimizeToTray.Checked = _configManager.GetValue(_config, "Settings", "MinimizeToTray", "true") == "true";
            cboAutoStart.SelectedItem = _configManager.GetValue(_config, "Settings", "AutoStart", "false");
            txtAcceptedFormats.Text = _configManager.GetValue(_config, "Settings", "AcceptedFormats", "png,jpg,jpeg,svg,mp4,gif");
            
            // GENERAL TAB - Logging
            chkLogToFile.Checked = _configManager.GetValue(_config, "Settings", "LogToFile", "false") == "true";
            txtLogPath.Text = _configManager.GetValue(_config, "Settings", "LogFilePath", "logs\\debug.log");
            
            // SCRAPING TAB - General
            chkAutoScraping.Checked = _configManager.GetValue(_config, "ScrapersSource", "MarqueeAutoScraping", "false") == "true";
            txtPrioritySource.Text = _configManager.GetValue(_config, "ScrapersSource", "PrioritySource", "");
            
            // SCRAPING TAB - ArcadeItalia
            txtArcadeItaliaUrl.Text = _configManager.GetValue(_config, "arcadeitalia", "ArcadeItaliaUrl", "http://adb.arcadeitalia.net");
            cboArcadeItaliaMediaType.SelectedItem = _configManager.GetValue(_config, "arcadeitalia", "ArcadeItaliaMediaType", "marquee");
            
            // SCRAPING TAB - ScreenScraper
            txtSSUser.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperUser", "");
            txtSSPass.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperPass", "");
            txtSSDevId.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperDevId", "");
            txtSSDevPass.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperDevPassword", "");
            txtSSThreads.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperThreads", "1");
            txtSSQueueLimit.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperQueueLimit", "5");
            txtSSQueueKeep.Text = _configManager.GetValue(_config, "ScreenScraper", "ScreenScraperQueueKeep", "3");
            
            chkSSGlobal.Checked = _configManager.GetValue(_config, "ScreenScraper", "MarqueeGlobalScraping", "false") == "true";
            txtMPVScrapMediaType.Text = _configManager.GetValue(_config, "ScreenScraper", "MPVScrapMediaType", "");
            txtDMDScrapMediaType.Text = _configManager.GetValue(_config, "ScreenScraper", "DMDScrapMediaType", "");
            
            // DMD TAB - General
            chkDMDEnabled.Checked = _configManager.GetValue(_config, "DMD", "DmdEnabled", "false") == "true";
            cboDMDModel.SelectedItem = _configManager.GetValue(_config, "DMD", "DmdModel", "virtualdmd");
            txtDMDExePath.Text = _configManager.GetValue(_config, "DMD", "DmdExePath", "tools\\dmd\\dmdext.exe");
            txtDMDMediaPath.Text = _configManager.GetValue(_config, "DMD", "DmdMediaPath", "medias\\customs\\games");
            txtSystemCustomDMDPath.Text = _configManager.GetValue(_config, "DMD", "SystemCustomDMDPath", "medias\\customs\\systems");
            txtDMDGameStartPath.Text = _configManager.GetValue(_config, "DMD", "DmdGameStartMediaPath", "medias\\customs\\games-start");
            
            // DMD TAB - Display
            chkDMDCompose.Checked = _configManager.GetValue(_config, "DMD", "DmdCompose", "true") == "true";
            cboDMDFormat.SelectedItem = _configManager.GetValue(_config, "DMD", "DmdFormat", "rgb24");
            txtDMDWidth.Text = _configManager.GetValue(_config, "DMD", "DmdWidth", "128");
            txtDMDHeight.Text = _configManager.GetValue(_config, "DMD", "DmdHeight", "32");
            txtDMDDotSize.Text = _configManager.GetValue(_config, "DMD", "DmdDotSize", "8");
            
            // SCREEN/MPV TAB
            txtMPVPath.Text = _configManager.GetValue(_config, "ScreenMPV", "MPVPath", "tools\\mpv\\mpv.exe");
            txtMarqueeWidth.Text = _configManager.GetValue(_config, "ScreenMPV", "MarqueeWidth", "1920");
            txtMarqueeHeight.Text = _configManager.GetValue(_config, "ScreenMPV", "MarqueeHeight", "360");
            txtScreenNumber.Text = _configManager.GetValue(_config, "ScreenMPV", "ScreenNumber", "1");
            txtSystemCustomMarqueePath.Text = _configManager.GetValue(_config, "ScreenMPV", "SystemCustomMarqueePath", "medias\\customs\\systems");
            txtGameCustomMarqueePath.Text = _configManager.GetValue(_config, "ScreenMPV", "GameCustomMarqueePath", "medias\\customs\\games");
            txtGameStartMediaPath.Text = _configManager.GetValue(_config, "ScreenMPV", "GameStartMediaPath", "medias\\customs\\games-start");
            cboHwDecoding.SelectedItem = _configManager.GetValue(_config, "ScreenMPV", "HwDecoding", "no");
            toolTipHint.SetToolTip(cboHwDecoding, _translationManager.Translate("tip_hw_decoding"));
            
            // ADVANCED TAB
            txtCollectionCorrelation.Text = _configManager.GetValue(_config, "Settings", "CollectionCorrelation", "");
            txtSystemAliases.Text = _configManager.GetValue(_config, "Settings", "SystemAliases", "");
            
            // PINBALL TAB
            LoadPinballToGrid();
        }

        private void SaveFormToConfig()
        {
            // EN: Save form control values to configuration / FR: Sauvegarder les valeurs des contrôles dans la configuration
            
            // GENERAL TAB - Core Paths
            _configManager.SetValue(_config, "Settings", "RetroBatPath", txtRetroBatPath.Text);
            _configManager.SetValue(_config, "Settings", "RomsPath", txtRomsPath.Text);
            _configManager.SetValue(_config, "Settings", "IMPath", txtIMPath.Text);
            
            // GENERAL TAB - Marquee Settings
            _configManager.SetValue(_config, "Settings", "MarqueeBackgroundColor", txtBackgroundColor.Text);
            _configManager.SetValue(_config, "Settings", "MarqueeCompose", chkCompose.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "Settings", "ComposeMedia", cboComposeMedia.SelectedItem?.ToString() ?? "fanart");
            _configManager.SetValue(_config, "Settings", "MarqueeLayout", cboLayout.SelectedItem?.ToString() ?? "gradient-standard");
            _configManager.SetValue(_config, "Settings", "MarqueeAutoConvert", chkAutoConvert.Checked.ToString().ToLower());
            
            _configManager.SetValue(_config, "Settings", "MarqueeVideoGeneration", cboVideoGeneration.SelectedItem?.ToString() ?? "false");
            _configManager.SetValue(_config, "Settings", "GenerateMarqueeVideoFolder", txtVideoFolder.Text);
            _configManager.SetValue(_config, "Settings", "FfmpegHwEncoding", cboFfmpegHwEncoding.SelectedItem?.ToString() ?? "");
            
            // GENERAL TAB - RetroAchievements
            _configManager.SetValue(_config, "Settings", "MarqueeRetroAchievements", chkRAEnable.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "Settings", "RetroAchievementsWebApiKey", txtRAApiKey.Text);
            _configManager.SetValue(_config, "Settings", "MarqueeRetroAchievementsDisplayTarget", cboRADisplayTarget.SelectedItem?.ToString() ?? "both");
            _configManager.SetValue(_config, "Settings", "MarqueeRetroAchievementsOverlays", txtRAOverlays.Text);
            _configManager.SetValue(_config, "Settings", "MpvRetroAchievementsOverlays", txtMpvRAOverlays.Text);
            _configManager.SetValue(_config, "Settings", "DmdRetroAchievementsOverlays", txtDmdRAOverlays.Text);
            _configManager.SetValue(_config, "Settings", "MpvRetroAchievementsNotifications", chk_ra_mpv_notifs.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "Settings", "DmdRetroAchievementsNotifications", chk_ra_dmd_notifs.Checked.ToString().ToLower());
            
            // GENERAL TAB - User Interface
            _configManager.SetValue(_config, "Settings", "MinimizeToTray", chkMinimizeToTray.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "Settings", "AutoStart", cboAutoStart.SelectedItem?.ToString() ?? "false");
            _configManager.SetValue(_config, "Settings", "AcceptedFormats", txtAcceptedFormats.Text);
            
            // GENERAL TAB - Logging
            _configManager.SetValue(_config, "Settings", "LogToFile", chkLogToFile.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "Settings", "LogFilePath", txtLogPath.Text);
            
            // SCRAPING TAB - General
            _configManager.SetValue(_config, "ScrapersSource", "MarqueeAutoScraping", chkAutoScraping.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "ScrapersSource", "PrioritySource", txtPrioritySource.Text);
            
            // SCRAPING TAB - ArcadeItalia
            _configManager.SetValue(_config, "arcadeitalia", "ArcadeItaliaUrl", txtArcadeItaliaUrl.Text);
            _configManager.SetValue(_config, "arcadeitalia", "ArcadeItaliaMediaType", cboArcadeItaliaMediaType.SelectedItem?.ToString() ?? "marquee");
            
            // SCRAPING TAB - ScreenScraper
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperUser", txtSSUser.Text);
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperPass", txtSSPass.Text);
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperDevId", txtSSDevId.Text);
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperDevPassword", txtSSDevPass.Text);
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperThreads", txtSSThreads.Text);
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperQueueLimit", txtSSQueueLimit.Text);
            _configManager.SetValue(_config, "ScreenScraper", "ScreenScraperQueueKeep", txtSSQueueKeep.Text);
            _configManager.SetValue(_config, "ScreenScraper", "MarqueeGlobalScraping", chkSSGlobal.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "ScreenScraper", "MPVScrapMediaType", txtMPVScrapMediaType.Text);
            _configManager.SetValue(_config, "ScreenScraper", "DMDScrapMediaType", txtDMDScrapMediaType.Text);
            
            // DMD TAB - General
            _configManager.SetValue(_config, "DMD", "DmdEnabled", chkDMDEnabled.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "DMD", "DmdModel", cboDMDModel.SelectedItem?.ToString() ?? "virtualdmd");
            _configManager.SetValue(_config, "DMD", "DmdExePath", txtDMDExePath.Text);
            _configManager.SetValue(_config, "DMD", "DmdMediaPath", txtDMDMediaPath.Text);
            _configManager.SetValue(_config, "DMD", "SystemCustomDMDPath", txtSystemCustomDMDPath.Text);
            _configManager.SetValue(_config, "DMD", "DmdGameStartMediaPath", txtDMDGameStartPath.Text);
            
            // DMD TAB - Display
            _configManager.SetValue(_config, "DMD", "DmdCompose", chkDMDCompose.Checked.ToString().ToLower());
            _configManager.SetValue(_config, "DMD", "DmdFormat", cboDMDFormat.SelectedItem?.ToString() ?? "rgb24");
            _configManager.SetValue(_config, "DMD", "DmdWidth", txtDMDWidth.Text);
            _configManager.SetValue(_config, "DMD", "DmdHeight", txtDMDHeight.Text);
            _configManager.SetValue(_config, "DMD", "DmdDotSize", txtDMDDotSize.Text);
            
            // SCREEN/MPV TAB
            _configManager.SetValue(_config, "ScreenMPV", "MPVPath", txtMPVPath.Text);
            _configManager.SetValue(_config, "ScreenMPV", "MarqueeWidth", txtMarqueeWidth.Text);
            _configManager.SetValue(_config, "ScreenMPV", "MarqueeHeight", txtMarqueeHeight.Text);
            _configManager.SetValue(_config, "ScreenMPV", "ScreenNumber", txtScreenNumber.Text);
            _configManager.SetValue(_config, "ScreenMPV", "SystemCustomMarqueePath", txtSystemCustomMarqueePath.Text);
            _configManager.SetValue(_config, "ScreenMPV", "GameCustomMarqueePath", txtGameCustomMarqueePath.Text);
            _configManager.SetValue(_config, "ScreenMPV", "GameStartMediaPath", txtGameStartMediaPath.Text);
            _configManager.SetValue(_config, "ScreenMPV", "HwDecoding", cboHwDecoding.SelectedItem?.ToString() ?? "no");
            
            // ADVANCED TAB
            _configManager.SetValue(_config, "Settings", "CollectionCorrelation", txtCollectionCorrelation.Text);
            _configManager.SetValue(_config, "Settings", "SystemAliases", txtSystemAliases.Text);
            
            // PINBALL TAB
            SavePinballFromGrid();
        }

        private void SaveConfiguration()
        {
            try
            {
                SaveFormToConfig();
                _configManager.SaveConfig(_config);
                _isDirty = false;
                
                MessageBox.Show(
                    _translationManager.Translate("msg_save_success"),
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    _translationManager.Translate("msg_save_error"), 
                    ex.Message
                );
                MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Form Closing

        private void ConfigMenuForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(
                    _translationManager.Translate("msg_unsaved_changes"),
                    "Confirm",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question
                );
                
                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                
                if (result == DialogResult.Yes)
                {
                    SaveConfiguration();
                }
            }
        }

        #endregion

        #region Pinball Tab Management

        private void LoadPinballToGrid()
        {
            dgvPinball.Rows.Clear();
            var pinballKeys = _configManager.GetSection(_config, "Pinball");
            if (pinballKeys != null)
            {
                foreach (var kvp in pinballKeys)
                {
                    // EN: Skip non-Pinball config keys (e.g., ScreenScraper settings) / FR: Ignorer les clés non-Pinball (ex: paramètres ScreenScraper)
                    if (kvp.Key.StartsWith("ScreenScraper", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    dgvPinball.Rows.Add(kvp.Key, kvp.Value);
                }
            }
        }

        private void SavePinballFromGrid()
        {
            // EN: Clear existing Pinball section to avoid duplicates / FR: Vider section Pinball pour éviter doublons
            if (_config.ContainsKey("Pinball"))
            {
                _config["Pinball"].Clear();
            }
            else
            {
                _config["Pinball"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            
            // EN: Add rows from DataGridView / FR: Ajouter lignes depuis DataGridView
            foreach (DataGridViewRow row in dgvPinball.Rows)
            {
                if (row.Cells[0].Value != null && !string.IsNullOrWhiteSpace(row.Cells[0].Value.ToString()))
                {
                    string system = row.Cells[0].Value.ToString();
                    string command = row.Cells[1].Value?.ToString() ?? "";
                    _config["Pinball"][system] = command;
                }
            }
        }

        private void btnPinballAdd_Click(object sender, EventArgs e)
        {
            using (var form = new Form())
            {
                form.Text = "Add Pinball System";
                form.Size = new Size(500, 200);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.BackColor = Color.FromArgb(45, 45, 48);
                form.ForeColor = Color.White;

                var lblSystem = new Label { Text = "System Name:", Location = new Point(15, 20), AutoSize = true };
                var txtSystem = new TextBox { Location = new Point(120, 17), Size = new Size(350, 23), BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White };
                var lblCommand = new Label { Text = "Command:", Location = new Point(15, 55), AutoSize = true };
                var txtCommand = new TextBox { Location = new Point(120, 52), Size = new Size(350, 23), BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White };
                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(270, 110), Size = new Size(90, 30), BackColor = Color.FromArgb(63, 63, 70), FlatStyle = FlatStyle.Flat };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(370, 110), Size = new Size(90, 30), BackColor = Color.FromArgb(63, 63, 70), FlatStyle = FlatStyle.Flat };

                form.Controls.AddRange(new Control[] { lblSystem, txtSystem, lblCommand, txtCommand, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txtSystem.Text))
                {
                    foreach (DataGridViewRow row in dgvPinball.Rows)
                    {
                        if (row.Cells[0].Value?.ToString() == txtSystem.Text)
                        {
                            MessageBox.Show($"System '{txtSystem.Text}' already exists!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                    dgvPinball.Rows.Add(txtSystem.Text, txtCommand.Text);
                    _isDirty = true;
                }
            }
        }

        private void btnPinballEdit_Click(object sender, EventArgs e)
        {
            if (dgvPinball.SelectedRows.Count == 0)
            {
                MessageBox.Show("Please select a system to edit.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedRow = dgvPinball.SelectedRows[0];
            string currentSystem = selectedRow.Cells[0].Value?.ToString() ?? "";
            string currentCommand = selectedRow.Cells[1].Value?.ToString() ?? "";

            using (var form = new Form())
            {
                form.Text = "Edit Pinball System";
                form.Size = new Size(500, 200);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.BackColor = Color.FromArgb(45, 45, 48);
                form.ForeColor = Color.White;

                var lblSystem = new Label { Text = "System Name:", Location = new Point(15, 20), AutoSize = true };
                var txtSystem = new TextBox { Location = new Point(120, 17), Size = new Size(350, 23), Text = currentSystem, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White };
                var lblCommand = new Label { Text = "Command:", Location = new Point(15, 55), AutoSize = true };
                var txtCommand = new TextBox { Location = new Point(120, 52), Size = new Size(350, 23), Text = currentCommand, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White };
                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(270, 110), Size = new Size(90, 30), BackColor = Color.FromArgb(63, 63, 70), FlatStyle = FlatStyle.Flat };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(370, 110), Size = new Size(90, 30), BackColor = Color.FromArgb(63, 63, 70), FlatStyle = FlatStyle.Flat };

                form.Controls.AddRange(new Control[] { lblSystem, txtSystem, lblCommand, txtCommand, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    selectedRow.Cells[1].Value = txtCommand.Text;
                    _isDirty = true;
                }
            }
        }

        private void btnPinballDelete_Click(object sender, EventArgs e)
        {
            if (dgvPinball.SelectedRows.Count == 0)
            {
                MessageBox.Show("Please select a system to delete.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedRow = dgvPinball.SelectedRows[0];
            string systemName = selectedRow.Cells[0].Value?.ToString() ?? "";

            var result = MessageBox.Show(
                $"Are you sure you want to delete the system '{systemName}'?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                dgvPinball.Rows.Remove(selectedRow);
                _isDirty = true;
            }
        }

        #endregion

        /// <summary>
        /// EN: Mark configuration as modified
        /// FR: Marquer la configuration comme modifiée
        /// </summary>
        public void MarkDirty()
        {
            _isDirty = true;
        }

        private void tabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            // EN: Custom drawing for tab headers in dark theme
            // FR: Dessin personnalisé des en-têtes d'onglets pour le thème sombre
            var tabPage = tabControl.TabPages[e.Index];
            var tabRect = tabControl.GetTabRect(e.Index);

            // Background
            using (var brush = new SolidBrush(Color.FromArgb(45, 45, 48)))
            {
                e.Graphics.FillRectangle(brush, tabRect);
            }

            // Text
            var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.DrawString(tabPage.Text, e.Font, brush, tabRect, textFormat);
            }
        }

        private void btnBrowseRetroBat_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select RetroBat folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtRetroBatPath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseRoms_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Roms folder";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtRomsPath.Text = fbd.SelectedPath;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseIM_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                ofd.FileName = "convert.exe";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtIMPath.Text = ofd.FileName;
                    MarkDirty();
                }
            }
        }

        private void btnBrowseMPV_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                ofd.FileName = "mpv.exe";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtMPVPath.Text = ofd.FileName;
                    MarkDirty();
                }
            }
        }

        private void txtDMDWidth_TextChanged(object sender, EventArgs e)
        {

        }
        private void menu_tools_restart_app_Click(object sender, EventArgs e)
        {
            try
            {
                if (MessageBox.Show(
                    _translationManager.Translate("msg_confirm_restart") ?? "Restart Application?",
                    "Confirm Restart",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    // 1. Kill Main App (RetroBatMarqueeManager.App.exe)
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName("RetroBatMarqueeManager.App"))
                    {
                        try { process.Kill(); } catch { }
                    }

                    // 2. Kill other Launcher instances (Watchdogs), but NOT ourselves
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName("RetroBatMarqueeManager.Launcher"))
                    {
                        if (process.Id != currentProcess.Id)
                        {
                            try { process.Kill(); } catch { }
                        }
                    }

                    // 3. Start new Launcher instance (which will start Watchdog -> App)
                    // EN: Trim trailing slash to prevent quote escaping issues in CMD
                    // FR: Supprimer le slash final pour éviter les problèmes d'échappement de guillemets dans CMD
                    var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    var launcherPath = Application.ExecutablePath;

                    // Use CMD to detach and wait slightly
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout /t 2 /nobreak & start \"\" /d \"{appDir}\" \"{launcherPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = appDir
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    
                    // 4. Exit this Config Menu
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restart failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
