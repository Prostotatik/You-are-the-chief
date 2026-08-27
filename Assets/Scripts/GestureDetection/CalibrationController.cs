using System;
using System.Collections;
using UnityEngine;

namespace GestureDetection
{
    // Runs a short T-pose calibration window against a live IPoseProvider,
    // then reports the resulting CalibrationData.
    public class CalibrationController : MonoBehaviour
    {
        public const float DurationSeconds = 3f;

        public event Action<CalibrationData> OnCalibrationComplete;

        private IPoseProvider _poseProvider;
        private readonly LandmarkBuffer _samples = new LandmarkBuffer(maxAgeSeconds: DurationSeconds + 1f);

        public void BeginCalibration(IPoseProvider poseProvider)
        {
            _poseProvider = poseProvider;
            _samples.Clear();
            _poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
            StartCoroutine(FinishAfterDuration());
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _samples.Add(frame);
        }

        private IEnumerator FinishAfterDuration()
        {
            yield return new WaitForSeconds(DurationSeconds);

            _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
            var result = CalibrationSequencer.Compute(_samples.GetWindow(DurationSeconds));
            OnCalibrationComplete?.Invoke(result);
        }
    }
}
