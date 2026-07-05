using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RetroBatMarqueeManager.Application.Services;

public sealed class DmdFrameRenderer
{
    private readonly ILogger<DmdFrameRenderer> _logger;

    public DmdFrameRenderer(ILogger<DmdFrameRenderer> logger) => _logger = logger;

    public byte[] RenderImage(string path, int width, int height)
    {
        try
        {
            using var source = Image.FromFile(path);
            using var frame = DrawContained(source, width, height);
            return ToRgb24(frame);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to render DMD image {Path}", path);
            return Array.Empty<byte>();
        }
    }

    public IReadOnlyList<(byte[] Pixels, int DelayMs)> RenderAnimation(string path, int width, int height)
    {
        var result = new List<(byte[], int)>();
        try
        {
            using var source = Image.FromFile(path);
            var dimension = new FrameDimension(source.FrameDimensionsList[0]);
            var count = source.GetFrameCount(dimension);
            var delays = ReadGifDelays(source, count);
            for (var index = 0; index < count; index++)
            {
                source.SelectActiveFrame(dimension, index);
                using var frame = DrawContained(source, width, height);
                result.Add((ToRgb24(frame), delays[index]));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to decode DMD animation {Path}", path);
        }
        return result;
    }

    public byte[] RenderText(
        string title,
        string detail,
        string? badgePath,
        int width,
        int height,
        string? backgroundPath = null,
        IReadOnlyList<string>? rightBadgePaths = null,
        string? detailColor = null)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        DrawTextBackground(graphics, backgroundPath, width, height);
        using (var shade = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
            graphics.FillRectangle(shade, 0, 0, width, height);

        var textX = 2;
        if (!string.IsNullOrWhiteSpace(badgePath) && File.Exists(badgePath))
        {
            try
            {
                using var badge = Image.FromFile(badgePath);
                var badgeSize = Math.Min(height, Math.Max(12, width / 4));
                graphics.DrawImage(badge, new Rectangle(0, 0, badgeSize, height));
                textX = badgeSize + 2;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Badge unavailable for DMD notification: {Path}", badgePath);
            }
        }

        var (displayTitle, displayDetail) = SplitTitleDetail(title, detail);
        var rightReserved = IsLeaderboardTitle(displayTitle) ? 0 : DrawRightBadges(graphics, rightBadgePaths, width, height);
        var available = Math.Max(8, width - textX - rightReserved - 1);
        if (IsLeaderboardTitle(displayTitle) && displayDetail.Contains("  ", StringComparison.Ordinal))
        {
            DrawLeaderboardText(graphics, displayTitle, displayDetail, textX, available, height);
            return ToRgb24(bitmap);
        }

        using var titleFont = FitFont(graphics, displayTitle, FontFamily.GenericSansSerif, FontStyle.Bold, available, Math.Max(6, height * 0.30f), Math.Max(6, height * 0.20f));
        using var detailFont = FitFont(graphics, displayDetail, FontFamily.GenericSansSerif, FontStyle.Bold, available, Math.Max(11, height * 0.60f), Math.Max(7, height * 0.34f));
        using var titleBrush = new SolidBrush(Color.White);
        using var detailBrush = new SolidBrush(ParseColor(detailColor, Color.Gold));
        using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        graphics.DrawString(TrimToWidth(graphics, displayTitle, titleFont, available), titleFont, titleBrush, new RectangleF(textX, 0, available, height * 0.34f), format);
        graphics.DrawString(TrimToWidth(graphics, displayDetail, detailFont, available), detailFont, detailBrush, new RectangleF(textX, height * 0.34f, available, height * 0.66f), format);
        return ToRgb24(bitmap);
    }

    private static bool IsLeaderboardTitle(string title)
        => title.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase) ||
           title.Equals("LEADERBOARD", StringComparison.OrdinalIgnoreCase);

