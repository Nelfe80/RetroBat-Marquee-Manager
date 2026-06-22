using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RetroBatMarqueeManager.Launcher.Models;

namespace RetroBatMarqueeManager.Launcher.Forms
{
    /// <summary>
    /// EN: Visual editor for overlay layouts
    /// FR: Éditeur visuel pour les mises en page d'overlays
    /// </summary>
    public partial class OverlayDesignerForm : Form
    {
        private MouseEventHandler _mouseWheelHandler;
        private OverlayLayout _layout;
        private string _screenType;
        private int _screenWidth;
        private int _screenHeight;
        private string _templatePath;
        private Panel _canvas;
        private List<OverlayBoxControl> _boxes = new List<OverlayBoxControl>();
        private OverlayBoxControl _selectedBox;
        private float _currentScale = 1.0f;
        private Panel _canvasContainer; // Hidden panel for better centering
        private bool _suppressEvents = false; // Prevent event loop during selection

        public OverlayDesignerForm(string templatePath, string screenType, int width, int height)
        {
            _templatePath = templatePath;
            _screenType = screenType;
            _screenWidth = width;
            _screenHeight = height;
            
            InitializeComponent();
            this.Text = $"Overlay Designer - {_screenType.ToUpper()} ({_screenWidth}x{_screenHeight})";
            lblTitle.Text = $"Editing {_screenType.ToUpper()} Layout ({_screenWidth}x{_screenHeight})";
            
            LoadLayout();
            LoadLayout();
            LoadLayout();
            SetupEvents();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SetupCanvas();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // EN: Send command to stop preview immediately
            // FR: Envoyer commande pour stopper la preview immédiatement
            SendIpcCommand("stop-preview");
        }

        private void LoadLayout()
        {
            if (File.Exists(_templatePath))
            {
                try
                {
                    string json = File.ReadAllText(_templatePath);
                    _layout = SimpleJson.Deserialize(json);
                    MigrateLayout(); // EN: Apply migrations to handle legacy/corrupt values / FR: Appliquer les migrations
                }
                catch 
                { 
                    _layout = new OverlayLayout(); 
                }
            }
            else
            {
                _layout = new OverlayLayout();
            }
        }

        private void MigrateLayout()
        {
            if (_layout == null) return;

            bool isDmd = _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase);
            var items = isDmd ? _layout.DmdItems : _layout.MpvItems;

            if (isDmd)
            {
                int targetCenterX = (_screenWidth - 64) / 2;
                if (items.TryGetValue("count", out var countItem) && (Math.Abs(countItem.X - targetCenterX) > 2 || countItem.Width != 64))
                {
                    countItem.Width = 64;
                    countItem.X = targetCenterX;
                }
                if (items.TryGetValue("score", out var scoreItem) && (Math.Abs(scoreItem.X - targetCenterX) > 2 || scoreItem.Width != 64))
                {
                    scoreItem.Width = 64;
                    scoreItem.X = targetCenterX;
                }
                if (items.TryGetValue("badges", out var badgesItem) && badgesItem.Height > (_screenHeight / 2 + 2))
                {
                    badgesItem.Height = _screenHeight / 2;
                    badgesItem.Y = _screenHeight / 2;
                }
            }
            else // MPV
            {
                if (items.TryGetValue("badges", out var mpvBadges))
                {
                    int currentBottom = mpvBadges.Y + mpvBadges.Height;
                    if (currentBottom < _screenHeight && currentBottom >= _screenHeight - 12)
                    {
                        mpvBadges.Y = _screenHeight - mpvBadges.Height;
                    }
                }
            }
        }

