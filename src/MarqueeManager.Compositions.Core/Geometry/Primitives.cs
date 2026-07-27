namespace MarqueeManager.Compositions.Core.Geometry;

/// <summary>Integer pixel dimensions of a media or a surface target.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public double Ratio => Height == 0 ? 0 : (double)Width / Height;
    public bool IsValid => Width > 0 && Height > 0;
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>A rectangle in a continuous coordinate space (source pixels or target
/// pixels depending on context). Kept in doubles because framing math produces
/// sub-pixel edges that the renderer rounds, not the domain.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public override string ToString() => $"({X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##})";
}

/// <summary>A rectangle expressed as fractions (0..1) of some reference frame —
/// used for protected regions, which travel with the source content.</summary>
public readonly record struct RelativeRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Letterbox/pillarbox gaps left on the target when the framing does not
/// cover it, in target pixels. All values are non-negative.</summary>
public readonly record struct Padding(double Left, double Top, double Right, double Bottom)
{
    public static readonly Padding Zero = new(0, 0, 0, 0);
    public bool IsEmpty => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;
}
