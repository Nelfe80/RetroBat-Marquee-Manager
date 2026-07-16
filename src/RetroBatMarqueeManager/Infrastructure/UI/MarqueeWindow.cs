using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Infrastructure.Rendering.Skia;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Panel = System.Windows.Controls.Panel;
using Orientation = System.Windows.Controls.Orientation;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;

namespace RetroBatMarqueeManager.Infrastructure.UI
{
    public class MarqueeWindow : Window
    {
        private readonly ILogger _logger;
        private readonly int _targetScreen;
        private readonly Core.Interfaces.TargetBounds? _bounds;

        // UI Layers
        private Grid _mainGrid = null!;
        private Image _backgroundImage = null!;
        private MediaElement _mediaElement = null!;
        private Viewbox _layViewbox = null!;
        private Canvas _layCanvas = null!;

        // Lighting Engine Layer (Skia) — sits above legacy image/video/.lay, below overlays
        private readonly LightingSurfaceOptions? _lightingOptions;
        private WpfSkiaSurfaceHost? _lightingHost;
        private Application.Lighting.MarqueeLightingRenderer? _lightingRenderer;

        // DMD mirror: the lighting frame downscaled to the physical DMD
        private readonly Core.Interfaces.IDmdService? _dmdMirror;
        private readonly int _dmdWidth;
        private readonly int _dmdHeight;
        /// <summary>The dynamic surface this window renders (null on legacy paths
        /// that never went through GetSurfaces — tests, tooling).</summary>
        private readonly Core.Surfaces.SurfaceDefinition? _surface;
        private ComponentHost? _componentHost;

        /// <summary>Media kinds of the current selection → the dynamic components.</summary>
        public void UpdateComponentMedia(IReadOnlyDictionary<string, string?> kinds)
            => Dispatcher.BeginInvoke(new Action(() => _componentHost?.ApplyMedia(kinds)));

        /// <summary>Selection meta (name/year/developer/publisher/system) → text.meta.</summary>
        public void UpdateComponentMeta(IReadOnlyDictionary<string, string> meta)
            => Dispatcher.BeginInvoke(new Action(() => _componentHost?.ApplyMeta(meta)));

        /// <summary>Direct feed of one component type (instruction cards…).</summary>
        public void SetComponentSource(string type, string? path)
            => Dispatcher.BeginInvoke(new Action(() => _componentHost?.SetSource(type, path)));

        public bool HasSurfaceComponent(string type) => _surface?.HasComponent(type) == true;
        private SkiaSharp.SKBitmap? _dmdSmall;
        private byte[]? _dmdBuffer;
        private long _dmdLastPushMs;
        private bool _dmdMirrorActive;
        private volatile bool _layDmdActive;

        /// <summary>The .lay DMD pipeline owns the panel; the mirror pauses without clearing it.</summary>
        public void SetLayDmdActive(bool active) => _layDmdActive = active;
        private readonly System.Diagnostics.Stopwatch _dmdClock = System.Diagnostics.Stopwatch.StartNew();

        // Logo Composition Layer
        private Image _logoImage = null!;
        private TranslateTransform _logoTranslate = null!;
        private ScaleTransform _logoScale = null!;

        // Custom Overlay Slot Layer
        private Canvas _overlayCanvas = null!;
        private readonly Dictionary<int, Image> _slotOverlays = new();

        // Badge Tray Layer (achievement badges at bottom)
        private StackPanel _badgeTrayPanel = null!;
        private readonly Dictionary<int, (Border Container, Image Img, TranslateTransform Transform)> _badgeSlots = new();
        private const int BadgeSize = 64;
        private const int BadgeSpacing = 3;
        private const int BadgeLockedOffsetY = 50; // locked badges: 14px peeking from bottom
        private readonly List<DispatcherTimer> _badgeAnimTimers = new();

        // Speedrun persistent overlay — kept alive across scroll ticks, only Text properties updated
        private FrameworkElement? _speedrunContainer;
        private Grid? _speedrunTimeGrid;
        private Grid? _speedrunLeaderboardIdGrid;
        private Grid? _speedrunLeaderboardTitleGrid;
        private Grid? _speedrunCurrentRankGrid;
        private Grid? _speedrunRankGrid;
        private Grid? _speedrunUserGrid;
        private Grid? _speedrunUserTimeGrid;
        private Grid? _speedrunTypeGrid;
        private Grid? _speedrunRecordGrid;
        private Grid? _speedrunUserRecordGrid;
        private Border? _speedrunBar;
        private string _speedrunLastUser = string.Empty;
        private string _speedrunLastCurrentRank = string.Empty;
        private string _speedrunLastUserTime = string.Empty;
        private double? _speedrunLastRecord;
        private double? _speedrunLastUserRecord;
        private static readonly System.Windows.Media.FontFamily SpeedrunDigitsFont = LoadSpeedrunFont();
        /// <summary>Speedrun on screen: every other display and effect stays out (focus + fps).</summary>
        private volatile bool _speedrunActive;

        // Typographic info blocks updated in place (fast score/timer refresh must not rebuild)
        private readonly Dictionary<string, (Grid Title, Grid Big, Grid? Small, int Parts)> _typoLive = new(StringComparer.OrdinalIgnoreCase);

        // Information Panel
        private UniformGrid _informationPanel = null!;
        private TranslateTransform _informationPanelSlide = null!;
        private TranslateTransform _badgeTraySlide = null!;
        private readonly Dictionary<string, FrameworkElement> _informationOverlays = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DispatcherTimer> _informationTimers = new(StringComparer.OrdinalIgnoreCase);

        // Achievement Takeover
        private bool _takeoverActive;
        private readonly Queue<Action> _takeoverQueue = new();

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

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly Lazy<System.Drawing.Text.PrivateFontCollection?> SpeedrunFontCollection = new(LoadSpeedrunFontCollection);

        public MarqueeWindow(int screenNumber, ILogger logger, LightingSurfaceOptions? lightingOptions = null, Core.Interfaces.TargetBounds? bounds = null,
            Core.Interfaces.IDmdService? dmdMirror = null, int dmdWidth = 128, int dmdHeight = 32,
            Core.Surfaces.SurfaceDefinition? surface = null)
        {
            _targetScreen = screenNumber;
            _logger = logger;
            _lightingOptions = lightingOptions;
            _bounds = bounds;
            _dmdMirror = dmdMirror;
            _dmdWidth = dmdWidth;
            _dmdHeight = dmdHeight;
            _surface = surface;

            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = Brushes.Black;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Title = "RetroBat Marquee Player";

            InitializeLayers();

            this.SourceInitialized += OnSourceInitialized;
            // Touch is promoted to mouse events by WPF, so a single handler covers
            // both a finger tap and a mouse click (useful to test without a touchscreen).
            this.PreviewMouseLeftButtonUp += OnSurfaceTapped;
        }

        /// <summary>
        /// Tap/click on the surface, as fractions (0..1) of the window. Wired by
        /// MarqueeController for the touch-enabled instruction card.
        /// </summary>
        public event Action<double, double>? SurfaceTapped;

        private void OnSurfaceTapped(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SurfaceTapped == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            var position = e.GetPosition(this);
            SurfaceTapped.Invoke(
                Math.Clamp(position.X / ActualWidth, 0, 1),
                Math.Clamp(position.Y / ActualHeight, 0, 1));
        }

        private static System.Windows.Media.FontFamily LoadSpeedrunFont()
        {
            try
            {
                var embedded = new System.Windows.Media.FontFamily(
                    new Uri("pack://application:,,,/"),
                    "./resources/fonts/#Nokia Cellphone FC");
                if (embedded.FamilyNames.Count > 0)
                {
                    return embedded;
                }

                var fontsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "fonts");
                var fontPath = Path.Combine(fontsDirectory, "nokiafc22.ttf");
                if (File.Exists(fontPath))
                {
                    var fontsUri = new Uri(fontsDirectory + Path.DirectorySeparatorChar, UriKind.Absolute);
                    return new System.Windows.Media.FontFamily(fontsUri, "./#Nokia Cellphone FC");
                }
            }
            catch
            {
                // Font loading must never block the marquee startup.
            }
            return new System.Windows.Media.FontFamily("Consolas");
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

