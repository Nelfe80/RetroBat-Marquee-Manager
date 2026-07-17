using System.IO;
using System.Runtime.InteropServices;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// REAL ZeDMD health probe through libzedmd (tools\zedmd\zedmd64.dll) — the
/// same library the runtime uses. ZeDMD_Open performs the actual serial
/// handshake: the lib sends the ZeDMD magic control frame ("ZeDMD" header +
/// handshake command) and only a ZeDMD firmware answers with its identity
/// (panel width/height, firmware version). No answer = no panel — a free COM
/// port or the LedManager Pico never passes this test.
/// The probe opens the port briefly then closes it; call it only from the
/// Setup (the runtime owns the panel while it runs).
/// </summary>
public static class ZeDmdProbe
{
    public sealed record Result(bool Found, string? Port, int Width, int Height, string? Firmware, string? Error);

    private delegate IntPtr GetInstanceDelegate();
    private delegate bool OpenDelegate(IntPtr handle);
    private delegate void CloseDelegate(IntPtr handle);
    private delegate bool SetDeviceDelegate(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string device);
    private delegate ushort GetU16Delegate(IntPtr handle);
    private delegate IntPtr GetPtrDelegate(IntPtr handle);

    /// <summary>Performs the handshake (a few seconds worst case: libzedmd scans
    /// the COM ports when no port is forced). Run OFF the UI thread.</summary>
    public static Result Probe(string pluginRoot, string? forcedPort = null)
    {
        var dll = Path.Combine(pluginRoot, "tools", "zedmd", Environment.Is64BitProcess ? "zedmd64.dll" : "zedmd.dll");
        if (!File.Exists(dll))
        {
            return new Result(false, null, 0, 0, null,
                Localization.L.T("zedmd64.dll introuvable (tools\\zedmd)", "zedmd64.dll not found (tools\\zedmd)"));
        }

        var library = IntPtr.Zero;
        try
        {
            library = NativeLibrary.Load(dll);
            var getInstance = Get<GetInstanceDelegate>(library, "ZeDMD_GetInstance");
            var open = Get<OpenDelegate>(library, "ZeDMD_Open");
            var close = Get<CloseDelegate>(library, "ZeDMD_Close");
            if (getInstance == null || open == null || close == null)
            {
                return new Result(false, null, 0, 0, null, "exports libzedmd manquants");
            }

            var instance = getInstance();
            if (instance == IntPtr.Zero)
            {
                return new Result(false, null, 0, 0, null, "ZeDMD_GetInstance");
            }

            if (forcedPort is { Length: > 0 })
            {
                Get<SetDeviceDelegate>(library, "ZeDMD_SetDevice")?.Invoke(instance, forcedPort);
            }

            // the HANDSHAKE: magic "ZeDMD" frame out, identity frame back
            if (!open(instance))
            {
                return new Result(false, forcedPort, 0, 0, null, null);
            }

            var width = Get<GetU16Delegate>(library, "ZeDMD_GetWidth")?.Invoke(instance) ?? 0;
            var height = Get<GetU16Delegate>(library, "ZeDMD_GetHeight")?.Invoke(instance) ?? 0;
            var firmwarePtr = Get<GetPtrDelegate>(library, "ZeDMD_GetFirmwareVersion")?.Invoke(instance) ?? IntPtr.Zero;
            var devicePtr = Get<GetPtrDelegate>(library, "ZeDMD_GetDevice")?.Invoke(instance) ?? IntPtr.Zero;
            var firmware = firmwarePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(firmwarePtr) : null;
            var device = devicePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(devicePtr) : forcedPort;
            close(instance);

            return new Result(true, device, width, height, firmware, null);
        }
        catch (Exception ex)
        {
            return new Result(false, forcedPort, 0, 0, null, ex.Message);
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                try { NativeLibrary.Free(library); } catch { /* leave loaded */ }
            }
        }
    }

    private static T? Get<T>(IntPtr library, string name) where T : class
        => NativeLibrary.TryGetExport(library, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address) as T
            : null;
}
