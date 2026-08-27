using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Rocket Soda: shake two fists together low near the belly, like shaking a bottle.
    // Detected as both wrists close together and below chest height, oscillating
    // vertically together.
    public class RocketSodaMatcher : IGestureMatcher
    {
        public const float BaseProximityThreshold = 0.15f;
        public const float BaseChestOffset = 0.05f;
        public const int RequiredOscillations = 3;

        public GestureType GestureType => GestureType.RocketSoda;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float proximityThreshold = BaseProximityThreshold * Mathf.Max(calibration.BodyScale, 0.01f);
            float chestOffset = BaseChestOffset * Mathf.Max(calibration.BodyScale, 0.01f);
            var midpointY = new List<float>();

            foreach (var frame in window)
            {
                bool hasLeftWrist = JointFilter.TryGet(frame, PoseJoint.LeftWrist, out var leftWrist);
                bool hasRightWrist = JointFilter.TryGet(frame, PoseJoint.RightWrist, out var rightWrist);
                bool hasLeftShoulder = JointFilter.TryGet(frame, PoseJoint.LeftShoulder, out var leftShoulder);
                bool hasRightShoulder = JointFilter.TryGet(frame, PoseJoint.RightShoulder, out var rightShoulder);
                if (!hasLeftWrist || !hasRightWrist || !hasLeftShoulder || !hasRightShoulder) continue;

                float chestY = (leftShoulder.y + rightShoulder.y) * 0.5f;
                bool belowChest = leftWrist.y > chestY + chestOffset && rightWrist.y > chestY + chestOffset;
                bool closeTogether = Vector2.Distance(leftWrist, rightWrist) <= proximityThreshold;
                if (!belowChest || !closeTogether) continue;

                midpointY.Add((leftWrist.y + rightWrist.y) * 0.5f);
            }

            if (midpointY.Count == 0) return MatchResult.None;

            int reversals = GestureMath.CountReversals(midpointY, chestOffset);
            float progress = Mathf.Clamp01((float)reversals / RequiredOscillations);
            return new MatchResult(reversals >= RequiredOscillations, progress);
        }
    }
}
