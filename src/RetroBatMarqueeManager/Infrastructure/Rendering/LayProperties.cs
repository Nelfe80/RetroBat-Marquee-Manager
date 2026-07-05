namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// ISO ra.lua properties bag — covers image, shape, text and gif objects.
    /// </summary>
    public class LayProperties
    {
        // ── Common ───────────────────────────────────────────────────────────
        public float X         { get; set; }
        public float Y         { get; set; }
        /// <summary>Width. -1 = preserve aspect ratio (auto).</summary>
        public float W         { get; set; } = -1f;
        /// <summary>Height. -1 = preserve aspect ratio (auto).</summary>
        public float H         { get; set; } = -1f;
        public bool  Show      { get; set; } = true;
        /// <summary>0.0 transparent → 1.0 opaque. ISO opacity_decimal in Lua.</summary>
        public float Opacity   { get; set; } = 1f;

        // ── Anchor (semantic placement — resolver calculates X/Y) ─────────────
        /// <summary>
        /// Optional semantic anchor. When set, X/Y are treated as offsets from anchor.
        /// Values: top-left | top-center | top-right |
        ///         center-left | center | center-right |
        ///         bottom-left | bottom-center | bottom-right
        /// </summary>
        public string? Anchor  { get; set; }

        // ── Image / Gif ──────────────────────────────────────────────────────
        public string? ImagePath  { get; set; }
        /// <summary>ISO logo_align: left | center | right</summary>
        public string? LogoAlign  { get; set; }
        /// <summary>Scale-to-fill with center crop (fills bounds, may cut edges).</summary>
        public bool    FitAndCrop { get; set; }
        /// <summary>Scale-to-fit with letterbox (preserves ratio, adds black bars).</summary>
        public bool    FitInside  { get; set; }

        // ── Shape ────────────────────────────────────────────────────────────
        /// <summary>Background color hex RRGGBB (e.g. "FF0000").</summary>
        public string? ColorHex   { get; set; }

        // ── Text ─────────────────────────────────────────────────────────────
        public string? Text         { get; set; }
        public string  Font         { get; set; } = "Arial";
        public int     Size         { get; set; } = 20;
        /// <summary>Foreground color hex RRGGBB.</summary>
        public string  Color        { get; set; } = "FFFFFF";
        /// <summary>
        /// ASS numpad alignment:
        /// 7=top-left  8=top-center  9=top-right
        /// 4=mid-left  5=center      6=mid-right
        /// 1=bot-left  2=bot-center  3=bot-right
        /// </summary>
        public int     Align        { get; set; } = 7;
        public int     BorderSize   { get; set; } = 2;
        public string  BorderColor  { get; set; } = "000000";
        public bool    Shad         { get; set; }
        public float?  BlurEdges    { get; set; }
        public float?  FontScaleX   { get; set; }
        public float?  FontScaleY   { get; set; }
        public float?  LetterSpacing { get; set; }
        public float?  RotationX    { get; set; }
        public float?  RotationY    { get; set; }
        public float?  RotationZ    { get; set; }

        public LayProperties Clone() => (LayProperties)MemberwiseClone();
    }
}
