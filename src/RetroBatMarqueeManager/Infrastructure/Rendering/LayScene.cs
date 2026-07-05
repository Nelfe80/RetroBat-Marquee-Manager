namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// ISO port of ra.lua's rendering engine:
    /// gfx_objects{} + create / set_object_properties / remove_object /
    /// clear_osd / clear_visible_objects / move / fade_opacity / animate_properties.
    /// </summary>
    public class LayScene
    {
        public float RefW { get; private set; }
        public float RefH { get; private set; }

        private readonly Dictionary<string, LayObject> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _lock = new(1, 1);

        /// <summary>Fires when any object is marked Updated. Debounced by LayPipeline.</summary>
        public event Action? RenderRequested;

        public LayScene(float refW = 0f, float refH = 0f)
        {
            RefW = refW;
            RefH = refH;
        }

        public void SetRefDimensions(float w, float h) { RefW = w; RefH = h; }

        // ── create(name, type, properties, z) ────────────────────────────────

        public void Create(string name, LayObjectType type, LayProperties props, int z)
        {
            _lock.Wait();
            try
            {
                if (_objects.TryGetValue(name, out var existing))
                {
                    existing.Type       = type;
                    existing.Properties = props.Clone();
                    existing.Z          = z;
                    existing.Updated    = true;
                }
                else
                {
                    _objects[name] = new LayObject
                    {
                        Name       = name,
                        Type       = type,
                        Properties = props.Clone(),
                        Z          = z,
                        Updated    = true
                    };
                }
            }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── set_object_properties(name, properties) ───────────────────────────

        public void SetProperties(string name, LayProperties props)
        {
            _lock.Wait();
            try
            {
                if (!_objects.TryGetValue(name, out var obj)) return;
                // Merge: only copy non-null / non-default fields
                MergeProperties(obj.Properties, props);
                obj.Updated = true;
            }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── get_object_property(name, key) ────────────────────────────────────

        public T? GetProperty<T>(string name, string key)
        {
            _lock.Wait();
            try
            {
                if (!_objects.TryGetValue(name, out var obj)) return default;
                var prop = typeof(LayProperties).GetProperty(key,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null) return default;
                var val = prop.GetValue(obj.Properties);
                return val is T t ? t : default;
            }
            finally { _lock.Release(); }
        }

        // ── remove_object(name) ───────────────────────────────────────────────

        public async Task Remove(string name)
        {
            await _lock.WaitAsync();
            try
            {
                if (_objects.TryGetValue(name, out var obj))
                {
                    obj.AnimCts?.Cancel();
                    obj.AnimCts?.Dispose();
                    _objects.Remove(name);
                }
            }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── clear_osd() ──────────────────────────────────────────────────────

        public async Task Clear()
        {
            await _lock.WaitAsync();
            try
            {
                foreach (var obj in _objects.Values)
                {
                    obj.AnimCts?.Cancel();
                    obj.AnimCts?.Dispose();
                }
                _objects.Clear();
            }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── clear_visible_objects() ───────────────────────────────────────────

        public async Task ClearVisible()
        {
            await _lock.WaitAsync();
            try
            {
                var toRemove = _objects.Values
                    .Where(o => o.Properties.Show)
                    .Select(o => o.Name)
                    .ToList();
                foreach (var name in toRemove)
                {
                    _objects[name].AnimCts?.Cancel();
                    _objects[name].AnimCts?.Dispose();
                    _objects.Remove(name);
                }
            }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── move(name, tx, ty, targetOpacity, durationSec, onComplete) ────────

        public Task Move(string name, float tx, float ty, float targetOp,
                         float durationSec, Action? onComplete = null,
                         EasingMode easing = EasingMode.Linear)
            => AnimateCore(name, durationSec, onComplete, easing, obj =>
            {
                var sx = obj.Properties.X;
                var sy = obj.Properties.Y;
                var so = obj.Properties.Opacity;
                return (float progress) =>
                {
                    obj.Properties.X       = Lerp(sx, tx, progress);
                    obj.Properties.Y       = Lerp(sy, ty, progress);
                    obj.Properties.Opacity = Lerp(so, targetOp, progress);
                    obj.Updated = true;
                };
            });

        // ── fade_opacity(name, targetOpacity, durationSec, onComplete) ────────

        public Task FadeOpacity(string name, float targetOp,
                                 float durationSec, Action? onComplete = null,
                                 EasingMode easing = EasingMode.Linear)
            => AnimateCore(name, durationSec, onComplete, easing, obj =>
            {
                var so = obj.Properties.Opacity;
                return (float progress) =>
                {
                    obj.Properties.Opacity = Lerp(so, targetOp, progress);
                    obj.Updated = true;
                };
            });

        // ── animate_properties(name, targets, durationSec, onComplete) ────────

        public Task AnimateProperties(string name, Dictionary<string, float> targets,
                                       float durationSec, Action? onComplete = null,
                                       EasingMode easing = EasingMode.Linear)
            => AnimateCore(name, durationSec, onComplete, easing, obj =>
            {
                // Snapshot start values
                var starts = new Dictionary<string, float>();
                var propInfos = typeof(LayProperties)
                    .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                foreach (var (key, _) in targets)
                {
                    var p = propInfos.FirstOrDefault(pi =>
                        pi.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (p?.GetValue(obj.Properties) is float f)
                        starts[key] = f;
                }

                return (float progress) =>
                {
                    foreach (var (key, targetVal) in targets)
                    {
                        if (!starts.TryGetValue(key, out var startVal)) continue;
                        var p = propInfos.FirstOrDefault(pi =>
                            pi.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                        p?.SetValue(obj.Properties, Lerp(startVal, targetVal, progress));
                    }
                    obj.Updated = true;
                };
            });

        // ── swap image ping-pong (Aynshe technique, zero flicker) ─────────────

        public void SwapImage(string name, string newPath)
        {
            _lock.Wait();
            try
            {
                if (_objects.TryGetValue(name, out var obj))
                    obj.Properties.ImagePath = newPath;
            }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── ForceRefresh ──────────────────────────────────────────────────────

        public void ForceRefresh()
        {
            _lock.Wait();
            try { foreach (var o in _objects.Values) o.Updated = true; }
            finally { _lock.Release(); }
            RenderRequested?.Invoke();
        }

        // ── Accessors ─────────────────────────────────────────────────────────

        public IReadOnlyList<LayObject> GetOrderedByZ()
        {
            _lock.Wait();
            try { return _objects.Values.OrderBy(o => o.Z).ToList(); }
            finally { _lock.Release(); }
        }

        public bool Exists(string name)
        {
            _lock.Wait();
            try { return _objects.ContainsKey(name); }
            finally { _lock.Release(); }
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private async Task AnimateCore(string name, float durationSec,
                                        Action? onComplete, EasingMode easing,
                                        Func<LayObject, Action<float>> buildTick)
        {
            LayObject? obj;
            _lock.Wait();
            try { _objects.TryGetValue(name, out obj); }
            finally { _lock.Release(); }
            if (obj == null) return;

            // Cancel previous animation on this object
            obj.AnimCts?.Cancel();
            obj.AnimCts?.Dispose();
            var cts = new CancellationTokenSource();
            obj.AnimCts = cts;

            var tick = buildTick(obj);
            var start = DateTime.UtcNow;
            var duration = TimeSpan.FromSeconds(Math.Max(0.001f, durationSec));

            try
            {
                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var elapsed  = DateTime.UtcNow - start;
                    var progress = (float)Math.Min(1.0, elapsed.TotalSeconds / duration.TotalSeconds);
                    var eased    = ApplyEasing(progress, easing);
                    tick(eased);
                    RenderRequested?.Invoke();
                    if (progress >= 1f) break;
                    await Task.Delay(16, cts.Token);
                }
                onComplete?.Invoke();
            }
            catch (OperationCanceledException) { }
        }

        private static void MergeProperties(LayProperties dest, LayProperties src)
        {
            // Copy only explicitly set fields (non-default values from src)
            if (src.X          != 0f)    dest.X           = src.X;
            if (src.Y          != 0f)    dest.Y           = src.Y;
            if (src.W          != -1f)   dest.W           = src.W;
            if (src.H          != -1f)   dest.H           = src.H;
            dest.Show      = src.Show;
            if (src.Opacity    != 1f)    dest.Opacity     = src.Opacity;
            if (src.Anchor     != null)  dest.Anchor      = src.Anchor;
            if (src.ImagePath  != null)  dest.ImagePath   = src.ImagePath;
            if (src.LogoAlign  != null)  dest.LogoAlign   = src.LogoAlign;
            if (src.FitAndCrop)          dest.FitAndCrop  = true;
            if (src.FitInside)           dest.FitInside   = true;
            if (src.ColorHex   != null)  dest.ColorHex    = src.ColorHex;
            if (src.Text       != null)  dest.Text        = src.Text;
            if (src.Font       != "Arial") dest.Font      = src.Font;
            if (src.Size       != 20)    dest.Size        = src.Size;
            if (src.Color      != "FFFFFF") dest.Color    = src.Color;
            if (src.Align      != 7)     dest.Align       = src.Align;
            if (src.BorderSize != 2)     dest.BorderSize  = src.BorderSize;
            if (src.BorderColor != "000000") dest.BorderColor = src.BorderColor;
            if (src.Shad)                dest.Shad        = true;
            if (src.BlurEdges  != null)  dest.BlurEdges   = src.BlurEdges;
            if (src.FontScaleX != null)  dest.FontScaleX  = src.FontScaleX;
            if (src.FontScaleY != null)  dest.FontScaleY  = src.FontScaleY;
            if (src.LetterSpacing != null) dest.LetterSpacing = src.LetterSpacing;
            if (src.RotationX  != null)  dest.RotationX   = src.RotationX;
            if (src.RotationY  != null)  dest.RotationY   = src.RotationY;
            if (src.RotationZ  != null)  dest.RotationZ   = src.RotationZ;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float ApplyEasing(float t, EasingMode mode) => mode switch
        {
            EasingMode.EaseOut    => 1f - (1f - t) * (1f - t),
            EasingMode.EaseInOut  => t < 0.5f ? 2f * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 2) / 2f,
            EasingMode.EaseIn     => t * t,
            _                     => t  // Linear
        };
    }

    public enum EasingMode { Linear, EaseIn, EaseOut, EaseInOut }
}
