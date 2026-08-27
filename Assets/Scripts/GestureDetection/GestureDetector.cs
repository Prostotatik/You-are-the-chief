using System;
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    public class GestureDetector : MonoBehaviour, IGestureDetector
    {
        [SerializeField] private float windowSeconds = 1.5f;

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
        }

        public void SetCalibration(CalibrationData calibration)
        {
            _calibration = calibration;
        }

        public void ResetLock()
        {
            _lockedGesture = null;
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _buffer.Add(frame);
            if (_lockedGesture.HasValue) return;

            var window = _buffer.GetWindow(windowSeconds);
            foreach (var matcher in _matchers)
            {
                var result = matcher.Evaluate(window, _calibration);
                OnGestureProgress?.Invoke(matcher.GestureType, result.Progress);
                if (result.IsMatch)
                {
                    _lockedGesture = matcher.GestureType;
                    OnGestureRecognized?.Invoke(matcher.GestureType);
                    break;
                }
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
