using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace RetroBatMarqueeManager.Infrastructure.UI
{
    public class MarqueeWindow : Window
    {
        private readonly ILogger _logger;
        private readonly int _targetScreen;

        // UI Layers
        private Grid _mainGrid = null!;
        private Image _backgroundImage = null!;
        private MediaElement _mediaElement = null!;
        private Viewbox _layViewbox = null!;
        private Canvas _layCanvas = null!;
        
        // Logo Composition Layer
        private Image _logoImage = null!;
        private TranslateTransform _logoTranslate = null!;
        private ScaleTransform _logoScale = null!;

        // Custom Overlay Slot Layer
        private Canvas _overlayCanvas = null!;
        private readonly Dictionary<int, Image> _slotOverlays = new();

        // OSD Text Layer
        private TextBlock _osdText = null!;
        private DispatcherTimer? _osdTimer;

        // MAME Lamp Map
        private readonly Dictionary<string, List<Image>> _lampImages = new(StringComparer.OrdinalIgnoreCase);

        // Latest-wins: only the last requested path is rendered — checked at dispatch time
        private volatile string? _latestImagePath;
        private volatile string? _latestVideoPath;

        // Win32 API to position window without DPI issues
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public MarqueeWindow(int screenNumber, ILogger logger)
        {
            _targetScreen = screenNumber;
            _logger = logger;

            // WPF Window setup
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = Brushes.Black;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Title = "RetroBat Marquee Player";

            InitializeLayers();

            this.SourceInitialized += OnSourceInitialized;
        }

        private void InitializeLayers()
        {
            _mainGrid = new Grid();

            // 1. Static Background Image
            _backgroundImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            _mainGrid.Children.Add(_backgroundImage);

            // 2. Video Player Layer
            _mediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                IsMuted = true,
                Visibility = Visibility.Collapsed
            };
            _mediaElement.MediaEnded += (s, e) =>
            {
                _mediaElement.Position = TimeSpan.FromMilliseconds(1);
                _mediaElement.Play();
            };
            _mainGrid.Children.Add(_mediaElement);

            // 3. MAME Layout Layer (Canvas scaled via Viewbox)
            _layCanvas = new Canvas();
            _layViewbox = new Viewbox
            {
                Stretch = Stretch.Fill,
                Visibility = Visibility.Collapsed,
                Child = _layCanvas
            };
            _mainGrid.Children.Add(_layViewbox);

            // 4. Logo Layer (for image composition fallback)
            _logoImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Visibility = Visibility.Collapsed
            };

            var transformGroup = new TransformGroup();
            _logoTranslate = new TranslateTransform(0, 0);
            _logoScale = new ScaleTransform(1, 1);
            transformGroup.Children.Add(_logoScale);
            transformGroup.Children.Add(_logoTranslate);
            _logoImage.RenderTransform = transformGroup;
            _mainGrid.Children.Add(_logoImage);

            // 5. Custom Overlay Layer (RetroAchievements, etc.)
            _overlayCanvas = new Canvas();
            _mainGrid.Children.Add(_overlayCanvas);

            // 6. OSD Text Layer
            _osdText = new TextBlock
            {
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 40),
                Padding = new Thickness(15, 8, 15, 8),
                Visibility = Visibility.Collapsed
            };
            _mainGrid.Children.Add(_osdText);

            this.Content = _mainGrid;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            PositionWindow();
        }

        private void PositionWindow()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                int screenIndex = _targetScreen;
                if (screenIndex < 0 || screenIndex >= screens.Length) screenIndex = 0;

                var screen = screens[screenIndex];
                _logger.LogInformation($"[WPF Player] Target Screen Index: {screenIndex}. Screen Bounds: {screen.Bounds}");

                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                SetWindowPos(
                    helper.Handle,
                    HWND_TOPMOST,
                    screen.Bounds.Left,
                    screen.Bounds.Top,
                    screen.Bounds.Width,
                    screen.Bounds.Height,
                    SWP_SHOWWINDOW
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WPF Player] PositionWindow error: {ex.Message}");
            }
        }

        // --- PUBLIC CONTROL INTERFACE (Thread-safe) ---

        public void DisplayImage(string path)
        {
            _latestImagePath = path; // Set BEFORE BeginInvoke — dispatcher checks this at run time
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_latestImagePath != path) return; // A newer path was requested — skip
                try
                {
                    _mediaElement.Stop();
                    _mediaElement.Visibility = Visibility.Collapsed;
                    _layViewbox.Visibility = Visibility.Collapsed;
                    _logoImage.Visibility = Visibility.Collapsed;

                    if (!File.Exists(path)) return;

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    _backgroundImage.Source = bitmap;
                    _backgroundImage.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WPF Player] DisplayImage error: {ex.Message}");
                }
            }));
        }

        public void DisplayVideo(string path)
        {
            _latestVideoPath = path;
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_latestVideoPath != path) return; // A newer video was requested — skip
                try
                {
                    _backgroundImage.Visibility = Visibility.Collapsed;
                    _layViewbox.Visibility = Visibility.Collapsed;
                    _logoImage.Visibility = Visibility.Collapsed;

                    if (!File.Exists(path)) return;

                    _mediaElement.Source = new Uri(path);
                    _mediaElement.Visibility = Visibility.Visible;
                    _mediaElement.Play();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WPF Player] DisplayVideo error: {ex.Message}");
                }
            }));
        }

        public void LoadMameLayout(Application.Services.MameLayout layout, string viewName)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _backgroundImage.Visibility = Visibility.Collapsed;
                    _mediaElement.Stop();
                    _mediaElement.Visibility = Visibility.Collapsed;
                    _logoImage.Visibility = Visibility.Collapsed;

                    _layCanvas.Children.Clear();
                    _lampImages.Clear();

                    if (!layout.Views.TryGetValue(viewName, out var view))
                    {
                        // Try fallback to first view
                        foreach (var kvp in layout.Views)
                        {
                            view = kvp.Value;
                            break;
                        }
                    }

                    if (view == null)
                    {
                        _logger.LogWarning($"[WPF Player] No view found in layout.");
                        return;
                    }

                    _layCanvas.Width = view.Width;
                    _layCanvas.Height = view.Height;

                    _logger.LogInformation($"[WPF Player] Loading MAME layout view '{view.Name}' ({view.Width}x{view.Height}) with {view.Elements.Count} elements.");

                    foreach (var viewElem in view.Elements)
                    {
                        if (layout.Elements.TryGetValue(viewElem.Ref, out var element))
                        {
                            var imgPath = Path.Combine(layout.Directory, element.ImageFile);
                            if (!File.Exists(imgPath)) continue;

                            var imgControl = new Image
                            {
                                Stretch = Stretch.Fill,
                                Width = viewElem.Width,
                                Height = viewElem.Height
                            };

                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(imgPath);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            imgControl.Source = bitmap;

                            Canvas.SetLeft(imgControl, viewElem.X);
                            Canvas.SetTop(imgControl, viewElem.Y);

                            _layCanvas.Children.Add(imgControl);

                            // Dynamic elements (lamps) have a name
                            if (!string.IsNullOrEmpty(viewElem.Name))
                            {
                                imgControl.Visibility = Visibility.Collapsed; // Hide by default
                                if (!_lampImages.TryGetValue(viewElem.Name, out var list))
                                {
                                    list = new List<Image>();
                                    _lampImages[viewElem.Name] = list;
                                }
                                list.Add(imgControl);
                            }
                            else
                            {
                                imgControl.Visibility = Visibility.Visible;
                            }
                        }
                    }

                    _layViewbox.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WPF Player] LoadMameLayout error: {ex.Message}");
                }
            }));
        }

        public void SetLampState(string lampName, int state)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_lampImages.TryGetValue(lampName, out var images))
                {
                    var visibility = (state != 0) ? Visibility.Visible : Visibility.Collapsed;
                    foreach (var img in images)
                    {
                        img.Visibility = visibility;
                    }
                }
            }));
        }

        public void ClearLayout()
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _layCanvas.Children.Clear();
                _lampImages.Clear();
                _layViewbox.Visibility = Visibility.Collapsed;
            }));
        }

        public void SetLogoComposition(string logoPath, double x, double y, double scale)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!File.Exists(logoPath))
                    {
                        _logoImage.Visibility = Visibility.Collapsed;
                        return;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(logoPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    _logoImage.Source = bitmap;
                    _logoTranslate.X = x;
                    _logoTranslate.Y = y;
                    _logoScale.ScaleX = scale;
                    _logoScale.ScaleY = scale;
                    _logoImage.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WPF Player] SetLogoComposition error: {ex.Message}");
                }
            }));
        }

        public void SetOverlayImage(int slot, string path, string position = "0:0")
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RemoveOverlayImage(slot);

                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                    var img = new Image { Stretch = Stretch.Uniform };
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    img.Source = bitmap;

                    // Parse position coordinates "X:Y" or top-right etc
                    // For simplified layout, we support absolute offsets if formatted as "X:Y"
                    double x = 0;
                    double y = 0;
                    if (position.Contains(":"))
                    {
                        var parts = position.Split(':');
                        if (parts.Length == 2 && double.TryParse(parts[0], out double px) && double.TryParse(parts[1], out double py))
                        {
                            x = px;
                            y = py;
                        }
                    }

                    Canvas.SetLeft(img, x);
                    Canvas.SetTop(img, y);

                    _overlayCanvas.Children.Add(img);
                    _slotOverlays[slot] = img;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WPF Player] SetOverlayImage error: {ex.Message}");
                }
            }));
        }

        public void RemoveOverlayImage(int slot)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_slotOverlays.TryGetValue(slot, out var img))
                {
                    _overlayCanvas.Children.Remove(img);
                    _slotOverlays.Remove(slot);
                }
            }));
        }

        public void ClearAllOverlays()
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _overlayCanvas.Children.Clear();
                _slotOverlays.Clear();
            }));
        }

        public double GetVideoCurrentTime()
        {
            double pos = 0;
            this.Dispatcher.Invoke(() =>
            {
                if (_mediaElement.Visibility == Visibility.Visible)
                {
                    pos = _mediaElement.Position.TotalSeconds;
                }
            });
            return pos;
        }

        public void StopPlayback()
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _mediaElement.Stop();
                _mediaElement.Source = null;
                _mediaElement.Visibility = Visibility.Collapsed;
                _backgroundImage.Visibility = Visibility.Collapsed;
                _backgroundImage.Source = null;
                _logoImage.Visibility = Visibility.Collapsed;
                _logoImage.Source = null;
                ClearLayout();
            }));
        }

        public void ShowOSDText(string text, int durationMs)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _osdText.Text = text;
                _osdText.Visibility = Visibility.Visible;

                _osdTimer?.Stop();
                _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
                _osdTimer.Tick += (s, e) =>
                {
                    _osdText.Visibility = Visibility.Collapsed;
                    _osdTimer.Stop();
                };
                _osdTimer.Start();
            }));
        }
    }
}
