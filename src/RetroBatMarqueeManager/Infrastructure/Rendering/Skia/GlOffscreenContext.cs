using System.Runtime.InteropServices;

namespace RetroBatMarqueeManager.Infrastructure.Rendering.Skia;

/// <summary>
/// Minimal offscreen desktop-OpenGL (WGL) context, created purely to host a
/// SkiaSharp GPU <c>GRContext</c>. A hidden 1×1 window supplies a device context
/// with a valid OpenGL pixel format; we never present to it — Skia renders into its
/// own offscreen GPU surface and the frame is read back to CPU for WPF.
///
/// The context is THREAD-AFFINE: <see cref="Create"/> makes the context current on
/// the calling thread and it stays current there until <see cref="Dispose"/>. Both
/// MUST run on the render thread. All failures throw, so the caller can cleanly fall
/// back to CPU rasterization. Pure P/Invoke on the system opengl32.dll — no extra
/// NuGet dependency and nothing new to bundle in the single-file publish.
/// </summary>
internal sealed class GlOffscreenContext : IDisposable
{
    private IntPtr _hwnd;
    private IntPtr _hdc;
    private IntPtr _hglrc;
    private readonly string _className;
    private readonly IntPtr _hInstance;

    private GlOffscreenContext(IntPtr hwnd, IntPtr hdc, IntPtr hglrc, string className, IntPtr hInstance)
    {
        _hwnd = hwnd;
        _hdc = hdc;
        _hglrc = hglrc;
        _className = className;
        _hInstance = hInstance;
    }

    public static GlOffscreenContext Create()
    {
        var className = "RBMQ_GL_" + Guid.NewGuid().ToString("N");
        var hInstance = GetModuleHandle(null);

        var wc = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = className,
        };
        if (RegisterClass(ref wc) == 0)
            throw new InvalidOperationException($"RegisterClass failed (0x{Marshal.GetLastWin32Error():X8}).");

        var hwnd = CreateWindowEx(0, className, "rbmq-gl", WS_OVERLAPPED,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            UnregisterClass(className, hInstance);
            throw new InvalidOperationException($"CreateWindowEx failed (0x{Marshal.GetLastWin32Error():X8}).");
        }

        var hdc = GetDC(hwnd);
        if (hdc == IntPtr.Zero)
        {
            DestroyWindow(hwnd);
            UnregisterClass(className, hInstance);
            throw new InvalidOperationException("GetDC failed.");
        }

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = PFD_MAIN_PLANE,
        };

        var pf = ChoosePixelFormat(hdc, ref pfd);
        if (pf == 0 || !SetPixelFormat(hdc, pf, ref pfd))
        {
            ReleaseDC(hwnd, hdc);
            DestroyWindow(hwnd);
            UnregisterClass(className, hInstance);
            throw new InvalidOperationException($"ChoosePixelFormat/SetPixelFormat failed (0x{Marshal.GetLastWin32Error():X8}).");
        }

        var hglrc = wglCreateContext(hdc);
        if (hglrc == IntPtr.Zero || !wglMakeCurrent(hdc, hglrc))
        {
            if (hglrc != IntPtr.Zero) wglDeleteContext(hglrc);
            ReleaseDC(hwnd, hdc);
            DestroyWindow(hwnd);
            UnregisterClass(className, hInstance);
            throw new InvalidOperationException($"wglCreateContext/wglMakeCurrent failed (0x{Marshal.GetLastWin32Error():X8}).");
        }

        return new GlOffscreenContext(hwnd, hdc, hglrc, className, hInstance);
    }

    private static readonly IntPtr _opengl32 = LoadLibrary("opengl32.dll");

    /// <summary>GL procedure loader for <c>GRGlInterface.Create</c>: extensions come
    /// from wglGetProcAddress, core 1.1 entry points from opengl32.dll (wglGetProcAddress
    /// returns them as null/1/2/3/-1 on many drivers).</summary>
    public static IntPtr GetProcAddress(string name)
    {
        var p = wglGetProcAddress(name);
        if (p == IntPtr.Zero || p == (IntPtr)1 || p == (IntPtr)2 || p == (IntPtr)3 || p == (IntPtr)(-1))
            p = _opengl32 != IntPtr.Zero ? GetProcAddress(_opengl32, name) : IntPtr.Zero;
        return p;
    }

    public void Dispose()
    {
        try { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); } catch { /* best effort */ }
        try { if (_hglrc != IntPtr.Zero) wglDeleteContext(_hglrc); } catch { /* best effort */ }
        try { if (_hdc != IntPtr.Zero && _hwnd != IntPtr.Zero) ReleaseDC(_hwnd, _hdc); } catch { /* best effort */ }
        try { if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd); } catch { /* best effort */ }
        try { UnregisterClass(_className, _hInstance); } catch { /* best effort */ }
        _hglrc = _hdc = _hwnd = IntPtr.Zero;
    }

    // ---- Win32 / WGL interop ----

    private const uint WS_OVERLAPPED = 0x00000000;
    private const byte PFD_TYPE_RGBA = 0;
    private const byte PFD_MAIN_PLANE = 0;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Kept alive for the process: the class references this as its window procedure.
    private static readonly WndProcDelegate _wndProc = DefWindowProcW;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr wglGetProcAddress(string name);
}
