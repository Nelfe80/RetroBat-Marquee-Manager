using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;
using RetroBatMarqueeManager.Infrastructure.Rendering;

namespace RetroBatMarqueeManager.Application.Services
{
    /// <summary>
    /// Orchestrator — ISO of ra.lua's mame_action() + process_*() control flow.
    /// Manages one LayPipeline per .lay view (lcd, dmd, topper, iccard).
    /// </summary>
    public class LayManager : IDisposable
    {
        private readonly IConfigService    _config;
        private readonly MarqueeController _marquee;
        private readonly IDmdService       _dmdService;
        private readonly ILogger<LayManager> _logger;

        private readonly Dictionary<string, LayPipeline> _pipelines = new(StringComparer.OrdinalIgnoreCase);

        // view name in .lay → physical target key
        private static readonly Dictionary<string, string> ViewTargetMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Marquee_Only", "lcd"    },
            { "DMD_Only",     "dmd"    },
            { "Topper_Only",  "topper" },
            { "ICCard_Only",  "iccard" },
        };

        // LCD .lay active flag (uses MarqueeController direct path, not compositor)
        private bool _lcdLayActive;
        private MameLayout? _currentLayout;
        private string? _lastLoadedRom; // debounce: skip reload for same ROM

        public LayManager(
            IConfigService config,
            MarqueeController marquee,
            IDmdService dmdService,
            ILogger<LayManager> logger)
        {
            _config     = config;
            _marquee    = marquee;
            _dmdService = dmdService;
            _logger     = logger;
        }

        // ── MAME .lay ─────────────────────────────────────────────────────────

        /// <summary>
        /// ISO mame_action("mame_start=romName") — loads .lay and creates pipelines.
        /// <paramref name="dmdOnly"/>: the rbmarquee lighting scene owns the marquee
        /// (CDC §26.3) but the .lay keeps its purpose-built DMD view.
        /// </summary>
        public void LoadMameLayout(MameLayout layout, string layDir, string? romName = null, bool dmdOnly = false)
        {
            // Debounce: skip if same ROM is already loaded (fired by .raw + enriched events)
            if (romName != null && romName == _lastLoadedRom && (_lcdLayActive || _pipelines.Count > 0))
            {
                _logger.LogDebug($"[LayManager] Skipping duplicate LoadMameLayout for '{romName}'");
                return;
            }
            Clear();
            _currentLayout = layout;
            _lastLoadedRom = romName; // set AFTER Clear() so debounce works on next duplicate call

            // ── LCD: use MarqueeController direct path (pushes to _layCanvas natively) ──
            if (!dmdOnly && _config.LayEnabled && _config.LayLcdEnabled)
            {
                _marquee.LoadMameLayout(layout, "Marquee_Only");
                _lcdLayActive = true;
                _logger.LogInformation("[LayManager] LCD .lay loaded via MarqueeController.");
            }

            // ── DMD: offscreen compositor → DmdOutputAdapter → DmdDeviceWrapper ──
            if (_config.LayEnabled && _config.LayDmdEnabled && _config.DmdEnabled)
            {
                if (layout.Views.TryGetValue("DMD_Only", out var dmdView))
                {
                    var dispatcher = EnsureStaDispatcher();
                    var adapter    = new DmdOutputAdapter(_dmdService,
                                                          _config.DmdWidth, _config.DmdHeight,
                                                          _logger, LayScaleMode.Fit);

                    // Entire WPF pipeline creation must run on the STA dispatcher thread
                    LayPipeline? dmdPipeline = null;
                    dispatcher.Invoke(() =>
                    {
                        dmdPipeline = new LayPipeline("dmd", dmdView.Width, dmdView.Height,
                                                       adapter, dispatcher, _logger);
                        PopulateFromView(dmdPipeline, dmdView, layout, layDir);
                    });

                    if (dmdPipeline != null)
                    {
                        _pipelines["dmd"] = dmdPipeline;
                        // the purpose-built .lay DMD view owns the panel: the lighting
                        // mirror steps aside until this pipeline is cleared
                        _marquee.SetLayDmdActive(true);
                        _logger.LogInformation($"[LayManager] DMD .lay loaded: {dmdView.Width}x{dmdView.Height}, {dmdView.Elements.Count} elements");
                    }
                }
            }
        }

        /// <summary>
        /// ISO mame_action("SIGNAL_NAME=0/1") — toggles lamp visibility on all active targets.
        /// </summary>
        public void SetLampState(string lampName, int state)
        {
            // LCD: direct MarqueeController path
            if (_lcdLayActive)
                _marquee.SetLampState(lampName, state);

            // DMD: compositor pipeline
            foreach (var pipeline in _pipelines.Values)
                pipeline.SetLampState(lampName, state);
        }

