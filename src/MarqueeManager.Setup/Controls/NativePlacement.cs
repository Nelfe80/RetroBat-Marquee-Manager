using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Places a borderless WPF window at exact pixel coordinates, exactly like the
/// runtime's MarqueeWindow does — bypassing WPF's DIP scaling so a zone tested here
/// is the zone the runtime will use.
/// </summary>
public static class NativePlacement
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;

    public static void Place(Window window, int x, int y, int width, int height)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(handle, HwndTopmost, x, y, width, height, SwpShowWindow);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
