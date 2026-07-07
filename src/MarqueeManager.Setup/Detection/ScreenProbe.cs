using System.Runtime.InteropServices;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace MarqueeManager.Setup.Detection;

public enum TouchSupport
{
    Unknown,
    None,
    Touch
}

/// <summary>
/// One physical screen as the runtime sees it: <see cref="Index"/> is the position in
/// Screen.AllScreens, which is exactly what MarqueeScreen/TopperScreen/... expect.
/// </summary>
public sealed record ScreenInfo(
    int Index,
    string DeviceName,
    bool Primary,
    System.Drawing.Rectangle Bounds,
    System.Drawing.Rectangle WorkingArea,
    TouchSupport Touch)
{
    public double Ratio => Bounds.Height == 0 ? 0 : Math.Round((double)Bounds.Width / Bounds.Height, 2);

    public string Orientation => Bounds.Width >= Bounds.Height
        ? (Ratio >= 3 ? "bandeau" : "paysage")
        : "portrait";

    /// <summary>Suggested surface, following the detection report examples of the spec.</summary>
    public string Suggestion
    {
        get
        {
            if (Primary)
            {
                return "écran principal RetroBat — à laisser libre";
            }

            if (Ratio >= 3)
            {
                return "suggéré : marquee";
            }

            if (Orientation == "portrait")
            {
                return "suggéré : écran vertical partagé (topper + marquee + iccard)";
            }

            if (Touch == TouchSupport.Touch)
            {
                return "suggéré : instruction card tactile";
            }

            if (Ratio >= 2.2)
            {
                return "suggéré : marquee ou instruction card";
            }

            return "suggéré : topper, instruction card ou DMD virtuel";
        }
    }

    public string Describe()
        => $"Écran {Index} : {Bounds.Width}x{Bounds.Height}, ratio {Ratio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
           + (Primary ? ", principal" : "")
           + Touch switch
           {
               TouchSupport.Touch => ", tactile",
               TouchSupport.None => "",
               _ => ", tactile inconnu"
           }
           + $", {Suggestion}.";
}

public static class ScreenProbe
{
    /// <summary>
    /// Enumerates screens in the same order as the runtime (Screen.AllScreens),
    /// enriched with per-monitor touch detection when Windows exposes pointer devices.
    /// </summary>
    public static IReadOnlyList<ScreenInfo> Detect()
    {
        var touchMonitors = DetectTouchMonitors();
        var screens = WinFormsScreen.AllScreens;
        var result = new List<ScreenInfo>(screens.Length);
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var touch = TouchSupport.Unknown;
            if (touchMonitors != null)
            {
                var center = new Point(
                    screen.Bounds.Left + screen.Bounds.Width / 2,
                    screen.Bounds.Top + screen.Bounds.Height / 2);
                var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
                touch = touchMonitors.Contains(monitor) ? TouchSupport.Touch : TouchSupport.None;
            }

            result.Add(new ScreenInfo(i, screen.DeviceName, screen.Primary, screen.Bounds, screen.WorkingArea, touch));
        }

        return result;
    }

    /// <summary>HMONITOR handles that have at least one touch digitizer attached, or null if unavailable.</summary>
    private static HashSet<IntPtr>? DetectTouchMonitors()
    {
        try
        {
            uint count = 0;
            if (!GetPointerDevices(ref count, null))
            {
                return null;
            }

            if (count == 0)
            {
                return new HashSet<IntPtr>();
            }

            var devices = new PointerDeviceInfo[count];
            if (!GetPointerDevices(ref count, devices))
            {
                return null;
            }

            var monitors = new HashSet<IntPtr>();
            foreach (var device in devices)
            {
                if (device.PointerDeviceType == PointerDeviceTypeTouch)
                {
                    monitors.Add(device.Monitor);
                }
            }

            return monitors;
        }
        catch
        {
            return null;
        }
    }

    private const int PointerDeviceTypeTouch = 0x00000003;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PointerDeviceInfo
    {
        public uint DisplayOrientation;
        public IntPtr Device;
        public int PointerDeviceType;
        public IntPtr Monitor;
        public uint StartingCursorId;
        public ushort MaxActiveContacts;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 520)]
        public string ProductString;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetPointerDevices(ref uint deviceCount, [Out] PointerDeviceInfo[]? pointerDevices);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);
}