        /// <summary>
        /// ISO mame_action("mame_stop") — clears all active .lay pipelines.
        /// </summary>
        public void Clear()
        {
            // LCD: clear MarqueeWindow lay canvas
            if (_lcdLayActive)
            {
                _marquee.ClearLayout();
                _lcdLayActive = false;
            }

            foreach (var p in _pipelines.Values) p.Dispose();
            _pipelines.Clear();
            _dmdService.SetLayoutFrame(Array.Empty<byte>());
            _marquee.SetLayDmdActive(false);
            _currentLayout = null;
            _lastLoadedRom = null;
        }

        // ── RA display helpers (high-level API — ISO ra.lua process_*) ─────────
        // These will be implemented progressively as RA events arrive from APIExpose WS.

        public async Task PushUserInfo(string username, string picPath, bool isHardcore)
        {
            // ISO process_user_info() — to be implemented
            await Task.CompletedTask;
        }

        public async Task PushAchievement(string id, string badgePath,
                                           string title, string desc, int points)
        {
            // ISO process_achievement() — to be implemented
            await Task.CompletedTask;
        }

        public async Task ShowAchievements()
        {
            // ISO show_achievements() — to be implemented
            await Task.CompletedTask;
        }

        public async Task ShowScore(int current, int total, bool isHardcore)
        {
            // ISO show_score() — to be implemented
            await Task.CompletedTask;
        }

        public async Task PushGameStop()
        {
            foreach (var p in _pipelines.Values) await p.Scene.Clear();
        }

        // ── Accessors ─────────────────────────────────────────────────────────

        public bool HasActivePipeline(string target)
            => _pipelines.ContainsKey(target) || (target == "lcd" && _lcdLayActive);

        public LayPipeline? GetPipeline(string target)
            => _pipelines.GetValueOrDefault(target);

        // ── Internal ──────────────────────────────────────────────────────────

        private bool IsTargetEnabled(string target) =>
            _config.LayEnabled && target switch
            {
                "lcd"    => _config.LayLcdEnabled,
                "dmd"    => _config.LayDmdEnabled && _config.DmdEnabled,
                "topper" => _config.LayLcdEnabled, // shares LCD enable for now
                "iccard" => _config.LayLcdEnabled,
                _        => false
            };

        private ILayOutputAdapter CreateAdapter(string target) => target switch
        {
            "lcd"    => new LcdOutputAdapter(_marquee, "marquee"),
            "topper" => new LcdOutputAdapter(_marquee, "topper"),
            "iccard" => new LcdOutputAdapter(_marquee, "iccard"),
            "dmd"    => new DmdOutputAdapter(_dmdService,
                                              _config.DmdWidth, _config.DmdHeight,
                                              _logger, LayScaleMode.Fit),
            _        => new LcdOutputAdapter(_marquee, target)
        };

        private void PopulateFromView(LayPipeline pipeline, MameView view,
                                       MameLayout layout, string layDir)
        {
            foreach (var viewElem in view.Elements)
            {
                if (!layout.Elements.TryGetValue(viewElem.Ref, out var element)) continue;

                var imgPath = System.IO.Path.Combine(layDir, element.ImageFile);
                var isLamp  = !string.IsNullOrEmpty(viewElem.Name); // named = dynamic lamp

                var props = new LayProperties
                {
                    X         = (float)viewElem.X,
                    Y         = (float)viewElem.Y,
                    W         = (float)viewElem.Width,
                    H         = (float)viewElem.Height,
                    ImagePath = imgPath,
                    Show      = !isLamp,  // lamps start hidden (state=0), background visible
                    Opacity   = 1f
                };

                // Use viewElem.Name as object key for lamps, ref for background
                var objectName = isLamp ? viewElem.Name : $"_bg_{viewElem.Ref}";
                pipeline.Scene.Create(objectName, LayObjectType.Image, props, isLamp ? 10 : 0);

                // Wire lamp to compositor immediately (background always visible)
                pipeline.Compositor.Apply(pipeline.Scene.GetOrderedByZ()
                    .First(o => o.Name == objectName));
            }
        }

        private static Dispatcher? _staDispatcher;
        private static readonly object _dispatcherLock = new();

        private static Dispatcher EnsureStaDispatcher()
        {
            lock (_dispatcherLock)
            {
                if (_staDispatcher != null) return _staDispatcher;

                var tcs = new TaskCompletionSource<Dispatcher>();
                var thread = new Thread(() =>
                {
                    _staDispatcher = Dispatcher.CurrentDispatcher;
                    tcs.SetResult(_staDispatcher);
                    Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "LayEngine-STA";
                thread.Start();
                return tcs.Task.GetAwaiter().GetResult();
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
