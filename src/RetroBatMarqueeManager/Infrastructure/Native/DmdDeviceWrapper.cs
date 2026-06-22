using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace RetroBatMarqueeManager.Infrastructure.Native
{
    /// <summary>
    /// Same rendering logic as Aynshe:
    ///   - DmdDevice64.dll  → Open / Close / Render_RGB24  (primary, handles ZeDMD internally)
    ///   - zedmd64.dll      → optional pre-boot calibration only (HwOpen → push params → HwClose)
    /// </summary>
    public class DmdDeviceWrapper : IDisposable
    {
        private readonly ILogger<DmdDeviceWrapper> _logger;

        // ── DmdDevice64.dll (dmdext) — primary rendering interface ──────────
        private IntPtr _dmdHandle = IntPtr.Zero;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int  OpenDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool CloseDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RenderDelegate(ushort width, ushort height, IntPtr buffer);

        private OpenDelegate?   _open;
        private CloseDelegate?  _close;
        private RenderDelegate? _render;

        // ── zedmd64.dll — pre-boot hardware calibration only ────────────────
        private IntPtr _zedmdHandle   = IntPtr.Zero;
        private IntPtr _zedmdInstance = IntPtr.Zero;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ZeDMD_GetInstanceDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool   ZeDMD_OpenDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_CloseDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_ClearScreenDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_EnableUpscalingDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_SetUsbPackageSizeDelegate(IntPtr h, ushort size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_SetPanelMinRefreshRateDelegate(IntPtr h, byte rate);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool   ZeDMD_SetDeviceDelegate(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_SetBrightnessDelegate(IntPtr h, byte v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_SaveSettingsDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void   ZeDMD_ResetDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort ZeDMD_GetWidthDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort ZeDMD_GetHeightDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort ZeDMD_GetUsbPackageSizeDelegate(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ZeDMD_GetFirmwareVersionDelegate(IntPtr h);

        private ZeDMD_GetInstanceDelegate?            _zGet;
        private ZeDMD_OpenDelegate?                   _zOpen;
        private ZeDMD_CloseDelegate?                  _zClose;
        private ZeDMD_ClearScreenDelegate?            _zClear;
        private ZeDMD_SetDeviceDelegate?              _zSetDevice;
        private ZeDMD_EnableUpscalingDelegate?        _zEnableUpscaling;
        private ZeDMD_SetUsbPackageSizeDelegate?      _zSetUsbPkg;
        private ZeDMD_SetPanelMinRefreshRateDelegate? _zSetRefreshRate;
        private ZeDMD_SetBrightnessDelegate?          _zSetBrightness;
        private ZeDMD_SaveSettingsDelegate?           _zSaveSettings;
        private ZeDMD_ResetDelegate?                  _zReset;
        private ZeDMD_GetWidthDelegate?               _zGetWidth;
        private ZeDMD_GetHeightDelegate?              _zGetHeight;
        private ZeDMD_GetUsbPackageSizeDelegate?      _zGetUsbPkg;
        private ZeDMD_GetFirmwareVersionDelegate?     _zGetFw;

        // ── Public state ─────────────────────────────────────────────────────
        public bool   IsLoaded         => _dmdHandle != IntPtr.Zero;
        public bool   IsZeDmdDllLoaded => _zedmdHandle != IntPtr.Zero;
        public string RenderMethodName { get; private set; } = string.Empty;
        public ushort ZeDmdWidth  { get; private set; }
        public ushort ZeDmdHeight { get; private set; }

        public DmdDeviceWrapper(ILogger<DmdDeviceWrapper> logger) => _logger = logger;

        // ─────────────────────────────────────────────────────────────────────
        // Load — DmdDevice64.dll first, then zedmd64.dll for calibration
        // ─────────────────────────────────────────────────────────────────────
        public bool Load(string folderPath)
        {
            LoadDmdDevice(folderPath);
            LoadZeDmdCalibration(folderPath);
            return IsLoaded;
        }

        private void LoadDmdDevice(string folderPath)
        {
            string dll  = Environment.Is64BitProcess ? "DmdDevice64.dll" : "DmdDevice.dll";
            string path = Path.Combine(folderPath, dll);
            if (!File.Exists(path)) path = Path.Combine(folderPath, "DmdDevice.dll");
            if (!File.Exists(path)) { _logger.LogError($"DmdDevice DLL not found: {folderPath}"); return; }

            try
            {
                _dmdHandle = NativeLibrary.Load(path);
                if (_dmdHandle == IntPtr.Zero) { _logger.LogError("Failed to load DmdDevice DLL."); return; }

                _open  = Fn<OpenDelegate>(_dmdHandle, "Open");
                _close = Fn<CloseDelegate>(_dmdHandle, "Close");

                string[] renders = { "Render_RGB24", "Render_RGB", "Render_RGBA", "Render",
                                     "PM_Render", "Render_16_Shades", "Render_4_Shades", "Render_Grey" };
                foreach (var r in renders)
                {
                    _render = Fn<RenderDelegate>(_dmdHandle, r);
                    if (_render != null) { RenderMethodName = r; break; }
                }

                if (_open == null || _close == null || _render == null)
                {
                    _logger.LogError("DmdDevice DLL missing required exports. Unloading.");
                    NativeLibrary.Free(_dmdHandle); _dmdHandle = IntPtr.Zero; return;
                }
                _logger.LogInformation($"DmdDevice DLL loaded from: {path} (render={RenderMethodName})");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading DmdDevice DLL: {ex.Message}");
                _dmdHandle = IntPtr.Zero;
            }
        }

        private void LoadZeDmdCalibration(string folderPath)
        {
            string dll  = Environment.Is64BitProcess ? "zedmd64.dll" : "zedmd.dll";
            string path = Path.Combine(folderPath, dll);
            if (!File.Exists(path))
            {
                var sibling = Path.Combine(Path.GetDirectoryName(folderPath) ?? folderPath, "zedmd");
                path = Path.Combine(sibling, dll);
            }
            if (!File.Exists(path)) { _logger.LogInformation("[ZeDMD] zedmd64.dll not found — calibration unavailable."); return; }

            try
            {
                _zedmdHandle     = NativeLibrary.Load(path);
                _zGet            = Fn<ZeDMD_GetInstanceDelegate>(_zedmdHandle, "ZeDMD_GetInstance");
                _zOpen           = Fn<ZeDMD_OpenDelegate>(_zedmdHandle, "ZeDMD_Open");
                _zClose          = Fn<ZeDMD_CloseDelegate>(_zedmdHandle, "ZeDMD_Close");
                _zClear          = Fn<ZeDMD_ClearScreenDelegate>(_zedmdHandle, "ZeDMD_ClearScreen");
                _zSetDevice      = Fn<ZeDMD_SetDeviceDelegate>(_zedmdHandle, "ZeDMD_SetDevice");
                _zEnableUpscaling= Fn<ZeDMD_EnableUpscalingDelegate>(_zedmdHandle, "ZeDMD_EnableUpscaling");
                _zSetUsbPkg      = Fn<ZeDMD_SetUsbPackageSizeDelegate>(_zedmdHandle, "ZeDMD_SetUsbPackageSize");
                _zSetRefreshRate = Fn<ZeDMD_SetPanelMinRefreshRateDelegate>(_zedmdHandle, "ZeDMD_SetPanelMinRefreshRate");
                _zSetBrightness  = Fn<ZeDMD_SetBrightnessDelegate>(_zedmdHandle, "ZeDMD_SetBrightness");
                _zSaveSettings   = Fn<ZeDMD_SaveSettingsDelegate>(_zedmdHandle, "ZeDMD_SaveSettings");
                _zReset          = Fn<ZeDMD_ResetDelegate>(_zedmdHandle, "ZeDMD_Reset");
                _zGetWidth       = Fn<ZeDMD_GetWidthDelegate>(_zedmdHandle, "ZeDMD_GetWidth");
                _zGetHeight      = Fn<ZeDMD_GetHeightDelegate>(_zedmdHandle, "ZeDMD_GetHeight");
                _zGetUsbPkg      = Fn<ZeDMD_GetUsbPackageSizeDelegate>(_zedmdHandle, "ZeDMD_GetUsbPackageSize");
                _zGetFw          = Fn<ZeDMD_GetFirmwareVersionDelegate>(_zedmdHandle, "ZeDMD_GetFirmwareVersion");
                _logger.LogInformation($"[ZeDMD] zedmd64.dll loaded — hardware calibration available.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ZeDMD] Failed to load zedmd64.dll: {ex.Message}");
                _zedmdHandle = IntPtr.Zero;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pre-boot calibration via zedmd64.dll (call BEFORE DmdDevice.Open)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens ZeDMD via zedmd64.dll for hardware calibration.
        /// Optionally tries a cached COM port first (fast reconnect).
        /// </summary>
        public bool HwOpen(string? cachedPort = null)
        {
            if (!IsZeDmdDllLoaded || _zGet == null || _zOpen == null) return false;
            try
            {
                _zedmdInstance = _zGet();
                if (_zedmdInstance == IntPtr.Zero) return false;

                bool ok = false;

                // Try cached port first (fast, avoids full COM scan)
                if (!string.IsNullOrEmpty(cachedPort) && _zSetDevice != null)
                {
                    _logger.LogInformation($"[ZeDMD] Trying cached port {cachedPort}...");
                    _zSetDevice(_zedmdInstance, cachedPort);
                    ok = _zOpen(_zedmdInstance);
                    if (!ok) _logger.LogWarning($"[ZeDMD] Cached port {cachedPort} failed — auto-detecting...");
                }

                if (!ok)
                {
                    _logger.LogInformation("[ZeDMD] Auto-detecting ZeDMD...");
                    ok = _zOpen(_zedmdInstance);
                }

                if (!ok) { _zedmdInstance = IntPtr.Zero; return false; }

                if (_zGetWidth  != null) ZeDmdWidth  = _zGetWidth(_zedmdInstance);
                if (_zGetHeight != null) ZeDmdHeight = _zGetHeight(_zedmdInstance);

                if (_zGetFw != null)
                    try { var p = _zGetFw(_zedmdInstance); if (p != IntPtr.Zero) _logger.LogInformation($"[ZeDMD] Firmware: {Marshal.PtrToStringAnsi(p)} | Panel: {ZeDmdWidth}x{ZeDmdHeight}"); } catch { }

                // Log current USB package size
                if (_zGetUsbPkg != null)
                    _logger.LogInformation($"[ZeDMD] Current UsbPackageSize: {_zGetUsbPkg(_zedmdInstance)}");

                return true;
            }
            catch (Exception ex) { _logger.LogError($"[ZeDMD] HwOpen error: {ex.Message}"); _zedmdInstance = IntPtr.Zero; return false; }
        }

        /// <summary>
        /// Pushes hardware calibration to ZeDMD firmware and saves permanently.
        /// Calls SaveSettings + Reset — firmware restarts with new settings.
        /// Caller must wait ~2s after this before DmdDevice.Open().
        /// </summary>
        public void PushHardwareCalibration(bool isHd, int brightness = -1, int usbPackageSizeOverride = -1, int refreshRateOverride = -1)
        {
            if (_zedmdInstance == IntPtr.Zero) return;
            try
            {
                // Clear screen (reset display state)
                _zClear?.Invoke(_zedmdInstance);
                _logger.LogInformation("[ZeDMD] ClearScreen sent.");

                // UsbPackageSize: firmware default is 64 (too small → causes sweep)
                // Config override via ZeDmdUsbPackageSize=, or auto: 512 for 128x32, 1024 for HD
                ushort usbPkg = usbPackageSizeOverride > 0
                    ? (ushort)usbPackageSizeOverride
                    : (ushort)(isHd ? 1024 : 512);
                _zSetUsbPkg?.Invoke(_zedmdInstance, usbPkg);
                _logger.LogInformation($"[ZeDMD] SetUsbPackageSize = {usbPkg}");

                // Panel min refresh rate (config override or skip)
                if (refreshRateOverride > 0)
                {
                    _zSetRefreshRate?.Invoke(_zedmdInstance, (byte)refreshRateOverride);
                    _logger.LogInformation($"[ZeDMD] SetPanelMinRefreshRate = {refreshRateOverride}Hz");
                }

                // EnableUpscaling ONLY for HD panels (256x64+)
                // On standard 128x32, upscaling causes sweep/distortion
                if (isHd)
                {
                    _zEnableUpscaling?.Invoke(_zedmdInstance);
                    _logger.LogInformation("[ZeDMD] EnableUpscaling (HD panel).");
                }

                // Optional brightness from config
                if (brightness >= 0 && brightness <= 15)
                {
                    _zSetBrightness?.Invoke(_zedmdInstance, (byte)brightness);
                    _logger.LogInformation($"[ZeDMD] Brightness = {brightness}");
                }

                // SAVE to firmware EEPROM (persistent across power cycles)
                _zSaveSettings?.Invoke(_zedmdInstance);
                _logger.LogInformation("[ZeDMD] SaveSettings → firmware EEPROM updated.");

                // RESET firmware to apply new settings
                _zReset?.Invoke(_zedmdInstance);
                _logger.LogInformation("[ZeDMD] Reset sent — firmware restarting with new settings...");
            }
            catch (Exception ex) { _logger.LogError($"[ZeDMD] PushHardwareCalibration error: {ex.Message}"); }
        }

        public void HwClose()
        {
            if (_zedmdInstance != IntPtr.Zero && _zClose != null)
            {
                try { _zClose(_zedmdInstance); } catch { }
                _zedmdInstance = IntPtr.Zero;
                _logger.LogInformation("[ZeDMD] HwClose — waiting for firmware restart...");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // DmdDevice standard API (Aynshe-compatible)
        // ─────────────────────────────────────────────────────────────────────
        public int Open()
        {
            if (!IsLoaded || _open == null) return -1;
            try { return _open(); }
            catch (Exception ex) { _logger.LogError($"DmdDevice Open() error: {ex.Message}"); return -1; }
        }

        public void Close()
        {
            if (!IsLoaded || _close == null) return;
            try { _close(); } catch { }
        }

        public void Render(ushort width, ushort height, byte[] buffer)
        {
            if (!IsLoaded || _render == null) return;
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try { _render(width, height, pin.AddrOfPinnedObject()); }
            catch { }
            finally { pin.Free(); }
        }

        public void EnableUpscaling()
        {
            if (_zedmdInstance != IntPtr.Zero) _zEnableUpscaling?.Invoke(_zedmdInstance);
        }

        // ─────────────────────────────────────────────────────────────────────
        private T? Fn<T>(IntPtr h, string name) where T : Delegate
        {
            try { if (NativeLibrary.TryGetExport(h, name, out var p)) return Marshal.GetDelegateForFunctionPointer<T>(p); }
            catch { }
            return null;
        }

        public void Dispose()
        {
            HwClose();
            if (_dmdHandle   != IntPtr.Zero) { NativeLibrary.Free(_dmdHandle);   _dmdHandle   = IntPtr.Zero; }
            if (_zedmdHandle != IntPtr.Zero) { NativeLibrary.Free(_zedmdHandle); _zedmdHandle = IntPtr.Zero; }
            GC.SuppressFinalize(this);
        }
    }
}
