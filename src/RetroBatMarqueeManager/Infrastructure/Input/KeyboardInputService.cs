using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Infrastructure.Input
{
    public class KeyboardInputService : IInputService
    {
        private readonly ILogger<KeyboardInputService> _logger;
        
        public event Action<int, int, bool>? OnMoveCommand;
        public event Action<double, bool>? OnScaleCommand;
        public event Action? OnVideoAdjustmentMode; // EN: Ctrl+V to enter video adjustment mode / FR: Ctrl+V pour entrer en mode ajustement vidéo
        public event Action? OnTogglePlayback;
        public event Action? OnTrimStart;
        public event Action? OnTrimEnd;

        // Detection rate limiter (Debounce)
        private DateTime _lastInputTime = DateTime.MinValue;
        private readonly TimeSpan _inputDelay = TimeSpan.FromMilliseconds(20); // 20ms delay = 50 updates/sec

        // Key States for One-Shot triggers
        private bool _wasPDown = false;
        private bool _wasIDown = false;
        private bool _wasODown = false;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt key
        private const int VK_SHIFT = 0x10; // Shift key
        
        // Custom Mapping: H(Left), J(Down), K(Right), U(Up)
        private const int VK_H = 0x48; // Left
        private const int VK_J = 0x4A; // Down
        private const int VK_K = 0x4B; // Right
        private const int VK_U = 0x55; // Up
        private const int VK_N = 0x4E; // Zoom
        private const int VK_B = 0x42; // Dezoom
        private const int VK_V = 0x56; // Video adjustment mode

        public KeyboardInputService(ILogger<KeyboardInputService> logger)
        {
            _logger = logger;
        }

        public void Update()
        {
            // Check limits first to avoid CPU spam if keys are held
            if ((DateTime.Now - _lastInputTime) < _inputDelay) return;

            // Check Modifiers (Ctrl OR Alt must be held)
            bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool altPressed = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            
            // EN: Check Ctrl+V for video adjustment mode (before other checks)
            // FR: Vérifier Ctrl+V pour mode ajustement vidéo (avant autres vérifications)
            if (ctrlPressed && !altPressed && !shiftPressed && (GetAsyncKeyState(VK_V) & 0x8000) != 0)
            {
                OnVideoAdjustmentMode?.Invoke();
                _lastInputTime = DateTime.Now;
                return; // Exit to avoid triggering other commands
            }
            
            if (!ctrlPressed && !altPressed) return;
            
            // Avoid conflict if both pressed? Prioritize Alt? Or treat as Alt?
            // User: "touche hotkey alt" -> imply separate function.
            // If both are pressed, we might trigger both?
            // Let's pass 'altPressed' as the flag. If ctrl+alt, it counts as logo move (Alt).

            int dx = 0;
            int dy = 0;
            int step = shiftPressed ? 50 : 5; // Shift = Turbo (50px), Normal = Precision (5px)

            if ((GetAsyncKeyState(VK_H) & 0x8000) != 0) dx -= step;
            if ((GetAsyncKeyState(VK_K) & 0x8000) != 0) dx += step;
            if ((GetAsyncKeyState(VK_U) & 0x8000) != 0) dy -= step;
            if ((GetAsyncKeyState(VK_J) & 0x8000) != 0) dy += step;

            if (dx != 0 || dy != 0)
            {
                OnMoveCommand?.Invoke(dx, dy, altPressed);
                _lastInputTime = DateTime.Now;
                // _logger.LogTrace($"Input Detected: dx={dx}, dy={dy}, alt={altPressed}");
            }

            // Check Zoom/Dezoom (CTRL+N, CTRL+B, ALT+N, ALT+B)
            double scaleDelta = 0.0;
            if ((GetAsyncKeyState(VK_N) & 0x8000) != 0) scaleDelta = 0.1; // Zoom
            if ((GetAsyncKeyState(VK_B) & 0x8000) != 0) scaleDelta = -0.1; // Dezoom

            if (scaleDelta != 0.0)
            {
                OnScaleCommand?.Invoke(scaleDelta, altPressed);
                _lastInputTime = DateTime.Now;
                // _logger.LogTrace($"Scale Input Detected: delta={scaleDelta}, alt={altPressed}");
            }

            // Video Trimming Shortcuts
            // Only trigger if CTRL is held (and not Alt/Shift preferably, similar to VideoMode)
            if (ctrlPressed && !altPressed && !shiftPressed)
            {
                // Ctrl+P: Toggle Playback (One-Shot)
                bool isPDown = (GetAsyncKeyState(0x50) & 0x8000) != 0; // VK_P
                if (isPDown && !_wasPDown)
                {
                    OnTogglePlayback?.Invoke();
                    _lastInputTime = DateTime.Now;
                }
                _wasPDown = isPDown;
                
                // Ctrl+I: Trim Start (One-Shot)
                bool isIDown = (GetAsyncKeyState(0x49) & 0x8000) != 0; // VK_I
                if (isIDown && !_wasIDown)
                {
                    OnTrimStart?.Invoke();
                    _lastInputTime = DateTime.Now;
                }
                _wasIDown = isIDown;
                
                // Ctrl+O: Trim End (One-Shot)
                bool isODown = (GetAsyncKeyState(0x4F) & 0x8000) != 0; // VK_O
                if (isODown && !_wasODown)
                {
                    OnTrimEnd?.Invoke();
                    _lastInputTime = DateTime.Now;
                }
                _wasODown = isODown;
            }
            else
            {
                // Reset states if CTRL is released to prevent getting stuck
                // Actually, we should track key up even if Ctrl is held, which we do above.
                // But if Ctrl is released while P is held, we should probably reset state to allow re-trigger if Ctrl pressed again?
                // For safety, if Ctrl is NOT pressed, we can assume 'Hotkeys' are not down in a valid combo.
                // But better to simply respect the key physical state:
                _wasPDown = (GetAsyncKeyState(0x50) & 0x8000) != 0;
                _wasIDown = (GetAsyncKeyState(0x49) & 0x8000) != 0;
                _wasODown = (GetAsyncKeyState(0x4F) & 0x8000) != 0;
            }
        }
    }
}