            // 3. MAME Layout Layer
            _layCanvas = new Canvas();
            _layViewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Collapsed,
                Child = _layCanvas
            };
            _mainGrid.Children.Add(_layViewbox);

            // 3b. Lighting Engine Layer (Skia rendered surface, replaces static image when ready)
            if (_lightingOptions != null)
            {
                _lightingHost = new WpfSkiaSurfaceHost(_logger, _lightingOptions.FpsLimit, _lightingOptions.ShowFps, _lightingOptions.RenderScale)
                {
                    Visibility = Visibility.Collapsed
                };
                _mainGrid.Children.Add(_lightingHost);
                if (_lightingOptions.TestPattern)
                {
                    this.Loaded += (_, _) =>
                    {
                        _lightingHost.Visibility = Visibility.Visible;
                        _lightingHost.Start(new TestPatternRenderer());
                        _logger.LogInformation("Lighting test pattern active on screen {Screen}", _targetScreen);
                    };
                }
                else
                {
                    // Live mode: layer is always visible but renders transparent until a
                    // scene is ready, so the static image below stays the fallback (§4.5).
                    Infrastructure.Audio.LightingSoundService? sound = null;
                    if (_lightingOptions.SoundEnabled)
                    {
                        sound = new Infrastructure.Audio.LightingSoundService(_logger,
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "sounds"),
                            (float)_lightingOptions.SoundVolume);
                        sound.Start();
                        this.Closed += (_, _) => sound.Dispose();
                    }
                    var lightingDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "lighting");
                    var libraries = Application.Lighting.LightingLibraries.Load(lightingDir, _logger);
                    _lightingRenderer = new Application.Lighting.MarqueeLightingRenderer(_logger, libraries,
                        _lightingOptions.FillHeightMaxCrop, sound, _lightingOptions.GlassReflection,
                        Path.Combine(lightingDir, "neon_fond_transparent.png"), _lightingOptions.TubeVisualOpacity);
                    if (_dmdMirror != null) _lightingHost.FrameRendered = MirrorFrameToDmd;
                    this.Loaded += (_, _) =>
                    {
                        _lightingHost.Visibility = Visibility.Visible;
                        _lightingHost.Start(_lightingRenderer);
                        _logger.LogInformation("Lighting engine layer active on screen {Screen}", _targetScreen);
                    };
                }
                this.Closed += (_, _) => _lightingHost?.Dispose();
            }

            // 4. Logo Layer
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

            // 4b. Dynamic surface components (media blocks, texts, web embeds…)
            if (_surface != null && ComponentHost.IsNeeded(_surface))
            {
                _componentHost = new ComponentHost(_surface, _logger);
                _mainGrid.Children.Add(_componentHost);
            }

            // 5. Custom Overlay Slot Layer
            _overlayCanvas = new Canvas();
            _mainGrid.Children.Add(_overlayCanvas);

            // 6. Badge Tray Layer (below information panel)
            _badgeTrayPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 0)
            };
            _mainGrid.Children.Add(_badgeTrayPanel);

            // 7. Information Panel
            _informationPanel = new UniformGrid
            {
                Rows = 1,
                Columns = 1,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(20)
            };
            _informationPanelSlide = new TranslateTransform();
            _informationPanel.RenderTransform = _informationPanelSlide;
            _badgeTraySlide = new TranslateTransform();
            _badgeTrayPanel.RenderTransform = _badgeTraySlide;
            _mainGrid.Children.Add(_informationPanel);

            // 8. OSD Text Layer
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

                // Optional sub-screen placement: several target windows (marquee, iccard…)
                // can share one physical screen, e.g. a vertical display.
                var left = screen.Bounds.Left;
                var top = screen.Bounds.Top;
                var width = screen.Bounds.Width;
                var height = screen.Bounds.Height;
                if (_bounds != null)
                {
                    left += Math.Clamp(_bounds.X, 0, Math.Max(0, screen.Bounds.Width - 1));
                    top += Math.Clamp(_bounds.Y, 0, Math.Max(0, screen.Bounds.Height - 1));
                    width = Math.Min(_bounds.Width, screen.Bounds.Right - left);
                    height = Math.Min(_bounds.Height, screen.Bounds.Bottom - top);
                }
                _logger.LogInformation($"[WPF Player] Target Screen Index: {screenIndex}. Screen Bounds: {screen.Bounds}. Window: {left},{top} {width}x{height}");

                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                SetWindowPos(
                    helper.Handle,
                    HWND_TOPMOST,
                    left,
                    top,
                    width,
                    height,
                    SWP_SHOWWINDOW
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WPF Player] PositionWindow error: {ex.Message}");
            }
        }

        // --- PUBLIC CONTROL INTERFACE (Thread-safe) ---

        /// <summary>
        /// DMD mirror (render thread): the lighting frame downscaled to the physical
        /// DMD, throttled to spare the USB link. Marquee 4:1 ≈ DMD 128×32 — direct fit.
        /// </summary>
        private void MirrorFrameToDmd(SkiaSharp.SKBitmap front)
        {
            if (_dmdMirror == null) return;
            // a purpose-built .lay DMD view has priority: pause without clearing its frame
            if (_layDmdActive) { _dmdMirrorActive = false; return; }
            if (_lightingRenderer?.HasScene != true)
            {
                if (_dmdMirrorActive)
                {
                    _dmdMirrorActive = false;
                    _dmdMirror.SetLayoutFrame(Array.Empty<byte>());
                }
                return;
            }
            var now = _dmdClock.ElapsedMilliseconds;
            if (now - _dmdLastPushMs < 125) return; // ~8 fps toward the panel
            _dmdLastPushMs = now;

            _dmdSmall ??= new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(
                _dmdWidth, _dmdHeight, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));

            // crop to the artwork zone: a small centered logo still fills the panel
            var art = _lightingRenderer.ArtRect;
            var source = front;
            SkiaSharp.SKBitmap? subset = null;
            if (!art.IsEmpty && (art.Width < front.Width || art.Height < front.Height))
            {
                var clamped = SkiaSharp.SKRectI.Intersect(art, new SkiaSharp.SKRectI(0, 0, front.Width, front.Height));
                if (clamped.Width > 8 && clamped.Height > 8)
                {
                    subset = new SkiaSharp.SKBitmap();
                    if (front.ExtractSubset(subset, clamped)) source = subset;
                    else { subset.Dispose(); subset = null; }
                }
            }
            var scaled = source.ScalePixels(_dmdSmall, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));
            subset?.Dispose();
            if (!scaled) return;

            var count = _dmdWidth * _dmdHeight;
            _dmdBuffer ??= new byte[count * 3];
            var span = _dmdSmall.GetPixelSpan();
            for (var i = 0; i < count; i++)
            {
                _dmdBuffer[i * 3] = span[i * 4 + 2];     // R (source BGRA)
                _dmdBuffer[i * 3 + 1] = span[i * 4 + 1]; // G
                _dmdBuffer[i * 3 + 2] = span[i * 4];     // B
            }
            _dmdMirror.SetLayoutFrame(_dmdBuffer);
            _dmdMirrorActive = true;
        }

        /// <summary>Restart the lighting scene with fresh random ignition scenarios.</summary>
        public void PowerCycleLighting() => _lightingRenderer?.PowerCycle();

        public void SetLightingIngame(bool ingame) => _lightingRenderer?.SetIngame(ingame);

        public void SetLightingOutput(string output, int value) => _lightingRenderer?.SetArcadeOutput(output, value);

        public void TriggerLightingEffect(Application.Lighting.IngameEffectRule rule)
        {
            if (_speedrunActive) return; // clean speedrun session: no light effects
            _lightingRenderer?.TriggerIngameEffect(rule);
        }

        public void DisplayImage(string path, Application.Lighting.LightingSceneMeta? lightingMeta = null)
        {
            _latestImagePath = path;
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_latestImagePath != path) return;
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
                    _lightingRenderer?.SetMarqueeImage(path, lightingMeta);
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
                if (_latestVideoPath != path) return;
                try
                {
                    _backgroundImage.Visibility = Visibility.Collapsed;
                    _layViewbox.Visibility = Visibility.Collapsed;
                    _logoImage.Visibility = Visibility.Collapsed;

                    if (!File.Exists(path)) return;

                    _lightingRenderer?.SetMarqueeImage(null);
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
                    _lightingRenderer?.SetMarqueeImage(null);

                    _layCanvas.Children.Clear();
                    _lampImages.Clear();

                    if (!layout.Views.TryGetValue(viewName, out var view))
                    {
                        foreach (var kvp in layout.Views) { view = kvp.Value; break; }
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

                            var imgControl = new Image { Stretch = Stretch.Fill, Width = viewElem.Width, Height = viewElem.Height };
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(imgPath);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            imgControl.Source = bitmap;

                            Canvas.SetLeft(imgControl, viewElem.X);
                            Canvas.SetTop(imgControl, viewElem.Y);
                            _layCanvas.Children.Add(imgControl);

                            if (!string.IsNullOrEmpty(viewElem.Name))
                            {
                                imgControl.Visibility = Visibility.Collapsed;
                                if (!_lampImages.TryGetValue(viewElem.Name, out var list))
                                    _lampImages[viewElem.Name] = list = new List<Image>();
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
                    foreach (var img in images) img.Visibility = visibility;
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
                if (_backgroundImage.Source != null)
                    _backgroundImage.Visibility = Visibility.Visible;
            }));
        }

        public void SetLogoComposition(string logoPath, double x, double y, double scale)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!File.Exists(logoPath)) { _logoImage.Visibility = Visibility.Collapsed; return; }
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
                catch (Exception ex) { _logger.LogError($"[WPF Player] SetLogoComposition error: {ex.Message}"); }
            }));
        }

        public void SetOverlayImage(int slot, string path, string position = "0:0")
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RemoveOverlayImageCore(slot);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    var img = new Image { Stretch = Stretch.Uniform };
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    img.Source = bitmap;
                    double x = 0, y = 0;
                    if (position.Contains(":"))
                    {
                        var parts = position.Split(':');
                        if (parts.Length == 2 && double.TryParse(parts[0], out double px) && double.TryParse(parts[1], out double py)) { x = px; y = py; }
                    }
                    Canvas.SetLeft(img, x);
                    Canvas.SetTop(img, y);
                    _overlayCanvas.Children.Add(img);
                    _slotOverlays[slot] = img;
                }
                catch (Exception ex) { _logger.LogError($"[WPF Player] SetOverlayImage error: {ex.Message}"); }
            }));
        }

        public void RemoveOverlayImage(int slot)
            => this.Dispatcher.BeginInvoke(new Action(() => RemoveOverlayImageCore(slot)));

        private void RemoveOverlayImageCore(int slot)
        {
            if (_slotOverlays.TryGetValue(slot, out var img))
            {
                _overlayCanvas.Children.Remove(img);
                _slotOverlays.Remove(slot);
            }
        }

        // ─── BADGE TRAY ──────────────────────────────────────────────────────

        public void UpdateBadgeTray(IReadOnlyList<(int Id, string Path, bool Unlocked)> badges)
        {
            // Load bitmaps on a thread-pool thread — disk I/O never blocks the WPF UI thread.
            // Freeze() makes each BitmapImage immutable so it can cross thread boundaries safely.
            _ = Task.Run(() =>
            {
                var preloaded = badges.Select(b =>
                {
                    BitmapImage? bmp = null;
                    try
                    {
                        bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource   = new Uri(b.Path);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                    }
                    catch { bmp = null; }
                    return (b.Id, Bitmap: bmp, b.Unlocked);
                }).ToList();

                this.Dispatcher.BeginInvoke(new Action(() => ApplyBadgeTray(preloaded)));
            });
        }

        private void ApplyBadgeTray(IReadOnlyList<(int Id, BitmapImage? Bitmap, bool Unlocked)> badges)
        {
            try
            {
                double availableWidth = this.ActualWidth > 64 ? this.ActualWidth : 1920;
                int maxBadges = (int)(availableWidth / (BadgeSize + BadgeSpacing * 2));
                var toShow    = badges.Take(maxBadges).ToList();
                var idsToShow = toShow.Select(b => b.Id).ToHashSet();

                foreach (var key in _badgeSlots.Keys.Where(k => !idsToShow.Contains(k)).ToList())
                {
                    _badgeTrayPanel.Children.Remove(_badgeSlots[key].Container);
                    _badgeSlots.Remove(key);
                }

                foreach (var (id, bmp, unlocked) in toShow)
                {
                    if (_badgeSlots.TryGetValue(id, out var slot))
                    {
                        slot.Img.Opacity = unlocked ? 1.0 : 0.2;
                        if (unlocked && slot.Transform.Y > 1)
                            AnimateBadgeUp(slot.Transform);
                    }
                    else
                    {
                        var transform = new TranslateTransform(0, BadgeLockedOffsetY);
                        var img = new Image
                        {
                            Width = BadgeSize, Height = BadgeSize,
                            Stretch = Stretch.Uniform,
                            Opacity = unlocked ? 1.0 : 0.2
                        };
                        if (bmp != null) img.Source = bmp; // already decoded on background thread

                        var container = new Border
                        {
                            Width = BadgeSize, Height = BadgeSize,
                            Margin = new Thickness(BadgeSpacing, 0, BadgeSpacing, 0),
                            RenderTransform = transform,
                            Child = img
                        };
                        _badgeTrayPanel.Children.Add(container);
                        _badgeSlots[id] = (container, img, transform);

                        if (unlocked) AnimateBadgeUp(transform);
                    }
                }
            }
            catch (Exception ex) { _logger.LogError($"[WPF Player] ApplyBadgeTray error: {ex.Message}"); }
        }

        public void ClearBadgeTray()
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var t in _badgeAnimTimers) t.Stop();
                _badgeAnimTimers.Clear();
                _badgeTrayPanel.Children.Clear();
                _badgeSlots.Clear();
            }));
        }

        // ─── SPEEDRUN PERSISTENT OVERLAY ─────────────────────────────────────

        /// <summary>
        /// Creates the speedrun 2×2 overlay on first call, then only updates the
        /// Text properties of the three variable cells (time, rank, user).
        /// Zero WPF object creation after the first frame — eliminates the 36-TextBlock
        /// create/destroy cycle that ran every 100 ms.
        /// </summary>
        public void UpdateSpeedrunDisplay(string title, string detail, string? badgePath,
            double elapsedSeconds = 0, double? recordSeconds = null, double? userRecordSeconds = null, string? currentRank = null,
            int? leaderboardId = null, string? leaderboardTitle = null)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                var (time, rank, user, userTime) = ParseSpeedrunDetail(detail);
                const int MaxUserChars = 11;
                var displayCurrentRank = !string.IsNullOrWhiteSpace(rank)
                    ? rank.Trim()
                    : currentRank?.Trim() ?? string.Empty;
                var displayUser = user.Length > MaxUserChars ? user[..MaxUserChars] + "…" : user;

                if (_speedrunContainer == null) CreateSpeedrunOverlay();

                if (_speedrunTimeGrid != null) SetOutlinedText(_speedrunTimeGrid, time);
                if (_speedrunLeaderboardIdGrid != null)
                    SetOutlinedText(_speedrunLeaderboardIdGrid, leaderboardId is > 0 ? $"LB #{leaderboardId}" : "LB ?");
                if (_speedrunLeaderboardTitleGrid != null)
                    SetOutlinedText(_speedrunLeaderboardTitleGrid,
                        string.IsNullOrWhiteSpace(leaderboardTitle) ? "WAITING FOR LEADERBOARD" : leaderboardTitle.Trim().ToUpperInvariant());
                if (_speedrunTypeGrid != null)
                    SetOutlinedText(_speedrunTypeGrid, string.IsNullOrWhiteSpace(title) ? "LEADERBOARD" : title.Trim().ToUpperInvariant());
                if (_speedrunCurrentRankGrid != null && displayCurrentRank != _speedrunLastCurrentRank)
                {
                    _speedrunLastCurrentRank = displayCurrentRank;
                    SetOutlinedText(_speedrunCurrentRankGrid, "CURRENT " + displayCurrentRank);
                }

                // rotating users: airport split-flap feel — the pair slides in
                // vertically with a motion blur on every change
                var flap = userTime + rank + displayUser;
                if (flap != _speedrunLastUser)
                {
                    _speedrunLastUser = flap;
                    if (_speedrunRankGrid != null) SetOutlinedText(_speedrunRankGrid, rank);
                    if (_speedrunUserGrid != null) SetOutlinedText(_speedrunUserGrid, displayUser);
                    if (_speedrunUserTimeGrid != null)
                    {
                        _speedrunLastUserTime = userTime;
                        SetOutlinedText(_speedrunUserTimeGrid, userTime);
                    }
                }

                // record line + progression bar growing with elapsed time,
                // green → orange → red as the record gets close (ra.lua behavior)
                if (_speedrunRecordGrid != null && recordSeconds != _speedrunLastRecord)
                {
                    _speedrunLastRecord = recordSeconds;
                    SetOutlinedText(_speedrunRecordGrid,
                        recordSeconds is { } rec ? "RECORD " + FormatRaceTime(rec) : string.Empty);
                }
                if (_speedrunUserRecordGrid != null && userRecordSeconds != _speedrunLastUserRecord)
                {
                    _speedrunLastUserRecord = userRecordSeconds;
                    SetOutlinedText(_speedrunUserRecordGrid,
                        userRecordSeconds is { } userRecord ? "USER RECORD " + FormatRaceTime(userRecord) : string.Empty);
                }
                if (_speedrunBar != null)
                {
                    // The personal best is the target; fall back to the leaderboard
                    // record until a personal result has been learned.
                    var scale = userRecordSeconds is > 0 ? userRecordSeconds.Value
                              : recordSeconds is > 0 ? recordSeconds.Value : 0;
                    var fillProgress = scale > 0 ? Math.Clamp(elapsedSeconds / scale, 0, 1) : 0;
                    var full = _speedrunContainer!.ActualWidth > 0 ? _speedrunContainer.ActualWidth : this.ActualWidth;
                    var width = full * fillProgress;
                    _speedrunBar.Width = double.IsFinite(width) ? Math.Max(0, width) : 0;
                    // colour follows the bar's own fill: red only when it reaches the far edge
                    _speedrunBar.Background = new SolidColorBrush(ProgressColor(fillProgress));
                }
            }));
        }

        private static Color ProgressColor(double progress)
        {
            static byte Lerp(byte from, byte to, double p) => (byte)(from + (to - from) * Math.Clamp(p, 0, 1));
            if (progress < 0.5)
                return Color.FromRgb(Lerp(0x00, 0xFF, progress / 0.5), Lerp(0xE0, 0xA5, progress / 0.5), Lerp(0x50, 0x00, progress / 0.5));
            if (progress < 0.8)
                return Color.FromRgb(0xFF, Lerp(0xA5, 0x20, (progress - 0.5) / 0.3), Lerp(0x00, 0x20, (progress - 0.5) / 0.3));
            return Color.FromRgb(0xFF, 0x20, 0x20);
        }

        private static string FormatRaceTime(double seconds)
        {
            var minutes = (int)(seconds / 60);
            var rest = seconds - minutes * 60;
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutes}:{rest:00.00}");
        }

        /// <summary>
        /// Full-surface speedrun scene (mix of the original ra.lua and the rotating
        /// leaderboard): dark veil, growing progression bar behind, GIANT chrono,
        /// rotating rank/user pair (split-flap), record on the right.
        /// </summary>
        private void CreateSpeedrunOverlay()
        {
            var root = new Grid
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Panel.SetZIndex(root, 150);

            root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) });

            _speedrunBar = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = 0,
                Opacity = 0.45,
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0xE0, 0x50))
            };
            root.Children.Add(_speedrunBar);

            var leaderboardHeader = new Grid
            {
                Margin = new Thickness(18, 10, 18, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            leaderboardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            leaderboardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _speedrunLeaderboardIdGrid = CreateBitmapOutlinedText("LB ?", 22, Brushes.Gold, 2);
            _speedrunLeaderboardIdGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            Grid.SetColumn(_speedrunLeaderboardIdGrid, 0);
            leaderboardHeader.Children.Add(_speedrunLeaderboardIdGrid);
            _speedrunCurrentRankGrid = CreateBitmapOutlinedText("CURRENT #0001", 22, Brushes.DeepSkyBlue, 2);
            _speedrunCurrentRankGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            Grid.SetColumn(_speedrunCurrentRankGrid, 1);
            leaderboardHeader.Children.Add(_speedrunCurrentRankGrid);
            Panel.SetZIndex(leaderboardHeader, 2);
            root.Children.Add(leaderboardHeader);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // giant chrono filling the surface — outline stays tiny: each outline
            // level squares the TextBlock count and this text refreshes 10x/s
            _speedrunTimeGrid = CreateBitmapOutlinedText("0:00.00", 112, Brushes.White, 2);
            var chronoBox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(24, 0, 24, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _speedrunTimeGrid
            };
            _speedrunLeaderboardTitleGrid = CreateBitmapOutlinedText("WAITING FOR LEADERBOARD", 22, Brushes.White, 2);
            _speedrunLeaderboardTitleGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            var chronoStack = new Grid
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            chronoStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            chronoStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(chronoBox, 0);
            Grid.SetRow(_speedrunLeaderboardTitleGrid, 1);
            chronoStack.Children.Add(chronoBox);
            chronoStack.Children.Add(_speedrunLeaderboardTitleGrid);
            Grid.SetRow(chronoStack, 0);
            layout.Children.Add(chronoStack);

            // bottom line: rotating rank+user (split-flap) | SPEEDRUN | record
            var bottom = new Grid { Margin = new Thickness(22, 0, 22, 10) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _speedrunUserTimeGrid = CreateBitmapOutlinedText(string.Empty, 30, Brushes.DeepSkyBlue, 2);
            _speedrunRankGrid = CreateBitmapOutlinedText("#0001", 30, Brushes.Gold, 2);
            _speedrunUserGrid = CreateBitmapOutlinedText("PLAYER", 30, Brushes.White, 2);
            var flapStack = new StackPanel { Orientation = Orientation.Horizontal };
            flapStack.Children.Add(_speedrunUserTimeGrid);
            flapStack.Children.Add(new TextBlock { Text = "  ", FontSize = 20, FontFamily = SpeedrunDigitsFont });
            flapStack.Children.Add(_speedrunRankGrid);
            flapStack.Children.Add(new TextBlock { Text = "  ", FontSize = 20, FontFamily = SpeedrunDigitsFont });
            flapStack.Children.Add(_speedrunUserGrid);
            var flapHost = new Border
            {
                ClipToBounds = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Child = flapStack
            };
            Grid.SetColumn(flapHost, 0);
            bottom.Children.Add(flapHost);

            _speedrunTypeGrid = CreateOutlinedText("SPEEDRUN", 22, Brushes.DeepSkyBlue, 2);
            _speedrunTypeGrid.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(_speedrunTypeGrid, 1);
            bottom.Children.Add(_speedrunTypeGrid);

            _speedrunUserRecordGrid = CreateBitmapOutlinedText(string.Empty, 22, Brushes.Gold, 2);
            _speedrunUserRecordGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            _speedrunRecordGrid = CreateBitmapOutlinedText(string.Empty, 22, Brushes.White, 2);
            _speedrunRecordGrid.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            var records = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            records.Children.Add(_speedrunUserRecordGrid);
            records.Children.Add(_speedrunRecordGrid);
            Grid.SetColumn(records, 2);
            bottom.Children.Add(records);

            Grid.SetRow(bottom, 1);
            layout.Children.Add(bottom);
            root.Children.Add(layout);

            _speedrunContainer = root;
            _speedrunActive = true;
            _mainGrid.Children.Add(root);
            _informationOverlays["ra-leaderboard"] = root;
            UpdateInformationPanelSpeedrunMargin();
        }

        // Updates all TextBlocks (shadow + foreground) inside a CreateOutlinedText Grid.
        private static void SetOutlinedText(Grid grid, string text)
        {
            if (grid.Tag is BitmapTextSpec bitmapText)
            {
                if (grid.Children.OfType<Image>().FirstOrDefault() is { } image)
                {
                    image.Source = RenderBitmapText(text, bitmapText);
                }
                return;
            }
            foreach (var tb in grid.Children.OfType<TextBlock>())
                tb.Text = text;
        }

        private void AnimateBadgeUp(TranslateTransform transform)
        {
            var startY = transform.Y;
            const double targetY = 0.0;
            const double durationMs = 380.0;
            var startTime = DateTime.UtcNow;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _badgeAnimTimers.Add(timer);
            timer.Tick += (_, _) =>
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var progress = Math.Min(1.0, elapsed / durationMs);
                // Ease-out cubic
                var eased = 1.0 - Math.Pow(1.0 - progress, 3);
                transform.Y = startY + (targetY - startY) * eased;
                if (progress >= 1.0)
                {
                    timer.Stop();
                    _badgeAnimTimers.Remove(timer);
                }
            };
            timer.Start();
        }

        // ─── ACHIEVEMENT TAKEOVER ─────────────────────────────────────────────

        public void ShowAchievementTakeover(string title, string detail, int points, string? badgePath, int durationMs)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_speedrunActive) return; // never disturb a running speedrun
                if (_takeoverActive)
                {
                    _takeoverQueue.Enqueue(() => ShowAchievementTakeover(title, detail, points, badgePath, durationMs));
                    return;
                }
                _takeoverActive = true;
                ShowAchievementTakeoverCore(title, detail, points, badgePath, durationMs);
            }));
        }

        /// <summary>
        /// Modern unlock choreography: the live blocks (score/timer/badges) slide
        /// down out of view, the badge banner slides in from the left with staggered
        /// texts, holds, exits to the right, then the blocks slide back up.
        /// </summary>
        private void ShowAchievementTakeoverCore(string title, string detail, int points, string? badgePath, int durationMs)
        {
            const int slideMs = 300;
            const int enterMs = 420;
            const int exitMs = 360;
            var holdMs = Math.Max(1000, durationMs - enterMs - exitMs);

            // 1. live blocks slide down out of the window
            AnimateDouble(_informationPanelSlide, TranslateTransform.YProperty, 0, 220, slideMs, true);
            AnimateDouble(_badgeTraySlide, TranslateTransform.YProperty, 0, 220, slideMs, true);

            // 2. banner: badge + texts
            var bannerContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrWhiteSpace(badgePath) && File.Exists(badgePath))
            {
                try
                {
                    var badgeBmp = new BitmapImage();
                    badgeBmp.BeginInit(); badgeBmp.UriSource = new Uri(badgePath); badgeBmp.CacheOption = BitmapCacheOption.OnLoad; badgeBmp.EndInit();
                    bannerContent.Children.Add(new Image
                    {
                        Source = badgeBmp, Width = 220, Height = 220, Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 30, 0), VerticalAlignment = VerticalAlignment.Center
                    });
                }
                catch { }
            }
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = CreateOutlinedText(title.ToUpperInvariant(), 64, Brushes.Gold, 4);
            var detailBlock = CreateOutlinedText(detail, 36, Brushes.White, 3);
            var ptsBlock = CreateOutlinedText(points > 0 ? $"+{points} pts" : string.Empty, 48, Brushes.LimeGreen, 3);
            titleBlock.Opacity = 0; detailBlock.Opacity = 0; ptsBlock.Opacity = 0;
            textStack.Children.Add(titleBlock);
            textStack.Children.Add(detailBlock);
            if (points > 0) textStack.Children.Add(ptsBlock);
            bannerContent.Children.Add(textStack);

            var bannerSlide = new TranslateTransform(-(this.ActualWidth > 0 ? this.ActualWidth : 2000), 0);
            var banner = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new Thickness(24, 14, 28, 14),
                CornerRadius = new CornerRadius(14),
                Background = new LinearGradientBrush(
                    Color.FromArgb(235, 8, 8, 14),
                    Color.FromArgb(200, 24, 18, 2),
                    new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 200, 60)),
                BorderThickness = new Thickness(1.2),
                RenderTransform = bannerSlide,
                // full-height banner: big badge, big type, scaled to the window
                Child = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    Height = Math.Max(100, this.ActualHeight * 0.80),
                    MaxWidth = Math.Max(300, this.ActualWidth * 0.92),
                    Child = bannerContent
                }
            };
            Panel.SetZIndex(banner, 200);
            _mainGrid.Children.Add(banner);

            // enter from the left, staggered text fades
            AnimateDouble(bannerSlide, TranslateTransform.XProperty, bannerSlide.X, 0, enterMs, false);
            ScheduleDelay(180, () => FadeElement(titleBlock, 0, 1, 220, null));
            ScheduleDelay(420, () => FadeElement(detailBlock, 0, 1, 220, null));
            ScheduleDelay(650, () => FadeElement(ptsBlock, 0, 1, 220, null));

            // hold, exit right, restore the live blocks
            ScheduleDelay(enterMs + holdMs, () =>
            {
                AnimateDouble(bannerSlide, TranslateTransform.XProperty, 0,
                    this.ActualWidth > 0 ? this.ActualWidth : 2000, exitMs, true, () =>
                    {
                        _mainGrid.Children.Remove(banner);
                        AnimateDouble(_informationPanelSlide, TranslateTransform.YProperty, 220, 0, slideMs, false);
                        AnimateDouble(_badgeTraySlide, TranslateTransform.YProperty, 220, 0, slideMs, false);
                        _takeoverActive = false;
                        if (_takeoverQueue.Count > 0) _takeoverQueue.Dequeue()();
                    });
            });
        }

        private void LegacyShowAchievementTakeoverCore(string title, string detail, int points, string? badgePath)
        {
            // Overlay container (full-screen, highest z-index)
            var container = new Grid
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            Panel.SetZIndex(container, 200);

            // Dark background
            var darkBg = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Opacity = 0
            };
            container.Children.Add(darkBg);

            // Content panel
            var content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Opacity = 0
            };

            // Optional background image
            var bgPath = RaResourcePath("background.png");
            if (File.Exists(bgPath))
            {
                try
                {
                    var bgBmp = new BitmapImage();
                    bgBmp.BeginInit(); bgBmp.UriSource = new Uri(bgPath); bgBmp.CacheOption = BitmapCacheOption.OnLoad; bgBmp.EndInit();
                    var bgImg = new Image
                    {
                        Source = bgBmp, Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                        Opacity = 0.5
                    };
                    container.Children.Add(bgImg);
                }
                catch { }
            }

            // Cup image (centered)
            var cupPath = RaResourcePath("biggoldencup.png");
            if (File.Exists(cupPath))
            {
                try
                {
                    var cupBmp = new BitmapImage();
                    cupBmp.BeginInit(); cupBmp.UriSource = new Uri(cupPath); cupBmp.CacheOption = BitmapCacheOption.OnLoad; cupBmp.EndInit();
                    content.Children.Add(new Image
                    {
                        Source = cupBmp, Width = 200, Height = 200,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 16)
                    });
                }
                catch { }
            }

            // Badge image (below cup)
            if (!string.IsNullOrWhiteSpace(badgePath) && File.Exists(badgePath))
            {
                try
                {
                    var badgeBmp = new BitmapImage();
                    badgeBmp.BeginInit(); badgeBmp.UriSource = new Uri(badgePath); badgeBmp.CacheOption = BitmapCacheOption.OnLoad; badgeBmp.EndInit();
                    content.Children.Add(new Image
                    {
                        Source = badgeBmp, Width = 100, Height = 100,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 20)
                    });
                }
                catch { }
            }

            // Three text phases — only title visible initially
            var titleBlock = CreateOutlinedText(title.ToUpperInvariant(), 62, Brushes.Gold, 4);
            var detailBlock = CreateOutlinedText(detail, 34, Brushes.White, 3);
            detailBlock.Opacity = 0;
            var ptsText = points > 0 ? $"+{points} pts" : string.Empty;
            var ptsBlock = CreateOutlinedText(ptsText, 52, Brushes.LimeGreen, 4);
            ptsBlock.Opacity = 0;

            content.Children.Add(titleBlock);
            content.Children.Add(detailBlock);
            content.Children.Add(ptsBlock);
            // scale the whole stack down to the window: on a low marquee band
            // (e.g. 1080x270) the cup + badge + texts must never overflow
            var contentFit = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(12),
                Child = content
            };
            container.Children.Add(contentFit);
            _mainGrid.Children.Add(container);

            const int fadeMs = 320;
            const int holdMs = 1600;

            // Phase 1: dark bg fades in
            FadeElement(darkBg, 0, 1, fadeMs, () =>
            {
                // Phase 2: content (cup + title) fades in
                FadeElement(content, 0, 1, fadeMs, () =>
                {
                    // Phase 3: after hold, cross-fade title → detail
                    ScheduleDelay(holdMs, () =>
                    {
                        FadeElement(titleBlock, 1, 0, fadeMs, () =>
                        {
                            FadeElement(detailBlock, 0, 1, fadeMs, () =>
                            {
                                // Phase 4: after hold, cross-fade detail → pts
                                ScheduleDelay(holdMs, () =>
                                {
                                    FadeElement(detailBlock, 1, 0, fadeMs, () =>
                                    {
                                        if (ptsText.Length > 0)
                                        {
                                            FadeElement(ptsBlock, 0, 1, fadeMs, () =>
                                            {
                                                // Phase 5: hold pts then fade everything out
                                                ScheduleDelay(holdMs + fadeMs, () => EndTakeover(container));
                                            });
                                        }
                                        else
                                        {
                                            EndTakeover(container);
                                        }
                                    });
                                });
                            });
                        });
                    });
                });
            });
        }

        private void EndTakeover(UIElement container)
        {
            FadeElement(container, 1, 0, 400, () =>
            {
                _mainGrid.Children.Remove(container);
                _takeoverActive = false;
                if (_takeoverQueue.Count > 0)
                    _takeoverQueue.Dequeue()();
            });
        }

        // ─── LEADERBOARD RESULT ───────────────────────────────────────────────

        public void ShowLeaderboardResult(string time, string rank, string diff, bool isRecord, int durationMs, string? badgePath = null)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var resolvedBadge = (!string.IsNullOrWhiteSpace(badgePath) && File.Exists(badgePath))
                        ? badgePath
                        : RaResourcePath("biggoldencup.png");
                    var resultDetail = string.Join("  ", new[]
                    {
                        string.IsNullOrWhiteSpace(time) ? "--:--" : time.Trim(),
                        rank?.Trim() ?? string.Empty,
                        diff?.Trim() ?? string.Empty
                    }.Where(part => !string.IsNullOrWhiteSpace(part)));
                    Action showResult = () =>
                    {
                        RemoveInformationOverlayCore("ra-leaderboard");
                        _takeoverActive = true;
                        ShowAchievementTakeoverCore(
                            isRecord ? "NEW RECORD !" : "LEADERBOARD RESULT",
                            resultDetail,
                            0,
                            File.Exists(resolvedBadge) ? resolvedBadge : null,
                            durationMs);
                    };
                    if (_takeoverActive)
                        _takeoverQueue.Enqueue(showResult);
                    else
                        showResult();
                }
                catch (Exception ex) { _logger.LogError($"[WPF Player] ShowLeaderboardResult error: {ex.Message}"); }
            }));
        }

        // ─── ANIMATION HELPERS ────────────────────────────────────────────────

        private void FadeElement(UIElement element, double from, double to, int durationMs, Action? onComplete)
        {
            element.Opacity = from;
            var startTime = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var progress = Math.Min(1.0, elapsed / durationMs);
                element.Opacity = from + (to - from) * progress;
                if (progress >= 1.0) { timer.Stop(); onComplete?.Invoke(); }
            };
            timer.Start();
        }

        private void ScheduleDelay(int delayMs, Action callback)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)) };
            timer.Tick += (_, _) => { timer.Stop(); callback(); };
            timer.Start();
        }

        private static string RaResourcePath(string name)
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "ra", name);

        // ─── INFORMATION OVERLAYS ────────────────────────────────────────────

        public void SetInformationOverlay(string owner, string title, string detail, string? badgePath, bool persistent, int durationMs)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // speedrun on screen: only the leaderboard itself may draw
                if (_speedrunActive && !owner.StartsWith("ra-leaderboard", StringComparison.OrdinalIgnoreCase))
                    return;

                if (owner.StartsWith("hiscore", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveInformationOverlayCore(owner);
                    AddOutlinedMarqueeTextOverlay(owner, title, detail, persistent, durationMs);
                    return;
                }

                if (persistent && IsTypographicInformation(owner))
                {
                    if (IsSpeedrunLeaderboardInformation(owner, title, detail))
                    {
                        RemoveInformationOverlayCore(owner);
                        AddSpeedrunLeaderboardOverlay(owner, detail, badgePath, durationMs);
                        return;
                    }
                    // fast score/timer refresh: updated in place, no rebuild, no jump
                    AddTypographicInformationOverlay(owner, title, detail, badgePath, durationMs);
                    return;
                }
                RemoveInformationOverlayCore(owner);

                var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                if (!string.IsNullOrWhiteSpace(badgePath) && File.Exists(badgePath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.UriSource = new Uri(badgePath); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                        content.Children.Add(new Image { Source = bitmap, Width = 80, Height = 80, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) });
                    }
                    catch (Exception ex) { _logger.LogDebug($"Overlay badge unavailable: {ex.Message}"); }
                }
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = detail, Foreground = Brushes.Gold, FontSize = 20, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(text);
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 10, 10, 10)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 0, 8),
                    MaxWidth = 700,
                    Child = content
                };
                border.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                _informationOverlays[owner] = border;
                InsertInformationSorted(owner, border);
                UpdateInformationGridColumns();
                if (!persistent)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(250, durationMs)) };
                    timer.Tick += (_, _) => RemoveInformationOverlayCore(owner);
                    _informationTimers[owner] = timer;
                    timer.Start();
                }
            }));
        }

        private static bool IsTypographicInformation(string owner)
            => owner.StartsWith("ra", StringComparison.OrdinalIgnoreCase) ||
               owner.StartsWith("live-score", StringComparison.OrdinalIgnoreCase) ||
               owner.StartsWith("live-timer", StringComparison.OrdinalIgnoreCase);

        private static bool IsSpeedrunLeaderboardInformation(string owner, string title, string detail)
            => owner.Equals("ra-leaderboard", StringComparison.OrdinalIgnoreCase) &&
               (title.Contains("SPEEDRUN", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("  #", StringComparison.OrdinalIgnoreCase));

        private void AddSpeedrunLeaderboardOverlay(string owner, string detail, string? badgePath, int durationMs)
        {
            // reuse the full speedrun scene (giant chrono + progression bar)
            UpdateSpeedrunDisplay("SPEEDRUN", detail, badgePath);
            if (durationMs > 0)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(250, durationMs)) };
                timer.Tick += (_, _) => RemoveInformationOverlayCore(owner);
                _informationTimers[owner] = timer;
                timer.Start();
            }
        }

        private static (string Time, string Rank, string User, string UserTime) ParseSpeedrunDetail(string detail)
        {
            var parts = (detail ?? string.Empty).Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var time = parts.Length > 0 ? parts[0] : "00:00.00";
            var rank = string.Empty;
            var user = string.Empty;
            var userTime = string.Empty;
            if (parts.Length > 1)
            {
                var reference = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (reference.Length > 0) rank = reference[0];
                if (reference.Length > 1)
                {
                    var tokens = reference.Skip(1).ToList();
                    if (tokens.Count > 0 && IsRaceTimeToken(tokens[^1]))
                    {
                        userTime = tokens[^1];
                        tokens.RemoveAt(tokens.Count - 1);
                    }
                    user = tokens.Count > 0 ? string.Join(' ', tokens) : string.Empty;
                }
            }
            return (time, rank, user, userTime);
        }

        private static bool IsRaceTimeToken(string value)
            => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, @"^\d{1,3}:\d{2}(?:\.\d{1,3})?$");

        private void AddTypographicInformationOverlay(string owner, string title, string detail, string? badgePath, int durationMs)
        {
            var titleText = (title ?? string.Empty).Trim();
            var detailText = (detail ?? string.Empty).Trim();
            var accent = ResolveInformationAccent(owner, titleText, detailText);
            var detailParts = detailText.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parts = detailParts.Length >= 2 ? 2 : 1;

            // in-place update: rapid score/timer changes must not rebuild the block
            if (_informationOverlays.ContainsKey(owner) &&
                _typoLive.TryGetValue(owner, out var live) && live.Parts == parts)
            {
                SetOutlinedText(live.Title, titleText);
                if (parts == 2)
                {
                    SetOutlinedText(live.Big, detailParts[0]);
                    if (live.Small != null) SetOutlinedText(live.Small, detailParts[1]);
                }
                else SetOutlinedText(live.Big, detailText);
                RestartInformationTimer(owner, durationMs);
                return;
            }
            RemoveInformationOverlayCore(owner);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var textStack = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            Grid.SetColumn(textStack, 1);

            if (!string.IsNullOrWhiteSpace(badgePath) && File.Exists(badgePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.UriSource = new Uri(badgePath); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                    var badge = new Image { Source = bitmap, Width = 86, Height = 86, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(badge, 0);
                    content.Children.Add(badge);
                }
                catch (Exception ex) { _logger.LogDebug($"Overlay badge unavailable: {ex.Message}"); }
            }

            var titleGrid = CreateOutlinedText(titleText, 26, Brushes.White, 2);
            textStack.Children.Add(titleGrid);
            Grid bigGrid;
            Grid? smallGrid = null;
            if (parts == 2)
            {
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                bigGrid = CreateOutlinedText(detailParts[0], 58, accent, 4);
                smallGrid = CreateOutlinedText(detailParts[1], 34, Brushes.White, 3);
                row.Children.Add(bigGrid);
                row.Children.Add(new TextBlock { Text = "  ", FontSize = 20 });
                row.Children.Add(smallGrid);
                textStack.Children.Add(row);
            }
            else
            {
                bigGrid = CreateOutlinedText(detailText, 52, accent, 4);
                textStack.Children.Add(bigGrid);
            }
            _typoLive[owner] = (titleGrid, bigGrid, smallGrid, parts);
            content.Children.Add(textStack);

            content.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            var container = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
                Padding = new Thickness(18, 8, 18, 10),
                CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(
                    Color.FromArgb(155, 0, 0, 0),
                    Color.FromArgb(45, 0, 0, 0),
                    new System.Windows.Point(0.5, 0),
                    new System.Windows.Point(0.5, 1)),
                Child = content
            };

            _informationOverlays[owner] = container;
            InsertInformationSorted(owner, container);
            UpdateInformationGridColumns();
            if (durationMs > 0)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(250, durationMs)) };
                timer.Tick += (_, _) => RemoveInformationOverlayCore(owner);
                _informationTimers[owner] = timer;
                timer.Start();
            }
        }

        private void RestartInformationTimer(string owner, int durationMs)
        {
            if (_informationTimers.Remove(owner, out var existing)) existing.Stop();
            if (durationMs <= 0) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(250, durationMs)) };
            timer.Tick += (_, _) => RemoveInformationOverlayCore(owner);
            _informationTimers[owner] = timer;
            timer.Start();
        }

        private static System.Windows.Media.Brush ResolveInformationAccent(string owner, string title, string detail)
        {
            if (detail.Contains("HARDCORE", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("LEADERBOARDS", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("SPEEDRUN", StringComparison.OrdinalIgnoreCase))
                return Brushes.DeepSkyBlue;
            if (detail.Contains("SOFTCORE", StringComparison.OrdinalIgnoreCase))
                return Brushes.LightGray;
            if (owner.StartsWith("live-timer", StringComparison.OrdinalIgnoreCase))
                return Brushes.Cyan;
            return Brushes.Gold;
        }

        private void AddOutlinedMarqueeTextOverlay(string owner, string title, string detail, bool persistent, int durationMs)
        {
            var titleText = string.IsNullOrWhiteSpace(title) ? "HIGH SCORE" : title.Trim();
            var detailText = (detail ?? string.Empty).Trim();
            var container = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(24),
                Padding = new Thickness(22, 10, 22, 12),
                MaxWidth = 1200,
                Background = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)),
                CornerRadius = new CornerRadius(10),
                Child = new StackPanel
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Children =
                    {
                        CreateOutlinedText(titleText, 28, Brushes.White, 2),
                        CreateOutlinedText(detailText, 44, Brushes.Gold, 3)
                    }
                }
            };

            _mainGrid.Children.Add(container);
            _informationOverlays[owner] = container;
            if (!persistent)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(250, durationMs)) };
                timer.Tick += (_, _) => RemoveInformationOverlayCore(owner);
                _informationTimers[owner] = timer;
                timer.Start();
            }
        }

        // textAlignment and horizontalAlignment control how text sits within its parent column.
        // Use Stretch + Right/Left to make the grid fill its column (prevents apparent size variation
        // when the same font renders shorter vs longer strings in a proportional layout).
        private static Grid CreateBitmapOutlinedText(string text, double fontSize, System.Windows.Media.Brush foreground, int outline)
        {
            var spec = new BitmapTextSpec(fontSize, outline, ToDrawingColor(foreground));
            var image = new Image
            {
                Source = RenderBitmapText(text, spec),
                Stretch = Stretch.None,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            return new Grid
            {
                Tag = spec,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Children = { image }
            };
        }

        private static System.Drawing.Text.PrivateFontCollection? LoadSpeedrunFontCollection()
        {
            try
            {
                var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "fonts", "nokiafc22.ttf");
                if (!File.Exists(fontPath)) return null;
                var collection = new System.Drawing.Text.PrivateFontCollection();
                collection.AddFontFile(fontPath);
                return collection.Families.Length > 0 ? collection : null;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource RenderBitmapText(string text, BitmapTextSpec spec)
        {
            var families = SpeedrunFontCollection.Value?.Families;
            var family = families is { Length: > 0 }
                ? families[0]
                : System.Drawing.FontFamily.GenericMonospace;
            var displayText = string.IsNullOrEmpty(text) ? " " : text;
            using var font = new System.Drawing.Font(family, (float)spec.FontSize, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            using var measureBitmap = new System.Drawing.Bitmap(1, 1);
            using var measureGraphics = System.Drawing.Graphics.FromImage(measureBitmap);
            measureGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            var size = measureGraphics.MeasureString(displayText, font, int.MaxValue, System.Drawing.StringFormat.GenericTypographic);
            var padding = Math.Max(2, spec.Outline + 2);
            var width = Math.Max(1, (int)Math.Ceiling(size.Width) + padding * 2);
            var height = Math.Max(1, (int)Math.Ceiling(size.Height) + padding * 2);

            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                using var black = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
                using var brush = new System.Drawing.SolidBrush(spec.Foreground);
                var origin = new System.Drawing.PointF(padding, padding);
                if (!string.IsNullOrEmpty(text))
                {
                    for (var x = -spec.Outline; x <= spec.Outline; x++)
                    for (var y = -spec.Outline; y <= spec.Outline; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        graphics.DrawString(displayText, font, black, new System.Drawing.PointF(origin.X + x, origin.Y + y), System.Drawing.StringFormat.GenericTypographic);
                    }
                    graphics.DrawString(displayText, font, brush, origin, System.Drawing.StringFormat.GenericTypographic);
                }
            }

            var handle = bitmap.GetHbitmap(System.Drawing.Color.Transparent);
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    handle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Brush brush)
        {
            if (brush is SolidColorBrush solid)
            {
                var color = solid.Color;
                return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
            }
            return System.Drawing.Color.White;
        }

        private static Grid CreateOutlinedText(string text, double fontSize, System.Windows.Media.Brush foreground, int outline,
            TextAlignment textAlignment = TextAlignment.Center,
            System.Windows.HorizontalAlignment horizontalAlignment = System.Windows.HorizontalAlignment.Center,
            System.Windows.Media.FontFamily? fontFamily = null,
            FontWeight? fontWeight = null)
        {
            var grid = new Grid { HorizontalAlignment = horizontalAlignment };
            var weight = fontWeight ?? FontWeights.Black;
            for (var x = -outline; x <= outline; x++)
            for (var y = -outline; y <= outline; y++)
            {
                if (x == 0 && y == 0) continue;
                grid.Children.Add(new TextBlock
                {
                    Text = text, Foreground = Brushes.Black, FontSize = fontSize,
                    FontFamily = fontFamily ?? System.Windows.SystemFonts.MessageFontFamily,
                    FontWeight = weight, TextAlignment = textAlignment,
                    TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(x, y, -x, -y)
                });
            }
            grid.Children.Add(new TextBlock
            {
                Text = text, Foreground = foreground, FontSize = fontSize,
                FontFamily = fontFamily ?? System.Windows.SystemFonts.MessageFontFamily,
                FontWeight = weight, TextAlignment = textAlignment,
                TextWrapping = TextWrapping.NoWrap
            });
            return grid;
        }

        private sealed record BitmapTextSpec(double FontSize, int Outline, System.Drawing.Color Foreground);

        public void RemoveInformationOverlay(string owner)
            => Dispatcher.BeginInvoke(new Action(() => RemoveInformationOverlayCore(owner)));

        /// <summary>
        /// Stable column slot per owner: score and timer never swap places when one
        /// of them re-registers during the game.
        /// </summary>
        private static int InformationSlot(string owner)
        {
            var key = owner.ToLowerInvariant();
            if (key.StartsWith("live-score")) return 0;
            if (key.StartsWith("live-timer")) return 1;
            if (key.StartsWith("ra-score") || key == "ra") return 2;
            if (key.StartsWith("ra-")) return 3;
            if (key.StartsWith("hiscore")) return 4;
            return 5;
        }

        /// <summary>Insert at the owner's stable slot, with a small entrance animation.</summary>
        private void InsertInformationSorted(string owner, FrameworkElement element)
        {
            var slot = InformationSlot(owner);
            var index = 0;
            foreach (FrameworkElement child in _informationPanel.Children)
            {
                var childOwner = _informationOverlays.FirstOrDefault(pair => ReferenceEquals(pair.Value, child)).Key;
                if (childOwner != null && InformationSlot(childOwner) <= slot) index++;
                else break;
            }
            _informationPanel.Children.Insert(Math.Min(index, _informationPanel.Children.Count), element);

            // modern entrance: rise + fade in
            var rise = new TranslateTransform(0, 16);
            element.RenderTransform = rise;
            element.Opacity = 0;
            AnimateDouble(element, UIElement.OpacityProperty, 0, 1, 200, false);
            AnimateDouble(rise, TranslateTransform.YProperty, 16, 0, 220, false);
        }

        private static void AnimateDouble(System.Windows.Media.Animation.IAnimatable target,
            DependencyProperty property, double from, double to, int durationMs, bool easeIn,
            Action? completed = null)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = easeIn ? System.Windows.Media.Animation.EasingMode.EaseIn
                                        : System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            if (completed != null) animation.Completed += (_, _) => completed();
            target.BeginAnimation(property, animation);
        }

        private void RemoveInformationOverlayCore(string owner)
        {
            if (_informationTimers.Remove(owner, out var timer)) timer.Stop();
            if (_informationOverlays.Remove(owner, out var element)) RemoveElementFromParent(element);
            // Clear cached speedrun references so UpdateSpeedrunDisplay recreates on next call
            if (owner.Equals("ra-leaderboard", StringComparison.OrdinalIgnoreCase)) ResetSpeedrunCache();
            _typoLive.Remove(owner);
            UpdateInformationGridColumns();
            UpdateInformationPanelSpeedrunMargin();
        }

        private void ResetSpeedrunCache()
        {
            _speedrunActive = false;
            _speedrunContainer = null;
            _speedrunTimeGrid = null;
            _speedrunLeaderboardIdGrid = null;
            _speedrunLeaderboardTitleGrid = null;
            _speedrunCurrentRankGrid = null;
            _speedrunRankGrid = null;
            _speedrunUserGrid = null;
            _speedrunUserTimeGrid = null;
            _speedrunTypeGrid = null;
            _speedrunRecordGrid = null;
            _speedrunUserRecordGrid = null;
            _speedrunBar = null;
            _speedrunLastUser = string.Empty;
            _speedrunLastCurrentRank = string.Empty;
            _speedrunLastUserTime = string.Empty;
            _speedrunLastRecord = null;
            _speedrunLastUserRecord = null;
        }

        private void RemoveElementFromParent(FrameworkElement element)
        {
            if (element.Parent is System.Windows.Controls.Panel panel) panel.Children.Remove(element);
        }

        public void ClearAllOverlays()
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _overlayCanvas.Children.Clear();
                _slotOverlays.Clear();
                foreach (var timer in _informationTimers.Values) timer.Stop();
                _informationTimers.Clear();
                foreach (var element in _informationOverlays.Values.ToArray()) RemoveElementFromParent(element);
                _informationOverlays.Clear();
                _informationPanel.Children.Clear();
                _typoLive.Clear();
                ResetSpeedrunCache();
                UpdateInformationGridColumns();
                UpdateInformationPanelSpeedrunMargin();
            }));
        }

        private void UpdateInformationGridColumns()
        {
            if (_informationPanel == null) return;
            _informationPanel.Columns = Math.Max(1, _informationPanel.Children.Count);
        }

        private void UpdateInformationPanelSpeedrunMargin()
        {
            if (_informationPanel == null) return;
            var hasSpeedrun = _informationOverlays.ContainsKey("ra-leaderboard");
            _informationPanel.Visibility = hasSpeedrun ? Visibility.Collapsed : Visibility.Visible;
            _informationPanel.Margin = hasSpeedrun ? new Thickness(20, 20, 20, 128) : new Thickness(20);
        }

        public double GetVideoCurrentTime()
        {
            double pos = 0;
            this.Dispatcher.Invoke(() =>
            {
                if (_mediaElement.Visibility == Visibility.Visible)
                    pos = _mediaElement.Position.TotalSeconds;
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
                _lightingRenderer?.SetMarqueeImage(null);
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
                _osdTimer.Tick += (s, e) => { _osdText.Visibility = Visibility.Collapsed; _osdTimer.Stop(); };
                _osdTimer.Start();
            }));
        }
    }
}
