using System;

namespace GestureDetection
{
    public interface IPoseProvider
    {
        event Action<LandmarkFrame> OnLandmarkFrame;
        event Action OnCameraUnavailable;

        // True once OnCameraUnavailable has fired (no device found at startup, or a
        // mid-session disconnect was detected). Lets a consumer that subscribes AFTER
        // the event already fired - e.g. due to Unity's Start() ordering between this
        // provider and whoever calls Initialize() - discover the current state instead
        // of missing the event entirely.
        bool IsCameraUnavailable { get; }
    }
}