        private void SetupEvents()
        {
            // Update Labels
            lblResolution.Text = $"Resolution: {_screenWidth}x{_screenHeight}";

            // Map Events
            // Map Events
            lstItems.SelectedIndexChanged += (s, e) => 
            {
                if (_suppressEvents) return;
                SelectBox(lstItems.SelectedItem as string);
            };
            
            chkEnabled.CheckedChanged += (s, e) => 
            { 
                if (_suppressEvents) return;
                if (_selectedBox != null) 
                {
                    _selectedBox.Item.IsEnabled = chkEnabled.Checked;
                    _selectedBox.Visible = chkEnabled.Checked;
                }
            };

            btnLayerUp.Click += (s, e) => ChangeZOrder(true);
            btnLayerDown.Click += (s, e) => ChangeZOrder(false);
            btnTextColor.Click += (s, e) => ChangeColor();
            
            numFontSize.ValueChanged += (s, e) => 
            {
                if (_suppressEvents) return;
                if (_selectedBox != null) _selectedBox.Item.FontSize = (float)numFontSize.Value;
            };

            bool isDmd = _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase);
            int refW = isDmd ? (_layout.DmdWidth > 0 ? _layout.DmdWidth : 128) : (_layout.MarqueeWidth > 0 ? _layout.MarqueeWidth : 1920);
            int refH = isDmd ? (_layout.DmdHeight > 0 ? _layout.DmdHeight : 32) : (_layout.MarqueeHeight > 0 ? _layout.MarqueeHeight : 360);

            btnFixScale.Text = $"Fix Scale ({refH}p -> {_screenHeight}p)";
            btnFixScale.Visible = (_screenHeight != refH || _screenWidth != refW);
            btnFixScale.Click += (s, e) => ScaleItemsProportionally(refW, refH);