    private static void DrawLeaderboardText(Graphics graphics, string title, string detail, int x, int width, int height)
    {
        var (time, rank, user) = ParseLeaderboardDetail(detail);
        var topHeight = height / 2f;
        var bottomHeight = height - topHeight;

        // Equal 50/50 column split — both rows share the same left/right boundaries.
        var leftWidth = Math.Max(1, (int)Math.Round(width * 0.50f));
        var rightWidth = Math.Max(1, width - leftWidth);

        // Font sizes maximised to half-DMD height; FitFont reduces if text too wide.
        // Top row (rank + user): slightly smaller to leave room for longer usernames.
        // Bottom row (time + SPEEDRUN): time as large as possible, SPEEDRUN as label.
        using var rankFont = FitFont(graphics, rank,       FontFamily.GenericSansSerif, FontStyle.Bold, leftWidth,  Math.Max(13, height * 0.52f), Math.Max(8, height * 0.28f));
        using var userFont = FitFont(graphics, user,       FontFamily.GenericSansSerif, FontStyle.Bold, rightWidth, Math.Max(12, height * 0.50f), Math.Max(7, height * 0.24f));
        using var timeFont = FitFont(graphics, time,       FontFamily.GenericSansSerif, FontStyle.Bold, leftWidth,  Math.Max(13, height * 0.54f), Math.Max(8, height * 0.28f));
        using var typeFont = FitFont(graphics, "SPEEDRUN", FontFamily.GenericSansSerif, FontStyle.Bold, rightWidth, Math.Max(9,  height * 0.42f), Math.Max(6, height * 0.22f));

        using var rankBrush = new SolidBrush(Color.Gold);
        using var userBrush = new SolidBrush(Color.White);
        using var timeBrush = new SolidBrush(Color.DeepSkyBlue);
        using var typeBrush = new SolidBrush(Color.White);
        using var near = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var far  = new StringFormat { Alignment = StringAlignment.Far,  LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        // Row 0 (top):    rank LEFT (Gold)       | user RIGHT (White)
        graphics.DrawString(TrimToWidth(graphics, rank,       rankFont, leftWidth),  rankFont, rankBrush, new RectangleF(x,             0,         leftWidth,  topHeight),    near);
        graphics.DrawString(TrimToWidth(graphics, user,       userFont, rightWidth), userFont, userBrush, new RectangleF(x + leftWidth, 0,         rightWidth, topHeight),    far);
        // Row 1 (bottom): time LEFT (DeepSkyBlue) | SPEEDRUN RIGHT (White)
        graphics.DrawString(TrimToWidth(graphics, time,       timeFont, leftWidth),  timeFont, timeBrush, new RectangleF(x,             topHeight, leftWidth,  bottomHeight), near);
        graphics.DrawString(TrimToWidth(graphics, "SPEEDRUN", typeFont, rightWidth), typeFont, typeBrush, new RectangleF(x + leftWidth, topHeight, rightWidth, bottomHeight), far);
    }

    private static (string Time, string Rank, string User) ParseLeaderboardDetail(string detail)
    {
        var parts = (detail ?? string.Empty).Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var time = parts.Length > 0 ? parts[0] : "00:00.00";
        var rank = "#0001";
        var user = "PLAYER";
        if (parts.Length > 1)
        {
            var reference = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (reference.Length > 0) rank = reference[0];
            if (reference.Length > 1) user = string.Join(' ', reference.Skip(1));
        }
        return (time, rank, user);
    }

    private int DrawRightBadges(Graphics graphics, IReadOnlyList<string>? paths, int width, int height)
    {
        var valid = paths?
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray() ?? Array.Empty<string>();
        if (valid.Length == 0) return 0;

        var badgeSize = Math.Max(1, (int)Math.Floor(height * 0.90));
        var spacing = 1;
        var totalWidth = valid.Length * badgeSize + (valid.Length - 1) * spacing;
        var x = width - totalWidth;
        var y = (height - badgeSize) / 2;
        foreach (var path in valid)
        {
            try
            {
                using var badge = Image.FromFile(path);
                graphics.DrawImage(badge, new Rectangle(x, y, badgeSize, badgeSize));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DMD status badge unavailable: {Path}", path);
            }
            x += badgeSize + spacing;
        }
        return totalWidth + 2;
    }

    private static Color ParseColor(string? value, Color fallback)
        => value?.Trim().ToLowerInvariant() switch
        {
            "hardcore" or "blue" => Color.DeepSkyBlue,
            "softcore" or "gray" or "grey" => Color.LightGray,
            "leaderboards" or "darkblue" or "dark-blue" => Color.DodgerBlue,
            _ => fallback
        };

    private void DrawTextBackground(Graphics graphics, string? path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || IsVideo(path)) return;
        try
        {
            using var source = Image.FromFile(path);
            DrawContained(graphics, source, width, height);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DMD text background unavailable: {Path}", path);
        }
    }

    private static Bitmap DrawContained(Image source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Black);
        DrawContained(graphics, source, width, height);
        return result;
    }

    private static void DrawContained(Graphics graphics, Image source, int width, int height)
    {
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var scale = Math.Min((float)width / source.Width, (float)height / source.Height);
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        graphics.DrawImage(source, (width - targetWidth) / 2, (height - targetHeight) / 2, targetWidth, targetHeight);
    }

    private static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ToRgb24(Bitmap bitmap)
    {
        var result = new byte[bitmap.Width * bitmap.Height * 3];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var source = new byte[Math.Abs(data.Stride) * bitmap.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, source, 0, source.Length);
            var output = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var input = y * data.Stride + x * 4;
                result[output++] = source[input + 2];
                result[output++] = source[input + 1];
                result[output++] = source[input];
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return result;
    }

    private static int[] ReadGifDelays(Image image, int count)
    {
        var delays = Enumerable.Repeat(100, count).ToArray();
        try
        {
            var item = image.GetPropertyItem(0x5100);
            if (item == null) return delays;
            var values = item.Value ?? Array.Empty<byte>();
            for (var index = 0; index < count && index * 4 + 3 < values.Length; index++)
                delays[index] = Math.Clamp(BitConverter.ToInt32(values, index * 4) * 10, 20, 10_000);
        }
        catch { }
        return delays;
    }

    private static string TrimToWidth(Graphics graphics, string? value, Font font, int width)
    {
        var text = value ?? string.Empty;
        if (graphics.MeasureString(text, font).Width <= width) return text;
        while (text.Length > 1 && graphics.MeasureString(text + "...", font).Width > width) text = text[..^1];
        return text + "...";
    }

    private static (string Title, string Detail) SplitTitleDetail(string title, string detail)
    {
        if (!string.IsNullOrWhiteSpace(detail) && detail.Contains('\n'))
        {
            var parts = detail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2) return (parts[0], parts[1]);
        }

        return ((title ?? string.Empty).Trim(), (detail ?? string.Empty).Trim());
    }

    private static Font FitFont(Graphics graphics, string text, FontFamily family, FontStyle style, int width, float preferredSize, float minimumSize)
    {
        var size = preferredSize;
        while (size > minimumSize)
        {
            using var probe = new Font(family, size, style, GraphicsUnit.Pixel);
            if (graphics.MeasureString(text, probe).Width <= width) break;
            size -= 1;
        }

        return new Font(family, Math.Max(minimumSize, size), style, GraphicsUnit.Pixel);
    }
}
