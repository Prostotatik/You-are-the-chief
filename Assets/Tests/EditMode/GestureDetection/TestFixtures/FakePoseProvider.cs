using System;

namespace GestureDetection.Tests
{
    public class FakePoseProvider : IPoseProvider
    {
        public event Action<LandmarkFrame> OnLandmarkFrame;
        public event Action OnCameraUnavailable;

        public void PushFrame(LandmarkFrame frame) => OnLandmarkFrame?.Invoke(frame);
        public void PushCameraUnavailable() => OnCameraUnavailable?.Invoke();
    }
}
