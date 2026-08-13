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
        // Animated ingame events: own renderer, own host, mounted ABOVE the media
        // stack (docs\DECOUPLAGE-MOTEUR-EVENEMENTS.md).
        private readonly LightingSurfaceOptions? _effectsOptions;
        private WpfSkiaSurfaceHost? _effectsHost;
        private Application.Lighting.IngameEffectsRenderer? _effectsRenderer;
        private Application.Lighting.MarqueeLightingRenderer? _lightingRenderer;

        // DMD mirror: the lighting frame downscaled to the physical DMD
        private readonly Core.Interfaces.IDmdService? _dmdMirror;
        private readonly int _dmdWidth;
        private readonly int _dmdHeight;
        /// <summary>The dynamic surface this window renders (null on legacy paths
        /// that never went through GetSurfaces — tests, tooling).</summary>
        private readonly Core.Surfaces.SurfaceDefinition? _surface;
        // The flux background layer (_backgroundImage) is only shown when the composition
        // includes a visible media.flux component. A surface built without it (e.g. a topper
        // carrying only score/leaderboard overlays) no longer shows the fanart/marquee flux.
        private readonly bool _fluxBackgroundEnabled;
        // One host per RUN of dynamic components: an engine declared between two of
        // them closes the run and takes its own slot, so the declared z-order holds.
        private readonly List<ComponentHost> _componentHosts = new();

        /// <summary>Media kinds of the current selection → the dynamic components.</summary>
        public void UpdateComponentMedia(IReadOnlyDictionary<string, string?> kinds)
            => Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var host in _componentHosts) host.ApplyMedia(kinds);
                UpdateGameAccent(kinds); // refresh the game colour for hiscore boards tinted "auto"
            }));

        /// <summary>Selection meta (name/year/developer/publisher/system) → text.meta.</summary>
        public void UpdateComponentMeta(IReadOnlyDictionary<string, string> meta)
            => Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var host in _componentHosts) host.ApplyMeta(meta);
            }));

        /// <summary>Direct feed of one component type (instruction cards…).</summary>
        public void SetComponentSource(string type, string? path)
            => Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var host in _componentHosts) host.SetSource(type, path);
            }));

        public bool HasSurfaceComponent(string type) => _surface?.HasComponent(type) == true;

        private string _activeScene = "navigation";
        private FrameworkElement? _mediaEffectOverlay;

        /// <summary>User effect media (webm via MediaElement, animated gif via
        /// decoded frames) played once: overlay over the composed marquee, or a
        /// temporary fullscreen takeover. Removed after durationMs (min 500 ms).</summary>
        public void PlayMediaEffect(string path, bool fullscreen, int durationMs)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_mediaEffectOverlay != null)
                    {
                        (_mediaEffectOverlay as MediaElement)?.Stop();
                        _mainGrid.Children.Remove(_mediaEffectOverlay);
                        _mediaEffectOverlay = null;
                    }
                    if (!File.Exists(path)) return;

                    FrameworkElement overlay;
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is ".gif" or ".apng" or ".png")
                    {
                        var frames = DecodeGifFrames(path);
                        if (frames.Count == 0) return;
                        var image = new Image { Source = frames[0], Stretch = fullscreen ? Stretch.UniformToFill : Stretch.Uniform };
                        if (frames.Count > 1)
                        {
                            var index = 0;
                            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
                            timer.Tick += (_, _) =>
                            {
                                index = (index + 1) % frames.Count;
                                image.Source = frames[index];
                            };
                            timer.Start();
                            image.Unloaded += (_, _) => timer.Stop();
                        }
                        overlay = image;
                    }
                    else
                    {
                        var media = new MediaElement
                        {
                            LoadedBehavior = MediaState.Manual,
                            UnloadedBehavior = MediaState.Manual,
                            Stretch = fullscreen ? Stretch.UniformToFill : Stretch.Uniform,
                            IsMuted = true,
                            Source = new Uri(path)
                        };
                        media.Play();
                        overlay = media;
                    }

                    overlay.IsHitTestVisible = false;
                    if (fullscreen)
                    {
                        overlay.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                        overlay.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                    }
                    _mediaEffectOverlay = overlay;
                    _mainGrid.Children.Add(overlay); // topmost layer: over the composed marquee

                    var lifetime = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(Math.Max(500, durationMs))
                    };
                    lifetime.Tick += (_, _) =>
                    {
                        lifetime.Stop();
                        if (_mediaEffectOverlay == overlay)
                        {
                            (overlay as MediaElement)?.Stop();
                            _mainGrid.Children.Remove(overlay);
                            _mediaEffectOverlay = null;
                        }
                    };
                    lifetime.Start();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Media effect failed for {Path}: {Message}", path, ex.Message);
                }
            }));
        }

        private static List<System.Windows.Media.Imaging.BitmapSource> DecodeGifFrames(string path)
        {
            var frames = new List<System.Windows.Media.Imaging.BitmapSource>();
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                foreach (var frame in decoder.Frames)
                {
                    frame.Freeze();
                    frames.Add(frame);
                }
            }
            catch
            {
                // undecodable: no overlay
            }
            return frames;
        }

        /// <summary>Display state switch (navigation ↔ ingame): the dynamic
        /// components filter on their `when`, the rich overlays gate through
        /// <see cref="IsComponentActive"/>, and a surface scoped to one state
        /// hides its WHOLE window in the other (e.g. nothing over ES while
        /// browsing when the surface is ingame-only).
        ///
        /// The built-in Skia layers are scoped here too (§4e): they used to keep
        /// painting out of their state while silently refusing the events routed to
        /// them — a `lighting.engine` scoped `navigation` still lit the surface during
        /// play. Visible and addressable now agree.</summary>
        public void SetDisplayScene(string scene)
        {
            _activeScene = scene;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var host in _componentHosts) host.ApplyScene(scene);
                ApplyDynamicSuppression(); // the flattened run differs per state
                ScopeLayer(_lightingHost, "lighting.engine");
                ScopeLayer(_effectsHost, "effects.engine");
                ScopeBuiltInOverlays();
                if (_surface != null && !_surface.When.Equals("both", StringComparison.OrdinalIgnoreCase))
                {
                    var active = _surface.ActiveIn(scene);
                    if (active && Visibility != Visibility.Visible) Show();
                    else if (!active && Visibility == Visibility.Visible) Hide();
                }
            }));
        }

        /// <summary>
        /// Mounts the surface's layers in the DECLARED order, so the composition
        /// editor's up/down arrows finally mean something for the engines too. Before
        /// this, every built-in sat at a hardcoded depth: a fullscreen `media.fanart`
        /// always won over the lighting engine and over the sprites, whatever the user
        /// had ordered — the root cause of both bugs of this session.
        ///
        /// A run of dynamic components becomes one ComponentHost; an engine closes the
        /// current run and takes its own slot. An engine declared twice (one per
        /// display state, a legitimate composition) has a single host: it is mounted at
        /// its FIRST declared position.
        ///
        /// Out of scope on purpose: the flux background, and the overlays (score, RA,
        /// hiscore, OSD) which stay on their fixed upper slots — they are readability
        /// panels, not part of the artwork stack.
        /// </summary>
        private void BuildOrderedSurfaceLayers()
        {
            if (_surface == null) return;
            var run = new List<Core.Surfaces.ComponentDefinition>();

            void FlushRun()
            {
                if (run.Count == 0) return;
                var slice = _surface with { Components = run.ToList() };
                run.Clear();
                if (!ComponentHost.IsNeeded(slice)) return; // built-ins only: nothing to host
                var host = new ComponentHost(slice, _logger);
                _componentHosts.Add(host);
                _mainGrid.Children.Add(host);
            }

            void Mount(WpfSkiaSurfaceHost? host)
            {
                FlushRun();
                if (host != null && !_mainGrid.Children.Contains(host)) _mainGrid.Children.Add(host);
            }

            foreach (var component in _surface.Components)
            {
                if (component.Type.Equals("lighting.engine", StringComparison.OrdinalIgnoreCase)) Mount(_lightingHost);
                else if (component.Type.Equals("effects.engine", StringComparison.OrdinalIgnoreCase)) Mount(_effectsHost);
                else run.Add(component);
            }
            FlushRun();

            // an engine the surface never declares still gets mounted (legacy safety)
            if (_lightingHost != null && !_mainGrid.Children.Contains(_lightingHost)) _mainGrid.Children.Add(_lightingHost);
            if (_effectsHost != null && !_mainGrid.Children.Contains(_effectsHost)) _mainGrid.Children.Add(_effectsHost);
        }

        /// <summary>A built-in Skia layer follows its component's `when` scope — which
        /// for the two ENGINES is always "both", normalized at load: the lighting layer
        /// also draws the rbmarquee lamps driven by live MAME outputs, so it can never
        /// be navigation-only. Only applied once the layer has been started.
        /// The events layer additionally unmounts whenever it draws nothing.</summary>
        /// <summary>
        /// The information overlays are BUILT-IN: ComponentHost skips them, so they have
        /// no visual in its list and RefreshVisibility never sees them. Their content is
        /// pushed by the services and stayed on screen for as long as nobody took it
        /// away — which is why the badges of a finished session followed the user around
        /// the library. New updates were already refused out of scope
        /// (WindowsWithComponent tests ActiveIn); what was missing was clearing what had
        /// already been drawn when the state changes.
        /// </summary>
        private void ScopeBuiltInOverlays()
        {
            if (!IsComponentActive("overlay.ra.badges")) ClearBadgeTray();
            if (!IsComponentActive("overlay.ra.speedrun")) ResetSpeedrunCache();

            foreach (var owner in _informationOverlays.Keys.ToArray())
            {
                if (!IsComponentActive(ComponentForOverlayOwner(owner))) RemoveInformationOverlay(owner);
            }
        }

        /// <summary>Mirror of the controller's owner → component mapping: the same
        /// owners must answer to the same component on both sides.</summary>
        private static string ComponentForOverlayOwner(string owner)
        {
            if (owner.StartsWith("hiscore", StringComparison.OrdinalIgnoreCase)) return "overlay.hiscore";
            if (owner.StartsWith("live-score", StringComparison.OrdinalIgnoreCase)) return "overlay.live.score";
            if (owner.StartsWith("live-timer", StringComparison.OrdinalIgnoreCase)) return "overlay.live.timer";
            return "overlay.ra.info";
        }

        private void ScopeLayer(WpfSkiaSurfaceHost? host, string componentType)
        {
            if (host == null || !host.IsRunning) return;
            var inScope = IsComponentActive(componentType);
            var visible = inScope && (!ReferenceEquals(host, _effectsHost) || _effectsHasContent);
            // Hidden, NOT Collapsed: Collapsed removes the element from layout, so
            // every effect would trigger a measure/arrange pass over the whole window
            // — a layout storm on a layer that toggles several times a second.
            host.Visibility = visible ? Visibility.Visible : Visibility.Hidden;

            // Suspended on SCOPE only — never on "nothing to draw". The effects renderer
            // announces new content from its OWN render thread, so a host suspended for
            // lack of content could never notice a sprite arriving: it would never wake.
            // An idle-but-in-scope layer keeps ticking at its idle rate instead.
            host.Suspended = !inScope;
        }

        private bool _effectsHasContent;

        private bool _dynamicRenderActive;

        /// <summary>
        /// The surface's flattened stack is now displayed (the dynamic render is
        /// mounted): its layers must stop being drawn live, or an unlit copy would sit
        /// on top of the lit one. Recomputed from the same pure function the renderer
        /// used, so the two can never disagree.
        /// </summary>
        public void SetDynamicRenderActive(bool active)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _dynamicRenderActive = active;
                ApplyDynamicSuppression();
            }));
        }

        private void ApplyDynamicSuppression()
        {
            var suppressed = _dynamicRenderActive && _surface != null
                ? Application.Media.DynamicSurfaceRenderer.FlattenableRun(_surface, _activeScene)
                : Array.Empty<Core.Surfaces.ComponentDefinition>();
            foreach (var host in _componentHosts) host.SetSuppressed(suppressed);
        }

        private volatile int _pixelWidth;
        private volatile int _pixelHeight;

        /// <summary>Window size in pixels, safe to read from any thread.</summary>
        public (int Width, int Height) PixelSize => (_pixelWidth, _pixelHeight);

        /// <summary>True when the surface carries the component AND it participates
        /// in the current display state (legacy surfaces: always, `when` = both).</summary>
        public bool IsComponentActive(string type)
            => _surface == null
               || _surface.Components.Any(c =>
                   c.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && c.ActiveIn(_activeScene));
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
        // Guaranteed teardown of an unlock banner even if the choreography hiccups.
        private DispatcherTimer? _takeoverFallback;
        private const int TakeoverSlideMs = 300;

        // OSD Text Layer
        private TextBlock _osdText = null!;
        private DispatcherTimer? _osdTimer;

        // MAME Lamp Map
        private readonly Dictionary<string, List<Image>> _lampImages = new(StringComparer.OrdinalIgnoreCase);

        // Latest-wins: only the last requested path is rendered — checked at dispatch time
        private volatile string? _latestImagePath;
        private volatile string? _latestVideoPath;
        // Monotonic marquee request id: a slow/late decode (or a stale snapshot that
        // "comes back over" the current one) is discarded when a newer request exists.
        private long _marqueeSeq;
        // Window width in physical pixels, captured when the window is positioned, so
        // the background decode thread caps the decode size WITHOUT touching the UI
        // thread (a blocking Dispatcher.Invoke from the pool starved it → freezes).
        private volatile int _windowPixelWidth;

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
            Core.Surfaces.SurfaceDefinition? surface = null, LightingSurfaceOptions? effectsOptions = null)
        {
            _targetScreen = screenNumber;
            _logger = logger;
            _lightingOptions = lightingOptions;
            _effectsOptions = effectsOptions;
            _bounds = bounds;
            _dmdMirror = dmdMirror;
            _dmdWidth = dmdWidth;
            _dmdHeight = dmdHeight;
            _surface = surface;
            // No surface definition = legacy behavior (always show the flux background).
            // The resolved media is the OUTPUT of the whole resolution chain (My systems
            // / My games): it is always shown, full frame, at the very back. It used to
            // require a `media.flux` component, which made a surface silently ignore
            // everything that chain decided the moment the component was dropped while
            // recomposing — the configuration "undid itself" with no trace. It is a
            // behaviour, not a placeable layer: its rectangle was ignored anyway.
            // The only opt-out is an EXPLICIT media.flux marked hidden (the split
            // instruction card does exactly that).
            _fluxBackgroundEnabled = surface?.Component("media.flux")?.Visible != false;

            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = Brushes.Black;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Title = "RetroBat Marquee Player";

            InitializeLayers();

            // plain mirror of the window size, readable from ANY thread (the WPF
            // properties are thread-affine and the renderers live off the UI thread)
            this.SizeChanged += (_, e) =>
            {
                _pixelWidth = (int)Math.Round(e.NewSize.Width);
                _pixelHeight = (int)Math.Round(e.NewSize.Height);
            };
            this.Closed += (_, _) => StopShake(); // never leave a Rendering handler behind
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
            _mainGrid = new Grid
            {
                // shake transform (§4b): identity until an event jolts the surface
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { _shakeScale, _shakeTranslate } }
            };

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
                _lightingHost = new WpfSkiaSurfaceHost(_logger, _lightingOptions.FpsLimit, _lightingOptions.ShowFps, _lightingOptions.RenderScale, _lightingOptions.PresentPipeline, _lightingOptions.GpuRaster)
                {
                    Visibility = Visibility.Collapsed
                };
                // A dynamic surface decides its own z-order (see BuildOrderedSurfaceLayers);
                // a legacy config keeps the historical slot, right here.
                if (_surface == null) _mainGrid.Children.Add(_lightingHost);
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
                        _lightingOptions.TubeVisualOpacity, _lightingOptions.TubeThickness,
                        _lightingOptions.TubeBlur, _lightingOptions.TubeEndFade, _lightingOptions.TubeColor,
                        _lightingOptions.LatestWinsGeneration, _lightingOptions.MapCache);
                    if (_dmdMirror != null) _lightingHost.FrameRendered = MirrorFrameToDmd;
                    // a surface that never declares lamps keeps them (legacy behaviour);
                    // declaring the row and closing its eye is what turns them off
                    _lightingRenderer.SetLampsVisible(_surface?.Component("lamps.scene")?.Visible != false);
                    this.Loaded += (_, _) =>
                    {
                        _lightingHost.Start(_lightingRenderer);
                        ScopeLayer(_lightingHost, "lighting.engine");
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

            // 4b. Ingame events layer (sprites, veils).
            if (_effectsOptions != null)
            {
                _effectsHost = new WpfSkiaSurfaceHost(_logger, _effectsOptions.FpsLimit, false,
                    _effectsOptions.RenderScale, _effectsOptions.PresentPipeline, _effectsOptions.GpuRaster)
                {
                    Visibility = Visibility.Hidden,
                    IsHitTestVisible = false
                };
                if (_surface == null) _mainGrid.Children.Add(_effectsHost);
                _effectsRenderer = new Application.Lighting.IngameEffectsRenderer(_logger);
                // mounted only while an event actually draws: a permanently visible
                // full-screen alpha layer forces WPF to recomposite the whole window
                // on every frame of the layers below (measured: lighting renderMs
                // 8.5 → 30 ms, writePixels 1.5 → 10 ms).
                _effectsRenderer.ContentChanged += content =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _effectsHasContent = content;
                        ScopeLayer(_effectsHost, "effects.engine");
                    }));
                this.Loaded += (_, _) =>
                {
                    _effectsHost.Start(_effectsRenderer);
                    ScopeLayer(_effectsHost, "effects.engine");
                    _logger.LogInformation("Ingame events layer ready on screen {Screen}", _targetScreen);
                };
                this.Closed += (_, _) => _effectsHost?.Dispose();
            }

            // 4c. The surface's own layers, in the order the composition editor shows.
            BuildOrderedSurfaceLayers();

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
                if (screenIndex < 0 || screenIndex >= screens.Length)
                {
                    // an invalid target is IGNORED, never silently redirected to
                    // screen 0: hide this window rather than paint the wrong display
                    _logger.LogWarning("[WPF Player] Target screen index {Index} is invalid ({Count} screen(s) detected); window hidden",
                        screenIndex, screens.Length);
                    Hide();
                    return;
                }

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
                _windowPixelWidth = width; // physical px, read by the background decode

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

        /// <summary>Play session boundary: both engines drop what belonged to the
        /// previous session (lighting sounds/outputs, running events and sprites).</summary>
        public void SetLightingIngame(bool ingame)
        {
            _lightingRenderer?.SetIngame(ingame);
            _effectsRenderer?.SetIngame(ingame);
        }

        public void SetLightingOutput(string output, int value) => _lightingRenderer?.SetArcadeOutput(output, value);

        /// <summary>Animated ingame event: sprites and veils on the events layer.
        /// `shake` is the exception — it belongs to no renderer, the window jolts its
        /// whole visual tree so the fanart and every media move with the sprites.</summary>
        public void TriggerIngameEffect(Application.Lighting.IngameEffectRule rule)
        {
            if (_speedrunActive) return; // clean speedrun session: no effects
            if (rule.Kind == Application.Lighting.IngameEffectKind.Shake)
            {
                StartShake(rule.DurationMs, rule.Dip > 0 ? rule.Dip : 0.5f);
                return;
            }
            _effectsRenderer?.TriggerIngameEffect(rule);
        }

        // ===== physical jolt of the whole surface (design note §4b) =====
        private readonly TranslateTransform _shakeTranslate = new();
        private readonly ScaleTransform _shakeScale = new(1, 1);
        private readonly Random _shakeRandom = new();
        private System.Diagnostics.Stopwatch? _shakeClock;
        private double _shakeDurationSeconds;
        private float _shakeAmplitude;
        private bool _shakeAttached;

        /// <summary>
        /// Jolts the entire window content — background, video, .lay, lighting layer,
        /// logo, dynamic components, events layer, overlays. Driven by
        /// CompositionTarget.Rendering (screen cadence, independent of the Skia render
        /// threads) and armed only while a shake runs, so it costs nothing at rest.
        /// A slight scale-up hides the empty edges the translation would reveal.
        /// </summary>
        private void StartShake(int durationMs, float amplitude)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _shakeDurationSeconds = Math.Max(0.05, durationMs / 1000.0);
                // a shake landing on a running one takes the strongest of the two
                _shakeAmplitude = _shakeClock != null ? Math.Max(_shakeAmplitude, amplitude) : amplitude;
                _shakeClock = System.Diagnostics.Stopwatch.StartNew();
                if (_shakeAttached) return;
                CompositionTarget.Rendering += OnShakeFrame;
                _shakeAttached = true;
            }));
        }

        private void OnShakeFrame(object? sender, EventArgs e)
        {
            var progress = _shakeClock == null ? 1 : _shakeClock.Elapsed.TotalSeconds / _shakeDurationSeconds;
            if (progress >= 1)
            {
                StopShake();
                return;
            }
            var envelope = (float)Math.Sin(Math.PI * Math.Clamp(progress, 0, 1)) * _shakeAmplitude;
            _shakeTranslate.X = (_shakeRandom.NextDouble() - 0.5) * 2 * envelope * 14;
            _shakeTranslate.Y = (_shakeRandom.NextDouble() - 0.5) * 2 * envelope * 8;
            // 3 % overscan: the translated content never uncovers the window edges
            _shakeScale.ScaleX = _shakeScale.ScaleY = 1 + 0.03 * envelope;
        }

        private void StopShake()
        {
            if (_shakeAttached)
            {
                CompositionTarget.Rendering -= OnShakeFrame;
                _shakeAttached = false;
            }
            _shakeClock = null;
            _shakeAmplitude = 0;
            _shakeTranslate.X = _shakeTranslate.Y = 0;
            _shakeScale.ScaleX = _shakeScale.ScaleY = 1;
        }

        /// <summary>Tube-level part of an ingame event (blackout, powerCycle): only
        /// the lighting engine can act on tubes it owns.</summary>
        public void TriggerTubeEffect(Application.Lighting.IngameEffectRule rule)
        {
            if (_speedrunActive) return;
            _lightingRenderer?.TriggerTubeEffect(rule);
        }

        public void DisplayImage(string path, Application.Lighting.LightingSceneMeta? lightingMeta = null)
        {
            _latestImagePath = path;
            var seq = System.Threading.Interlocked.Increment(ref _marqueeSeq);

            // The DYNAMIC marquee (lighting scene) must track EVERY selection right
            // away — attract and ingame. The call is trivial (it only records the
            // request; the renderer generates on its own background thread and
            // coalesces to the latest), so it is never debounced nor gated. Gating it
            // by sequence made the lit marquee vanish during selection bursts.
            _ = this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _mediaElement.Stop();
                _mediaElement.Visibility = Visibility.Collapsed;
                _layViewbox.Visibility = Visibility.Collapsed;
                _logoImage.Visibility = Visibility.Collapsed;
                _lightingRenderer?.SetMarqueeImage(path, lightingMeta);
            }));

            // The WPF fallback image (shown when lighting is off, or beneath the lit
            // layer) is the expensive part: a 100-234 ms decode. Do it OFF the UI
            // thread with a short debounce (skip games flown past) and a sequence
            // guard (a late decode never overwrites a newer selection). This is what
            // froze navigation and let a stale marquee "come back over" the current.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(20).ConfigureAwait(false);
                    if (System.Threading.Interlocked.Read(ref _marqueeSeq) != seq) return; // flown past
                    if (!_fluxBackgroundEnabled) return; // surface composed without a visible media.flux
                    if (!File.Exists(path)) return;

                    var decodeWidth = _windowPixelWidth; // captured at positioning, no UI call
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
                    bitmap.EndInit();
                    bitmap.Freeze(); // the decode happens here, on this background thread

                    _ = this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (System.Threading.Interlocked.Read(ref _marqueeSeq) != seq) return; // a newer marquee won
                        _backgroundImage.Source = bitmap;
                        _backgroundImage.Visibility = Visibility.Visible;
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WPF Player] DisplayImage error: {ex.Message}");
                }
            });
        }

        /// <summary>The marquee window's on-screen width in pixels, used to cap image
        /// decode resolution. 0 before the first layout pass (full decode, one image).</summary>
        private int DisplayDecodeWidth()
        {
            if (this.ActualWidth <= 0) return 0;
            var dpiScale = 1.0;
            try { dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { /* not yet connected */ }
            return (int)Math.Ceiling(this.ActualWidth * dpiScale);
        }

        public void DisplayVideo(string path)
        {
            _latestVideoPath = path;
            // supersede any marquee image decode still in flight (video wins now)
            System.Threading.Interlocked.Increment(ref _marqueeSeq);
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_latestVideoPath != path) return;
                if (!_fluxBackgroundEnabled) return; // surface composed without a visible media.flux
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
                    this.ActualWidth > 0 ? this.ActualWidth : 2000, exitMs, true, () => FinishTakeover(banner));
            });

            // safety net: whatever happens to the choreography above, force the banner
            // out after its full duration + margin so an unlock can never get stuck.
            _takeoverFallback?.Stop();
            _takeoverFallback = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(durationMs + 1500)
            };
            _takeoverFallback.Tick += (_, _) => FinishTakeover(banner);
            _takeoverFallback.Start();
        }

        /// <summary>Idempotent teardown of an unlock banner: removes it, restores the
        /// live blocks, releases the takeover and starts the next queued one. Called by
        /// both the normal exit animation and the safety-net timer; the first to run
        /// wins (the banner is gone for the second, which then no-ops).</summary>
        private void FinishTakeover(FrameworkElement banner)
        {
            _takeoverFallback?.Stop();
            _takeoverFallback = null;
            if (!_mainGrid.Children.Contains(banner)) return; // already torn down
            _mainGrid.Children.Remove(banner);
            AnimateDouble(_informationPanelSlide, TranslateTransform.YProperty, 220, 0, TakeoverSlideMs, false);
            AnimateDouble(_badgeTraySlide, TranslateTransform.YProperty, 220, 0, TakeoverSlideMs, false);
            _takeoverActive = false;
            if (_takeoverQueue.Count > 0) _takeoverQueue.Dequeue()();
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
            // Normal priority, NOT the DispatcherTimer default (Background): during
            // gameplay the UI thread is saturated (lighting presents at Render + live
            // overlays), which starved Background ticks — the unlock banner then never
            // reached its exit and stayed on screen forever.
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };
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
            // Normal priority (see FadeElement): the takeover's hold/exit delay must
            // fire even while the lighting engine saturates the UI thread.
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)) };
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

        /// <summary>Lot 2: native WPF Top-N local leaderboard (rank · name · score) with the
        /// title "&lt;GAME&gt; — LOCAL LEADERBOARD", shown when APIExpose sends the full ranking.
        /// Rows flagged as new (highlightKeys) pulse briefly. Centered for now — Lot 4 will
        /// make it honor the rect drawn on the overlay.hiscore component in the Setup.</summary>
        private sealed class HiscoreBoard
        {
            public string Owner = "hiscore", Source = "local", Game = "", Sys = "", TitleTemplate = "", Mode = "full", Background = "dark";
            public IReadOnlyList<Core.HiscoreRow> Rows = System.Array.Empty<Core.HiscoreRow>();
            public IReadOnlyCollection<string> Highlight = System.Array.Empty<string>();
            public bool ShowTitle = true, ShowRank = true, HighlightOn = true, ShowSourceTag = true;
            public int PageSize = 10;            // 0 = dynamic (fit rows to the zone)
            public int RenderedPageSize = 10;    // rows actually shown last render (drives paging)
            public int PageSeconds = 6, Total, Page;
            public string Align = "middle";      // vertical placement in the zone: top|middle|bottom
            public string ColorSpec = "gold";    // rank/score tint: gold|auto|<named>|#RRGGBB
            public string? Footer;               // "your best rank" line drawn under the grid, all pages
        }
        // One board per source. In "dual" mode both may be present and the page timer cycles
        // world → local → world…; a single-source component keeps exactly one entry.
        private readonly List<HiscoreBoard> _hiscoreBoards = new();
        private int _hiscoreBoardIndex;
        private DispatcherTimer? _hiscorePageTimer;
        private System.Windows.Media.Color? _gameAccent;   // vibrant colour extracted from the game media (color=auto)
        private string? _gameAccentPath;                   // media the accent was computed from (skip recompute)
        private HiscoreBoard? CurrentHiscoreBoard
            => _hiscoreBoards.Count == 0 ? null : _hiscoreBoards[Math.Clamp(_hiscoreBoardIndex, 0, _hiscoreBoards.Count - 1)];
        private static int HiscoreSourceOrder(string source) // "d'abord le nelfeplay"
            => source.Equals("nelfeplay", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        private const int HiscoreMaxTotal = 100; // top 100 per machine per game

        public void SetHiscoreLeaderboard(string owner, string game, string system,
            IReadOnlyList<Core.HiscoreRow> rows, IReadOnlyCollection<string> highlightKeys, string source = "local",
            Core.HiscoreMyRank? myRank = null)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_speedrunActive) return; // focus mode: nothing but the speedrun may draw

                var options = _surface?.Component("overlay.hiscore")?.Options;
                string Opt(string key, string fallback)
                    => options != null && options.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;
                bool Flag(string key, bool fallback)
                    => options != null && options.TryGetValue(key, out var v) ? !v.Equals("false", StringComparison.OrdinalIgnoreCase) : fallback;

                // A surface shows one source (local / nelfeplay) — or "dual", which accepts
                // BOTH feeds and cycles them. Ignore a feed the component didn't ask for.
                var compSource = Opt("source", "local");
                var isDual = compSource.Equals("dual", StringComparison.OrdinalIgnoreCase);
                if (!isDual && !string.Equals(source, compSource, StringComparison.OrdinalIgnoreCase)) return;

                // Empty feed for this source: drop its slot; keep the other board in dual.
                if (rows == null || rows.Count == 0)
                {
                    _hiscoreBoards.RemoveAll(b => b.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
                    if (_hiscoreBoards.Count == 0) { RemoveInformationOverlayCore(owner); return; }
                    _hiscoreBoardIndex = Math.Clamp(_hiscoreBoardIndex, 0, _hiscoreBoards.Count - 1);
                    RenderHiscorePage();
                    EnsureHiscorePageTimer();
                    return;
                }

                var mode = Opt("mode", "full");
                var best = mode.Equals("best", StringComparison.OrdinalIgnoreCase);
                // rows: a free number, or "auto"/"dynamique"/0/empty = dynamic (fit to zone).
                var rowsOpt = Opt("rows", "10").Trim().ToLowerInvariant();
                var dynamic = rowsOpt is "" or "0" or "auto" or "dynamic" or "dynamique";
                var pageSize = best ? 1 : (dynamic ? 0 : (int.TryParse(rowsOpt, out var rn) ? Math.Clamp(rn, 1, HiscoreMaxTotal) : 0));
                var pageSeconds = int.TryParse(Opt("pageSeconds", "6"), out var ps) ? Math.Clamp(ps, 2, 60) : 6;
                var total = Math.Min(rows.Count, best ? 1 : HiscoreMaxTotal);
                var fr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("fr", StringComparison.OrdinalIgnoreCase);
                // Default title/footer wording is per-source; a custom value (even in dual)
                // is honoured for both boards, since the operator asked for that text.
                var defaultTitle = source.Equals("nelfeplay", StringComparison.OrdinalIgnoreCase)
                    ? (fr ? "{name} — CLASSEMENT MONDIAL" : "{name} — WORLD RANKING")
                    : (fr ? "{name} — CLASSEMENT LOCAL" : "{name} — LOCAL LEADERBOARD");

                var board = new HiscoreBoard
                {
                    Owner = owner, Source = source, Game = game, Sys = system, Rows = rows,
                    Highlight = highlightKeys ?? System.Array.Empty<string>(),
                    ShowTitle = Flag("showTitle", true), ShowRank = Flag("showRank", true), HighlightOn = Flag("highlight", true),
                    ShowSourceTag = Flag("showSource", true),
                    TitleTemplate = Opt("title", defaultTitle), Mode = mode, Background = Opt("background", "dark"),
                    Align = Opt("align", "middle").ToLowerInvariant(), ColorSpec = Opt("color", "gold"),
                    PageSize = pageSize, RenderedPageSize = pageSize > 0 ? pageSize : 10,
                    PageSeconds = pageSeconds, Total = total, Page = 0,
                    Footer = (myRank != null && Flag("showMyRank", true)) ? BuildMyRankFooter(myRank, fr, Opt) : null
                };

                // Upsert this source's board, world first so dual shows nelfeplay then local.
                _hiscoreBoards.RemoveAll(b => b.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
                _hiscoreBoards.Add(board);
                _hiscoreBoards.Sort((a, b) => HiscoreSourceOrder(a.Source).CompareTo(HiscoreSourceOrder(b.Source)));
                _hiscoreBoardIndex = Math.Clamp(_hiscoreBoardIndex, 0, _hiscoreBoards.Count - 1);

                RenderHiscorePage();
                EnsureHiscorePageTimer();
            }));
        }

        /// <summary>Starts/stops the shared page timer. Needed when any board has more rows
        /// than a page, OR when two boards must alternate (dual). Each tick advances the
        /// current board's page and, past its last page, hands over to the next board.</summary>
        private void EnsureHiscorePageTimer()
        {
            var needed = _hiscoreBoards.Count > 1 || _hiscoreBoards.Any(b => b.Total > Math.Max(1, b.RenderedPageSize));
            if (!needed) { _hiscorePageTimer?.Stop(); _hiscorePageTimer = null; return; }

            var seconds = CurrentHiscoreBoard?.PageSeconds ?? 6;
            if (_hiscorePageTimer == null)
            {
                _hiscorePageTimer = new DispatcherTimer();
                _hiscorePageTimer.Tick += (_, _) =>
                {
                    var b = CurrentHiscoreBoard;
                    if (b == null) { _hiscorePageTimer?.Stop(); return; }
                    var pages = Math.Max(1, (int)Math.Ceiling(b.Total / (double)Math.Max(1, b.RenderedPageSize)));
                    if (b.Page + 1 < pages) b.Page++;
                    else
                    {
                        b.Page = 0;
                        if (_hiscoreBoards.Count > 1)
                            _hiscoreBoardIndex = (_hiscoreBoardIndex + 1) % _hiscoreBoards.Count;
                    }
                    if (_hiscorePageTimer != null) _hiscorePageTimer.Interval = TimeSpan.FromSeconds(CurrentHiscoreBoard?.PageSeconds ?? 6);
                    RenderHiscorePage();
                };
            }
            _hiscorePageTimer.Interval = TimeSpan.FromSeconds(seconds);
            _hiscorePageTimer.Start();
        }

        /// <summary>Builds the "your rank" footer text. Labels are component options so an
        /// operator can reword or translate them freely (placeholders {rank} {of} {score}
        /// {pseudo}); the defaults follow the cabinet UI language (fr/en).</summary>
        private static string? BuildMyRankFooter(Core.HiscoreMyRank m, bool fr, Func<string, string, string> opt)
        {
            if (m.World)
            {
                if (m.Present)
                {
                    var tpl = opt("myRankTemplate", fr ? "★ TON RANG MONDIAL  {rank} / {of}" : "★ YOUR WORLD RANK  {rank} / {of}");
                    return tpl.Replace("{rank}", "#" + m.Rank)
                              .Replace("{of}", m.Of > 0 ? m.Of.ToString() : "?")
                              .Replace("{score}", m.Score);
                }
                return m.Paired
                    ? opt("myRankNoneLabel", fr ? "★ Pas encore classé au niveau mondial" : "★ Not ranked worldwide yet")
                    : opt("myRankIdentifyLabel", fr ? "Identifie-toi sur NelfePlay pour apparaître au classement" : "Identify on NelfePlay to enter the ranking");
            }
            if (m.Present)
            {
                var rankLabel = string.IsNullOrEmpty(m.Rank) ? string.Empty : "#" + m.Rank;
                var tpl = opt("myRankTemplate", fr ? "★ TON MEILLEUR ICI  {rank}   {score}" : "★ YOUR BEST HERE  {rank}   {score}");
                return tpl.Replace("{rank}", rankLabel).Replace("{score}", m.Score).Replace("{pseudo}", m.Pseudo);
            }
            return opt("myRankNoneLabel", fr ? "★ {pseudo} : pas encore classé ici" : "★ {pseudo}: not ranked here yet")
                .Replace("{pseudo}", m.Pseudo);
        }

        /// <summary>Rank/score colour from the component option: "gold" (default), "auto"
        /// (vibrant colour of the current game, gold until it's computed), a named colour, or
        /// a #RRGGBB value.</summary>
        private System.Windows.Media.Brush ResolveHiscoreTint(string spec)
        {
            spec = string.IsNullOrWhiteSpace(spec) ? "gold" : spec.Trim();
            Color color;
            if (spec.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (_gameAccent is { } c) color = c;
                else return Brushes.Gold;
            }
            else if (spec.StartsWith("#", StringComparison.Ordinal))
            {
                try { color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(spec); }
                catch { return Brushes.Gold; }
            }
            else
            {
                switch (spec.ToLowerInvariant())
                {
                    case "white": return Brushes.White;
                    case "cyan": color = Color.FromRgb(0x7C, 0xE7, 0xFF); break;
                    case "green": color = Color.FromRgb(0x53, 0xD0, 0x73); break;
                    case "red": color = Color.FromRgb(0xFF, 0x5A, 0x4A); break;
                    case "pink": color = Color.FromRgb(0xFF, 0x63, 0xA4); break;
                    case "orange": color = Color.FromRgb(0xFF, 0x96, 0x1E); break;
                    default: return Brushes.Gold;
                }
            }
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>Recomputes the game accent colour (off the UI thread) from the game media,
        /// then re-renders if a hiscore board is tinted "auto". No-op if the media is unchanged.</summary>
        private void UpdateGameAccent(IReadOnlyDictionary<string, string?> kinds)
        {
            string? Path4(string k) => kinds.TryGetValue(k, out var p) && !string.IsNullOrWhiteSpace(p) && File.Exists(p) ? p : null;
            var path = Path4("logo") ?? Path4("screenmarquee") ?? Path4("marquee") ?? Path4("fanart");
            if (string.Equals(path, _gameAccentPath, StringComparison.OrdinalIgnoreCase)) return;
            _gameAccentPath = path;

            if (path == null) { _gameAccent = null; if (UsesAutoTint()) RenderHiscorePage(); return; }
            _ = Task.Run(() =>
            {
                var color = TryExtractAccent(path);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!string.Equals(path, _gameAccentPath, StringComparison.OrdinalIgnoreCase)) return; // superseded
                    _gameAccent = color;
                    if (UsesAutoTint()) RenderHiscorePage();
                }));
            });
        }

        private bool UsesAutoTint()
            => _hiscoreBoards.Any(b => b.ColorSpec.Equals("auto", StringComparison.OrdinalIgnoreCase));

        /// <summary>Picks a punchy, legible colour representative of the game media: the
        /// saturation×value-weighted average of its vibrant pixels, then boosted so it reads
        /// on a dark board. Null when the image has no vibrant colour (caller keeps gold).</summary>
        private static System.Windows.Media.Color? TryExtractAccent(string path)
        {
            try
            {
                using var src = SkiaSharp.SKBitmap.Decode(path);
                if (src == null || src.Width == 0 || src.Height == 0) return null;
                var stepX = Math.Max(1, src.Width / 40);
                var stepY = Math.Max(1, src.Height / 40);
                double sr = 0, sg = 0, sb = 0, wsum = 0;
                for (var y = 0; y < src.Height; y += stepY)
                for (var x = 0; x < src.Width; x += stepX)
                {
                    var p = src.GetPixel(x, y);
                    if (p.Alpha < 128) continue;
                    RgbToHsv(p.Red, p.Green, p.Blue, out _, out var s, out var v);
                    if (v < 0.25 || s < 0.30) continue; // ignore dark / greyish pixels
                    var wgt = s * v;
                    sr += p.Red * wgt; sg += p.Green * wgt; sb += p.Blue * wgt; wsum += wgt;
                }
                if (wsum <= 0) return null;
                RgbToHsv((byte)(sr / wsum), (byte)(sg / wsum), (byte)(sb / wsum), out var hh, out var ss, out var vv);
                HsvToRgb(hh, Math.Max(ss, 0.55), Math.Max(vv, 0.85), out var r, out var g, out var b);
                return Color.FromRgb(r, g, b);
            }
            catch
            {
                return null;
            }
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
            v = max;
            var d = max - min;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == rd) h = 60 * (((gd - bd) / d) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / d) + 2);
            else h = 60 * (((rd - gd) / d) + 4);
            if (h < 0) h += 360;
        }

        private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            var c = v * s;
            var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
            var m = v - c;
            double rd, gd, bd;
            if (h < 60) { rd = c; gd = x; bd = 0; }
            else if (h < 120) { rd = x; gd = c; bd = 0; }
            else if (h < 180) { rd = 0; gd = c; bd = x; }
            else if (h < 240) { rd = 0; gd = x; bd = c; }
            else if (h < 300) { rd = x; gd = 0; bd = c; }
            else { rd = c; gd = 0; bd = x; }
            r = (byte)Math.Clamp((rd + m) * 255, 0, 255);
            g = (byte)Math.Clamp((gd + m) * 255, 0, 255);
            b = (byte)Math.Clamp((bd + m) * 255, 0, 255);
        }

        /// <summary>Renders the current page of the stored leaderboard. Called on data
        /// arrival and by the page timer. Replaces the previous page element in place
        /// (without touching the timer or the stored board).</summary>
        private void RenderHiscorePage()
        {
            var board = CurrentHiscoreBoard;
            if (board == null) return;
            if (_informationOverlays.Remove(board.Owner, out var previous)) RemoveElementFromParent(previous);

            var mono = new System.Windows.Media.FontFamily("Consolas");
            var accent = new SolidColorBrush(Color.FromRgb(0x7C, 0xE7, 0xFF));
            accent.Freeze();
            var valueTint = ResolveHiscoreTint(board.ColorSpec); // rank/score colour (gold, game-auto, custom)

            // Geometry of the zone first: needed to size the list and, when rows are dynamic,
            // to pick a count that keeps the font legible.
            var comp = _surface?.Component("overlay.hiscore");
            var winW = _mainGrid.ActualWidth;
            var winH = _mainGrid.ActualHeight;
            var hasRect = comp != null && winW > 0 && winH > 0
                          && !(comp.X <= 0 && comp.Y <= 0 && comp.W >= 1 && comp.H >= 1);
            var zoneW = hasRect ? Math.Max(1, comp!.W * winW) : Math.Max(1, winW * 0.9);
            var zoneH = hasRect ? Math.Max(1, comp!.H * winH) : Math.Max(1, winH * 0.9);

            // Rows per page: fixed number, or dynamic from the zone aspect (tall/narrow zones
            // fit more rows; a wide, short marquee fits fewer so they stay readable).
            var pageSize = board.PageSize;
            if (pageSize <= 0)
                pageSize = Math.Clamp((int)Math.Round(zoneH / zoneW * 12) + 3, 3, HiscoreMaxTotal);
            pageSize = Math.Min(pageSize, Math.Max(1, board.Total));
            board.RenderedPageSize = pageSize;

            var pages = Math.Max(1, (int)Math.Ceiling(board.Total / (double)pageSize));
            if (board.Page >= pages) board.Page = 0;
            var start = board.Page * pageSize;
            var end = Math.Min(start + pageSize, board.Total);

            var vAlign = board.Align switch
            {
                "top" => System.Windows.VerticalAlignment.Top,
                "bottom" => System.Windows.VerticalAlignment.Bottom,
                _ => System.Windows.VerticalAlignment.Center
            };

            // The LIST only (rank | name | score) in its OWN Viewbox, so its scale never
            // depends on the title width — a long title no longer shrinks the score.
            var listGrid = new Grid();
            listGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // rank
            listGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            listGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // score
            for (var i = start; i < end; i++)
            {
                var row = board.Rows[i];
                var line = i - start;
                listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var hot = board.HighlightOn && board.Highlight.Contains((row.Name + "|" + row.Score).Trim());
                var valueBrush = hot ? (System.Windows.Media.Brush)accent : valueTint;
                var nameBrush = hot ? (System.Windows.Media.Brush)accent : Brushes.White;

                if (board.ShowRank)
                {
                    var rankLabel = string.IsNullOrWhiteSpace(row.Rank) ? (i + 1).ToString() : row.Rank.Trim();
                    var rankCell = CreateOutlinedText(rankLabel, 30, valueBrush, 2,
                        TextAlignment.Right, System.Windows.HorizontalAlignment.Right, mono);
                    rankCell.Margin = new Thickness(0, 2, 18, 2);
                    Grid.SetRow(rankCell, line); Grid.SetColumn(rankCell, 0);
                    listGrid.Children.Add(rankCell);
                    if (hot) PulseHighlight(rankCell);
                }

                var nameCell = CreateOutlinedText(row.Name, 30, nameBrush, 2,
                    TextAlignment.Left, System.Windows.HorizontalAlignment.Left, mono);
                var scoreCell = CreateOutlinedText(row.Score, 30, valueBrush, 2,
                    TextAlignment.Right, System.Windows.HorizontalAlignment.Right, mono);
                nameCell.Margin = new Thickness(0, 2, 28, 2);
                scoreCell.Margin = new Thickness(0, 2, 0, 2);
                Grid.SetRow(nameCell, line); Grid.SetColumn(nameCell, 1);
                Grid.SetRow(scoreCell, line); Grid.SetColumn(scoreCell, 2);
                listGrid.Children.Add(nameCell); listGrid.Children.Add(scoreCell);
                if (hot) { PulseHighlight(nameCell); PulseHighlight(scoreCell); }
            }
            var listViewbox = new System.Windows.Controls.Viewbox
            {
                Child = listGrid,
                Stretch = Stretch.Uniform,
                StretchDirection = System.Windows.Controls.StretchDirection.Both,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = vAlign
            };

            // Stacked layout: title band (own scale) / list (fills) / footer + source tag.
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // title
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // footer + tag

            if (board.ShowTitle)
            {
                var titleText = FormatHiscoreTitle(board.TitleTemplate, board.Game, board.Sys);
                if (pages > 1) titleText += $"   ({board.Page + 1}/{pages})";
                var title = CreateOutlinedText(titleText, 26, Brushes.White, 2,
                    TextAlignment.Center, System.Windows.HorizontalAlignment.Center);
                var titleVb = new System.Windows.Controls.Viewbox
                {
                    Child = title,
                    Stretch = Stretch.Uniform,
                    StretchDirection = System.Windows.Controls.StretchDirection.Both,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    MaxHeight = Math.Max(24, zoneH * 0.22), // long titles shrink here, not the list
                    Margin = new Thickness(0, 0, 0, zoneH * 0.03)
                };
                Grid.SetRow(titleVb, 0);
                root.Children.Add(titleVb);
            }

            Grid.SetRow(listViewbox, 1);
            root.Children.Add(listViewbox);

            var bottom = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Margin = new Thickness(0, zoneH * 0.02, 0, 0)
            };
            // "Your best rank" line, scaled on its own so it never squeezes the list.
            if (!string.IsNullOrWhiteSpace(board.Footer))
            {
                var footer = CreateOutlinedText(board.Footer!, 22, accent, 2,
                    TextAlignment.Center, System.Windows.HorizontalAlignment.Center);
                bottom.Children.Add(new System.Windows.Controls.Viewbox
                {
                    Child = footer,
                    Stretch = Stretch.Uniform,
                    StretchDirection = System.Windows.Controls.StretchDirection.DownOnly,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    MaxHeight = Math.Max(16, zoneH * 0.12)
                });
            }
            // Faint watermark telling which board is on screen right now (key for dual).
            if (board.ShowSourceTag)
            {
                var frTag = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("fr", StringComparison.OrdinalIgnoreCase);
                var tagText = board.Source.Equals("nelfeplay", StringComparison.OrdinalIgnoreCase)
                    ? "NELFEPLAY · " + (frTag ? "MONDIAL" : "WORLD")
                    : "LOCAL";
                var tag = CreateOutlinedText(tagText, 13, new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), 1,
                    TextAlignment.Center, System.Windows.HorizontalAlignment.Center);
                tag.Opacity = 0.45; // filigrane
                tag.Margin = new Thickness(0, 6, 0, 0);
                bottom.Children.Add(tag);
            }
            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);

            System.Windows.Media.Brush bg = board.Background.ToLowerInvariant() switch
            {
                "transparent" => System.Windows.Media.Brushes.Transparent,
                "gradient" => new LinearGradientBrush(Color.FromArgb(205, 0, 0, 0), Color.FromArgb(40, 0, 0, 0), 90),
                _ => new SolidColorBrush(Color.FromArgb(150, 0, 0, 0))
            };
            var container = new Border
            {
                Padding = new Thickness(30, 18, 30, 20),
                Background = bg,
                CornerRadius = new CornerRadius(12),
                Child = root
            };

            // Honor the rect drawn on the overlay.hiscore component; else centered.
            if (hasRect)
            {
                container.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                container.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                container.Margin = new Thickness(comp!.X * winW, comp.Y * winH, 0, 0);
                container.Width = zoneW;
                container.Height = zoneH;
            }
            else
            {
                container.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                container.VerticalAlignment = vAlign; // top / middle / bottom of the window
                container.Margin = new Thickness(24);
                container.MaxWidth = 1400;
            }

            _mainGrid.Children.Add(container);
            _informationOverlays[board.Owner] = container;
        }

        /// <summary>Fills a title template with the game/system. Accepts {name} and friendly
        /// aliases ({gamename}, {game}, {title}; {system}, {systemname}); and, when someone
        /// typed a bare token as the whole title (e.g. "gamename"), substitutes that too.</summary>
        private static string FormatHiscoreTitle(string template, string game, string system)
        {
            var t = (template ?? string.Empty)
                .Replace("{name}", game).Replace("{gamename}", game).Replace("{game}", game).Replace("{title}", game)
                .Replace("{system}", system).Replace("{systemname}", system).Replace("{sys}", system);
            if (t.Equals(template, StringComparison.Ordinal)) // no placeholder was present
            {
                switch ((template ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "gamename": case "game name": case "name": case "gametitle": case "game title": case "game":
                        return game;
                    case "system": case "systemname": case "system name":
                        return system;
                }
            }
            return t;
        }

        private static void PulseHighlight(UIElement element)
        {
            var pulse = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.25,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(420),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3)
            };
            element.BeginAnimation(UIElement.OpacityProperty, pulse);
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
            if (owner.StartsWith("hiscore", StringComparison.OrdinalIgnoreCase))
            {
                _hiscorePageTimer?.Stop();
                _hiscorePageTimer = null;
                _hiscoreBoards.Clear();
                _hiscoreBoardIndex = 0;
            }
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

        /// <summary>
        /// Empties the surface's media: no image, no video, no lighting scene. The
        /// missing counterpart of DisplayImage — without it, an entry with no media of
        /// its own simply kept the previous one's, and a single topper or instruction
        /// card followed the user across the whole library.
        /// Supersedes any decode still in flight, or a late one would restore what we
        /// just cleared.
        /// </summary>
        public void ClearMedia()
        {
            System.Threading.Interlocked.Increment(ref _marqueeSeq);
            _latestImagePath = null;
            _latestVideoPath = null;
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _mediaElement.Stop();
                _mediaElement.Source = null;
                _mediaElement.Visibility = Visibility.Collapsed;
                _backgroundImage.Visibility = Visibility.Collapsed;
                _backgroundImage.Source = null;
                _logoImage.Visibility = Visibility.Collapsed;
                _logoImage.Source = null;
                _lightingRenderer?.SetMarqueeImage(null);
            }));
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
