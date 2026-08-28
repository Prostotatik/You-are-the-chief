using UnityEngine;

namespace GestureDetection
{
    // Owns the real (webcam-based) pipeline wiring: pose provider -> calibration ->
    // detector. This is the composition root a scene needs when NOT using
    // StubGestureDetector - i.e. the actual gameplay-facing setup, as opposed to the
    // keyboard-simulated demo in GestureDetectionDemoController. Nothing in this
    // subsystem wired SentisPoseProvider, GestureDetector, and CalibrationController
    // together before this - this is the first time the composed pipeline exists,
    // even though its own logic has no automated test (there is no webcam in this
    // development environment to test it against).
    public class GestureDetectionBootstrap : MonoBehaviour
    {
        [SerializeField] private SentisPoseProvider poseProvider;
        [SerializeField] private GestureDetector gestureDetector;
        [SerializeField] private CalibrationController calibrationController;

        private void Start()
        {
            if (poseProvider == null || gestureDetector == null || calibrationController == null)
            {
                Debug.LogError($"{nameof(GestureDetectionBootstrap)}: one or more required references are unassigned - disabling.", this);
                enabled = false;
                return;
            }

            // Subscribe BEFORE Initialize(): Initialize() synchronously fires a catch-up
            // OnCameraUnavailable call if the provider already latched unavailable (e.g.
            // no device found in its own Start(), which may run before this Start()).
            // Subscribing after Initialize would let that catch-up call fire into zero
            // subscribers, silently losing it - exactly the bug this wiring exists to avoid.
            gestureDetector.OnGestureRecognized += gesture => Debug.Log($"[GestureDetectionBootstrap] Recognized: {gesture}");
            gestureDetector.OnCameraUnavailable += () => Debug.Log("[GestureDetectionBootstrap] Camera unavailable");
            gestureDetector.Initialize(poseProvider);

            calibrationController.OnCalibrationComplete += gestureDetector.SetCalibration;
            calibrationController.BeginCalibration(poseProvider);
        }
    }
}
