using System;

namespace GestureDetection
{
    public interface IGestureDetector
    {
        event Action<GestureType> OnGestureRecognized;
        event Action<GestureType, float> OnGestureProgress;
        event Action OnCameraUnavailable;
    }
}
