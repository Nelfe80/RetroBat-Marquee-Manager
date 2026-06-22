namespace RetroBatMarqueeManager.Launcher.Forms
{
    partial class OverlayDesignerForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.panelHeader = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.panelFooter = new System.Windows.Forms.Panel();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnReset = new System.Windows.Forms.Button();
            this.lblHint = new System.Windows.Forms.Label();
            this.panelCanvasContainer = new System.Windows.Forms.Panel();
            this.panelSide = new System.Windows.Forms.Panel();
            this.lblResolution = new System.Windows.Forms.Label();
            this.lblLayerStack = new System.Windows.Forms.Label();
            this.lstItems = new System.Windows.Forms.ListBox();
            this.chkEnabled = new System.Windows.Forms.CheckBox();
            this.lblCoordinates = new System.Windows.Forms.Label();
            this.btnLayerUp = new System.Windows.Forms.Button();
            this.btnLayerDown = new System.Windows.Forms.Button();
            this.btnTextColor = new System.Windows.Forms.Button();
            this.lblFontSize = new System.Windows.Forms.Label();
            this.numFontSize = new System.Windows.Forms.NumericUpDown();
            this.btnFixScale = new System.Windows.Forms.Button();
            this.btnLivePreview = new System.Windows.Forms.Button();
            this.panelHeader.SuspendLayout();
            this.panelFooter.SuspendLayout();
            this.panelSide.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numFontSize)).BeginInit();
            this.SuspendLayout();
            // 
            // panelHeader
            // 
            this.panelHeader.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.panelHeader.Controls.Add(this.lblTitle);
            this.panelHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelHeader.Location = new System.Drawing.Point(0, 0);
            this.panelHeader.Name = "panelHeader";
            this.panelHeader.Size = new System.Drawing.Size(984, 50);
            this.panelHeader.TabIndex = 3;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(12, 14);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(183, 21);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Overlay Layout Editor";
            // 
            // panelFooter
            // 
            this.panelFooter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.panelFooter.Controls.Add(this.lblHint);
            this.panelFooter.Controls.Add(this.btnReset);
            this.panelFooter.Controls.Add(this.btnSave);
            this.panelFooter.Controls.Add(this.btnCancel);
            this.panelFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelFooter.Location = new System.Drawing.Point(0, 501);
            this.panelFooter.Name = "panelFooter";
            this.panelFooter.Size = new System.Drawing.Size(984, 60);
            this.panelFooter.TabIndex = 2;
            // 
            // btnSave
            // 
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Location = new System.Drawing.Point(862, 15);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(110, 33);
            this.btnSave.TabIndex = 1;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.ForeColor = System.Drawing.Color.White;
            this.btnCancel.Location = new System.Drawing.Point(746, 15);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(110, 33);
            this.btnCancel.TabIndex = 0;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnReset
            // 
            this.btnReset.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnReset.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnReset.ForeColor = System.Drawing.Color.White;
            this.btnReset.Location = new System.Drawing.Point(12, 15);
            this.btnReset.Name = "btnReset";
            this.btnReset.Size = new System.Drawing.Size(110, 33);
            this.btnReset.TabIndex = 2;
            this.btnReset.Text = "Reset to Default";
            this.btnReset.UseVisualStyleBackColor = true;
            this.btnReset.Click += new System.EventHandler(this.btnReset_Click);
            // 
            // lblHint
            // 
            this.lblHint.AutoSize = true;
            this.lblHint.ForeColor = System.Drawing.Color.Silver;
            this.lblHint.Location = new System.Drawing.Point(140, 25);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(325, 13);
            this.lblHint.TabIndex = 3;
            this.lblHint.Text = "Drag to move. Right click or use handles (future) to resize.";
            //
            // panelSide
            //
            this.panelSide.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.panelSide.Dock = System.Windows.Forms.DockStyle.Right;
            this.panelSide.Location = new System.Drawing.Point(764, 50);
            this.panelSide.Name = "panelSide";
            this.panelSide.Size = new System.Drawing.Size(220, 451);
            this.panelSide.Padding = new System.Windows.Forms.Padding(10);
            this.panelSide.TabIndex = 1;
            this.panelSide.AutoScroll = true;
            this.panelSide.Controls.Add(this.btnLivePreview);
            this.panelSide.Controls.Add(this.btnFixScale);
            this.panelSide.Controls.Add(this.numFontSize);
            this.panelSide.Controls.Add(this.lblFontSize);
            this.panelSide.Controls.Add(this.btnTextColor);
            this.panelSide.Controls.Add(this.btnLayerDown);
            this.panelSide.Controls.Add(this.btnLayerUp);
            this.panelSide.Controls.Add(this.lblCoordinates);
            this.panelSide.Controls.Add(this.chkEnabled);
            this.panelSide.Controls.Add(this.lstItems);
            this.panelSide.Controls.Add(this.lblLayerStack);
            this.panelSide.Controls.Add(this.lblResolution);
            //
            // Controls settings inside panelSide
            //
            // lblResolution
            this.lblResolution.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblResolution.ForeColor = System.Drawing.Color.Gold;
            this.lblResolution.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblResolution.Height = 25;
            this.lblResolution.Text = "Resolution: 0x0";
            // lblLayerStack
            this.lblLayerStack.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblLayerStack.ForeColor = System.Drawing.Color.White;
            this.lblLayerStack.Height = 25;
            this.lblLayerStack.Text = "Layer Stack:";
            // lstItems
            this.lstItems.Dock = System.Windows.Forms.DockStyle.Top;
            this.lstItems.Height = 100;
            this.lstItems.BackColor = System.Drawing.Color.DimGray;
            this.lstItems.ForeColor = System.Drawing.Color.White;
            this.lstItems.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            // chkEnabled
            this.chkEnabled.Dock = System.Windows.Forms.DockStyle.Top;
            this.chkEnabled.ForeColor = System.Drawing.Color.White;
            this.chkEnabled.Height = 25;
            this.chkEnabled.Text = "Enabled";
            // lblCoordinates
            this.lblCoordinates.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblCoordinates.ForeColor = System.Drawing.Color.White;
            this.lblCoordinates.Height = 40;
            this.lblCoordinates.Text = "X: 0, Y: 0\nW: 0, H: 0";
            // btnLayerUp
            this.btnLayerUp.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnLayerUp.Height = 30;
            this.btnLayerUp.Text = "Move Up";
            this.btnLayerUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLayerUp.ForeColor = System.Drawing.Color.White;
            // btnLayerDown
            this.btnLayerDown.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnLayerDown.Height = 30;
            this.btnLayerDown.Text = "Move Down";
            this.btnLayerDown.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLayerDown.ForeColor = System.Drawing.Color.White;
            // btnTextColor
            this.btnTextColor.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnTextColor.Height = 30;
            this.btnTextColor.Text = "Text Color";
            this.btnTextColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTextColor.ForeColor = System.Drawing.Color.White;
            // lblFontSize
            this.lblFontSize.Dock = System.Windows.Forms.DockStyle.Top; 
            this.lblFontSize.Height = 20;
            this.lblFontSize.ForeColor = System.Drawing.Color.White;
            this.lblFontSize.Text = "Font Size (0=Auto):";
            // numFontSize
            this.numFontSize.Dock = System.Windows.Forms.DockStyle.Top;
            this.numFontSize.Height = 25;
            this.numFontSize.Minimum = 0;
            this.numFontSize.Maximum = 200;
            this.numFontSize.DecimalPlaces = 1;
            this.numFontSize.Increment = 0.5m;
            this.numFontSize.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // btnFixScale
            this.btnFixScale.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnFixScale.Height = 30;
            this.btnFixScale.Text = "Fix Scale";
            this.btnFixScale.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFixScale.ForeColor = System.Drawing.Color.LightSkyBlue;
            this.btnFixScale.Visible = false;
            // btnLivePreview
            this.btnLivePreview.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnLivePreview.Height = 40;
            this.btnLivePreview.Text = "LIVE PREVIEW";
            this.btnLivePreview.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLivePreview.ForeColor = System.Drawing.Color.Gold;
            this.btnLivePreview.Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold);

            // 
            // panelCanvasContainer
            // 
            this.panelCanvasContainer.AutoScroll = true;
            this.panelCanvasContainer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.panelCanvasContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelCanvasContainer.Location = new System.Drawing.Point(0, 50);
            this.panelCanvasContainer.Name = "panelCanvasContainer";
            this.panelCanvasContainer.Size = new System.Drawing.Size(984, 451);
            this.panelCanvasContainer.TabIndex = 0;
            // 
            // OverlayDesignerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(28)))), ((int)(((byte)(28)))));
            this.ClientSize = new System.Drawing.Size(984, 561);
            this.Controls.Add(this.panelCanvasContainer);
            this.Controls.Add(this.panelSide);
            this.Controls.Add(this.panelFooter);
            this.Controls.Add(this.panelHeader);
            this.Name = "OverlayDesignerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Overlay Designer";
            this.panelHeader.ResumeLayout(false);
            this.panelHeader.PerformLayout();
            this.panelFooter.ResumeLayout(false);
            this.panelFooter.PerformLayout();
            this.panelSide.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.numFontSize)).EndInit();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel panelFooter;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnReset;
        private System.Windows.Forms.Label lblHint;
        private System.Windows.Forms.Panel panelCanvasContainer;
        // New Controls
        public System.Windows.Forms.Panel panelSide;
        public System.Windows.Forms.Label lblResolution;
        public System.Windows.Forms.Label lblLayerStack;
        public System.Windows.Forms.ListBox lstItems;
        public System.Windows.Forms.CheckBox chkEnabled;
        public System.Windows.Forms.Label lblCoordinates;
        public System.Windows.Forms.Button btnLayerUp;
        public System.Windows.Forms.Button btnLayerDown;
        public System.Windows.Forms.Button btnTextColor;
        public System.Windows.Forms.Label lblFontSize;
        public System.Windows.Forms.NumericUpDown numFontSize;
        public System.Windows.Forms.Button btnFixScale;
        public System.Windows.Forms.Button btnLivePreview;
    }
}
