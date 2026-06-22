using System;

namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IInputService
    {
        void Update();
        event Action<int, int, bool> OnMoveCommand; // deltaX, deltaY, isAlt
        event Action<double, bool> OnScaleCommand; // scaleDelta, isAlt
        event Action OnVideoAdjustmentMode; // EN: Ctrl+V video adjustment / FR: Ctrl+V ajustement vidéo
        event Action OnTogglePlayback; // EN: Ctrl+P toggle playback / FR: Ctrl+P basculer lecture
        event Action OnTrimStart; // EN: Ctrl+I set start time / FR: Ctrl+I définir début
        event Action OnTrimEnd; // EN: Ctrl+O set end time / FR: Ctrl+O définir fin
    }
}
