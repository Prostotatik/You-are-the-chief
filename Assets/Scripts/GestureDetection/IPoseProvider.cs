using System;

namespace GestureDetection
{
    public interface IPoseProvider
    {
        event Action<LandmarkFrame> OnLandmarkFrame;
        event Action OnCameraUnavailable;
    }
}