            btnLivePreview.Click += (s, e) => RunLivePreview();
        }

        private void SelectBox(string type)
        {
            if (string.IsNullOrEmpty(type)) return;
            var box = _boxes.FirstOrDefault(b => b.OverlayType == type);
            if (box != null)
            {
                _selectedBox = box;
                _suppressEvents = true; // Block UI events while populating values
                
                // Sync ListBox without triggering event (if possible, but flag handles it)
                if (lstItems.SelectedItem as string != box.OverlayType)
                {
                    lstItems.SelectedItem = box.OverlayType;
                }

                chkEnabled.Checked = box.Item.IsEnabled;
                UpdateCoordsLabel();

                // Highlight
                foreach (var b in _boxes) b.BorderStyle = BorderStyle.FixedSingle;
                box.BorderStyle = BorderStyle.Fixed3D;
                box.BringToFront();

                // Update color picker button appearance
                btnTextColor.BackColor = GetColorFromHex(box.Item.TextColor);
                numFontSize.Value = (decimal)box.Item.FontSize;
                
                _suppressEvents = false; // Re-enable events
                
                // Refresh visual feedback on selection / FR: Rafraîchir l'aperçu visuel à la sélection
                // EN: Simple color for designer rectangles / FR: Couleur simple pour les rectangles du designer
                box.BackColor = Color.FromArgb(80, GetColorForType(box.OverlayType));
            }
        }

        private string ColorToHex(Color c)
        {
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private void ChangeColor()
        {
            if (_selectedBox == null) return;
            
            using (var cd = new ColorDialog())
            {
                cd.Color = GetColorFromHex(_selectedBox.Item.TextColor);
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    string hex = ColorToHex(cd.Color);
                    _selectedBox.Item.TextColor = hex;
                    btnTextColor.BackColor = cd.Color;
                }
            }
        }


        private void ChangeZOrder(bool moveUp)
        {
            if (_selectedBox == null) return;
            
            // Get all boxes ordered by ZOrder (ascending) / FR: Récupérer tous les éléments triés par ZOrder
            var ordered = _boxes.OrderBy(b => b.Item.ZOrder).ToList();
            int currentIndex = ordered.IndexOf(_selectedBox);
            
            if (moveUp)
            {
                // Move Up = Increase ZOrder (Swap with next)
                if (currentIndex < ordered.Count - 1)
                {
                    var nextBox = ordered[currentIndex + 1];
                    // Simple swap of ZOrder values / FR: Échange simple des valeurs ZOrder
                    int tempZ = _selectedBox.Item.ZOrder;
                    _selectedBox.Item.ZOrder = nextBox.Item.ZOrder;
                    nextBox.Item.ZOrder = tempZ;

                    // Ensure there's a difference if they were identical / FR: Assurer une différence s'ils étaient identiques
                    if (_selectedBox.Item.ZOrder <= nextBox.Item.ZOrder)
                        _selectedBox.Item.ZOrder = nextBox.Item.ZOrder + 1;
                }
            }
            else
            {
                // Move Down = Decrease ZOrder (Swap with previous)
                if (currentIndex > 0)
                {
                    var prevBox = ordered[currentIndex - 1];
                    int tempZ = _selectedBox.Item.ZOrder;
                    _selectedBox.Item.ZOrder = prevBox.Item.ZOrder;
                    prevBox.Item.ZOrder = tempZ;

                    if (_selectedBox.Item.ZOrder >= prevBox.Item.ZOrder)
                        _selectedBox.Item.ZOrder = prevBox.Item.ZOrder - 1;
                }
            }

            // Apply visual stacking to canvas (WinForms) / FR: Appliquer l'empilement visuel
            // Controls at the end of the collection are drawn last (on top)
            var finalOrdered = _boxes.OrderBy(b => b.Item.ZOrder).ToList();
            foreach (var b in finalOrdered) b.BringToFront();

            UpdateSelectorOrder();
        }

        private void UpdateSelectorOrder()
        {
            string currentSelection = lstItems.SelectedItem as string;
            lstItems.Items.Clear();
            // Show Top to Bottom (Highest ZOrder first)
            var ordered = _boxes.OrderByDescending(b => b.Item.ZOrder).ToList();
            foreach (var b in ordered) lstItems.Items.Add(b.OverlayType);
            
            if (!string.IsNullOrEmpty(currentSelection))
                lstItems.SelectedItem = currentSelection;
        }

        private void RunLivePreview()
        {
            // First save temp changes
            UpdateItemsFromBoxes();
            try
            {
                string json = SimpleJson.Serialize(_layout);
                File.WriteAllText(_templatePath, json);
                
                // Send IPC command to main app
                SendIpcCommand($"preview-overlay|{_screenType}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to trigger preview: {ex.Message}");
            }
        }

        private void SendIpcCommand(string message)
        {
            Task.Run(() =>
            {
                try
                {
                    using (var client = new System.IO.Pipes.NamedPipeClientStream(".", "RetroBatMarqueeManagerPipe", System.IO.Pipes.PipeDirection.Out))
                    {
                        client.Connect(1000);
                        using (var writer = new StreamWriter(client) { AutoFlush = true })
                        {
                            writer.Write(message);
                        }
                    }
                }
                catch { /* App might not be running or pipe busy */ }
            });
        }

        private void UpdateCoordsLabel()
        {
            if (_selectedBox != null)
                lblCoordinates.Text = $"X: {_selectedBox.Item.X}, Y: {_selectedBox.Item.Y}\nW: {_selectedBox.Item.Width}, H: {_selectedBox.Item.Height}";
        }

        private void SetupCanvas()
        {
            // EN: Calculate a scale to fit the canvas in our form container
            // FR: Calculer une échelle pour faire tenir le canevas dans le conteneur du formulaire
            // EN: Padding of 50px on each side
            float availableW = Math.Max(100, panelCanvasContainer.Width - 100);
            float availableH = Math.Max(100, panelCanvasContainer.Height - 100);

            float scaleW = availableW / (float)_screenWidth;
            float scaleH = availableH / (float)_screenHeight;
            
            // EN: Use the smaller scale to ensure it fits entirely
            // FR: Utiliser la plus petite échelle pour s'assurer que tout rentre
            float scale = Math.Min(scaleW, scaleH);
            
            // EN: Limit min scale to avoid invisibility
            if (scale < 0.1f) scale = 0.1f;

            // EN: Hardcoded DMD override removed in favor of dynamic fitting
            // if (_screenWidth < 400) scale = 4.0f;

            _currentScale = scale;
            int scaledW = (int)(_screenWidth * _currentScale);
            int scaledH = (int)(_screenHeight * _currentScale);

            _canvas = new Panel
            {
                BackColor = Color.Black,
                Size = new Size(scaledW, scaledH),
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = false,
                Location = new Point(50, 50)
            };
            _canvas.Paint += (s, e) => DrawGrid(e.Graphics, _canvas.Width, _canvas.Height, _currentScale);
            
            _canvasContainer = new Panel
            {
                Size = new Size(scaledW + 100, scaledH + 100),
                BackColor = Color.Transparent
            };
            _canvasContainer.Controls.Add(_canvas);
            
            // Interaction: Zoom with MouseWheel
            if (_mouseWheelHandler == null)
            {
                _mouseWheelHandler = (s, e) =>
                {
                    float oldScale = _currentScale;
                    if (e.Delta > 0) _currentScale += 0.1f;
                    else _currentScale = Math.Max(0.1f, _currentScale - 0.1f);
                    
                    if (Math.Abs(oldScale - _currentScale) > 0.001f) UpdateCanvasZoom();
                };
                panelCanvasContainer.MouseWheel += _mouseWheelHandler;
            }
            
            panelCanvasContainer.Controls.Add(_canvasContainer);
            
            var items = _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase) 
                ? _layout.DmdItems : _layout.MpvItems;
                
            var defaultItems = GetDefaultItems();
            foreach (var kvp in defaultItems)
            {
                if (!items.ContainsKey(kvp.Key))
                {
                    items[kvp.Key] = kvp.Value;
                }
                
                var box = new OverlayBoxControl(kvp.Key, items[kvp.Key], _currentScale);
                box.Visible = items[kvp.Key].IsEnabled;
                box.OnSelected += (s, e) => { lstItems.SelectedItem = box.OverlayType; };
                box.OnMoved += (s, e) => UpdateCoordsLabel();
                _canvas.Controls.Add(box);
                _boxes.Add(box);
            }

            _boxes = _boxes.OrderBy(b => b.Item.ZOrder).ToList();
            foreach (var box in _boxes) box.BringToFront();
            
            UpdateSelectorOrder();
            if (lstItems.Items.Count > 0) lstItems.SelectedIndex = 0;
        }

        private void UpdateCanvasZoom()
        {
            int scaledW = (int)(_screenWidth * _currentScale);
            int scaledH = (int)(_screenHeight * _currentScale);
            
            _canvas.Size = new Size(scaledW, scaledH);
            _canvasContainer.Size = new Size(scaledW + 100, scaledH + 100);
            
            foreach (var box in _boxes)
            {
                box.SetScale(_currentScale);
            }
            
            _canvas.Invalidate(); 
        }

        private void ScaleItemsProportionally(int oldW, int oldH)
        {
            if (_screenWidth == oldW && _screenHeight == oldH)
            {
                MessageBox.Show("Resolution is already at the target.", "Info");
                return;
            }

            if (MessageBox.Show($"Scale all items from {oldW}x{oldH} to {_screenWidth}x{_screenHeight}?\n(Aspect ratio will be preserved)", "Confirm Scale", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                float ratioW = (float)_screenWidth / oldW;
                float ratioH = (float)_screenHeight / oldH;
                float uniformScale = Math.Min(ratioW, ratioH);

                foreach (var box in _boxes)
                {
                    // EN: Positions follow exact screen ratio
                    // FR: Les positions suivent le ratio exact de l'écran
                    box.Item.X = (int)(box.Item.X * ratioW);
                    box.Item.Y = (int)(box.Item.Y * ratioH);
                    
                    // EN: Dimensions preserve aspect ratio (no stretching)
                    // FR: Les dimensions préservent le ratio d'aspect (pas d'étirement)
                    box.Item.Width = (int)(box.Item.Width * uniformScale);
                    box.Item.Height = (int)(box.Item.Height * uniformScale);
                    
                    box.SetScale(_currentScale);
                }
                UpdateCoordsLabel();
                btnFixScale.Enabled = false; // EN: Prevent multiple scaling / FR: Empêcher le scaling multiple
            }
        }
        
        private Dictionary<string, OverlayItem> GetDefaultItems()
        {
            var dict = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);
            int w = _screenWidth;
            int h = _screenHeight;

            if (_screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase))
            {
                // EN: DMD adaptive logic (proportional to canvas)
                // FR: Logique DMD adaptative (proportionnelle au canevas)
                // Default Runtime is Centered for independent events, so Designer should reflect that.
                // W=64 (50%), H=14 (45%). Center X=(128-64)/2=32, Y=(32-14)/2=9 - 1 = 8.
                dict["count"] = new OverlayItem { X = (w - 64) / 2, Y = (int)(h * 0.25), Width = 64, Height = (int)(h * 0.45), ZOrder = 2, FontSize = 0 };
                dict["score"] = new OverlayItem { X = (w - 64) / 2, Y = (int)(h * 0.25), Width = 64, Height = (int)(h * 0.45), ZOrder = 3, FontSize = 0 };
                dict["badges"] = new OverlayItem { X = 0, Y = h / 2, Width = w, Height = h / 2, ZOrder = 1 };
                dict["unlock"] = new OverlayItem { X = 0, Y = 0, Width = w, Height = h, ZOrder = 4 };
                dict["challenge"] = new OverlayItem { X = 0, Y = 0, Width = w, Height = h, ZOrder = 4 };
                
                dict["rp_score"] = new OverlayItem { X = (int)(w * 0.62), Y = (int)(h * 0.03), Width = (int)(w * 0.36), Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
                dict["rp_lives"] = new OverlayItem { X = (int)(w * 0.015), Y = (int)(h * 0.03), Width = (int)(w * 0.31), Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
                dict["rp_rank"] = new OverlayItem { X = (int)(w * 0.62), Y = (int)(h * 0.03), Width = (int)(w * 0.36), Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
                dict["rp_lap"] = new OverlayItem { X = (int)(w * 0.015), Y = (int)(h * 0.03), Width = (int)(w * 0.31), Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
                dict["rp_narration"] = new OverlayItem { X = 0, Y = (int)(h * 0.68), Width = w, Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
                dict["rp_stat"] = new OverlayItem { X = 0, Y = (int)(h * 0.34), Width = (int)(w * 0.5), Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
                dict["rp_weapon"] = new OverlayItem { X = (int)(w * 0.5), Y = (int)(h * 0.34), Width = (int)(w * 0.5), Height = (int)(h * 0.31), ZOrder = 5, FontSize = 0 };
            }
            else
            {
                // EN: MPV Adaptive Layout / FR: Layout MPV Adaptatif
                if (w <= 0) w = 1920;
                if (h <= 0) h = 360;

                // Unlock (Achievements): 50% width
                int unlockW = (int)(w * 0.5);
                int unlockH = (int)(h * ((double)w / h > 3.0 ? 0.6 : 0.4));
                dict["unlock"] = new OverlayItem { X = (w - unlockW) / 2, Y = (h - unlockH) / 2, Width = unlockW, Height = unlockH, ZOrder = 5 };
                
                // Badges (Ribbon): Bottom 1/4.5 of height (~22% for visibility)
                int badgeH = (int)(h / 4.5);
                dict["badges"] = new OverlayItem { X = 0, Y = h - badgeH, Width = w, Height = badgeH, ZOrder = 1 };
                
                // Achievement Indicators (Count & Score)
                dict["count"] = new OverlayItem { X = 20, Y = 20, Width = 200, Height = 60, ZOrder = 2, FontSize = 0 };
                dict["score"] = new OverlayItem { X = w - 220, Y = 20, Width = 200, Height = 60, ZOrder = 3, FontSize = 0 };

                // Challenge
                dict["challenge"] = new OverlayItem { X = 20, Y = (h - 120) / 2, Width = 350, Height = 120, ZOrder = 4 };

                // Rich Presence (Fixed Overlaps)
                dict["rp_stat"] = new OverlayItem { X = 20, Y = 100, Width = 400, Height = 80, ZOrder = 5, FontSize = 0 }; // Top-Left
                dict["rp_lap"] = new OverlayItem { X = 20, Y = 100, Width = 400, Height = 80, ZOrder = 5, FontSize = 0 }; // Top-Left (Shared with rp_stat)
                dict["rp_weapon"] = new OverlayItem { X = 20, Y = 190, Width = 400, Height = 80, ZOrder = 5, FontSize = 0 }; // Left, Under Stat
                dict["rp_score"] = new OverlayItem { X = w - 420, Y = 100, Width = 400, Height = 80, ZOrder = 5, FontSize = 0 }; // Top-Right
                dict["rp_rank"] = new OverlayItem { X = w - 420, Y = 100, Width = 400, Height = 80, ZOrder = 5, FontSize = 0 }; // Top-Right (Shared with rp_score)
                dict["rp_lives"] = new OverlayItem { X = w - 420, Y = 190, Width = 400, Height = 80, ZOrder = 5, FontSize = 0 }; // Right, Under Score
                
                int narrationW = (int)(w * 0.5); // 50%
                int narrationH = 100;
                dict["rp_narration"] = new OverlayItem { X = (w - narrationW) / 2, Y = h - narrationH - (int)(h / 7.2), Width = narrationW, Height = narrationH, ZOrder = 6, FontSize = 0 }; // Bottom Center
            }
            return dict;
        }

        private void UpdateItemsFromBoxes()
        {
            foreach (var box in _boxes)
            {
                box.UpdateItem();
            }
        }

        private void DrawGrid(Graphics g, int width, int height, float scale)
        {
            // EN: Step 8 for DMD, 40 for MPV. 
            // FR: Pas de 8 pour DMD, 40 pour MPV.
            int rawStep = _screenWidth < 400 ? 8 : 40;
            
            using (var pen = new Pen(Color.FromArgb(40, Color.White), 1))
            {
                // EN: Vertical lines (From left to ensure whole squares)
                for (int lx = 0; lx <= _screenWidth; lx += rawStep)
                {
                    int x = (int)(lx * scale);
                    if (x >= width) x = width - 1; 
                    g.DrawLine(pen, x, 0, x, height);
                }
                // EN: Last edge line if resolution is not multiple
                if (_screenWidth % rawStep != 0) g.DrawLine(pen, width - 1, 0, width - 1, height);

                // EN: Horizontal lines (From top)
                for (int ly = 0; ly <= _screenHeight; ly += rawStep)
                {
                    int y = (int)(ly * scale);
                    if (y >= height) y = height - 1;
                    g.DrawLine(pen, 0, y, width, y);
                }
                // EN: Last edge line if resolution is not multiple
                if (_screenHeight % rawStep != 0) g.DrawLine(pen, 0, height - 1, width, height - 1);
            }

            // EN: Draw Center Axes (Gold for high visibility alignment)
            int centerX = width / 2;
            int centerY = height / 2;
            using (var axisPen = new Pen(Color.FromArgb(120, Color.Gold), 1))
            {
                g.DrawLine(axisPen, centerX, 0, centerX, height);
                g.DrawLine(axisPen, 0, centerY, width, centerY);
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            UpdateItemsFromBoxes();

            // EN: Persist current resolutions / FR: Persister les résolutions actuelles
            _layout.DmdWidth = _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase) ? _screenWidth : _layout.DmdWidth;
            _layout.DmdHeight = _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase) ? _screenHeight : _layout.DmdHeight;
            _layout.MarqueeWidth = _screenType.Equals("mpv", StringComparison.OrdinalIgnoreCase) ? _screenWidth : _layout.MarqueeWidth;
            _layout.MarqueeHeight = _screenType.Equals("mpv", StringComparison.OrdinalIgnoreCase) ? _screenHeight : _layout.MarqueeHeight;
            
            // EN: Ensure resolutions are NEVER 0 if we have current values
            if (_layout.DmdWidth <= 0 && _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase)) _layout.DmdWidth = _screenWidth;
            if (_layout.DmdHeight <= 0 && _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase)) _layout.DmdHeight = _screenHeight;
            if (_layout.MarqueeWidth <= 0 && _screenType.Equals("mpv", StringComparison.OrdinalIgnoreCase)) _layout.MarqueeWidth = _screenWidth;
            if (_layout.MarqueeHeight <= 0 && _screenType.Equals("mpv", StringComparison.OrdinalIgnoreCase)) _layout.MarqueeHeight = _screenHeight;
            
            try
            {
                string json = SimpleJson.Serialize(_layout);
                File.WriteAllText(_templatePath, json);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save layout: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
             if (MessageBox.Show("Reset this screen's layout to defaults?", "Reset", MessageBoxButtons.YesNo) == DialogResult.Yes)
             {
                 _boxes.Clear();
                 
                 var items = _screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase) 
                    ? _layout.DmdItems : _layout.MpvItems;
                 items.Clear();
                 
                 panelCanvasContainer.Controls.Clear();
                 if (btnFixScale != null) btnFixScale.Enabled = true;
                 SetupCanvas();
             }
        }

        public static Color GetColorFromHex(string hex, int opacity = -1)
        {
            if (string.IsNullOrEmpty(hex) || hex == "Default")
                return Color.Transparent; // Signal for default behavior

            if (hex == "Transparent" || hex == "#00000000")
                return Color.FromArgb(0, 0, 0, 0);

            try
            {
                Color c = ColorTranslator.FromHtml(hex);
                if (opacity >= 0) return Color.FromArgb(opacity, c);
                return c;
            }
            catch
            {
                return Color.Transparent;
            }
        }

        public static Color GetColorForType(string type)
        {
            switch (type.ToLower())
            {
                case "unlock": return Color.Gold;
                case "score": return Color.DeepSkyBlue;
                case "count": return Color.LimeGreen;
                case "badges": return Color.OrangeRed;
                case "challenge": return Color.Purple;
                case "rp_score": return Color.Gold;
                case "rp_rank": return Color.Violet;
                case "rp_lives": return Color.LightCoral;
                case "rp_lap": return Color.LightCyan;
                case "rp_narration": return Color.LightBlue;
                case "rp_stat": return Color.LightGreen;
                case "rp_weapon": return Color.Orange;
                case "items": return Color.Teal;
                default: return Color.Gray;
            }
        }
    }

    public class OverlayBoxControl : Panel
    {
        public string OverlayType { get; }
        public OverlayItem Item { get; }
        
        private float _scale;
        private bool _isDragging = false;
        private bool _isResizing = false;
        private Point _startMousePos;
        private Rectangle _startBounds;

        public event EventHandler OnSelected;
        public event EventHandler OnMoved;

        public OverlayBoxControl(string type, OverlayItem item, float scale)
        {
            OverlayType = type;
            Item = item;
            _scale = scale;
            
            // Initial scaled bounds
            this.Bounds = new Rectangle((int)Math.Round(item.X * _scale), (int)Math.Round(item.Y * _scale), (int)Math.Round(item.Width * _scale), (int)Math.Round(item.Height * _scale));
            
            // EN: In the designer, we use a semi-transparent version of the type-based color for visual feedback
            // FR: Dans l'éditeur, on utilise une version semi-transparente de la couleur du type pour le retour visuel
            this.BackColor = Color.FromArgb(80, OverlayDesignerForm.GetColorForType(type));

            this.BorderStyle = BorderStyle.FixedSingle;
            this.Cursor = Cursors.SizeAll;
            
            var lbl = new Label
            {
                Text = type.ToUpper(),
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Location = new Point(2, 2)
            };
            this.Controls.Add(lbl);
            
            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (e.X > this.Width - 10 && e.Y > this.Height - 10) _isResizing = true;
                    else _isDragging = true;
                    
                    _startMousePos = Cursor.Position;
                    _startBounds = this.Bounds;
                    OnSelected?.Invoke(this, EventArgs.Empty);
                }
            };
            
            this.MouseMove += (s, e) =>
            {
                if (_isDragging)
                {
                    var diff = new Point(Cursor.Position.X - _startMousePos.X, Cursor.Position.Y - _startMousePos.Y);
                    this.Left = _startBounds.Left + diff.X;
                    this.Top = _startBounds.Top + diff.Y;
                    UpdateItem();
                }
                else if (_isResizing)
                {
                    var diff = new Point(Cursor.Position.X - _startMousePos.X, Cursor.Position.Y - _startMousePos.Y);
                    this.Width = Math.Max(10, _startBounds.Width + diff.X);
                    this.Height = Math.Max(10, _startBounds.Height + diff.Y);
                    UpdateItem();
                    OnMoved?.Invoke(this, EventArgs.Empty);
                }
                
                if (e.X > this.Width - 10 && e.Y > this.Height - 10)
                    this.Cursor = Cursors.SizeNWSE;
                else
                    this.Cursor = Cursors.SizeAll;
            };
            
            this.MouseUp += (s, e) => { _isDragging = false; _isResizing = false; };
        }

        public void SetScale(float scale)
        {
            _scale = scale;
            this.Bounds = new Rectangle((int)Math.Round(Item.X * _scale), (int)Math.Round(Item.Y * _scale), (int)Math.Round(Item.Width * _scale), (int)Math.Round(Item.Height * _scale));
        }

        public void UpdateItem()
        {
            Item.X = (int)Math.Round(this.Left / _scale);
            Item.Y = (int)Math.Round(this.Top / _scale);
            Item.Width = (int)Math.Round(this.Width / _scale);
            Item.Height = (int)Math.Round(this.Height / _scale);
        }
    }
    
    public static class SimpleJson
    {
        public static string Serialize(OverlayLayout layout)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"MarqueeWidth\": {layout.MarqueeWidth},");
            sb.AppendLine($"  \"MarqueeHeight\": {layout.MarqueeHeight},");
            sb.AppendLine($"  \"DmdWidth\": {layout.DmdWidth},");
            sb.AppendLine($"  \"DmdHeight\": {layout.DmdHeight},");
            sb.AppendLine("  \"DmdItems\": {");
            WriteDict(sb, layout.DmdItems, "    ");
            sb.AppendLine("  },");
            sb.AppendLine("  \"MpvItems\": {");
            WriteDict(sb, layout.MpvItems, "    ");
            sb.AppendLine("  }");
            sb.Append("}");
            return sb.ToString();
        }

        private static void WriteDict(System.Text.StringBuilder sb, Dictionary<string, OverlayItem> dict, string indent)
        {
            var keys = dict.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                var v = dict[k];
                sb.Append($"{indent}\"{k}\": {{ \"X\": {v.X}, \"Y\": {v.Y}, \"Width\": {v.Width}, \"Height\": {v.Height}, \"ZOrder\": {v.ZOrder}, \"IsEnabled\": {v.IsEnabled.ToString().ToLower()}, \"TextColor\": \"{v.TextColor}\", \"FontSize\": {v.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}");
                if (i < keys.Count - 1) sb.AppendLine(",");
                else sb.AppendLine("");
            }
        }

        public static OverlayLayout Deserialize(string json)
        {
             var layout = new OverlayLayout();
             layout.MarqueeWidth = GetVal(json, "MarqueeWidth");
             layout.MarqueeHeight = GetVal(json, "MarqueeHeight");
             layout.DmdWidth = GetVal(json, "DmdWidth");
             layout.DmdHeight = GetVal(json, "DmdHeight");
             ParseSection(json, "DmdItems", layout.DmdItems);
             ParseSection(json, "MpvItems", layout.MpvItems);
             return layout;
        }

        private static void ParseSection(string json, string sectionName, Dictionary<string, OverlayItem> dict)
        {
            int start = json.IndexOf($"\"{sectionName}\"");
            if (start == -1) return;
            int openBrace = json.IndexOf("{", start);
            int count = 1;
            int closeBrace = -1;
            for (int i = openBrace + 1; i < json.Length; i++)
            {
                if (json[i] == '{') count++;
                else if (json[i] == '}') count--;
                if (count == 0) { closeBrace = i; break; }
            }
            if (openBrace == -1 || closeBrace == -1) return;
            
            string content = json.Substring(openBrace + 1, closeBrace - openBrace - 1);
            var parts = content.Split(new[] { "}," }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                int colon = part.IndexOf(":");
                if (colon == -1) continue;
                string key = part.Substring(0, colon).Trim(' ', '"', '\n', '\r', ',');
                string val = part.Substring(colon + 1);
                
                var item = new OverlayItem();
                item.X = GetVal(val, "X");
                item.Y = GetVal(val, "Y");
                item.Width = GetVal(val, "Width");
                item.Height = GetVal(val, "Height");
                item.ZOrder = GetVal(val, "ZOrder");
                item.IsEnabled = val.Contains("\"IsEnabled\": true");
                item.TextColor = GetStrVal(val, "TextColor") ?? "#FFFFD700";
                item.FontSize = GetFloatVal(val, "FontSize");
                dict[key] = item;
            }
        }

        private static int GetVal(string content, string key)
        {
            int kIdx = content.IndexOf($"\"{key}\"");
            if (kIdx == -1) return 0;
            int colon = content.IndexOf(":", kIdx);
            int comma = content.IndexOf(",", colon);
            if (comma == -1) comma = content.IndexOf("}", colon);
            if (comma == -1) comma = content.Length;
            string val = content.Substring(colon + 1, comma - colon - 1).Trim(' ', '"', '\n', '\r');
            int.TryParse(val, out var res);
            return res;
        }

        private static string GetStrVal(string content, string key)
        {
            int kIdx = content.IndexOf($"\"{key}\"");
            if (kIdx == -1) return null;
            int colon = content.IndexOf(":", kIdx);
            int comma = content.IndexOf(",", colon);
            if (comma == -1) comma = content.IndexOf("}", colon);
            if (comma == -1) comma = content.Length;
            string val = content.Substring(colon + 1, comma - colon - 1).Trim(' ', '"', '\n', '\r');
            return val;
        }

        private static float GetFloatVal(string content, string key)
        {
            int kIdx = content.IndexOf($"\"{key}\"");
            if (kIdx == -1) return 0f;
            int colon = content.IndexOf(":", kIdx);
            int comma = content.IndexOf(",", colon);
            if (comma == -1) comma = content.IndexOf("}", colon);
            if (comma == -1) comma = content.Length;
            string val = content.Substring(colon + 1, comma - colon - 1).Trim(' ', '"', '\n', '\r');
            float.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var res);
            return res;
        }
    }
}
