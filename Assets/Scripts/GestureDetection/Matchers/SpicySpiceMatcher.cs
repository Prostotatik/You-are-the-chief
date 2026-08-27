using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Spicy Spice: raise both fists to face height, move them toward/away from the face.
    // Detected as both wrists staying near the nose's height while their distance to
    // the nose oscillates.
    public class SpicySpiceMatcher : IGestureMatcher
    {
        public const float BaseFaceHeightTolerance = 0.12f;
        public const int RequiredOscillations = 2;

        public GestureType GestureType => GestureType.SpicySpice;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float faceTolerance = BaseFaceHeightTolerance * Mathf.Max(calibration.BodyScale, 0.01f);
            var wristToNoseDistance = new List<float>();

            foreach (var frame in window)
            {
                bool hasNose = JointFilter.TryGet(frame, PoseJoint.Nose, out var nose);
                bool hasLeftWrist = JointFilter.TryGet(frame, PoseJoint.LeftWrist, out var leftWrist);
                bool hasRightWrist = JointFilter.TryGet(frame, PoseJoint.RightWrist, out var rightWrist);
                if (!hasNose || !hasLeftWrist || !hasRightWrist) continue;

                bool leftAtFace = Mathf.Abs(leftWrist.y - nose.y) <= faceTolerance;
                bool rightAtFace = Mathf.Abs(rightWrist.y - nose.y) <= faceTolerance;
                if (!leftAtFace || !rightAtFace) continue;

                float avgDistance = (Vector2.Distance(leftWrist, nose) + Vector2.Distance(rightWrist, nose)) * 0.5f;
                wristToNoseDistance.Add(avgDistance);
            }

            if (wristToNoseDistance.Count == 0) return MatchResult.None;

            int reversals = GestureMath.CountReversals(wristToNoseDistance, faceTolerance * 0.3f);
            float progress = Mathf.Clamp01((float)reversals / RequiredOscillations);
            return new MatchResult(reversals >= RequiredOscillations, progress);
        }
    }
}
