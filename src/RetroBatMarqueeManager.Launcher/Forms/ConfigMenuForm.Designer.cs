namespace RetroBatMarqueeManager.Launcher.Forms
{
    partial class ConfigMenuForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ConfigMenuForm));
            this.toolTipHint = new System.Windows.Forms.ToolTip(this.components);
            this.txtIMPath = new System.Windows.Forms.TextBox();
            this.txtRomsPath = new System.Windows.Forms.TextBox();
            this.txtRetroBatPath = new System.Windows.Forms.TextBox();
            this.txtVideoFolder = new System.Windows.Forms.TextBox();
            this.cboVideoGeneration = new System.Windows.Forms.ComboBox();
            this.lbl_ffmpeg_hw_encoding = new System.Windows.Forms.Label();
            this.cboFfmpegHwEncoding = new System.Windows.Forms.ComboBox();
            this.chkAutoConvert = new System.Windows.Forms.CheckBox();
            this.cboLayout = new System.Windows.Forms.ComboBox();
            this.cboComposeMedia = new System.Windows.Forms.ComboBox();
            this.chkCompose = new System.Windows.Forms.CheckBox();
            this.txtBackgroundColor = new System.Windows.Forms.TextBox();
            this.txtRAOverlays = new System.Windows.Forms.TextBox();
            this.cboRADisplayTarget = new System.Windows.Forms.ComboBox();
            this.txtRAApiKey = new System.Windows.Forms.TextBox();
            this.chkRAEnable = new System.Windows.Forms.CheckBox();
            this.txtMpvRAOverlays = new System.Windows.Forms.TextBox();
            this.lblMpvRAOverlays = new System.Windows.Forms.Label();
            this.txtDmdRAOverlays = new System.Windows.Forms.TextBox();
            this.lblDmdRAOverlays = new System.Windows.Forms.Label();
            this.chk_ra_mpv_notifs = new System.Windows.Forms.CheckBox();
            this.chk_ra_dmd_notifs = new System.Windows.Forms.CheckBox();
            this.btnEditDmdLayout = new System.Windows.Forms.Button();
            this.btnEditMpvLayout = new System.Windows.Forms.Button();
            this.txtAcceptedFormats = new System.Windows.Forms.TextBox();
            this.cboAutoStart = new System.Windows.Forms.ComboBox();
            this.txtPrioritySource = new System.Windows.Forms.TextBox();
            this.chkAutoScraping = new System.Windows.Forms.CheckBox();
            this.cboArcadeItaliaMediaType = new System.Windows.Forms.ComboBox();
            this.txtDMDScrapMediaType = new System.Windows.Forms.TextBox();
            this.txtMPVScrapMediaType = new System.Windows.Forms.TextBox();
            this.chkSSGlobal = new System.Windows.Forms.CheckBox();
            this.cboDMDModel = new System.Windows.Forms.ComboBox();
            this.chkDMDEnabled = new System.Windows.Forms.CheckBox();
            this.txtDMDWidth = new System.Windows.Forms.TextBox();
            this.txtScreenNumber = new System.Windows.Forms.TextBox();
            this.lbl_hw_decoding = new System.Windows.Forms.Label();
            this.cboHwDecoding = new System.Windows.Forms.ComboBox();
            this.txtSSQueueLimit = new System.Windows.Forms.TextBox();
            this.txtSSQueueKeep = new System.Windows.Forms.TextBox();
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.menu_file = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_file_save = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_file_reload = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.menu_file_exit = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_tools = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_tools_clear_cache = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_tools_open_logs = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_tools_restart_app = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_help = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_help_readme = new System.Windows.Forms.ToolStripMenuItem();
            this.menu_help_about = new System.Windows.Forms.ToolStripMenuItem();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.btnTabAdvanced = new System.Windows.Forms.Button();
            this.btnTabPinball = new System.Windows.Forms.Button();
            this.btnTabScreen = new System.Windows.Forms.Button();
            this.btnTabDMD = new System.Windows.Forms.Button();
            this.btnTabScraping = new System.Windows.Forms.Button();
            this.btnTabGeneral = new System.Windows.Forms.Button();
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabGeneral = new System.Windows.Forms.TabPage();
            this.flpGeneral = new System.Windows.Forms.FlowLayoutPanel();
            this.groupPaths = new System.Windows.Forms.GroupBox();
            this.btnBrowseIM = new System.Windows.Forms.Button();
            this.lblIMPath = new System.Windows.Forms.Label();
            this.btnBrowseRoms = new System.Windows.Forms.Button();
            this.lblRomsPath = new System.Windows.Forms.Label();
            this.btnBrowseRetroBat = new System.Windows.Forms.Button();
            this.lblRetroBatPath = new System.Windows.Forms.Label();
            this.groupMarquee = new System.Windows.Forms.GroupBox();
            this.lblVideoFolder = new System.Windows.Forms.Label();
            this.lblVideoGeneration = new System.Windows.Forms.Label();
            this.lblLayout = new System.Windows.Forms.Label();
            this.lblComposeMedia = new System.Windows.Forms.Label();
            this.lblBackgroundColor = new System.Windows.Forms.Label();
            this.groupRA = new System.Windows.Forms.GroupBox();
            this.lblRAOverlays = new System.Windows.Forms.Label();
            this.lblRADisplayTarget = new System.Windows.Forms.Label();
            this.lblRAApiKey = new System.Windows.Forms.Label();
            this.groupUI = new System.Windows.Forms.GroupBox();
            this.lblAcceptedFormats = new System.Windows.Forms.Label();
            this.lblAutoStart = new System.Windows.Forms.Label();
            this.chkMinimizeToTray = new System.Windows.Forms.CheckBox();
            this.groupLogging = new System.Windows.Forms.GroupBox();
            this.txtLogPath = new System.Windows.Forms.TextBox();
            this.lblLogPath = new System.Windows.Forms.Label();
            this.chkLogToFile = new System.Windows.Forms.CheckBox();
            this.tabScraping = new System.Windows.Forms.TabPage();
            this.flpScraping = new System.Windows.Forms.FlowLayoutPanel();
            this.groupScrapingGeneral = new System.Windows.Forms.GroupBox();
            this.lblPrioritySource = new System.Windows.Forms.Label();
            this.groupArcadeItalia = new System.Windows.Forms.GroupBox();
            this.lblArcadeItaliaMediaType = new System.Windows.Forms.Label();
            this.txtArcadeItaliaUrl = new System.Windows.Forms.TextBox();
            this.lblArcadeItaliaUrl = new System.Windows.Forms.Label();
            this.groupScreenScraper = new System.Windows.Forms.GroupBox();
            this.lblDMDScrapMediaType = new System.Windows.Forms.Label();
            this.lblMPVScrapMediaType = new System.Windows.Forms.Label();
            this.lblSSQueueKeep = new System.Windows.Forms.Label();
            this.lblSSQueueLimit = new System.Windows.Forms.Label();
            this.txtSSThreads = new System.Windows.Forms.TextBox();
            this.lblSSThreads = new System.Windows.Forms.Label();
            this.txtSSDevPass = new System.Windows.Forms.TextBox();
            this.lblSSDevPass = new System.Windows.Forms.Label();
            this.txtSSDevId = new System.Windows.Forms.TextBox();
            this.lblSSDevId = new System.Windows.Forms.Label();
            this.txtSSPass = new System.Windows.Forms.TextBox();
            this.lblSSPass = new System.Windows.Forms.Label();
            this.txtSSUser = new System.Windows.Forms.TextBox();
            this.lblSSUser = new System.Windows.Forms.Label();
            this.tabDMD = new System.Windows.Forms.TabPage();
            this.flpDMD = new System.Windows.Forms.FlowLayoutPanel();
            this.groupDMDGeneral = new System.Windows.Forms.GroupBox();
            this.btnBrowseDMDGameStart = new System.Windows.Forms.Button();
            this.txtDMDGameStartPath = new System.Windows.Forms.TextBox();
            this.lblDMDGameStartPath = new System.Windows.Forms.Label();
            this.btnBrowseSystemDMD = new System.Windows.Forms.Button();
            this.txtSystemCustomDMDPath = new System.Windows.Forms.TextBox();
            this.lblSystemCustomDMDPath = new System.Windows.Forms.Label();
            this.btnBrowseDMDMedia = new System.Windows.Forms.Button();
            this.txtDMDMediaPath = new System.Windows.Forms.TextBox();
            this.lblDMDMediaPath = new System.Windows.Forms.Label();
            this.btnBrowseDMDExe = new System.Windows.Forms.Button();
            this.txtDMDExePath = new System.Windows.Forms.TextBox();
            this.lblDMDExePath = new System.Windows.Forms.Label();
            this.lblDMDModel = new System.Windows.Forms.Label();
            this.groupDMDDisplay = new System.Windows.Forms.GroupBox();
            this.cboDMDFormat = new System.Windows.Forms.ComboBox();
            this.lblDMDFormat = new System.Windows.Forms.Label();
            this.chkDMDCompose = new System.Windows.Forms.CheckBox();
            this.lblDMDWidth = new System.Windows.Forms.Label();
            this.txtDMDHeight = new System.Windows.Forms.TextBox();
            this.lblDMDHeight = new System.Windows.Forms.Label();
            this.txtDMDDotSize = new System.Windows.Forms.TextBox();
            this.lblDMDDotSize = new System.Windows.Forms.Label();
            this.tabScreen = new System.Windows.Forms.TabPage();
            this.flpScreen = new System.Windows.Forms.FlowLayoutPanel();
            this.groupMPV = new System.Windows.Forms.GroupBox();
            this.btnBrowseGameStartMedia = new System.Windows.Forms.Button();
            this.txtGameStartMediaPath = new System.Windows.Forms.TextBox();
            this.lblGameStartMediaPath = new System.Windows.Forms.Label();
            this.btnBrowseGameCustomMarquee = new System.Windows.Forms.Button();
            this.txtGameCustomMarqueePath = new System.Windows.Forms.TextBox();
            this.lblGameCustomMarqueePath = new System.Windows.Forms.Label();
            this.btnBrowseSystemCustomMarquee = new System.Windows.Forms.Button();
            this.txtSystemCustomMarqueePath = new System.Windows.Forms.TextBox();
            this.lblSystemCustomMarqueePath = new System.Windows.Forms.Label();
            this.lblScreenNumber = new System.Windows.Forms.Label();
            this.txtMarqueeHeight = new System.Windows.Forms.TextBox();
            this.lblMarqueeHeight = new System.Windows.Forms.Label();
            this.txtMarqueeWidth = new System.Windows.Forms.TextBox();
            this.lblMarqueeWidth = new System.Windows.Forms.Label();
            this.btnBrowseMPV = new System.Windows.Forms.Button();
            this.txtMPVPath = new System.Windows.Forms.TextBox();
            this.lblMPVPath = new System.Windows.Forms.Label();
            this.tabPinball = new System.Windows.Forms.TabPage();
            this.flpPinball = new System.Windows.Forms.FlowLayoutPanel();
            this.groupPinball = new System.Windows.Forms.GroupBox();
            this.lblPinballHelp = new System.Windows.Forms.Label();
            this.btnPinballDelete = new System.Windows.Forms.Button();
            this.btnPinballEdit = new System.Windows.Forms.Button();
            this.btnPinballAdd = new System.Windows.Forms.Button();
            this.dgvPinball = new System.Windows.Forms.DataGridView();
            this.colPinballSystem = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPinballCommand = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.tabAdvanced = new System.Windows.Forms.TabPage();
            this.flpAdvanced = new System.Windows.Forms.FlowLayoutPanel();
            this.groupAdvanced = new System.Windows.Forms.GroupBox();
            this.txtSystemAliases = new System.Windows.Forms.TextBox();
            this.lblSystemAliases = new System.Windows.Forms.Label();
            this.txtCollectionCorrelation = new System.Windows.Forms.TextBox();
            this.lblCollectionCorrelation = new System.Windows.Forms.Label();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.btn_cancel = new System.Windows.Forms.Button();
            this.btn_save = new System.Windows.Forms.Button();
            this.menuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.tabControl.SuspendLayout();
            this.tabGeneral.SuspendLayout();
            this.flpGeneral.SuspendLayout();
            this.groupPaths.SuspendLayout();
            this.groupMarquee.SuspendLayout();
            this.groupRA.SuspendLayout();
            this.groupUI.SuspendLayout();
            this.groupLogging.SuspendLayout();
            this.tabScraping.SuspendLayout();
            this.flpScraping.SuspendLayout();
            this.groupScrapingGeneral.SuspendLayout();
            this.groupArcadeItalia.SuspendLayout();
            this.groupScreenScraper.SuspendLayout();
            this.tabDMD.SuspendLayout();
            this.flpDMD.SuspendLayout();
            this.groupDMDGeneral.SuspendLayout();
            this.groupDMDDisplay.SuspendLayout();
            this.tabScreen.SuspendLayout();
            this.flpScreen.SuspendLayout();
            this.groupMPV.SuspendLayout();
            this.tabPinball.SuspendLayout();
            this.flpPinball.SuspendLayout();
            this.groupPinball.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvPinball)).BeginInit();
            this.tabAdvanced.SuspendLayout();
            this.flpAdvanced.SuspendLayout();
            this.groupAdvanced.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.SuspendLayout();
            // 
            // txtIMPath
            // 
            this.txtIMPath.BackColor = System.Drawing.Color.White;
            this.txtIMPath.ForeColor = System.Drawing.Color.Black;
            this.txtIMPath.Location = new System.Drawing.Point(150, 88);
            this.txtIMPath.Name = "txtIMPath";
            this.txtIMPath.Size = new System.Drawing.Size(436, 23);
            this.txtIMPath.TabIndex = 7;
            this.toolTipHint.SetToolTip(this.txtIMPath, "Path to ImageMagick \'convert.exe\'. (EN)\nChemin vers \'convert.exe\' d\'ImageMagick. " +
        "(FR)");
            // 
            // txtRomsPath
            // 
            this.txtRomsPath.BackColor = System.Drawing.Color.White;
            this.txtRomsPath.ForeColor = System.Drawing.Color.Black;
            this.txtRomsPath.Location = new System.Drawing.Point(150, 55);
            this.txtRomsPath.Name = "txtRomsPath";
            this.txtRomsPath.Size = new System.Drawing.Size(436, 23);
            this.txtRomsPath.TabIndex = 4;
            this.toolTipHint.SetToolTip(this.txtRomsPath, "Base ROMs path for RetroBat. (EN)\nChemin de base des ROMs pour RetroBat. (FR)");
            // 
            // txtRetroBatPath
            // 
            this.txtRetroBatPath.BackColor = System.Drawing.Color.White;
            this.txtRetroBatPath.ForeColor = System.Drawing.Color.Black;
            this.txtRetroBatPath.Location = new System.Drawing.Point(150, 22);
            this.txtRetroBatPath.Name = "txtRetroBatPath";
            this.txtRetroBatPath.Size = new System.Drawing.Size(436, 23);
            this.txtRetroBatPath.TabIndex = 1;
            this.toolTipHint.SetToolTip(this.txtRetroBatPath, "Main installation directory of RetroBat. (EN)\nRépertoire d\'installation principal" +
        " de RetroBat. (FR)");
            // 
            // txtVideoFolder
            // 
            this.txtVideoFolder.BackColor = System.Drawing.Color.White;
            this.txtVideoFolder.ForeColor = System.Drawing.Color.Black;
            this.txtVideoFolder.Location = new System.Drawing.Point(450, 175);
            this.txtVideoFolder.Name = "txtVideoFolder";
            this.txtVideoFolder.Size = new System.Drawing.Size(264, 23);
            this.txtVideoFolder.TabIndex = 11;
            this.toolTipHint.SetToolTip(this.txtVideoFolder, "Subfolder name where generated videos will be stored. (EN)\nNom du sous-dossier où" +
        " les vidéos générées seront stockées. (FR)");
            // 
            // cboVideoGeneration
            // 
            this.cboVideoGeneration.BackColor = System.Drawing.Color.White;
            this.cboVideoGeneration.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboVideoGeneration.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboVideoGeneration.ForeColor = System.Drawing.Color.Black;
            this.cboVideoGeneration.FormattingEnabled = true;
            this.cboVideoGeneration.Items.AddRange(new object[] {
            "false",
            "true"});
            this.cboVideoGeneration.Location = new System.Drawing.Point(180, 175);
            this.cboVideoGeneration.Name = "cboVideoGeneration";
            this.cboVideoGeneration.Size = new System.Drawing.Size(200, 23);
            this.cboVideoGeneration.TabIndex = 9;
            this.toolTipHint.SetToolTip(this.cboVideoGeneration, "Enable generation of marquee videos from game previews. (EN)\nActive la génération" +
        " de vidéos marquee à partir des aperçus de jeu. (FR)");
            // 
            // lbl_ffmpeg_hw_encoding
            // 
            this.lbl_ffmpeg_hw_encoding.AutoSize = true;
            this.lbl_ffmpeg_hw_encoding.Location = new System.Drawing.Point(15, 211);
            this.lbl_ffmpeg_hw_encoding.Name = "lbl_ffmpeg_hw_encoding";
            this.lbl_ffmpeg_hw_encoding.Size = new System.Drawing.Size(121, 15);
            this.lbl_ffmpeg_hw_encoding.TabIndex = 12;
            this.lbl_ffmpeg_hw_encoding.Text = "FFmpeg HW Encoding:";
            // 
            // cboFfmpegHwEncoding
            // 
            this.cboFfmpegHwEncoding.BackColor = System.Drawing.Color.White;
            this.cboFfmpegHwEncoding.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboFfmpegHwEncoding.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboFfmpegHwEncoding.ForeColor = System.Drawing.Color.Black;
            this.cboFfmpegHwEncoding.FormattingEnabled = true;
            this.cboFfmpegHwEncoding.Items.AddRange(new object[] {
            "",
            "h264_nvenc",
            "h264_amf",
            "h264_qsv"});
            this.cboFfmpegHwEncoding.Location = new System.Drawing.Point(180, 208);
            this.cboFfmpegHwEncoding.Name = "cboFfmpegHwEncoding";
            this.cboFfmpegHwEncoding.Size = new System.Drawing.Size(200, 23);
            this.cboFfmpegHwEncoding.TabIndex = 13;
            // 
            // chkAutoConvert
            // 
            this.chkAutoConvert.AutoSize = true;
            this.chkAutoConvert.Location = new System.Drawing.Point(180, 148);
            this.chkAutoConvert.Name = "chkAutoConvert";
            this.chkAutoConvert.Size = new System.Drawing.Size(138, 19);
            this.chkAutoConvert.TabIndex = 7;
            this.chkAutoConvert.Text = "Auto Convert Images";
            this.toolTipHint.SetToolTip(this.chkAutoConvert, "Automatically convert and resize static images for better performance. (EN)\nConve" +
        "rtit et redimensionne automatiquement les images statiques pour de meilleures pe" +
        "rformances. (FR)");
            this.chkAutoConvert.UseVisualStyleBackColor = true;
            // 
            // cboLayout
            // 
            this.cboLayout.BackColor = System.Drawing.Color.White;
            this.cboLayout.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboLayout.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboLayout.ForeColor = System.Drawing.Color.Black;
            this.cboLayout.FormattingEnabled = true;
            this.cboLayout.Items.AddRange(new object[] {
            "standard",
            "gradient-left",
            "gradient-right",
            "gradient-standard"});
            this.cboLayout.Location = new System.Drawing.Point(180, 115);
            this.cboLayout.Name = "cboLayout";
            this.cboLayout.Size = new System.Drawing.Size(200, 23);
            this.cboLayout.TabIndex = 6;
            this.toolTipHint.SetToolTip(this.cboLayout, "Visual layout style for the marquee. (EN)\nStyle de mise en page visuelle pour la " +
        "marquee. (FR)");
            // 
            // cboComposeMedia
            // 
            this.cboComposeMedia.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboComposeMedia.FormattingEnabled = true;
            this.cboComposeMedia.Items.AddRange(new object[] {
            "fanart",
            "image"});
            this.cboComposeMedia.Location = new System.Drawing.Point(180, 82);
            this.cboComposeMedia.Name = "cboComposeMedia";
            this.cboComposeMedia.Size = new System.Drawing.Size(200, 23);
            this.cboComposeMedia.TabIndex = 4;
            this.toolTipHint.SetToolTip(this.cboComposeMedia, "Preferred media type to use as background for composition. (EN)\nType de média pré" +
        "féré à utiliser comme fond pour la composition. (FR)");
            // 
            // chkCompose
            // 
            this.chkCompose.AutoSize = true;
            this.chkCompose.Location = new System.Drawing.Point(180, 55);
            this.chkCompose.Name = "chkCompose";
            this.chkCompose.Size = new System.Drawing.Size(133, 19);
            this.chkCompose.TabIndex = 2;
            this.chkCompose.Text = "Enable Composition";
            this.toolTipHint.SetToolTip(this.chkCompose, "Enable dynamic merging of logo and background if no marquee is found. (EN)\nActive" +
        " la fusion dynamique du logo et du fond si aucune marquee n\'est trouvée. (FR)");
            this.chkCompose.UseVisualStyleBackColor = true;
            // 
            // txtBackgroundColor
            // 
            this.txtBackgroundColor.BackColor = System.Drawing.Color.White;
            this.txtBackgroundColor.ForeColor = System.Drawing.Color.Black;
            this.txtBackgroundColor.Location = new System.Drawing.Point(180, 22);
            this.txtBackgroundColor.Name = "txtBackgroundColor";
            this.txtBackgroundColor.Size = new System.Drawing.Size(200, 23);
            this.txtBackgroundColor.TabIndex = 1;
            this.toolTipHint.SetToolTip(this.txtBackgroundColor, "Color or Hex code for background (e.g. Black, #222222). (EN)\nCouleur ou code Hex " +
        "pour le fond (ex: Black, #222222). (FR)");
            // 
            // txtRAOverlays
            // 
            this.txtRAOverlays.BackColor = System.Drawing.Color.White;
            this.txtRAOverlays.ForeColor = System.Drawing.Color.Black;
            this.txtRAOverlays.Location = new System.Drawing.Point(180, 121);
            this.txtRAOverlays.Name = "txtRAOverlays";
            this.txtRAOverlays.Size = new System.Drawing.Size(534, 23);
            this.txtRAOverlays.TabIndex = 6;
            // 
            // cboRADisplayTarget
            // 
            this.cboRADisplayTarget.BackColor = System.Drawing.Color.White;
            this.cboRADisplayTarget.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboRADisplayTarget.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboRADisplayTarget.ForeColor = System.Drawing.Color.Black;
            this.cboRADisplayTarget.FormattingEnabled = true;
            this.cboRADisplayTarget.Items.AddRange(new object[] {
            "both",
            "mpv",
            "dmd"});
            this.cboRADisplayTarget.Location = new System.Drawing.Point(180, 88);
            this.cboRADisplayTarget.Name = "cboRADisplayTarget";
            this.cboRADisplayTarget.Size = new System.Drawing.Size(200, 23);
            this.cboRADisplayTarget.TabIndex = 4;
            this.toolTipHint.SetToolTip(this.cboRADisplayTarget, "Display target for RetroAchievements info (MPV, DMD, or Both). (EN)\nCible d\'affic" +
        "hage pour les infos RetroAchievements (MPV, DMD, ou les deux). (FR)");
            // 
            // txtRAApiKey
            // 
            this.txtRAApiKey.BackColor = System.Drawing.Color.White;
            this.txtRAApiKey.ForeColor = System.Drawing.Color.Black;
            this.txtRAApiKey.Location = new System.Drawing.Point(180, 55);
            this.txtRAApiKey.Name = "txtRAApiKey";
            this.txtRAApiKey.PasswordChar = '*';
            this.txtRAApiKey.Size = new System.Drawing.Size(534, 23);
            this.txtRAApiKey.TabIndex = 2;
            this.toolTipHint.SetToolTip(this.txtRAApiKey, "Your RetroAchievements Web API Key (required). (EN)\nVotre clé API Web RetroAchiev" +
        "ements (requise). (FR)");
            // 
            // chkRAEnable
            // 
            this.chkRAEnable.AutoSize = true;
            this.chkRAEnable.Location = new System.Drawing.Point(15, 25);
            this.chkRAEnable.Name = "chkRAEnable";
            this.chkRAEnable.Size = new System.Drawing.Size(167, 19);
            this.chkRAEnable.TabIndex = 0;
            this.chkRAEnable.Text = "Enable RetroAchievements";
            this.toolTipHint.SetToolTip(this.chkRAEnable, "Master switch to enable RetroAchievements integration. (EN)\nInterrupteur principa" +
        "l pour activer l\'intégration RetroAchievements. (FR)");
            this.chkRAEnable.UseVisualStyleBackColor = true;
            // 
            // txtAcceptedFormats
            // 
            this.txtAcceptedFormats.BackColor = System.Drawing.Color.White;
            this.txtAcceptedFormats.ForeColor = System.Drawing.Color.Black;
            this.txtAcceptedFormats.Location = new System.Drawing.Point(180, 88);
            this.txtAcceptedFormats.Name = "txtAcceptedFormats";
            this.txtAcceptedFormats.Size = new System.Drawing.Size(534, 23);
            this.txtAcceptedFormats.TabIndex = 4;
            // 
            // cboAutoStart
            // 
            this.cboAutoStart.BackColor = System.Drawing.Color.White;
            this.cboAutoStart.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboAutoStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboAutoStart.ForeColor = System.Drawing.Color.Black;
            this.cboAutoStart.FormattingEnabled = true;
            this.cboAutoStart.Items.AddRange(new object[] {
            "false",
            "windows",
            "retrobat"});
            this.cboAutoStart.Location = new System.Drawing.Point(180, 55);
            this.cboAutoStart.Name = "cboAutoStart";
            this.cboAutoStart.Size = new System.Drawing.Size(200, 23);
            this.cboAutoStart.TabIndex = 2;
            // 
            // txtPrioritySource
            // 
            this.txtPrioritySource.BackColor = System.Drawing.Color.White;
            this.txtPrioritySource.ForeColor = System.Drawing.Color.Black;
            this.txtPrioritySource.Location = new System.Drawing.Point(180, 55);
            this.txtPrioritySource.Name = "txtPrioritySource";
            this.txtPrioritySource.Size = new System.Drawing.Size(543, 23);
            this.txtPrioritySource.TabIndex = 2;
            // 
            // chkAutoScraping
            // 
            this.chkAutoScraping.AutoSize = true;
            this.chkAutoScraping.Location = new System.Drawing.Point(15, 25);
            this.chkAutoScraping.Name = "chkAutoScraping";
            this.chkAutoScraping.Size = new System.Drawing.Size(101, 19);
            this.chkAutoScraping.TabIndex = 0;
            this.chkAutoScraping.Text = "Auto Scraping";
            this.chkAutoScraping.UseVisualStyleBackColor = true;
            // 
            // cboArcadeItaliaMediaType
            // 
            this.cboArcadeItaliaMediaType.BackColor = System.Drawing.Color.White;
            this.cboArcadeItaliaMediaType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboArcadeItaliaMediaType.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboArcadeItaliaMediaType.ForeColor = System.Drawing.Color.Black;
            this.cboArcadeItaliaMediaType.FormattingEnabled = true;
            this.cboArcadeItaliaMediaType.Items.AddRange(new object[] {
            "marquee",
            "snapshot",
            "title",
            "cabinet"});
            this.cboArcadeItaliaMediaType.Location = new System.Drawing.Point(180, 55);
            this.cboArcadeItaliaMediaType.Name = "cboArcadeItaliaMediaType";
            this.cboArcadeItaliaMediaType.Size = new System.Drawing.Size(200, 23);
            this.cboArcadeItaliaMediaType.TabIndex = 2;
            this.toolTipHint.SetToolTip(this.cboArcadeItaliaMediaType, "Preferred media type to download from ArcadeItalia. (EN)\nType de média préféré à " +
        "télécharger d\'ArcadeItalia. (FR)");
            // 
            // txtDMDScrapMediaType
            // 
            this.txtDMDScrapMediaType.BackColor = System.Drawing.Color.White;
            this.txtDMDScrapMediaType.ForeColor = System.Drawing.Color.Black;
            this.txtDMDScrapMediaType.Location = new System.Drawing.Point(180, 253);
            this.txtDMDScrapMediaType.Name = "txtDMDScrapMediaType";
            this.txtDMDScrapMediaType.Size = new System.Drawing.Size(543, 23);
            this.txtDMDScrapMediaType.TabIndex = 14;
            this.toolTipHint.SetToolTip(this.txtDMDScrapMediaType, "Media type to scrape for DMD. (EN)\nType de média à scraper pour DMD. (FR)\nOptions" +
        ": screenmarqueesmall, wheel, marquee, steamgrid");
            // 
            // txtMPVScrapMediaType
            // 
            this.txtMPVScrapMediaType.BackColor = System.Drawing.Color.White;
            this.txtMPVScrapMediaType.ForeColor = System.Drawing.Color.Black;
            this.txtMPVScrapMediaType.Location = new System.Drawing.Point(180, 220);
            this.txtMPVScrapMediaType.Name = "txtMPVScrapMediaType";
            this.txtMPVScrapMediaType.Size = new System.Drawing.Size(543, 23);
            this.txtMPVScrapMediaType.TabIndex = 12;
            this.toolTipHint.SetToolTip(this.txtMPVScrapMediaType, "Media type to scrape for Marquee/MPV. (EN)\nType de média à scraper pour Marquee/M" +
        "PV. (FR)\nOptions: screenmarquee, wheel, fanart, video, box-2D, box-3D");
            // 
            // chkSSGlobal
            // 
            this.chkSSGlobal.AutoSize = true;
            this.chkSSGlobal.Location = new System.Drawing.Point(180, 190);
            this.chkSSGlobal.Name = "chkSSGlobal";
            this.chkSSGlobal.Size = new System.Drawing.Size(109, 19);
            this.chkSSGlobal.TabIndex = 10;
            this.chkSSGlobal.Text = "Global Scraping";
            this.toolTipHint.SetToolTip(this.chkSSGlobal, "Enable global scraping for all systems (overrides individual settings). (EN)\nActi" +
        "ver le scraping global pour tous les systèmes (écrase les réglages individuels)." +
        " (FR)");
            this.chkSSGlobal.UseVisualStyleBackColor = true;
            // 
            // cboDMDModel
            // 
            this.cboDMDModel.BackColor = System.Drawing.Color.White;
            this.cboDMDModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDMDModel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboDMDModel.ForeColor = System.Drawing.Color.Black;
            this.cboDMDModel.FormattingEnabled = true;
            this.cboDMDModel.Items.AddRange(new object[] {
            "virtual",
            "virtualdmd",
            "pin2dmd",
            "zedmd",
            "zedmdhd",
            "zedmdwifi",
            "zedmdhdwifi",
            "pindmdv1",
            "pindmdv2",
            "pindmdv3",
            "pixelcade"});
            this.cboDMDModel.Location = new System.Drawing.Point(180, 55);
            this.cboDMDModel.Name = "cboDMDModel";
            this.cboDMDModel.Size = new System.Drawing.Size(200, 23);
            this.cboDMDModel.TabIndex = 9;
            this.toolTipHint.SetToolTip(this.cboDMDModel, "Select your DMD hardware model or use \'virtualdmd\'. (EN)\nSélectionnez votre modèl" +
        "e de DMD ou utilisez \'virtualdmd\'. (FR)");
            // 
            // chkDMDEnabled
            // 
            this.chkDMDEnabled.AutoSize = true;
            this.chkDMDEnabled.Location = new System.Drawing.Point(15, 25);
            this.chkDMDEnabled.Name = "chkDMDEnabled";
            this.chkDMDEnabled.Size = new System.Drawing.Size(91, 19);
            this.chkDMDEnabled.TabIndex = 0;
            this.chkDMDEnabled.Text = "Enable DMD";
            this.toolTipHint.SetToolTip(this.chkDMDEnabled, "Master switch for DMD hardware or virtual display. (EN)\nInterrupteur principal po" +
        "ur le matériel DMD ou l\'affichage virtuel. (FR)");
            this.chkDMDEnabled.UseVisualStyleBackColor = true;
            // 
            // txtDMDWidth
            // 
            this.txtDMDWidth.BackColor = System.Drawing.Color.White;
            this.txtDMDWidth.ForeColor = System.Drawing.Color.Black;
            this.txtDMDWidth.Location = new System.Drawing.Point(180, 89);
            this.txtDMDWidth.Name = "txtDMDWidth";
            this.txtDMDWidth.Size = new System.Drawing.Size(100, 23);
            this.txtDMDWidth.TabIndex = 4;
            this.toolTipHint.SetToolTip(this.txtDMDWidth, "Width pixels of the DMD display. (EN)\nLargeur en pixels de l\'affichage DMD. (FR)");
            this.txtDMDWidth.TextChanged += new System.EventHandler(this.txtDMDWidth_TextChanged);
            // 
            // txtScreenNumber
            // 
            this.txtScreenNumber.BackColor = System.Drawing.Color.White;
            this.txtScreenNumber.ForeColor = System.Drawing.Color.Black;
            this.txtScreenNumber.Location = new System.Drawing.Point(180, 89);
            this.txtScreenNumber.Name = "txtScreenNumber";
            this.txtScreenNumber.Size = new System.Drawing.Size(100, 23);
            this.txtScreenNumber.TabIndex = 8;
            this.toolTipHint.SetToolTip(this.txtScreenNumber, "Index of secondary monitor (0, 1, 2...).\nSet to \'false\' to disable MPV screen. (E" +
        "N)\nIndex du moniteur secondaire (0, 1, 2...).\nMettre sur \'false\' pour désactiver" +
        " l\'écran MPV. (FR)");
            // 
            // lbl_hw_decoding
            // 
            this.lbl_hw_decoding.AutoSize = true;
            this.lbl_hw_decoding.Location = new System.Drawing.Point(15, 124);
            this.lbl_hw_decoding.Name = "lbl_hw_decoding";
            this.lbl_hw_decoding.Size = new System.Drawing.Size(111, 15);
            this.lbl_hw_decoding.TabIndex = 9;
            this.lbl_hw_decoding.Text = "Hardware Decoding:";
            // 
            // cboHwDecoding
            // 
            this.cboHwDecoding.BackColor = System.Drawing.Color.White;
            this.cboHwDecoding.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboHwDecoding.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboHwDecoding.ForeColor = System.Drawing.Color.Black;
            this.cboHwDecoding.FormattingEnabled = true;
            this.cboHwDecoding.Items.AddRange(new object[] {
            "no",
            "auto",
            "d3d11va",
            "dxva2"});
            this.cboHwDecoding.Location = new System.Drawing.Point(180, 121);
            this.cboHwDecoding.Name = "cboHwDecoding";
            this.cboHwDecoding.Size = new System.Drawing.Size(200, 23);
            this.cboHwDecoding.TabIndex = 10;
            // 
            // txtSSQueueLimit
            // 
            this.txtSSQueueLimit.BackColor = System.Drawing.Color.White;
            this.txtSSQueueLimit.ForeColor = System.Drawing.Color.Black;
            this.txtSSQueueLimit.Location = new System.Drawing.Point(402, 157);
            this.txtSSQueueLimit.Name = "txtSSQueueLimit";
            this.txtSSQueueLimit.Size = new System.Drawing.Size(60, 23);
            this.txtSSQueueLimit.TabIndex = 10;
            this.txtSSQueueLimit.Text = "5";
            this.toolTipHint.SetToolTip(this.txtSSQueueLimit, "EN: Maximum games in download queue (0 = unlimited).\r\nFR: Nombre maximum de jeux " +
        "en file d\'attente (0 = illimité).");
            // 
            // txtSSQueueKeep
            // 
            this.txtSSQueueKeep.BackColor = System.Drawing.Color.White;
            this.txtSSQueueKeep.ForeColor = System.Drawing.Color.Black;
            this.txtSSQueueKeep.Location = new System.Drawing.Point(581, 157);
            this.txtSSQueueKeep.Name = "txtSSQueueKeep";
            this.txtSSQueueKeep.Size = new System.Drawing.Size(60, 23);
            this.txtSSQueueKeep.TabIndex = 12;
            this.txtSSQueueKeep.Text = "3";
            this.toolTipHint.SetToolTip(this.txtSSQueueKeep, "EN: Games to keep when pruning queue.\r\nFR: Jeux à conserver lors de l\'élagage.");
            // 
            // menuStrip
            // 
            this.menuStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.menuStrip.ForeColor = System.Drawing.Color.White;
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menu_file,
            this.menu_tools,
            this.menu_help});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(1013, 24);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip";
            // 
            // menu_file
            // 
            this.menu_file.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menu_file_save,
            this.menu_file_reload,
            this.toolStripSeparator1,
            this.menu_file_exit});
            this.menu_file.Name = "menu_file";
            this.menu_file.Size = new System.Drawing.Size(37, 20);
            this.menu_file.Text = "File";
            // 
            // menu_file_save
            // 
            this.menu_file_save.Name = "menu_file_save";
            this.menu_file_save.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)));
            this.menu_file_save.Size = new System.Drawing.Size(138, 22);
            this.menu_file_save.Text = "Save";
            this.menu_file_save.Click += new System.EventHandler(this.menu_file_save_Click);
            // 
            // menu_file_reload
            // 
            this.menu_file_reload.Name = "menu_file_reload";
            this.menu_file_reload.ShortcutKeys = System.Windows.Forms.Keys.F5;
            this.menu_file_reload.Size = new System.Drawing.Size(138, 22);
            this.menu_file_reload.Text = "Reload";
            this.menu_file_reload.Click += new System.EventHandler(this.menu_file_reload_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(135, 6);
            // 
            // menu_file_exit
            // 
            this.menu_file_exit.Name = "menu_file_exit";
            this.menu_file_exit.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4)));
            this.menu_file_exit.Size = new System.Drawing.Size(138, 22);
            this.menu_file_exit.Text = "Exit";
            this.menu_file_exit.Click += new System.EventHandler(this.menu_file_exit_Click);
            // 
            // menu_tools
            // 
            this.menu_tools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menu_tools_clear_cache,
            this.menu_tools_open_logs,
            this.menu_tools_restart_app});
            this.menu_tools.Name = "menu_tools";
            this.menu_tools.Size = new System.Drawing.Size(47, 20);
            this.menu_tools.Text = "Tools";
            // 
            // menu_tools_clear_cache
            // 
            this.menu_tools_clear_cache.Name = "menu_tools_clear_cache";
            this.menu_tools_clear_cache.Size = new System.Drawing.Size(174, 22);
            this.menu_tools_clear_cache.Text = "Clear Cache";
            this.menu_tools_clear_cache.Click += new System.EventHandler(this.menu_tools_clear_cache_Click);
            // 
            // menu_tools_open_logs
            // 
            this.menu_tools_open_logs.Name = "menu_tools_open_logs";
            this.menu_tools_open_logs.Size = new System.Drawing.Size(174, 22);
            this.menu_tools_open_logs.Text = "Open Logs Folder";
            this.menu_tools_open_logs.Click += new System.EventHandler(this.menu_tools_open_logs_Click);
            // 
            // menu_tools_restart_app
            // 
            this.menu_tools_restart_app.Name = "menu_tools_restart_app";
            this.menu_tools_restart_app.Size = new System.Drawing.Size(174, 22);
            this.menu_tools_restart_app.Text = "Restart Application";
            // 
            // menu_help
            // 
            this.menu_help.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menu_help_readme,
            this.menu_help_about});
            this.menu_help.Name = "menu_help";
            this.menu_help.Size = new System.Drawing.Size(44, 20);
            this.menu_help.Text = "Help";
            // 
            // menu_help_readme
            // 
            this.menu_help_readme.Name = "menu_help_readme";
            this.menu_help_readme.Size = new System.Drawing.Size(117, 22);
            this.menu_help_readme.Text = "Readme";
            this.menu_help_readme.Click += new System.EventHandler(this.menu_help_readme_Click);
            // 
            // menu_help_about
            // 
            this.menu_help_about.Name = "menu_help_about";
            this.menu_help_about.Size = new System.Drawing.Size(117, 22);
            this.menu_help_about.Text = "About";
            this.menu_help_about.Click += new System.EventHandler(this.menu_help_about_Click);
            // 
            // splitContainer
            // 
            this.splitContainer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainer.IsSplitterFixed = true;
            this.splitContainer.Location = new System.Drawing.Point(0, 24);
            this.splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            this.splitContainer.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.splitContainer.Panel1.Controls.Add(this.btnTabAdvanced);
            this.splitContainer.Panel1.Controls.Add(this.btnTabPinball);
            this.splitContainer.Panel1.Controls.Add(this.btnTabScreen);
            this.splitContainer.Panel1.Controls.Add(this.btnTabDMD);
            this.splitContainer.Panel1.Controls.Add(this.btnTabScraping);
            this.splitContainer.Panel1.Controls.Add(this.btnTabGeneral);
            // 
            // splitContainer.Panel2
            // 
            this.splitContainer.Panel2.Controls.Add(this.tabControl);
            this.splitContainer.Size = new System.Drawing.Size(1013, 578);
            this.splitContainer.SplitterDistance = 200;
            this.splitContainer.TabIndex = 1;
            // 
            // btnTabAdvanced
            // 
            this.btnTabAdvanced.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTabAdvanced.FlatAppearance.BorderSize = 0;
            this.btnTabAdvanced.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabAdvanced.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnTabAdvanced.ForeColor = System.Drawing.Color.White;
            this.btnTabAdvanced.Location = new System.Drawing.Point(0, 225);
            this.btnTabAdvanced.Name = "btnTabAdvanced";
            this.btnTabAdvanced.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.btnTabAdvanced.Size = new System.Drawing.Size(200, 45);
            this.btnTabAdvanced.TabIndex = 5;
            this.btnTabAdvanced.Text = "Advanced";
            this.btnTabAdvanced.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTabAdvanced.UseVisualStyleBackColor = true;
            this.btnTabAdvanced.Click += new System.EventHandler(this.btnTabAdvanced_Click);
            // 
            // btnTabPinball
            // 
            this.btnTabPinball.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTabPinball.FlatAppearance.BorderSize = 0;
            this.btnTabPinball.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabPinball.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnTabPinball.ForeColor = System.Drawing.Color.White;
            this.btnTabPinball.Location = new System.Drawing.Point(0, 180);
            this.btnTabPinball.Name = "btnTabPinball";
            this.btnTabPinball.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.btnTabPinball.Size = new System.Drawing.Size(200, 45);
            this.btnTabPinball.TabIndex = 4;
            this.btnTabPinball.Text = "Pinball";
            this.btnTabPinball.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTabPinball.UseVisualStyleBackColor = true;
            this.btnTabPinball.Click += new System.EventHandler(this.btnTabPinball_Click);
            // 
            // btnTabScreen
            // 
            this.btnTabScreen.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTabScreen.FlatAppearance.BorderSize = 0;
            this.btnTabScreen.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabScreen.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnTabScreen.ForeColor = System.Drawing.Color.White;
            this.btnTabScreen.Location = new System.Drawing.Point(0, 135);
            this.btnTabScreen.Name = "btnTabScreen";
            this.btnTabScreen.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.btnTabScreen.Size = new System.Drawing.Size(200, 45);
            this.btnTabScreen.TabIndex = 3;
            this.btnTabScreen.Text = "Screen && MPV";
            this.btnTabScreen.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTabScreen.UseVisualStyleBackColor = true;
            this.btnTabScreen.Click += new System.EventHandler(this.btnTabScreen_Click);
            // 
            // btnTabDMD
            // 
            this.btnTabDMD.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTabDMD.FlatAppearance.BorderSize = 0;
            this.btnTabDMD.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabDMD.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnTabDMD.ForeColor = System.Drawing.Color.White;
            this.btnTabDMD.Location = new System.Drawing.Point(0, 90);
            this.btnTabDMD.Name = "btnTabDMD";
            this.btnTabDMD.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.btnTabDMD.Size = new System.Drawing.Size(200, 45);
            this.btnTabDMD.TabIndex = 2;
            this.btnTabDMD.Text = "DMD Display";
            this.btnTabDMD.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTabDMD.UseVisualStyleBackColor = true;
            this.btnTabDMD.Click += new System.EventHandler(this.btnTabDMD_Click);
            // 
            // btnTabScraping
            // 
            this.btnTabScraping.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTabScraping.FlatAppearance.BorderSize = 0;
            this.btnTabScraping.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabScraping.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.btnTabScraping.ForeColor = System.Drawing.Color.White;
            this.btnTabScraping.Location = new System.Drawing.Point(0, 45);
            this.btnTabScraping.Name = "btnTabScraping";
            this.btnTabScraping.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.btnTabScraping.Size = new System.Drawing.Size(200, 45);
            this.btnTabScraping.TabIndex = 1;
            this.btnTabScraping.Text = "Scraping";
            this.btnTabScraping.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTabScraping.UseVisualStyleBackColor = true;
            this.btnTabScraping.Click += new System.EventHandler(this.btnTabScraping_Click);
            // 
            // btnTabGeneral
            // 
            this.btnTabGeneral.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(122)))), ((int)(((byte)(204)))));
            this.btnTabGeneral.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTabGeneral.FlatAppearance.BorderSize = 0;
            this.btnTabGeneral.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabGeneral.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnTabGeneral.ForeColor = System.Drawing.Color.White;
            this.btnTabGeneral.Location = new System.Drawing.Point(0, 0);
            this.btnTabGeneral.Name = "btnTabGeneral";
            this.btnTabGeneral.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.btnTabGeneral.Size = new System.Drawing.Size(200, 45);
            this.btnTabGeneral.TabIndex = 0;
            this.btnTabGeneral.Text = "General";
            this.btnTabGeneral.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnTabGeneral.UseVisualStyleBackColor = false;
            this.btnTabGeneral.Click += new System.EventHandler(this.btnTabGeneral_Click);
            // 
            // tabControl
            // 
            this.tabControl.Appearance = System.Windows.Forms.TabAppearance.FlatButtons;
            this.tabControl.Controls.Add(this.tabGeneral);
            this.tabControl.Controls.Add(this.tabScraping);
            this.tabControl.Controls.Add(this.tabDMD);
            this.tabControl.Controls.Add(this.tabScreen);
            this.tabControl.Controls.Add(this.tabPinball);
            this.tabControl.Controls.Add(this.tabAdvanced);
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.tabControl.ForeColor = System.Drawing.Color.Black;
            this.tabControl.Location = new System.Drawing.Point(0, 0);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(809, 578);
            this.tabControl.TabIndex = 0;
            this.tabControl.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.tabControl_DrawItem);
            // 
            // tabGeneral
            // 
            this.tabGeneral.AutoScroll = true;
            this.tabGeneral.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.tabGeneral.Controls.Add(this.flpGeneral);
            this.tabGeneral.ForeColor = System.Drawing.Color.Black;
            this.tabGeneral.Location = new System.Drawing.Point(4, 27);
            this.tabGeneral.Name = "tabGeneral";
            this.tabGeneral.Padding = new System.Windows.Forms.Padding(10);
            this.tabGeneral.Size = new System.Drawing.Size(801, 547);
            this.tabGeneral.TabIndex = 0;
            this.tabGeneral.Text = "General";
            // 
            // flpGeneral
            // 
            this.flpGeneral.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flpGeneral.AutoScroll = true;
            this.flpGeneral.AutoSize = true;
            this.flpGeneral.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpGeneral.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.flpGeneral.Controls.Add(this.groupPaths);
            this.flpGeneral.Controls.Add(this.groupMarquee);
            this.flpGeneral.Controls.Add(this.groupRA);
            this.flpGeneral.Controls.Add(this.groupUI);
            this.flpGeneral.Controls.Add(this.groupLogging);
            this.flpGeneral.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpGeneral.ForeColor = System.Drawing.Color.White;
            this.flpGeneral.Location = new System.Drawing.Point(0, 0);
            this.flpGeneral.Name = "flpGeneral";
            this.flpGeneral.Padding = new System.Windows.Forms.Padding(10);
            this.flpGeneral.Size = new System.Drawing.Size(789, 770);
            this.flpGeneral.TabIndex = 0;
            this.flpGeneral.WrapContents = false;
            // 
            // groupPaths
            // 
            this.groupPaths.Controls.Add(this.btnBrowseIM);
            this.groupPaths.Controls.Add(this.txtIMPath);
            this.groupPaths.Controls.Add(this.lblIMPath);
            this.groupPaths.Controls.Add(this.btnBrowseRoms);
            this.groupPaths.Controls.Add(this.txtRomsPath);
            this.groupPaths.Controls.Add(this.lblRomsPath);
            this.groupPaths.Controls.Add(this.btnBrowseRetroBat);
            this.groupPaths.Controls.Add(this.txtRetroBatPath);
            this.groupPaths.Controls.Add(this.lblRetroBatPath);
            this.groupPaths.ForeColor = System.Drawing.Color.White;
            this.groupPaths.Location = new System.Drawing.Point(13, 13);
            this.groupPaths.Name = "groupPaths";
            this.groupPaths.Size = new System.Drawing.Size(763, 130);
            this.groupPaths.TabIndex = 0;
            this.groupPaths.TabStop = false;
            this.groupPaths.Text = "Core Paths";
            // 
            // btnBrowseIM
            // 
            this.btnBrowseIM.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseIM.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseIM.ForeColor = System.Drawing.Color.White;
            this.btnBrowseIM.Location = new System.Drawing.Point(629, 87);
            this.btnBrowseIM.Name = "btnBrowseIM";
            this.btnBrowseIM.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseIM.TabIndex = 8;
            this.btnBrowseIM.Text = "Browse...";
            this.btnBrowseIM.UseVisualStyleBackColor = false;
            this.btnBrowseIM.Click += new System.EventHandler(this.btnBrowseIM_Click);
            // 
            // lblIMPath
            // 
            this.lblIMPath.AutoSize = true;
            this.lblIMPath.Location = new System.Drawing.Point(15, 91);
            this.lblIMPath.Name = "lblIMPath";
            this.lblIMPath.Size = new System.Drawing.Size(109, 15);
            this.lblIMPath.TabIndex = 6;
            this.lblIMPath.Text = "ImageMagick Path:";
            // 
            // btnBrowseRoms
            // 
            this.btnBrowseRoms.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseRoms.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseRoms.ForeColor = System.Drawing.Color.White;
            this.btnBrowseRoms.Location = new System.Drawing.Point(629, 54);
            this.btnBrowseRoms.Name = "btnBrowseRoms";
            this.btnBrowseRoms.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseRoms.TabIndex = 5;
            this.btnBrowseRoms.Text = "Browse...";
            this.btnBrowseRoms.UseVisualStyleBackColor = false;
            this.btnBrowseRoms.Click += new System.EventHandler(this.btnBrowseRoms_Click);
            // 
            // lblRomsPath
            // 
            this.lblRomsPath.AutoSize = true;
            this.lblRomsPath.Location = new System.Drawing.Point(15, 58);
            this.lblRomsPath.Name = "lblRomsPath";
            this.lblRomsPath.Size = new System.Drawing.Size(69, 15);
            this.lblRomsPath.TabIndex = 3;
            this.lblRomsPath.Text = "ROMs Path:";
            // 
            // btnBrowseRetroBat
            // 
            this.btnBrowseRetroBat.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseRetroBat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseRetroBat.ForeColor = System.Drawing.Color.White;
            this.btnBrowseRetroBat.Location = new System.Drawing.Point(629, 21);
            this.btnBrowseRetroBat.Name = "btnBrowseRetroBat";
            this.btnBrowseRetroBat.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseRetroBat.TabIndex = 2;
            this.btnBrowseRetroBat.Text = "Browse...";
            this.btnBrowseRetroBat.UseVisualStyleBackColor = false;
            this.btnBrowseRetroBat.Click += new System.EventHandler(this.btnBrowseRetroBat_Click);
            // 
            // lblRetroBatPath
            // 
            this.lblRetroBatPath.AutoSize = true;
            this.lblRetroBatPath.Location = new System.Drawing.Point(15, 25);
            this.lblRetroBatPath.Name = "lblRetroBatPath";
            this.lblRetroBatPath.Size = new System.Drawing.Size(82, 15);
            this.lblRetroBatPath.TabIndex = 0;
            this.lblRetroBatPath.Text = "RetroBat Path:";
            // 
            // groupMarquee
            // 
            this.groupMarquee.Controls.Add(this.cboFfmpegHwEncoding);
            this.groupMarquee.Controls.Add(this.lbl_ffmpeg_hw_encoding);
            this.groupMarquee.Controls.Add(this.txtVideoFolder);
            this.groupMarquee.Controls.Add(this.lblVideoFolder);
            this.groupMarquee.Controls.Add(this.cboVideoGeneration);
            this.groupMarquee.Controls.Add(this.lblVideoGeneration);
            this.groupMarquee.Controls.Add(this.chkAutoConvert);
            this.groupMarquee.Controls.Add(this.cboLayout);
            this.groupMarquee.Controls.Add(this.lblLayout);
            this.groupMarquee.Controls.Add(this.cboComposeMedia);
            this.groupMarquee.Controls.Add(this.lblComposeMedia);
            this.groupMarquee.Controls.Add(this.chkCompose);
            this.groupMarquee.Controls.Add(this.txtBackgroundColor);
            this.groupMarquee.Controls.Add(this.lblBackgroundColor);
            this.groupMarquee.ForeColor = System.Drawing.Color.White;
            this.groupMarquee.Location = new System.Drawing.Point(13, 149);
            this.groupMarquee.Name = "groupMarquee";
            this.groupMarquee.Size = new System.Drawing.Size(763, 250);
            this.groupMarquee.TabIndex = 1;
            this.groupMarquee.TabStop = false;
            this.groupMarquee.Text = "Marquee Settings";
            // 
            // lblVideoFolder
            // 
            this.lblVideoFolder.AutoSize = true;
            this.lblVideoFolder.Location = new System.Drawing.Point(400, 178);
            this.lblVideoFolder.Name = "lblVideoFolder";
            this.lblVideoFolder.Size = new System.Drawing.Size(43, 15);
            this.lblVideoFolder.TabIndex = 10;
            this.lblVideoFolder.Text = "Folder:";
            // 
            // lblVideoGeneration
            // 
            this.lblVideoGeneration.AutoSize = true;
            this.lblVideoGeneration.Location = new System.Drawing.Point(15, 178);
            this.lblVideoGeneration.Name = "lblVideoGeneration";
            this.lblVideoGeneration.Size = new System.Drawing.Size(101, 15);
            this.lblVideoGeneration.TabIndex = 8;
            this.lblVideoGeneration.Text = "Video Generation:";
            // 
            // lblLayout
            // 
            this.lblLayout.AutoSize = true;
            this.lblLayout.Location = new System.Drawing.Point(15, 118);
            this.lblLayout.Name = "lblLayout";
            this.lblLayout.Size = new System.Drawing.Size(96, 15);
            this.lblLayout.TabIndex = 5;
            this.lblLayout.Text = "Marquee Layout:";
            // 
            // lblComposeMedia
            // 
            this.lblComposeMedia.AutoSize = true;
            this.lblComposeMedia.Location = new System.Drawing.Point(15, 85);
            this.lblComposeMedia.Name = "lblComposeMedia";
            this.lblComposeMedia.Size = new System.Drawing.Size(97, 15);
            this.lblComposeMedia.TabIndex = 3;
            this.lblComposeMedia.Text = "Compose Media:";
            // 
            // lblBackgroundColor
            // 
            this.lblBackgroundColor.AutoSize = true;
            this.lblBackgroundColor.Location = new System.Drawing.Point(15, 25);
            this.lblBackgroundColor.Name = "lblBackgroundColor";
            this.lblBackgroundColor.Size = new System.Drawing.Size(106, 15);
            this.lblBackgroundColor.TabIndex = 0;
            this.lblBackgroundColor.Text = "Background Color:";
            // 
            // groupRA
            // 
            this.groupRA.Controls.Add(this.chk_ra_dmd_notifs);
            this.groupRA.Controls.Add(this.chk_ra_mpv_notifs);
            this.groupRA.Controls.Add(this.txtDmdRAOverlays);
            this.groupRA.Controls.Add(this.lblDmdRAOverlays);
            this.groupRA.Controls.Add(this.txtMpvRAOverlays);
            this.groupRA.Controls.Add(this.lblMpvRAOverlays);
            this.groupRA.Controls.Add(this.txtRAOverlays);
            this.groupRA.Controls.Add(this.lblRAOverlays);
            this.groupRA.Controls.Add(this.cboRADisplayTarget);
            this.groupRA.Controls.Add(this.lblRADisplayTarget);
            this.groupRA.Controls.Add(this.txtRAApiKey);
            this.groupRA.Controls.Add(this.lblRAApiKey);
            this.groupRA.Controls.Add(this.chkRAEnable);
            this.groupRA.ForeColor = System.Drawing.Color.White;
            this.groupRA.Location = new System.Drawing.Point(13, 375);
            this.groupRA.Name = "groupRA";
            this.groupRA.Size = new System.Drawing.Size(763, 210);
            this.groupRA.TabIndex = 2;
            this.groupRA.TabStop = false;
            this.groupRA.Text = "RetroAchievements";
            // 
            // lblRAOverlays
            // 
            this.lblRAOverlays.AutoSize = true;
            this.lblRAOverlays.Location = new System.Drawing.Point(15, 124);
            this.lblRAOverlays.Name = "lblRAOverlays";
            this.lblRAOverlays.Size = new System.Drawing.Size(83, 15);
            this.lblRAOverlays.TabIndex = 5;
            this.lblRAOverlays.Text = "Overlay Types:";
            // 
            // lblRADisplayTarget
            // 
            this.lblRADisplayTarget.AutoSize = true;
            this.lblRADisplayTarget.Location = new System.Drawing.Point(15, 91);
            this.lblRADisplayTarget.Name = "lblRADisplayTarget";
            this.lblRADisplayTarget.Size = new System.Drawing.Size(84, 15);
            this.lblRADisplayTarget.TabIndex = 3;
            this.lblRADisplayTarget.Text = "Display Target:";
            // 
            // lblRAApiKey
            // 
            this.lblRAApiKey.AutoSize = true;
            this.lblRAApiKey.Location = new System.Drawing.Point(15, 58);
            this.lblRAApiKey.Name = "lblRAApiKey";
            this.lblRAApiKey.Size = new System.Drawing.Size(77, 15);
            this.lblRAApiKey.TabIndex = 1;
            this.lblRAApiKey.Text = "Web API Key:";
            // 
            // lblMpvRAOverlays
            // 
            this.lblMpvRAOverlays.AutoSize = true;
            this.lblMpvRAOverlays.Location = new System.Drawing.Point(15, 154);
            this.lblMpvRAOverlays.Name = "lblMpvRAOverlays";
            this.lblMpvRAOverlays.Size = new System.Drawing.Size(83, 15);
            this.lblMpvRAOverlays.TabIndex = 7;
            this.lblMpvRAOverlays.Text = "MPV Overlays (optional):";
            // 
            // txtMpvRAOverlays
            // 
            this.txtMpvRAOverlays.BackColor = System.Drawing.Color.White;
            this.txtMpvRAOverlays.ForeColor = System.Drawing.Color.Black;
            this.txtMpvRAOverlays.Location = new System.Drawing.Point(180, 151);
            this.txtMpvRAOverlays.Name = "txtMpvRAOverlays";
            this.txtMpvRAOverlays.Size = new System.Drawing.Size(534, 23);
            this.txtMpvRAOverlays.TabIndex = 8;
            // 
            // lblDmdRAOverlays
            // 
            this.lblDmdRAOverlays.AutoSize = true;
            this.lblDmdRAOverlays.Location = new System.Drawing.Point(15, 184);
            this.lblDmdRAOverlays.Name = "lblDmdRAOverlays";
            this.lblDmdRAOverlays.Size = new System.Drawing.Size(83, 15);
            this.lblDmdRAOverlays.TabIndex = 9;
            this.lblDmdRAOverlays.Text = "DMD Overlays (optional):";
            // 
            // txtDmdRAOverlays
            // 
            this.txtDmdRAOverlays.BackColor = System.Drawing.Color.White;
            this.txtDmdRAOverlays.ForeColor = System.Drawing.Color.Black;
            this.txtDmdRAOverlays.Location = new System.Drawing.Point(180, 181);
            this.txtDmdRAOverlays.Name = "txtDmdRAOverlays";
            this.txtDmdRAOverlays.Size = new System.Drawing.Size(534, 23);
            this.txtDmdRAOverlays.TabIndex = 10;
            // 
            // chk_ra_mpv_notifs
            // 
            this.chk_ra_mpv_notifs.AutoSize = true;
            this.chk_ra_mpv_notifs.Location = new System.Drawing.Point(220, 25);
            this.chk_ra_mpv_notifs.Name = "chk_ra_mpv_notifs";
            this.chk_ra_mpv_notifs.Size = new System.Drawing.Size(130, 19);
            this.chk_ra_mpv_notifs.TabIndex = 11;
            this.chk_ra_mpv_notifs.Text = "Show Unlocks (MPV)";
            this.chk_ra_mpv_notifs.UseVisualStyleBackColor = true;
            // 
            // chk_ra_dmd_notifs
            // 
            this.chk_ra_dmd_notifs.AutoSize = true;
            this.chk_ra_dmd_notifs.Location = new System.Drawing.Point(400, 25);
            this.chk_ra_dmd_notifs.Name = "chk_ra_dmd_notifs";
            this.chk_ra_dmd_notifs.Size = new System.Drawing.Size(130, 19);
            this.chk_ra_dmd_notifs.TabIndex = 12;
            this.chk_ra_dmd_notifs.Text = "Show Unlocks (DMD)";
            this.chk_ra_dmd_notifs.UseVisualStyleBackColor = true;
            // 
            // groupUI
            // 
            this.groupUI.Controls.Add(this.txtAcceptedFormats);
            this.groupUI.Controls.Add(this.lblAcceptedFormats);
            this.groupUI.Controls.Add(this.cboAutoStart);
            this.groupUI.Controls.Add(this.lblAutoStart);
            this.groupUI.Controls.Add(this.chkMinimizeToTray);
            this.groupUI.ForeColor = System.Drawing.Color.White;
            this.groupUI.Location = new System.Drawing.Point(13, 531);
            this.groupUI.Name = "groupUI";
            this.groupUI.Size = new System.Drawing.Size(750, 130);
            this.groupUI.TabIndex = 3;
            this.groupUI.TabStop = false;
            this.groupUI.Text = "User Interface";
            // 
            // lblAcceptedFormats
            // 
            this.lblAcceptedFormats.AutoSize = true;
            this.lblAcceptedFormats.Location = new System.Drawing.Point(15, 91);
            this.lblAcceptedFormats.Name = "lblAcceptedFormats";
            this.lblAcceptedFormats.Size = new System.Drawing.Size(127, 15);
            this.lblAcceptedFormats.TabIndex = 3;
            this.lblAcceptedFormats.Text = "Accepted File Formats:";
            // 
            // lblAutoStart
            // 
            this.lblAutoStart.AutoSize = true;
            this.lblAutoStart.Location = new System.Drawing.Point(15, 58);
            this.lblAutoStart.Name = "lblAutoStart";
            this.lblAutoStart.Size = new System.Drawing.Size(97, 15);
            this.lblAutoStart.TabIndex = 1;
            this.lblAutoStart.Text = "Auto Start Mode:";
            // 
            // chkMinimizeToTray
            // 
            this.chkMinimizeToTray.AutoSize = true;
            this.chkMinimizeToTray.Location = new System.Drawing.Point(15, 25);
            this.chkMinimizeToTray.Name = "chkMinimizeToTray";
            this.chkMinimizeToTray.Size = new System.Drawing.Size(155, 19);
            this.chkMinimizeToTray.TabIndex = 0;
            this.chkMinimizeToTray.Text = "Minimize to System Tray";
            this.chkMinimizeToTray.UseVisualStyleBackColor = true;
            // 
            // groupLogging
            // 
            this.groupLogging.Controls.Add(this.txtLogPath);
            this.groupLogging.Controls.Add(this.lblLogPath);
            this.groupLogging.Controls.Add(this.chkLogToFile);
            this.groupLogging.ForeColor = System.Drawing.Color.White;
            this.groupLogging.Location = new System.Drawing.Point(13, 667);
            this.groupLogging.Name = "groupLogging";
            this.groupLogging.Size = new System.Drawing.Size(750, 90);
            this.groupLogging.TabIndex = 4;
            this.groupLogging.TabStop = false;
            this.groupLogging.Text = "Logging";
            // 
            // txtLogPath
            // 
            this.txtLogPath.BackColor = System.Drawing.Color.White;
            this.txtLogPath.ForeColor = System.Drawing.Color.Black;
            this.txtLogPath.Location = new System.Drawing.Point(180, 55);
            this.txtLogPath.Name = "txtLogPath";
            this.txtLogPath.Size = new System.Drawing.Size(534, 23);
            this.txtLogPath.TabIndex = 2;
            // 
            // lblLogPath
            // 
            this.lblLogPath.AutoSize = true;
            this.lblLogPath.Location = new System.Drawing.Point(15, 58);
            this.lblLogPath.Name = "lblLogPath";
            this.lblLogPath.Size = new System.Drawing.Size(78, 15);
            this.lblLogPath.TabIndex = 1;
            this.lblLogPath.Text = "Log File Path:";
            // 
            // chkLogToFile
            // 
            this.chkLogToFile.AutoSize = true;
            this.chkLogToFile.Location = new System.Drawing.Point(15, 25);
            this.chkLogToFile.Name = "chkLogToFile";
            this.chkLogToFile.Size = new System.Drawing.Size(129, 19);
            this.chkLogToFile.TabIndex = 0;
            this.chkLogToFile.Text = "Enable File Logging";
            this.chkLogToFile.UseVisualStyleBackColor = true;
            // 
            // tabScraping
            // 
            this.tabScraping.AutoScroll = true;
            this.tabScraping.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.tabScraping.Controls.Add(this.flpScraping);
            this.tabScraping.ForeColor = System.Drawing.Color.Black;
            this.tabScraping.Location = new System.Drawing.Point(4, 27);
            this.tabScraping.Name = "tabScraping";
            this.tabScraping.Padding = new System.Windows.Forms.Padding(3);
            this.tabScraping.Size = new System.Drawing.Size(801, 547);
            this.tabScraping.TabIndex = 1;
            this.tabScraping.Text = "Scraping";
            // 
            // flpScraping
            // 
            this.flpScraping.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flpScraping.AutoScroll = true;
            this.flpScraping.AutoSize = true;
            this.flpScraping.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpScraping.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.flpScraping.Controls.Add(this.groupScrapingGeneral);
            this.flpScraping.Controls.Add(this.groupArcadeItalia);
            this.flpScraping.Controls.Add(this.groupScreenScraper);
            this.flpScraping.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpScraping.ForeColor = System.Drawing.Color.White;
            this.flpScraping.Location = new System.Drawing.Point(0, 0);
            this.flpScraping.Name = "flpScraping";
            this.flpScraping.Padding = new System.Windows.Forms.Padding(10);
            this.flpScraping.Size = new System.Drawing.Size(788, 536);
            this.flpScraping.TabIndex = 0;
            this.flpScraping.WrapContents = false;
            // 
            // groupScrapingGeneral
            // 
            this.groupScrapingGeneral.Controls.Add(this.txtPrioritySource);
            this.groupScrapingGeneral.Controls.Add(this.lblPrioritySource);
            this.groupScrapingGeneral.Controls.Add(this.chkAutoScraping);
            this.groupScrapingGeneral.ForeColor = System.Drawing.Color.White;
            this.groupScrapingGeneral.Location = new System.Drawing.Point(13, 13);
            this.groupScrapingGeneral.Name = "groupScrapingGeneral";
            this.groupScrapingGeneral.Size = new System.Drawing.Size(762, 100);
            this.groupScrapingGeneral.TabIndex = 0;
            this.groupScrapingGeneral.TabStop = false;
            this.groupScrapingGeneral.Text = "General Scraping Settings";
            // 
            // lblPrioritySource
            // 
            this.lblPrioritySource.AutoSize = true;
            this.lblPrioritySource.Location = new System.Drawing.Point(15, 58);
            this.lblPrioritySource.Name = "lblPrioritySource";
            this.lblPrioritySource.Size = new System.Drawing.Size(90, 15);
            this.lblPrioritySource.TabIndex = 1;
            this.lblPrioritySource.Text = "Scraper Priority:";
            // 
            // groupArcadeItalia
            // 
            this.groupArcadeItalia.Controls.Add(this.cboArcadeItaliaMediaType);
            this.groupArcadeItalia.Controls.Add(this.lblArcadeItaliaMediaType);
            this.groupArcadeItalia.Controls.Add(this.txtArcadeItaliaUrl);
            this.groupArcadeItalia.Controls.Add(this.lblArcadeItaliaUrl);
            this.groupArcadeItalia.ForeColor = System.Drawing.Color.White;
            this.groupArcadeItalia.Location = new System.Drawing.Point(13, 119);
            this.groupArcadeItalia.Name = "groupArcadeItalia";
            this.groupArcadeItalia.Size = new System.Drawing.Size(762, 100);
            this.groupArcadeItalia.TabIndex = 1;
            this.groupArcadeItalia.TabStop = false;
            this.groupArcadeItalia.Text = "ArcadeItalia Settings";
            // 
            // lblArcadeItaliaMediaType
            // 
            this.lblArcadeItaliaMediaType.AutoSize = true;
            this.lblArcadeItaliaMediaType.Location = new System.Drawing.Point(15, 58);
            this.lblArcadeItaliaMediaType.Name = "lblArcadeItaliaMediaType";
            this.lblArcadeItaliaMediaType.Size = new System.Drawing.Size(71, 15);
            this.lblArcadeItaliaMediaType.TabIndex = 2;
            this.lblArcadeItaliaMediaType.Text = "Media Type:";
            // 
            // txtArcadeItaliaUrl
            // 
            this.txtArcadeItaliaUrl.BackColor = System.Drawing.Color.White;
            this.txtArcadeItaliaUrl.ForeColor = System.Drawing.Color.Black;
            this.txtArcadeItaliaUrl.Location = new System.Drawing.Point(180, 22);
            this.txtArcadeItaliaUrl.Name = "txtArcadeItaliaUrl";
            this.txtArcadeItaliaUrl.Size = new System.Drawing.Size(543, 23);
            this.txtArcadeItaliaUrl.TabIndex = 1;
            // 
            // lblArcadeItaliaUrl
            // 
            this.lblArcadeItaliaUrl.AutoSize = true;
            this.lblArcadeItaliaUrl.Location = new System.Drawing.Point(15, 25);
            this.lblArcadeItaliaUrl.Name = "lblArcadeItaliaUrl";
            this.lblArcadeItaliaUrl.Size = new System.Drawing.Size(31, 15);
            this.lblArcadeItaliaUrl.TabIndex = 0;
            this.lblArcadeItaliaUrl.Text = "URL:";
            // 
            // groupScreenScraper
            // 
            this.groupScreenScraper.Controls.Add(this.txtDMDScrapMediaType);
            this.groupScreenScraper.Controls.Add(this.lblDMDScrapMediaType);
            this.groupScreenScraper.Controls.Add(this.txtMPVScrapMediaType);
            this.groupScreenScraper.Controls.Add(this.lblMPVScrapMediaType);
            this.groupScreenScraper.Controls.Add(this.chkSSGlobal);
            this.groupScreenScraper.Controls.Add(this.txtSSQueueKeep);
            this.groupScreenScraper.Controls.Add(this.lblSSQueueKeep);
            this.groupScreenScraper.Controls.Add(this.txtSSQueueLimit);
            this.groupScreenScraper.Controls.Add(this.lblSSQueueLimit);
            this.groupScreenScraper.Controls.Add(this.txtSSThreads);
            this.groupScreenScraper.Controls.Add(this.lblSSThreads);
            this.groupScreenScraper.Controls.Add(this.txtSSDevPass);
            this.groupScreenScraper.Controls.Add(this.lblSSDevPass);
            this.groupScreenScraper.Controls.Add(this.txtSSDevId);
            this.groupScreenScraper.Controls.Add(this.lblSSDevId);
            this.groupScreenScraper.Controls.Add(this.txtSSPass);
            this.groupScreenScraper.Controls.Add(this.lblSSPass);
            this.groupScreenScraper.Controls.Add(this.txtSSUser);
            this.groupScreenScraper.Controls.Add(this.lblSSUser);
            this.groupScreenScraper.ForeColor = System.Drawing.Color.White;
            this.groupScreenScraper.Location = new System.Drawing.Point(13, 225);
            this.groupScreenScraper.Name = "groupScreenScraper";
            this.groupScreenScraper.Size = new System.Drawing.Size(762, 298);
            this.groupScreenScraper.TabIndex = 2;
            this.groupScreenScraper.TabStop = false;
            this.groupScreenScraper.Text = "ScreenScraper Settings";
            // 
            // lblDMDScrapMediaType
            // 
            this.lblDMDScrapMediaType.AutoSize = true;
            this.lblDMDScrapMediaType.Location = new System.Drawing.Point(15, 256);
            this.lblDMDScrapMediaType.Name = "lblDMDScrapMediaType";
            this.lblDMDScrapMediaType.Size = new System.Drawing.Size(139, 15);
            this.lblDMDScrapMediaType.TabIndex = 13;
            this.lblDMDScrapMediaType.Text = "DMD Scrape Media Type:";
            // 
            // lblMPVScrapMediaType
            // 
            this.lblMPVScrapMediaType.AutoSize = true;
            this.lblMPVScrapMediaType.Location = new System.Drawing.Point(15, 223);
            this.lblMPVScrapMediaType.Name = "lblMPVScrapMediaType";
            this.lblMPVScrapMediaType.Size = new System.Drawing.Size(137, 15);
            this.lblMPVScrapMediaType.TabIndex = 11;
            this.lblMPVScrapMediaType.Text = "MPV Scrape Media Type:";
            // 
            // lblSSQueueKeep
            // 
            this.lblSSQueueKeep.AutoSize = true;
            this.lblSSQueueKeep.Location = new System.Drawing.Point(482, 160);
            this.lblSSQueueKeep.Name = "lblSSQueueKeep";
            this.lblSSQueueKeep.Size = new System.Drawing.Size(74, 15);
            this.lblSSQueueKeep.TabIndex = 11;
            this.lblSSQueueKeep.Text = "Queue Keep:";
            // 
            // lblSSQueueLimit
            // 
            this.lblSSQueueLimit.AutoSize = true;
            this.lblSSQueueLimit.Location = new System.Drawing.Point(305, 160);
            this.lblSSQueueLimit.Name = "lblSSQueueLimit";
            this.lblSSQueueLimit.Size = new System.Drawing.Size(75, 15);
            this.lblSSQueueLimit.TabIndex = 10;
            this.lblSSQueueLimit.Text = "Queue Limit:";
            // 
            // txtSSThreads
            // 
            this.txtSSThreads.BackColor = System.Drawing.Color.White;
            this.txtSSThreads.ForeColor = System.Drawing.Color.Black;
            this.txtSSThreads.Location = new System.Drawing.Point(180, 157);
            this.txtSSThreads.Name = "txtSSThreads";
            this.txtSSThreads.Size = new System.Drawing.Size(100, 23);
            this.txtSSThreads.TabIndex = 13;
            // 
            // lblSSThreads
            // 
            this.lblSSThreads.AutoSize = true;
            this.lblSSThreads.Location = new System.Drawing.Point(16, 160);
            this.lblSSThreads.Name = "lblSSThreads";
            this.lblSSThreads.Size = new System.Drawing.Size(115, 15);
            this.lblSSThreads.TabIndex = 12;
            this.lblSSThreads.Text = "Concurrent Threads:";
            // 
            // txtSSDevPass
            // 
            this.txtSSDevPass.BackColor = System.Drawing.Color.White;
            this.txtSSDevPass.ForeColor = System.Drawing.Color.Black;
            this.txtSSDevPass.Location = new System.Drawing.Point(180, 121);
            this.txtSSDevPass.Name = "txtSSDevPass";
            this.txtSSDevPass.PasswordChar = '*';
            this.txtSSDevPass.Size = new System.Drawing.Size(300, 23);
            this.txtSSDevPass.TabIndex = 7;
            // 
            // lblSSDevPass
            // 
            this.lblSSDevPass.AutoSize = true;
            this.lblSSDevPass.Location = new System.Drawing.Point(15, 124);
            this.lblSSDevPass.Name = "lblSSDevPass";
            this.lblSSDevPass.Size = new System.Drawing.Size(116, 15);
            this.lblSSDevPass.TabIndex = 6;
            this.lblSSDevPass.Text = "Developer Password:";
            // 
            // txtSSDevId
            // 
            this.txtSSDevId.BackColor = System.Drawing.Color.White;
            this.txtSSDevId.ForeColor = System.Drawing.Color.Black;
            this.txtSSDevId.Location = new System.Drawing.Point(180, 88);
            this.txtSSDevId.Name = "txtSSDevId";
            this.txtSSDevId.Size = new System.Drawing.Size(300, 23);
            this.txtSSDevId.TabIndex = 5;
            // 
            // lblSSDevId
            // 
            this.lblSSDevId.AutoSize = true;
            this.lblSSDevId.Location = new System.Drawing.Point(15, 91);
            this.lblSSDevId.Name = "lblSSDevId";
            this.lblSSDevId.Size = new System.Drawing.Size(77, 15);
            this.lblSSDevId.TabIndex = 4;
            this.lblSSDevId.Text = "Developer ID:";
            // 
            // txtSSPass
            // 
            this.txtSSPass.BackColor = System.Drawing.Color.White;
            this.txtSSPass.ForeColor = System.Drawing.Color.Black;
            this.txtSSPass.Location = new System.Drawing.Point(180, 55);
            this.txtSSPass.Name = "txtSSPass";
            this.txtSSPass.PasswordChar = '*';
            this.txtSSPass.Size = new System.Drawing.Size(300, 23);
            this.txtSSPass.TabIndex = 3;
            // 
            // lblSSPass
            // 
            this.lblSSPass.AutoSize = true;
            this.lblSSPass.Location = new System.Drawing.Point(15, 58);
            this.lblSSPass.Name = "lblSSPass";
            this.lblSSPass.Size = new System.Drawing.Size(60, 15);
            this.lblSSPass.TabIndex = 2;
            this.lblSSPass.Text = "Password:";
            // 
            // txtSSUser
            // 
            this.txtSSUser.BackColor = System.Drawing.Color.White;
            this.txtSSUser.ForeColor = System.Drawing.Color.Black;
            this.txtSSUser.Location = new System.Drawing.Point(180, 22);
            this.txtSSUser.Name = "txtSSUser";
            this.txtSSUser.Size = new System.Drawing.Size(300, 23);
            this.txtSSUser.TabIndex = 1;
            // 
            // lblSSUser
            // 
            this.lblSSUser.AutoSize = true;
            this.lblSSUser.Location = new System.Drawing.Point(15, 25);
            this.lblSSUser.Name = "lblSSUser";
            this.lblSSUser.Size = new System.Drawing.Size(63, 15);
            this.lblSSUser.TabIndex = 0;
            this.lblSSUser.Text = "Username:";
            // 
            // tabDMD
            // 
            this.tabDMD.AutoScroll = true;
            this.tabDMD.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.tabDMD.Controls.Add(this.flpDMD);
            this.tabDMD.ForeColor = System.Drawing.Color.Black;
            this.tabDMD.Location = new System.Drawing.Point(4, 27);
            this.tabDMD.Name = "tabDMD";
            this.tabDMD.Padding = new System.Windows.Forms.Padding(3);
            this.tabDMD.Size = new System.Drawing.Size(801, 547);
            this.tabDMD.TabIndex = 2;
            this.tabDMD.Text = "DMD Display";
            // 
            // flpDMD
            // 
            this.flpDMD.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flpDMD.AutoScroll = true;
            this.flpDMD.AutoSize = true;
            this.flpDMD.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpDMD.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.flpDMD.Controls.Add(this.groupDMDGeneral);
            this.flpDMD.Controls.Add(this.groupDMDDisplay);
            this.flpDMD.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpDMD.ForeColor = System.Drawing.Color.White;
            this.flpDMD.Location = new System.Drawing.Point(0, 0);
            this.flpDMD.Name = "flpDMD";
            this.flpDMD.Padding = new System.Windows.Forms.Padding(10);
            this.flpDMD.Size = new System.Drawing.Size(805, 472);
            this.flpDMD.TabIndex = 0;
            this.flpDMD.WrapContents = false;
            // 
            // groupDMDGeneral
            // 
            this.groupDMDGeneral.Controls.Add(this.btnBrowseDMDGameStart);
            this.groupDMDGeneral.Controls.Add(this.txtDMDGameStartPath);
            this.groupDMDGeneral.Controls.Add(this.lblDMDGameStartPath);
            this.groupDMDGeneral.Controls.Add(this.btnBrowseSystemDMD);
            this.groupDMDGeneral.Controls.Add(this.txtSystemCustomDMDPath);
            this.groupDMDGeneral.Controls.Add(this.lblSystemCustomDMDPath);
            this.groupDMDGeneral.Controls.Add(this.btnBrowseDMDMedia);
            this.groupDMDGeneral.Controls.Add(this.txtDMDMediaPath);
            this.groupDMDGeneral.Controls.Add(this.lblDMDMediaPath);
            this.groupDMDGeneral.Controls.Add(this.btnBrowseDMDExe);
            this.groupDMDGeneral.Controls.Add(this.txtDMDExePath);
            this.groupDMDGeneral.Controls.Add(this.lblDMDExePath);
            this.groupDMDGeneral.Controls.Add(this.cboDMDModel);
            this.groupDMDGeneral.Controls.Add(this.lblDMDModel);
            this.groupDMDGeneral.Controls.Add(this.chkDMDEnabled);
            this.groupDMDGeneral.ForeColor = System.Drawing.Color.White;
            this.groupDMDGeneral.Location = new System.Drawing.Point(13, 13);
            this.groupDMDGeneral.Name = "groupDMDGeneral";
            this.groupDMDGeneral.Size = new System.Drawing.Size(779, 220);
            this.groupDMDGeneral.TabIndex = 0;
            this.groupDMDGeneral.TabStop = false;
            this.groupDMDGeneral.Text = "DMD General Settings";
            // 
            // btnBrowseDMDGameStart
            // 
            this.btnBrowseDMDGameStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseDMDGameStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseDMDGameStart.ForeColor = System.Drawing.Color.White;
            this.btnBrowseDMDGameStart.Location = new System.Drawing.Point(650, 186);
            this.btnBrowseDMDGameStart.Name = "btnBrowseDMDGameStart";
            this.btnBrowseDMDGameStart.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseDMDGameStart.TabIndex = 14;
            this.btnBrowseDMDGameStart.Text = "Browse...";
            this.btnBrowseDMDGameStart.UseVisualStyleBackColor = false;
            this.btnBrowseDMDGameStart.Click += new System.EventHandler(this.btnBrowseDMDGameStart_Click);
            // 
            // txtDMDGameStartPath
            // 
            this.txtDMDGameStartPath.BackColor = System.Drawing.Color.White;
            this.txtDMDGameStartPath.ForeColor = System.Drawing.Color.Black;
            this.txtDMDGameStartPath.Location = new System.Drawing.Point(180, 187);
            this.txtDMDGameStartPath.Name = "txtDMDGameStartPath";
            this.txtDMDGameStartPath.Size = new System.Drawing.Size(460, 23);
            this.txtDMDGameStartPath.TabIndex = 13;
            // 
            // lblDMDGameStartPath
            // 
            this.lblDMDGameStartPath.AutoSize = true;
            this.lblDMDGameStartPath.Location = new System.Drawing.Point(15, 190);
            this.lblDMDGameStartPath.Name = "lblDMDGameStartPath";
            this.lblDMDGameStartPath.Size = new System.Drawing.Size(125, 15);
            this.lblDMDGameStartPath.TabIndex = 12;
            this.lblDMDGameStartPath.Text = "DMD Game Start Path:";
            // 
            // btnBrowseSystemDMD
            // 
            this.btnBrowseSystemDMD.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseSystemDMD.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseSystemDMD.ForeColor = System.Drawing.Color.White;
            this.btnBrowseSystemDMD.Location = new System.Drawing.Point(650, 153);
            this.btnBrowseSystemDMD.Name = "btnBrowseSystemDMD";
            this.btnBrowseSystemDMD.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseSystemDMD.TabIndex = 11;
            this.btnBrowseSystemDMD.Text = "Browse...";
            this.btnBrowseSystemDMD.UseVisualStyleBackColor = false;
            this.btnBrowseSystemDMD.Click += new System.EventHandler(this.btnBrowseSystemDMD_Click);
            // 
            // txtSystemCustomDMDPath
            // 
            this.txtSystemCustomDMDPath.BackColor = System.Drawing.Color.White;
            this.txtSystemCustomDMDPath.ForeColor = System.Drawing.Color.Black;
            this.txtSystemCustomDMDPath.Location = new System.Drawing.Point(180, 154);
            this.txtSystemCustomDMDPath.Name = "txtSystemCustomDMDPath";
            this.txtSystemCustomDMDPath.Size = new System.Drawing.Size(460, 23);
            this.txtSystemCustomDMDPath.TabIndex = 10;
            // 
            // lblSystemCustomDMDPath
            // 
            this.lblSystemCustomDMDPath.AutoSize = true;
            this.lblSystemCustomDMDPath.Location = new System.Drawing.Point(15, 157);
            this.lblSystemCustomDMDPath.Name = "lblSystemCustomDMDPath";
            this.lblSystemCustomDMDPath.Size = new System.Drawing.Size(150, 15);
            this.lblSystemCustomDMDPath.TabIndex = 9;
            this.lblSystemCustomDMDPath.Text = "System Custom DMD Path:";
            // 
            // btnBrowseDMDMedia
            // 
            this.btnBrowseDMDMedia.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseDMDMedia.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseDMDMedia.ForeColor = System.Drawing.Color.White;
            this.btnBrowseDMDMedia.Location = new System.Drawing.Point(650, 120);
            this.btnBrowseDMDMedia.Name = "btnBrowseDMDMedia";
            this.btnBrowseDMDMedia.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseDMDMedia.TabIndex = 8;
            this.btnBrowseDMDMedia.Text = "Browse...";
            this.btnBrowseDMDMedia.UseVisualStyleBackColor = false;
            this.btnBrowseDMDMedia.Click += new System.EventHandler(this.btnBrowseDMDMedia_Click);
            // 
            // txtDMDMediaPath
            // 
            this.txtDMDMediaPath.BackColor = System.Drawing.Color.White;
            this.txtDMDMediaPath.ForeColor = System.Drawing.Color.Black;
            this.txtDMDMediaPath.Location = new System.Drawing.Point(180, 121);
            this.txtDMDMediaPath.Name = "txtDMDMediaPath";
            this.txtDMDMediaPath.Size = new System.Drawing.Size(460, 23);
            this.txtDMDMediaPath.TabIndex = 7;
            // 
            // lblDMDMediaPath
            // 
            this.lblDMDMediaPath.AutoSize = true;
            this.lblDMDMediaPath.Location = new System.Drawing.Point(15, 124);
            this.lblDMDMediaPath.Name = "lblDMDMediaPath";
            this.lblDMDMediaPath.Size = new System.Drawing.Size(100, 15);
            this.lblDMDMediaPath.TabIndex = 6;
            this.lblDMDMediaPath.Text = "DMD Media Path:";
            // 
            // btnBrowseDMDExe
            // 
            this.btnBrowseDMDExe.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseDMDExe.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseDMDExe.ForeColor = System.Drawing.Color.White;
            this.btnBrowseDMDExe.Location = new System.Drawing.Point(650, 87);
            this.btnBrowseDMDExe.Name = "btnBrowseDMDExe";
            this.btnBrowseDMDExe.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseDMDExe.TabIndex = 5;
            this.btnBrowseDMDExe.Text = "Browse...";
            this.btnBrowseDMDExe.UseVisualStyleBackColor = false;
            this.btnBrowseDMDExe.Click += new System.EventHandler(this.btnBrowseDMDExe_Click);
            // 
            // txtDMDExePath
            // 
            this.txtDMDExePath.BackColor = System.Drawing.Color.White;
            this.txtDMDExePath.ForeColor = System.Drawing.Color.Black;
            this.txtDMDExePath.Location = new System.Drawing.Point(180, 88);
            this.txtDMDExePath.Name = "txtDMDExePath";
            this.txtDMDExePath.Size = new System.Drawing.Size(460, 23);
            this.txtDMDExePath.TabIndex = 4;
            // 
            // lblDMDExePath
            // 
            this.lblDMDExePath.AutoSize = true;
            this.lblDMDExePath.Location = new System.Drawing.Point(15, 91);
            this.lblDMDExePath.Name = "lblDMDExePath";
            this.lblDMDExePath.Size = new System.Drawing.Size(96, 15);
            this.lblDMDExePath.TabIndex = 3;
            this.lblDMDExePath.Text = "DMD Executable:";
            // 
            // lblDMDModel
            // 
            this.lblDMDModel.AutoSize = true;
            this.lblDMDModel.Location = new System.Drawing.Point(15, 58);
            this.lblDMDModel.Name = "lblDMDModel";
            this.lblDMDModel.Size = new System.Drawing.Size(74, 15);
            this.lblDMDModel.TabIndex = 1;
            this.lblDMDModel.Text = "DMD Model:";
            // 
            // groupDMDDisplay
            // 
            this.groupDMDDisplay.Controls.Add(this.btnEditDmdLayout);
            this.groupDMDDisplay.Controls.Add(this.cboDMDFormat);
            this.groupDMDDisplay.Controls.Add(this.lblDMDFormat);
            this.groupDMDDisplay.Controls.Add(this.chkDMDCompose);
            this.groupDMDDisplay.Controls.Add(this.txtDMDWidth);
            this.groupDMDDisplay.Controls.Add(this.lblDMDWidth);
            this.groupDMDDisplay.Controls.Add(this.txtDMDHeight);
            this.groupDMDDisplay.Controls.Add(this.lblDMDHeight);
            this.groupDMDDisplay.Controls.Add(this.txtDMDDotSize);
            this.groupDMDDisplay.Controls.Add(this.lblDMDDotSize);
            this.groupDMDDisplay.ForeColor = System.Drawing.Color.White;
            this.groupDMDDisplay.Location = new System.Drawing.Point(13, 239);
            this.groupDMDDisplay.Name = "groupDMDDisplay";
            this.groupDMDDisplay.Size = new System.Drawing.Size(779, 220);
            this.groupDMDDisplay.TabIndex = 1;
            this.groupDMDDisplay.TabStop = false;
            this.groupDMDDisplay.Text = "DMD Display Settings";
            // 
            // cboDMDFormat
            // 
            this.cboDMDFormat.BackColor = System.Drawing.Color.White;
            this.cboDMDFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDMDFormat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cboDMDFormat.ForeColor = System.Drawing.Color.Black;
            this.cboDMDFormat.FormattingEnabled = true;
            this.cboDMDFormat.Items.AddRange(new object[] {
            "rgb24",
            "gray2",
            "gray4",
            "coloredgray2",
            "coloredgray4",
            "coloredgray6"});
            this.cboDMDFormat.Location = new System.Drawing.Point(180, 55);
            this.cboDMDFormat.Name = "cboDMDFormat";
            this.cboDMDFormat.Size = new System.Drawing.Size(200, 23);
            this.cboDMDFormat.TabIndex = 2;
            // 
            // lblDMDFormat
            // 
            this.lblDMDFormat.AutoSize = true;
            this.lblDMDFormat.Location = new System.Drawing.Point(15, 58);
            this.lblDMDFormat.Name = "lblDMDFormat";
            this.lblDMDFormat.Size = new System.Drawing.Size(84, 15);
            this.lblDMDFormat.TabIndex = 1;
            this.lblDMDFormat.Text = "Frame Format:";
            // 
            // chkDMDCompose
            // 
            this.chkDMDCompose.AutoSize = true;
            this.chkDMDCompose.Location = new System.Drawing.Point(15, 25);
            this.chkDMDCompose.Name = "chkDMDCompose";
            this.chkDMDCompose.Size = new System.Drawing.Size(145, 19);
            this.chkDMDCompose.TabIndex = 0;
            this.chkDMDCompose.Text = "Enable DMD Compose";
            this.chkDMDCompose.UseVisualStyleBackColor = true;
            // 
            // lblDMDWidth
            // 
            this.lblDMDWidth.AutoSize = true;
            this.lblDMDWidth.Location = new System.Drawing.Point(15, 91);
            this.lblDMDWidth.Name = "lblDMDWidth";
            this.lblDMDWidth.Size = new System.Drawing.Size(72, 15);
            this.lblDMDWidth.TabIndex = 3;
            this.lblDMDWidth.Text = "DMD Width:";
            // 
            // txtDMDHeight
            // 
            this.txtDMDHeight.BackColor = System.Drawing.Color.White;
            this.txtDMDHeight.ForeColor = System.Drawing.Color.Black;
            this.txtDMDHeight.Location = new System.Drawing.Point(390, 89);
            this.txtDMDHeight.Name = "txtDMDHeight";
            this.txtDMDHeight.Size = new System.Drawing.Size(100, 23);
            this.txtDMDHeight.TabIndex = 6;
            // 
            // lblDMDHeight
            // 
            this.lblDMDHeight.AutoSize = true;
            this.lblDMDHeight.Location = new System.Drawing.Point(300, 91);
            this.lblDMDHeight.Name = "lblDMDHeight";
            this.lblDMDHeight.Size = new System.Drawing.Size(76, 15);
            this.lblDMDHeight.TabIndex = 5;
            this.lblDMDHeight.Text = "DMD Height:";
            // 
            // txtDMDDotSize
            // 
            this.txtDMDDotSize.BackColor = System.Drawing.Color.White;
            this.txtDMDDotSize.ForeColor = System.Drawing.Color.Black;
            this.txtDMDDotSize.Location = new System.Drawing.Point(180, 122);
            this.txtDMDDotSize.Name = "txtDMDDotSize";
            this.txtDMDDotSize.Size = new System.Drawing.Size(100, 23);
            this.txtDMDDotSize.TabIndex = 8;
            // 
            // lblDMDDotSize
            // 
            this.lblDMDDotSize.AutoSize = true;
            this.lblDMDDotSize.Location = new System.Drawing.Point(15, 124);
            this.lblDMDDotSize.Name = "lblDMDDotSize";
            this.lblDMDDotSize.Size = new System.Drawing.Size(119, 15);
            this.lblDMDDotSize.TabIndex = 7;
            this.lblDMDDotSize.Text = "Virtual DMD Dot Size:";
            // 
            // btnEditDmdLayout
            // 
            this.btnEditDmdLayout.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnEditDmdLayout.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditDmdLayout.ForeColor = System.Drawing.Color.White;
            this.btnEditDmdLayout.Location = new System.Drawing.Point(180, 155);
            this.btnEditDmdLayout.Name = "btnEditDmdLayout";
            this.btnEditDmdLayout.Size = new System.Drawing.Size(200, 30);
            this.btnEditDmdLayout.TabIndex = 9;
            this.btnEditDmdLayout.Text = "Edit Overlay Layout...";
            this.btnEditDmdLayout.UseVisualStyleBackColor = false;
            this.btnEditDmdLayout.Click += new System.EventHandler(this.btnEditDmdLayout_Click);
            // 
            // tabScreen
            // 
            this.tabScreen.AutoScroll = true;
            this.tabScreen.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.tabScreen.Controls.Add(this.flpScreen);
            this.tabScreen.ForeColor = System.Drawing.Color.Black;
            this.tabScreen.Location = new System.Drawing.Point(4, 27);
            this.tabScreen.Name = "tabScreen";
            this.tabScreen.Padding = new System.Windows.Forms.Padding(3);
            this.tabScreen.Size = new System.Drawing.Size(801, 547);
            this.tabScreen.TabIndex = 3;
            this.tabScreen.Text = "Screen && MPV";
            // 
            // flpScreen
            // 
            this.flpScreen.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flpScreen.AutoScroll = true;
            this.flpScreen.AutoSize = true;
            this.flpScreen.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpScreen.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.flpScreen.Controls.Add(this.groupMPV);
            this.flpScreen.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpScreen.ForeColor = System.Drawing.Color.White;
            this.flpScreen.Location = new System.Drawing.Point(0, 0);
            this.flpScreen.Name = "flpScreen";
            this.flpScreen.Padding = new System.Windows.Forms.Padding(10);
            this.flpScreen.Size = new System.Drawing.Size(805, 306);
            this.flpScreen.TabIndex = 0;
            this.flpScreen.WrapContents = false;
            // 
            // groupMPV
            // 
            this.groupMPV.Controls.Add(this.btnEditMpvLayout);
            this.groupMPV.Controls.Add(this.cboHwDecoding);
            this.groupMPV.Controls.Add(this.lbl_hw_decoding);
            this.groupMPV.Controls.Add(this.btnBrowseGameStartMedia);
            this.groupMPV.Controls.Add(this.txtGameStartMediaPath);
            this.groupMPV.Controls.Add(this.lblGameStartMediaPath);
            this.groupMPV.Controls.Add(this.btnBrowseGameCustomMarquee);
            this.groupMPV.Controls.Add(this.txtGameCustomMarqueePath);
            this.groupMPV.Controls.Add(this.lblGameCustomMarqueePath);
            this.groupMPV.Controls.Add(this.btnBrowseSystemCustomMarquee);
            this.groupMPV.Controls.Add(this.txtSystemCustomMarqueePath);
            this.groupMPV.Controls.Add(this.lblSystemCustomMarqueePath);
            this.groupMPV.Controls.Add(this.txtScreenNumber);
            this.groupMPV.Controls.Add(this.lblScreenNumber);
            this.groupMPV.Controls.Add(this.txtMarqueeHeight);
            this.groupMPV.Controls.Add(this.lblMarqueeHeight);
            this.groupMPV.Controls.Add(this.txtMarqueeWidth);
            this.groupMPV.Controls.Add(this.lblMarqueeWidth);
            this.groupMPV.Controls.Add(this.btnBrowseMPV);
            this.groupMPV.Controls.Add(this.txtMPVPath);
            this.groupMPV.Controls.Add(this.lblMPVPath);
            this.groupMPV.ForeColor = System.Drawing.Color.White;
            this.groupMPV.Location = new System.Drawing.Point(13, 13);
            this.groupMPV.Name = "groupMPV";
            this.groupMPV.Size = new System.Drawing.Size(779, 313);
            this.groupMPV.TabIndex = 0;
            this.groupMPV.TabStop = false;
            this.groupMPV.Text = "MPV Screen Settings";
            // 
            // btnBrowseGameStartMedia
            // 
            this.btnBrowseGameStartMedia.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseGameStartMedia.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseGameStartMedia.ForeColor = System.Drawing.Color.White;
            this.btnBrowseGameStartMedia.Location = new System.Drawing.Point(650, 219);
            this.btnBrowseGameStartMedia.Name = "btnBrowseGameStartMedia";
            this.btnBrowseGameStartMedia.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseGameStartMedia.TabIndex = 17;
            this.btnBrowseGameStartMedia.Text = "Browse...";
            this.btnBrowseGameStartMedia.UseVisualStyleBackColor = false;
            this.btnBrowseGameStartMedia.Click += new System.EventHandler(this.btnBrowseGameStartMedia_Click);
            // 
            // txtGameStartMediaPath
            // 
            this.txtGameStartMediaPath.BackColor = System.Drawing.Color.White;
            this.txtGameStartMediaPath.ForeColor = System.Drawing.Color.Black;
            this.txtGameStartMediaPath.Location = new System.Drawing.Point(180, 220);
            this.txtGameStartMediaPath.Name = "txtGameStartMediaPath";
            this.txtGameStartMediaPath.Size = new System.Drawing.Size(460, 23);
            this.txtGameStartMediaPath.TabIndex = 16;
            // 
            // lblGameStartMediaPath
            // 
            this.lblGameStartMediaPath.AutoSize = true;
            this.lblGameStartMediaPath.Location = new System.Drawing.Point(15, 223);
            this.lblGameStartMediaPath.Name = "lblGameStartMediaPath";
            this.lblGameStartMediaPath.Size = new System.Drawing.Size(131, 15);
            this.lblGameStartMediaPath.TabIndex = 15;
            this.lblGameStartMediaPath.Text = "Game Start Media Path:";
            // 
            // btnBrowseGameCustomMarquee
            // 
            this.btnBrowseGameCustomMarquee.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseGameCustomMarquee.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseGameCustomMarquee.ForeColor = System.Drawing.Color.White;
            this.btnBrowseGameCustomMarquee.Location = new System.Drawing.Point(650, 186);
            this.btnBrowseGameCustomMarquee.Name = "btnBrowseGameCustomMarquee";
            this.btnBrowseGameCustomMarquee.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseGameCustomMarquee.TabIndex = 14;
            this.btnBrowseGameCustomMarquee.Text = "Browse...";
            this.btnBrowseGameCustomMarquee.UseVisualStyleBackColor = false;
            this.btnBrowseGameCustomMarquee.Click += new System.EventHandler(this.btnBrowseGameCustomMarquee_Click);
            // 
            // txtGameCustomMarqueePath
            // 
            this.txtGameCustomMarqueePath.BackColor = System.Drawing.Color.White;
            this.txtGameCustomMarqueePath.ForeColor = System.Drawing.Color.Black;
            this.txtGameCustomMarqueePath.Location = new System.Drawing.Point(180, 187);
            this.txtGameCustomMarqueePath.Name = "txtGameCustomMarqueePath";
            this.txtGameCustomMarqueePath.Size = new System.Drawing.Size(460, 23);
            this.txtGameCustomMarqueePath.TabIndex = 13;
            // 
            // lblGameCustomMarqueePath
            // 
            this.lblGameCustomMarqueePath.AutoSize = true;
            this.lblGameCustomMarqueePath.Location = new System.Drawing.Point(15, 190);
            this.lblGameCustomMarqueePath.Name = "lblGameCustomMarqueePath";
            this.lblGameCustomMarqueePath.Size = new System.Drawing.Size(163, 15);
            this.lblGameCustomMarqueePath.TabIndex = 12;
            this.lblGameCustomMarqueePath.Text = "Game Custom Marquee Path:";
            // 
            // btnBrowseSystemCustomMarquee
            // 
            this.btnBrowseSystemCustomMarquee.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseSystemCustomMarquee.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseSystemCustomMarquee.ForeColor = System.Drawing.Color.White;
            this.btnBrowseSystemCustomMarquee.Location = new System.Drawing.Point(650, 153);
            this.btnBrowseSystemCustomMarquee.Name = "btnBrowseSystemCustomMarquee";
            this.btnBrowseSystemCustomMarquee.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseSystemCustomMarquee.TabIndex = 11;
            this.btnBrowseSystemCustomMarquee.Text = "Browse...";
            this.btnBrowseSystemCustomMarquee.UseVisualStyleBackColor = false;
            this.btnBrowseSystemCustomMarquee.Click += new System.EventHandler(this.btnBrowseSystemCustomMarquee_Click);
            // 
            // txtSystemCustomMarqueePath
            // 
            this.txtSystemCustomMarqueePath.BackColor = System.Drawing.Color.White;
            this.txtSystemCustomMarqueePath.ForeColor = System.Drawing.Color.Black;
            this.txtSystemCustomMarqueePath.Location = new System.Drawing.Point(180, 154);
            this.txtSystemCustomMarqueePath.Name = "txtSystemCustomMarqueePath";
            this.txtSystemCustomMarqueePath.Size = new System.Drawing.Size(460, 23);
            this.txtSystemCustomMarqueePath.TabIndex = 10;
            // 
            // lblSystemCustomMarqueePath
            // 
            this.lblSystemCustomMarqueePath.AutoSize = true;
            this.lblSystemCustomMarqueePath.Location = new System.Drawing.Point(15, 157);
            this.lblSystemCustomMarqueePath.Name = "lblSystemCustomMarqueePath";
            this.lblSystemCustomMarqueePath.Size = new System.Drawing.Size(170, 15);
            this.lblSystemCustomMarqueePath.TabIndex = 9;
            this.lblSystemCustomMarqueePath.Text = "System Custom Marquee Path:";
            // 
            // lblScreenNumber
            // 
            this.lblScreenNumber.AutoSize = true;
            this.lblScreenNumber.Location = new System.Drawing.Point(15, 91);
            this.lblScreenNumber.Name = "lblScreenNumber";
            this.lblScreenNumber.Size = new System.Drawing.Size(92, 15);
            this.lblScreenNumber.TabIndex = 7;
            this.lblScreenNumber.Text = "Screen Number:";
            // 
            // txtMarqueeHeight
            // 
            this.txtMarqueeHeight.BackColor = System.Drawing.Color.White;
            this.txtMarqueeHeight.ForeColor = System.Drawing.Color.Black;
            this.txtMarqueeHeight.Location = new System.Drawing.Point(420, 56);
            this.txtMarqueeHeight.Name = "txtMarqueeHeight";
            this.txtMarqueeHeight.Size = new System.Drawing.Size(100, 23);
            this.txtMarqueeHeight.TabIndex = 6;
            // 
            // lblMarqueeHeight
            // 
            this.lblMarqueeHeight.AutoSize = true;
            this.lblMarqueeHeight.Location = new System.Drawing.Point(300, 58);
            this.lblMarqueeHeight.Name = "lblMarqueeHeight";
            this.lblMarqueeHeight.Size = new System.Drawing.Size(96, 15);
            this.lblMarqueeHeight.TabIndex = 5;
            this.lblMarqueeHeight.Text = "Marquee Height:";
            // 
            // txtMarqueeWidth
            // 
            this.txtMarqueeWidth.BackColor = System.Drawing.Color.White;
            this.txtMarqueeWidth.ForeColor = System.Drawing.Color.Black;
            this.txtMarqueeWidth.Location = new System.Drawing.Point(180, 56);
            this.txtMarqueeWidth.Name = "txtMarqueeWidth";
            this.txtMarqueeWidth.Size = new System.Drawing.Size(100, 23);
            this.txtMarqueeWidth.TabIndex = 4;
            // 
            // lblMarqueeWidth
            // 
            this.lblMarqueeWidth.AutoSize = true;
            this.lblMarqueeWidth.Location = new System.Drawing.Point(15, 58);
            this.lblMarqueeWidth.Name = "lblMarqueeWidth";
            this.lblMarqueeWidth.Size = new System.Drawing.Size(92, 15);
            this.lblMarqueeWidth.TabIndex = 3;
            this.lblMarqueeWidth.Text = "Marquee Width:";
            // 
            // btnBrowseMPV
            // 
            this.btnBrowseMPV.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnBrowseMPV.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseMPV.ForeColor = System.Drawing.Color.White;
            this.btnBrowseMPV.Location = new System.Drawing.Point(650, 21);
            this.btnBrowseMPV.Name = "btnBrowseMPV";
            this.btnBrowseMPV.Size = new System.Drawing.Size(85, 25);
            this.btnBrowseMPV.TabIndex = 2;
            this.btnBrowseMPV.Text = "Browse...";
            this.btnBrowseMPV.UseVisualStyleBackColor = false;
            this.btnBrowseMPV.Click += new System.EventHandler(this.btnBrowseMPV_Click);
            // 
            // txtMPVPath
            // 
            this.txtMPVPath.BackColor = System.Drawing.Color.White;
            this.txtMPVPath.ForeColor = System.Drawing.Color.Black;
            this.txtMPVPath.Location = new System.Drawing.Point(180, 22);
            this.txtMPVPath.Name = "txtMPVPath";
            this.txtMPVPath.Size = new System.Drawing.Size(460, 23);
            this.txtMPVPath.TabIndex = 1;
            // 
            // lblMPVPath
            // 
            this.lblMPVPath.AutoSize = true;
            this.lblMPVPath.Location = new System.Drawing.Point(15, 25);
            this.lblMPVPath.Name = "lblMPVPath";
            this.lblMPVPath.Size = new System.Drawing.Size(62, 15);
            this.lblMPVPath.TabIndex = 0;
            this.lblMPVPath.Text = "MPV Path:";
            // 
            // btnEditMpvLayout
            // 
            this.btnEditMpvLayout.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnEditMpvLayout.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditMpvLayout.ForeColor = System.Drawing.Color.White;
            this.btnEditMpvLayout.Location = new System.Drawing.Point(180, 260);
            this.btnEditMpvLayout.Name = "btnEditMpvLayout";
            this.btnEditMpvLayout.Size = new System.Drawing.Size(200, 30);
            this.btnEditMpvLayout.TabIndex = 18;
            this.btnEditMpvLayout.Text = "Edit Overlay Layout...";
            this.btnEditMpvLayout.UseVisualStyleBackColor = false;
            this.btnEditMpvLayout.Click += new System.EventHandler(this.btnEditMpvLayout_Click);
            // 
            // tabPinball
            // 
            this.tabPinball.AutoScroll = true;
            this.tabPinball.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.tabPinball.Controls.Add(this.flpPinball);
            this.tabPinball.ForeColor = System.Drawing.Color.Black;
            this.tabPinball.Location = new System.Drawing.Point(4, 27);
            this.tabPinball.Name = "tabPinball";
            this.tabPinball.Padding = new System.Windows.Forms.Padding(3);
            this.tabPinball.Size = new System.Drawing.Size(801, 547);
            this.tabPinball.TabIndex = 4;
            this.tabPinball.Text = "Pinball";
            // 
            // flpPinball
            // 
            this.flpPinball.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flpPinball.AutoScroll = true;
            this.flpPinball.AutoSize = true;
            this.flpPinball.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpPinball.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.flpPinball.Controls.Add(this.groupPinball);
            this.flpPinball.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpPinball.ForeColor = System.Drawing.Color.White;
            this.flpPinball.Location = new System.Drawing.Point(0, 0);
            this.flpPinball.Name = "flpPinball";
            this.flpPinball.Padding = new System.Windows.Forms.Padding(10);
            this.flpPinball.Size = new System.Drawing.Size(805, 476);
            this.flpPinball.TabIndex = 0;
            this.flpPinball.WrapContents = false;
            // 
            // groupPinball
            // 
            this.groupPinball.Controls.Add(this.lblPinballHelp);
            this.groupPinball.Controls.Add(this.btnPinballDelete);
            this.groupPinball.Controls.Add(this.btnPinballEdit);
            this.groupPinball.Controls.Add(this.btnPinballAdd);
            this.groupPinball.Controls.Add(this.dgvPinball);
            this.groupPinball.ForeColor = System.Drawing.Color.White;
            this.groupPinball.Location = new System.Drawing.Point(13, 13);
            this.groupPinball.Name = "groupPinball";
            this.groupPinball.Size = new System.Drawing.Size(779, 450);
            this.groupPinball.TabIndex = 0;
            this.groupPinball.TabStop = false;
            this.groupPinball.Text = "Pinball System Commands";
            // 
            // lblPinballHelp
            // 
            this.lblPinballHelp.Location = new System.Drawing.Point(15, 375);
            this.lblPinballHelp.Name = "lblPinballHelp";
            this.lblPinballHelp.Size = new System.Drawing.Size(720, 60);
            this.lblPinballHelp.TabIndex = 4;
            // 
            // btnPinballDelete
            // 
            this.btnPinballDelete.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnPinballDelete.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPinballDelete.ForeColor = System.Drawing.Color.White;
            this.btnPinballDelete.Location = new System.Drawing.Point(235, 335);
            this.btnPinballDelete.Name = "btnPinballDelete";
            this.btnPinballDelete.Size = new System.Drawing.Size(100, 30);
            this.btnPinballDelete.TabIndex = 3;
            this.btnPinballDelete.Text = "Delete";
            this.btnPinballDelete.UseVisualStyleBackColor = false;
            this.btnPinballDelete.Click += new System.EventHandler(this.btnPinballDelete_Click);
            // 
            // btnPinballEdit
            // 
            this.btnPinballEdit.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnPinballEdit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPinballEdit.ForeColor = System.Drawing.Color.White;
            this.btnPinballEdit.Location = new System.Drawing.Point(125, 335);
            this.btnPinballEdit.Name = "btnPinballEdit";
            this.btnPinballEdit.Size = new System.Drawing.Size(100, 30);
            this.btnPinballEdit.TabIndex = 2;
            this.btnPinballEdit.Text = "Edit";
            this.btnPinballEdit.UseVisualStyleBackColor = false;
            this.btnPinballEdit.Click += new System.EventHandler(this.btnPinballEdit_Click);
            // 
            // btnPinballAdd
            // 
            this.btnPinballAdd.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btnPinballAdd.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPinballAdd.ForeColor = System.Drawing.Color.White;
            this.btnPinballAdd.Location = new System.Drawing.Point(15, 335);
            this.btnPinballAdd.Name = "btnPinballAdd";
            this.btnPinballAdd.Size = new System.Drawing.Size(100, 30);
            this.btnPinballAdd.TabIndex = 1;
            this.btnPinballAdd.Text = "Add";
            this.btnPinballAdd.UseVisualStyleBackColor = false;
            this.btnPinballAdd.Click += new System.EventHandler(this.btnPinballAdd_Click);
            // 
            // dgvPinball
            // 
            this.dgvPinball.AllowUserToAddRows = false;
            this.dgvPinball.AllowUserToDeleteRows = false;
            this.dgvPinball.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle1.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(122)))), ((int)(((byte)(204)))));
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dgvPinball.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dgvPinball.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvPinball.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colPinballSystem,
            this.colPinballCommand});
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle4.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            dataGridViewCellStyle4.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle4.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle4.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(122)))), ((int)(((byte)(204)))));
            dataGridViewCellStyle4.SelectionForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle4.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dgvPinball.DefaultCellStyle = dataGridViewCellStyle4;
            this.dgvPinball.EnableHeadersVisualStyles = false;
            this.dgvPinball.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.dgvPinball.Location = new System.Drawing.Point(15, 25);
            this.dgvPinball.MultiSelect = false;
            this.dgvPinball.Name = "dgvPinball";
            this.dgvPinball.ReadOnly = true;
            dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle5.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            dataGridViewCellStyle5.Font = new System.Drawing.Font("Segoe UI", 9F);
            dataGridViewCellStyle5.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle5.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle5.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle5.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dgvPinball.RowHeadersDefaultCellStyle = dataGridViewCellStyle5;
            this.dgvPinball.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvPinball.Size = new System.Drawing.Size(720, 304);
            this.dgvPinball.TabIndex = 0;
            // 
            // colPinballSystem
            // 
            dataGridViewCellStyle2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.White;
            this.colPinballSystem.DefaultCellStyle = dataGridViewCellStyle2;
            this.colPinballSystem.HeaderText = "System Name";
            this.colPinballSystem.Name = "colPinballSystem";
            this.colPinballSystem.ReadOnly = true;
            this.colPinballSystem.Width = 200;
            // 
            // colPinballCommand
            // 
            this.colPinballCommand.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            dataGridViewCellStyle3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            dataGridViewCellStyle3.ForeColor = System.Drawing.Color.White;
            this.colPinballCommand.DefaultCellStyle = dataGridViewCellStyle3;
            this.colPinballCommand.HeaderText = "Command";
            this.colPinballCommand.Name = "colPinballCommand";
            this.colPinballCommand.ReadOnly = true;
            // 
            // tabAdvanced
            // 
            this.tabAdvanced.AutoScroll = true;
            this.tabAdvanced.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.tabAdvanced.Controls.Add(this.flpAdvanced);
            this.tabAdvanced.ForeColor = System.Drawing.Color.Black;
            this.tabAdvanced.Location = new System.Drawing.Point(4, 27);
            this.tabAdvanced.Name = "tabAdvanced";
            this.tabAdvanced.Padding = new System.Windows.Forms.Padding(3);
            this.tabAdvanced.Size = new System.Drawing.Size(801, 547);
            this.tabAdvanced.TabIndex = 5;
            this.tabAdvanced.Text = "Advanced";
            // 
            // flpAdvanced
            // 
            this.flpAdvanced.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.flpAdvanced.AutoScroll = true;
            this.flpAdvanced.AutoSize = true;
            this.flpAdvanced.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpAdvanced.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.flpAdvanced.Controls.Add(this.groupAdvanced);
            this.flpAdvanced.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpAdvanced.ForeColor = System.Drawing.Color.White;
            this.flpAdvanced.Location = new System.Drawing.Point(0, 0);
            this.flpAdvanced.Name = "flpAdvanced";
            this.flpAdvanced.Padding = new System.Windows.Forms.Padding(10);
            this.flpAdvanced.Size = new System.Drawing.Size(805, 376);
            this.flpAdvanced.TabIndex = 0;
            this.flpAdvanced.WrapContents = false;
            // 
            // groupAdvanced
            // 
            this.groupAdvanced.Controls.Add(this.txtSystemAliases);
            this.groupAdvanced.Controls.Add(this.lblSystemAliases);
            this.groupAdvanced.Controls.Add(this.txtCollectionCorrelation);
            this.groupAdvanced.Controls.Add(this.lblCollectionCorrelation);
            this.groupAdvanced.ForeColor = System.Drawing.Color.White;
            this.groupAdvanced.Location = new System.Drawing.Point(13, 13);
            this.groupAdvanced.Name = "groupAdvanced";
            this.groupAdvanced.Size = new System.Drawing.Size(779, 350);
            this.groupAdvanced.TabIndex = 0;
            this.groupAdvanced.TabStop = false;
            this.groupAdvanced.Text = "Advanced Settings";
            // 
            // txtSystemAliases
            // 
            this.txtSystemAliases.BackColor = System.Drawing.Color.White;
            this.txtSystemAliases.ForeColor = System.Drawing.Color.Black;
            this.txtSystemAliases.Location = new System.Drawing.Point(15, 200);
            this.txtSystemAliases.Multiline = true;
            this.txtSystemAliases.Name = "txtSystemAliases";
            this.txtSystemAliases.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtSystemAliases.Size = new System.Drawing.Size(720, 120);
            this.txtSystemAliases.TabIndex = 3;
            // 
            // lblSystemAliases
            // 
            this.lblSystemAliases.AutoSize = true;
            this.lblSystemAliases.Location = new System.Drawing.Point(15, 180);
            this.lblSystemAliases.Name = "lblSystemAliases";
            this.lblSystemAliases.Size = new System.Drawing.Size(87, 15);
            this.lblSystemAliases.TabIndex = 2;
            this.lblSystemAliases.Text = "System Aliases:";
            // 
            // txtCollectionCorrelation
            // 
            this.txtCollectionCorrelation.BackColor = System.Drawing.Color.White;
            this.txtCollectionCorrelation.ForeColor = System.Drawing.Color.Black;
            this.txtCollectionCorrelation.Location = new System.Drawing.Point(15, 45);
            this.txtCollectionCorrelation.Multiline = true;
            this.txtCollectionCorrelation.Name = "txtCollectionCorrelation";
            this.txtCollectionCorrelation.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtCollectionCorrelation.Size = new System.Drawing.Size(720, 120);
            this.txtCollectionCorrelation.TabIndex = 1;
            // 
            // lblCollectionCorrelation
            // 
            this.lblCollectionCorrelation.AutoSize = true;
            this.lblCollectionCorrelation.Location = new System.Drawing.Point(15, 25);
            this.lblCollectionCorrelation.Name = "lblCollectionCorrelation";
            this.lblCollectionCorrelation.Size = new System.Drawing.Size(126, 15);
            this.lblCollectionCorrelation.TabIndex = 0;
            this.lblCollectionCorrelation.Text = "Collection Correlation:";
            // 
            // panelBottom
            // 
            this.panelBottom.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelBottom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.panelBottom.Controls.Add(this.btn_cancel);
            this.panelBottom.Controls.Add(this.btn_save);
            this.panelBottom.Location = new System.Drawing.Point(0, 552);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(1013, 50);
            this.panelBottom.TabIndex = 2;
            // 
            // btn_cancel
            // 
            this.btn_cancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btn_cancel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(63)))), ((int)(((byte)(63)))), ((int)(((byte)(70)))));
            this.btn_cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btn_cancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_cancel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btn_cancel.ForeColor = System.Drawing.Color.White;
            this.btn_cancel.Location = new System.Drawing.Point(818, 12);
            this.btn_cancel.Name = "btn_cancel";
            this.btn_cancel.Size = new System.Drawing.Size(90, 30);
            this.btn_cancel.TabIndex = 1;
            this.btn_cancel.Text = "Cancel";
            this.btn_cancel.UseVisualStyleBackColor = false;
            this.btn_cancel.Click += new System.EventHandler(this.btn_cancel_Click);
            // 
            // btn_save
            // 
            this.btn_save.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btn_save.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(122)))), ((int)(((byte)(204)))));
            this.btn_save.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btn_save.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btn_save.ForeColor = System.Drawing.Color.White;
            this.btn_save.Location = new System.Drawing.Point(914, 12);
            this.btn_save.Name = "btn_save";
            this.btn_save.Size = new System.Drawing.Size(90, 30);
            this.btn_save.TabIndex = 0;
            this.btn_save.Text = "Save";
            this.btn_save.UseVisualStyleBackColor = false;
            this.btn_save.Click += new System.EventHandler(this.btn_save_Click);
            // 
            // ConfigMenuForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.ClientSize = new System.Drawing.Size(1013, 602);
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.menuStrip);
            this.ForeColor = System.Drawing.Color.White;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip;
            this.MaximizeBox = false;
            this.Name = "ConfigMenuForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "RetroBat Marquee Manager - Configuration";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ConfigMenuForm_FormClosing);
            this.Load += new System.EventHandler(this.ConfigMenuForm_Load);
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.tabControl.ResumeLayout(false);
            this.tabGeneral.ResumeLayout(false);
            this.tabGeneral.PerformLayout();
            this.flpGeneral.ResumeLayout(false);
            this.groupPaths.ResumeLayout(false);
            this.groupPaths.PerformLayout();
            this.groupMarquee.ResumeLayout(false);
            this.groupMarquee.PerformLayout();
            this.groupRA.ResumeLayout(false);
            this.groupRA.PerformLayout();
            this.groupUI.ResumeLayout(false);
            this.groupUI.PerformLayout();
            this.groupLogging.ResumeLayout(false);
            this.groupLogging.PerformLayout();
            this.tabScraping.ResumeLayout(false);
            this.tabScraping.PerformLayout();
            this.flpScraping.ResumeLayout(false);
            this.groupScrapingGeneral.ResumeLayout(false);
            this.groupScrapingGeneral.PerformLayout();
            this.groupArcadeItalia.ResumeLayout(false);
            this.groupArcadeItalia.PerformLayout();
            this.groupScreenScraper.ResumeLayout(false);
            this.groupScreenScraper.PerformLayout();
            this.tabDMD.ResumeLayout(false);
            this.tabDMD.PerformLayout();
            this.flpDMD.ResumeLayout(false);
            this.groupDMDGeneral.ResumeLayout(false);
            this.groupDMDGeneral.PerformLayout();
            this.groupDMDDisplay.ResumeLayout(false);
            this.groupDMDDisplay.PerformLayout();
            this.tabScreen.ResumeLayout(false);
            this.tabScreen.PerformLayout();
            this.flpScreen.ResumeLayout(false);
            this.groupMPV.ResumeLayout(false);
            this.groupMPV.PerformLayout();
            this.tabPinball.ResumeLayout(false);
            this.tabPinball.PerformLayout();
            this.flpPinball.ResumeLayout(false);
            this.groupPinball.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvPinball)).EndInit();
            this.tabAdvanced.ResumeLayout(false);
            this.tabAdvanced.PerformLayout();
            this.flpAdvanced.ResumeLayout(false);
            this.groupAdvanced.ResumeLayout(false);
            this.groupAdvanced.PerformLayout();
            this.panelBottom.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolTip toolTipHint;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem menu_file;
        private System.Windows.Forms.ToolStripMenuItem menu_file_save;
        private System.Windows.Forms.ToolStripMenuItem menu_file_reload;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem menu_file_exit;
        private System.Windows.Forms.ToolStripMenuItem menu_tools;
        private System.Windows.Forms.ToolStripMenuItem menu_tools_clear_cache;
        private System.Windows.Forms.ToolStripMenuItem menu_tools_open_logs;
        private System.Windows.Forms.ToolStripMenuItem menu_tools_restart_app;
        private System.Windows.Forms.ToolStripMenuItem menu_help;
        private System.Windows.Forms.ToolStripMenuItem menu_help_readme;
        private System.Windows.Forms.ToolStripMenuItem menu_help_about;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.Button btnTabGeneral;
        private System.Windows.Forms.Button btnTabScraping;
        private System.Windows.Forms.Button btnTabDMD;
        private System.Windows.Forms.Button btnTabScreen;
        private System.Windows.Forms.Button btnTabPinball;
        private System.Windows.Forms.Button btnTabAdvanced;
        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabGeneral;
        private System.Windows.Forms.TabPage tabScraping;
        private System.Windows.Forms.TabPage tabDMD;
        private System.Windows.Forms.TabPage tabScreen;
        private System.Windows.Forms.TabPage tabPinball;
        private System.Windows.Forms.TabPage tabAdvanced;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Button btn_save;
        private System.Windows.Forms.Button btn_cancel;
        // General Tab Controls
        private System.Windows.Forms.GroupBox groupPaths;
        private System.Windows.Forms.Button btnBrowseIM;
        private System.Windows.Forms.TextBox txtIMPath;
        private System.Windows.Forms.Label lblIMPath;
        private System.Windows.Forms.Button btnBrowseRoms;
        private System.Windows.Forms.TextBox txtRomsPath;
        private System.Windows.Forms.Label lblRomsPath;
        private System.Windows.Forms.Button btnBrowseRetroBat;
        private System.Windows.Forms.TextBox txtRetroBatPath;
        private System.Windows.Forms.Label lblRetroBatPath;
        private System.Windows.Forms.GroupBox groupMarquee;
        private System.Windows.Forms.TextBox txtVideoFolder;
        private System.Windows.Forms.Label lblVideoFolder;
        private System.Windows.Forms.ComboBox cboVideoGeneration;
        private System.Windows.Forms.Label lblVideoGeneration;
        private System.Windows.Forms.ComboBox cboFfmpegHwEncoding;
        private System.Windows.Forms.Label lbl_ffmpeg_hw_encoding;
        private System.Windows.Forms.CheckBox chkAutoConvert;
        private System.Windows.Forms.ComboBox cboLayout;
        private System.Windows.Forms.Label lblLayout;
        private System.Windows.Forms.ComboBox cboComposeMedia;
        private System.Windows.Forms.Label lblComposeMedia;
        private System.Windows.Forms.CheckBox chkCompose;
        private System.Windows.Forms.TextBox txtBackgroundColor;
        private System.Windows.Forms.Label lblBackgroundColor;
        private System.Windows.Forms.GroupBox groupRA;
        private System.Windows.Forms.TextBox txtRAOverlays;
        private System.Windows.Forms.Label lblRAOverlays;
        private System.Windows.Forms.ComboBox cboRADisplayTarget;
        private System.Windows.Forms.Label lblRADisplayTarget;
        private System.Windows.Forms.TextBox txtRAApiKey;
        private System.Windows.Forms.Label lblRAApiKey;
        private System.Windows.Forms.CheckBox chkRAEnable;
        private System.Windows.Forms.TextBox txtMpvRAOverlays;
        private System.Windows.Forms.Label lblMpvRAOverlays;
        private System.Windows.Forms.TextBox txtDmdRAOverlays;
        private System.Windows.Forms.Label lblDmdRAOverlays;
        private System.Windows.Forms.Button btnEditDmdLayout;
        private System.Windows.Forms.Button btnEditMpvLayout;
        private System.Windows.Forms.CheckBox chk_ra_mpv_notifs;
        private System.Windows.Forms.CheckBox chk_ra_dmd_notifs;
        private System.Windows.Forms.GroupBox groupUI;
        private System.Windows.Forms.TextBox txtAcceptedFormats;
        private System.Windows.Forms.Label lblAcceptedFormats;
        private System.Windows.Forms.ComboBox cboAutoStart;
        private System.Windows.Forms.Label lblAutoStart;
        private System.Windows.Forms.CheckBox chkMinimizeToTray;
        private System.Windows.Forms.GroupBox groupLogging;
        private System.Windows.Forms.TextBox txtLogPath;
        private System.Windows.Forms.Label lblLogPath;
        private System.Windows.Forms.CheckBox chkLogToFile;
        // Scraping Tab Controls
        private System.Windows.Forms.GroupBox groupScrapingGeneral;
        private System.Windows.Forms.TextBox txtPrioritySource;
        private System.Windows.Forms.Label lblPrioritySource;
        private System.Windows.Forms.CheckBox chkAutoScraping;
        private System.Windows.Forms.GroupBox groupArcadeItalia;
        private System.Windows.Forms.ComboBox cboArcadeItaliaMediaType;
        private System.Windows.Forms.Label lblArcadeItaliaMediaType;
        private System.Windows.Forms.TextBox txtArcadeItaliaUrl;
        private System.Windows.Forms.Label lblArcadeItaliaUrl;
        private System.Windows.Forms.GroupBox groupScreenScraper;
        private System.Windows.Forms.TextBox txtDMDScrapMediaType;
        private System.Windows.Forms.Label lblDMDScrapMediaType;
        private System.Windows.Forms.TextBox txtMPVScrapMediaType;
        private System.Windows.Forms.Label lblMPVScrapMediaType;
        private System.Windows.Forms.CheckBox chkSSGlobal;
        private System.Windows.Forms.TextBox txtSSThreads;
        private System.Windows.Forms.Label lblSSThreads;
        private System.Windows.Forms.TextBox txtSSQueueLimit;
        private System.Windows.Forms.Label lblSSQueueLimit;
        private System.Windows.Forms.TextBox txtSSQueueKeep;
        private System.Windows.Forms.Label lblSSQueueKeep;
        private System.Windows.Forms.TextBox txtSSDevPass;
        private System.Windows.Forms.Label lblSSDevPass;
        private System.Windows.Forms.TextBox txtSSDevId;
        private System.Windows.Forms.Label lblSSDevId;
        private System.Windows.Forms.TextBox txtSSPass;
        private System.Windows.Forms.Label lblSSPass;
        private System.Windows.Forms.TextBox txtSSUser;
        private System.Windows.Forms.Label lblSSUser;
        // DMD Tab Controls
        private System.Windows.Forms.GroupBox groupDMDGeneral;
        private System.Windows.Forms.Button btnBrowseDMDGameStart;
        private System.Windows.Forms.TextBox txtDMDGameStartPath;
        private System.Windows.Forms.Label lblDMDGameStartPath;
        private System.Windows.Forms.Button btnBrowseSystemDMD;
        private System.Windows.Forms.TextBox txtSystemCustomDMDPath;
        private System.Windows.Forms.Label lblSystemCustomDMDPath;
        private System.Windows.Forms.Button btnBrowseDMDMedia;
        private System.Windows.Forms.TextBox txtDMDMediaPath;
        private System.Windows.Forms.Label lblDMDMediaPath;
        private System.Windows.Forms.Button btnBrowseDMDExe;
        private System.Windows.Forms.TextBox txtDMDExePath;
        private System.Windows.Forms.Label lblDMDExePath;
        private System.Windows.Forms.ComboBox cboDMDModel;
        private System.Windows.Forms.Label lblDMDModel;
        private System.Windows.Forms.CheckBox chkDMDEnabled;
        private System.Windows.Forms.GroupBox groupDMDDisplay;

        private System.Windows.Forms.TextBox txtDMDDotSize;
        private System.Windows.Forms.Label lblDMDDotSize;
        private System.Windows.Forms.TextBox txtDMDHeight;
        private System.Windows.Forms.Label lblDMDHeight;
        private System.Windows.Forms.TextBox txtDMDWidth;
        private System.Windows.Forms.Label lblDMDWidth;
        private System.Windows.Forms.ComboBox cboDMDFormat;
        private System.Windows.Forms.Label lblDMDFormat;
        private System.Windows.Forms.CheckBox chkDMDCompose;
        // Screen/MPV Tab Controls
        private System.Windows.Forms.GroupBox groupMPV;
        private System.Windows.Forms.Button btnBrowseGameStartMedia;
        private System.Windows.Forms.TextBox txtGameStartMediaPath;
        private System.Windows.Forms.Label lblGameStartMediaPath;
        private System.Windows.Forms.Button btnBrowseGameCustomMarquee;
        private System.Windows.Forms.TextBox txtGameCustomMarqueePath;
        private System.Windows.Forms.Label lblGameCustomMarqueePath;
        private System.Windows.Forms.Button btnBrowseSystemCustomMarquee;
        private System.Windows.Forms.TextBox txtSystemCustomMarqueePath;
        private System.Windows.Forms.Label lblSystemCustomMarqueePath;
        private System.Windows.Forms.TextBox txtScreenNumber;
        private System.Windows.Forms.Label lblScreenNumber;
        private System.Windows.Forms.ComboBox cboHwDecoding;
        private System.Windows.Forms.Label lbl_hw_decoding;
        private System.Windows.Forms.TextBox txtMarqueeHeight;
        private System.Windows.Forms.Label lblMarqueeHeight;
        private System.Windows.Forms.TextBox txtMarqueeWidth;
        private System.Windows.Forms.Label lblMarqueeWidth;
        private System.Windows.Forms.Button btnBrowseMPV;
        private System.Windows.Forms.TextBox txtMPVPath;
        private System.Windows.Forms.Label lblMPVPath;
        // Advanced Tab Controls
        private System.Windows.Forms.GroupBox groupAdvanced;
        private System.Windows.Forms.TextBox txtSystemAliases;
        private System.Windows.Forms.Label lblSystemAliases;
        private System.Windows.Forms.TextBox txtCollectionCorrelation;
        private System.Windows.Forms.Label lblCollectionCorrelation;
        // Pinball Tab Controls
        private System.Windows.Forms.GroupBox groupPinball;
        private System.Windows.Forms.DataGridView dgvPinball;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPinballSystem;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPinballCommand;
        private System.Windows.Forms.Button btnPinballAdd;
        private System.Windows.Forms.Button btnPinballEdit;
        private System.Windows.Forms.Button btnPinballDelete;
        private System.Windows.Forms.Label lblPinballHelp;
        // FlowLayoutPanels
        private System.Windows.Forms.FlowLayoutPanel flpGeneral;
        private System.Windows.Forms.FlowLayoutPanel flpScraping;
        private System.Windows.Forms.FlowLayoutPanel flpDMD;
        private System.Windows.Forms.FlowLayoutPanel flpScreen;
        private System.Windows.Forms.FlowLayoutPanel flpPinball;
        private System.Windows.Forms.FlowLayoutPanel flpAdvanced;
    }
}


