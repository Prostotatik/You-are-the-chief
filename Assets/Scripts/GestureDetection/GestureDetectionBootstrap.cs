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
            gestureDetector.Initialize(poseProvider);
            gestureDetector.OnGestureRecognized += gesture => Debug.Log($"[GestureDetectionBootstrap] Recognized: {gesture}");
            gestureDetector.OnCameraUnavailable += () => Debug.Log("[GestureDetectionBootstrap] Camera unavailable");

            calibrationController.OnCalibrationComplete += gestureDetector.SetCalibration;
            calibrationController.BeginCalibration(poseProvider);
        }
    }
}
