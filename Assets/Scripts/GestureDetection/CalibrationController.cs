using System;
using UnityEngine;

namespace GestureDetection
{
    // Continuously (re)calibrates against a live IPoseProvider in the background,
    // instead of requiring the player to strike and hold a special pose for a fixed
    // window. CalibrationSequencer only ever reads shoulders/hips, so any ordinary
    // frame where those are visible - sitting, standing, mid-gesture - is usable;
    // there is no calibration "step" the player has to consciously perform.
    public class CalibrationController : MonoBehaviour
    {
        // How much recent history to average over each time calibration recomputes.
        public const float SampleWindowSeconds = 3f;

        // Minimum spacing between successive OnCalibrationComplete updates, so this
        // doesn't recompute and re-fire on every single incoming frame.
        public const float RecalibrationIntervalSeconds = 1f;

        public event Action<CalibrationData> OnCalibrationComplete;

        private IPoseProvider _poseProvider;
        private readonly LandmarkBuffer _samples = new LandmarkBuffer(maxAgeSeconds: SampleWindowSeconds + 1f);
        private float _lastRecalibrationTimestamp = float.NegativeInfinity;

        public void BeginCalibration(IPoseProvider poseProvider)
        {
            StopCalibration();

            _poseProvider = poseProvider;
            _samples.Clear();
            _lastRecalibrationTimestamp = float.NegativeInfinity;
            _poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
        }

        public void StopCalibration()
        {
            if (_poseProvider == null) return;
            _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
            _poseProvider = null;
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _samples.Add(frame);

            if (frame.Timestamp - _lastRecalibrationTimestamp < RecalibrationIntervalSeconds) return;
            _lastRecalibrationTimestamp = frame.Timestamp;

            var window = _samples.GetWindow(SampleWindowSeconds);
            OnCalibrationComplete?.Invoke(CalibrationSequencer.Compute(window));
        }

        private void OnDestroy() => StopCalibration();
    }
}
