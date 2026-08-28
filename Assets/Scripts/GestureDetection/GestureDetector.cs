using System;
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    public class GestureDetector : MonoBehaviour, IGestureDetector
    {
        [SerializeField] private float windowSeconds = 1.5f;

        // Below this, a matcher's progress is treated as "not really doing anything" and
        // suppressed from OnGestureProgress, so the event isn't spammed with near-zero
        // noise from all 4 non-active matchers every single frame.
        private const float ProgressReportFloor = 0.1f;

        public event Action<GestureType> OnGestureRecognized;
        public event Action<GestureType, float> OnGestureProgress;
        public event Action OnCameraUnavailable;

        private IPoseProvider _poseProvider;
        private readonly LandmarkBuffer _buffer = new LandmarkBuffer();
        private readonly List<IGestureMatcher> _matchers = new List<IGestureMatcher>
        {
            new PizzaMatcher(),
            new MacAndCheeseMatcher(),
            new RocketSodaMatcher(),
            new WineMatcher(),
            new SpicySpiceMatcher(),
        };

        private CalibrationData _calibration = CalibrationData.Identity;
        private GestureType? _lockedGesture;

        public void Initialize(IPoseProvider poseProvider)
        {
            if (_poseProvider != null)
            {
                _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
                _poseProvider.OnCameraUnavailable -= HandleCameraUnavailable;
            }

            _poseProvider = poseProvider;
            _poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
            _poseProvider.OnCameraUnavailable += HandleCameraUnavailable;

            // Catch up on an unavailability that already happened before this call - e.g.
            // if the provider's own Start() ran (and fired OnCameraUnavailable) before
            // whoever calls Initialize() got a chance to subscribe. Unity's Start() order
            // between separate components isn't guaranteed.
            if (_poseProvider.IsCameraUnavailable) HandleCameraUnavailable();
        }

        public void SetCalibration(CalibrationData calibration)
        {
            _calibration = calibration;
        }

        public void ResetLock()
        {
            _lockedGesture = null;
            // Without this, frames already in the buffer from the just-recognized gesture
            // are still inside the next evaluation window and immediately re-match,
            // firing OnGestureRecognized again on the very next incoming frame.
            _buffer.Clear();
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _buffer.Add(frame);
            if (_lockedGesture.HasValue) return;

            var window = _buffer.GetWindow(windowSeconds);

            // Evaluate every matcher, but only ever report progress for ONE of them per
            // frame - the best-scoring non-matched one, above a floor - instead of firing
            // OnGestureProgress once per matcher per frame (5x/frame, mostly near-zero
            // noise from matchers the player isn't performing). On a match, report a
            // final 1f for the matched gesture before OnGestureRecognized, so a UI driven
            // by this event has a clean "complete" signal to freeze on instead of
            // whatever fractional value the last evaluated frame happened to produce.
            GestureType bestGesture = default;
            float bestProgress = 0f;

            foreach (var matcher in _matchers)
            {
                var result = matcher.Evaluate(window, _calibration);
                if (result.IsMatch)
                {
                    _lockedGesture = matcher.GestureType;
                    OnGestureProgress?.Invoke(matcher.GestureType, 1f);
                    OnGestureRecognized?.Invoke(matcher.GestureType);
                    return;
                }

                if (result.Progress > bestProgress)
                {
                    bestProgress = result.Progress;
                    bestGesture = matcher.GestureType;
                }
            }

            if (bestProgress >= ProgressReportFloor)
            {
                OnGestureProgress?.Invoke(bestGesture, bestProgress);
            }
        }

        private void HandleCameraUnavailable()
        {
            OnCameraUnavailable?.Invoke();
        }

        private void OnDestroy()
        {
            if (_poseProvider == null) return;
            _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
            _poseProvider.OnCameraUnavailable -= HandleCameraUnavailable;
        }
    }
}
