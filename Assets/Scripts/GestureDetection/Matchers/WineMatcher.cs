using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Wine: stomp feet repeatedly, as if stomping grapes.
    // Detected as combined vertical strikes (direction reversals) across both ankles.
    public class WineMatcher : IGestureMatcher
    {
        public const int RequiredStrikes = 2;
        public const float BaseMinStrikeAmplitude = 0.05f;

        public GestureType GestureType => GestureType.Wine;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float amplitude = BaseMinStrikeAmplitude * Mathf.Max(calibration.BodyScale, 0.01f);
            var leftY = new List<float>();
            var rightY = new List<float>();

            foreach (var frame in window)
            {
                if (JointFilter.TryGet(frame, PoseJoint.LeftAnkle, out var leftAnkle)) leftY.Add(leftAnkle.y);
                if (JointFilter.TryGet(frame, PoseJoint.RightAnkle, out var rightAnkle)) rightY.Add(rightAnkle.y);
            }

            int leftStrikes = GestureMath.CountReversals(leftY, amplitude);
            int rightStrikes = GestureMath.CountReversals(rightY, amplitude);
            int totalStrikes = leftStrikes + rightStrikes;

            float progress = Mathf.Clamp01((float)totalStrikes / RequiredStrikes);
            return new MatchResult(totalStrikes >= RequiredStrikes, progress);
        }
    }
}
