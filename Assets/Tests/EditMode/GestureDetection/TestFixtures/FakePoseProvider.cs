using System;

namespace GestureDetection.Tests
{
    public class FakePoseProvider : IPoseProvider
    {
        public event Action<LandmarkFrame> OnLandmarkFrame;
        public event Action OnCameraUnavailable;

        public bool IsCameraUnavailable { get; private set; }

        public void PushFrame(LandmarkFrame frame) => OnLandmarkFrame?.Invoke(frame);

        public void PushCameraUnavailable()
        {
            IsCameraUnavailable = true;
            OnCameraUnavailable?.Invoke();
        }
    }
}
