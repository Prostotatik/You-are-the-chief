using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Mac&Cheese: raise a heel and rub a fist against it (grating parmesan).
    // Detected as one ankle raised above its own knee, with the opposite wrist
    // staying close to that ankle and oscillating (the rubbing motion).
    public class MacAndCheeseMatcher : IGestureMatcher
    {
        public const float BaseProximityThreshold = 0.18f;
        public const int RequiredOscillations = 2;

        public GestureType GestureType => GestureType.MacAndCheese;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float left = EvaluateSide(window, PoseJoint.LeftAnkle, PoseJoint.LeftKnee, PoseJoint.RightWrist, calibration);
            float right = EvaluateSide(window, PoseJoint.RightAnkle, PoseJoint.RightKnee, PoseJoint.LeftWrist, calibration);
            float progress = Mathf.Max(left, right);
            return new MatchResult(progress >= 1f, progress);
        }

        private static float EvaluateSide(IReadOnlyList<LandmarkFrame> window, PoseJoint ankleJoint, PoseJoint kneeJoint, PoseJoint wristJoint, CalibrationData calibration)
        {
            float proximityThreshold = BaseProximityThreshold * Mathf.Max(calibration.BodyScale, 0.01f);
            var distances = new List<float>();

            foreach (var frame in window)
            {
                bool hasAnkle = JointFilter.TryGet(frame, ankleJoint, out var ankle);
                bool hasKnee = JointFilter.TryGet(frame, kneeJoint, out var knee);
                bool hasWrist = JointFilter.TryGet(frame, wristJoint, out var wrist);
                if (!hasAnkle || !hasKnee || !hasWrist) continue;
                if (ankle.y >= knee.y) continue; // leg must be raised: ankle above the knee

                // Skip (not reject the whole window for) a single frame where the wrist
                // strayed too far from the ankle - one noisy/out-of-range sample shouldn't
                // erase an otherwise clean rubbing motion.
                float distance = Vector2.Distance(ankle, wrist);
                if (distance > proximityThreshold) continue;

                distances.Add(distance);
            }

            if (distances.Count == 0) return 0f;

            int reversals = GestureMath.CountReversals(distances, proximityThreshold * 0.2f);
            return Mathf.Clamp01((float)reversals / RequiredOscillations);
        }
    }
}
