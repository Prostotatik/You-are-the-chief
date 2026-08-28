using System;
using UnityEngine;

namespace GestureDetection
{
    // Manually-driven IGestureDetector for developing and testing the gameplay
    // layer before the real webcam-based detector (Task 10) is wired in.
    public class StubGestureDetector : MonoBehaviour, IGestureDetector
    {
        public event Action<GestureType> OnGestureRecognized;
        public event Action<GestureType, float> OnGestureProgress;
        public event Action OnCameraUnavailable;

        public void SimulateGesture(GestureType gesture) => OnGestureRecognized?.Invoke(gesture);

        public void SimulateProgress(GestureType gesture, float progress) =>
            OnGestureProgress?.Invoke(gesture, Mathf.Clamp01(progress));

        public void SimulateCameraUnavailable() => OnCameraUnavailable?.Invoke();

        // No-op: the stub has no internal lock state - each Simulate* call is an
        // explicit, one-shot trigger from the caller, so there's nothing to reset.
        public void ResetLock() { }
    }
}
