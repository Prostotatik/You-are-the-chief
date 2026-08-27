using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Pizza: rotate a hand as if twirling dough. Detected as a wrist tracing a
    // circular path around its elbow while raised above it on average.
    //
    // The "raised above the elbow" check is a window-average, not a per-frame
    // gate: a full loop around the elbow necessarily has the wrist below elbow
    // height for part of the loop, so gating individual frames by "wrist above
    // elbow" would drop exactly the frames needed to keep the angle sweep
    // continuous and make RequiredRotationDegrees unreachable.
    public class PizzaMatcher : IGestureMatcher
    {
        public const float RequiredRotationDegrees = 300f;

        public GestureType GestureType => GestureType.Pizza;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float rightRotation = EvaluateArm(window, PoseJoint.RightElbow, PoseJoint.RightWrist);
            float leftRotation = EvaluateArm(window, PoseJoint.LeftElbow, PoseJoint.LeftWrist);
            float rotation = Mathf.Max(rightRotation, leftRotation);

            float progress = Mathf.Clamp01(rotation / RequiredRotationDegrees);
            return new MatchResult(rotation >= RequiredRotationDegrees, progress);
        }

        private static float EvaluateArm(IReadOnlyList<LandmarkFrame> window, PoseJoint elbowJoint, PoseJoint wristJoint)
        {
            var relative = new List<Vector2>();
            float wristYSum = 0f;
            float elbowYSum = 0f;
            int sampleCount = 0;

            foreach (var frame in window)
            {
                bool hasElbow = JointFilter.TryGet(frame, elbowJoint, out var elbow);
                bool hasWrist = JointFilter.TryGet(frame, wristJoint, out var wrist);
                if (!hasElbow || !hasWrist) continue;

                relative.Add(wrist - elbow);
                wristYSum += wrist.y;
                elbowYSum += elbow.y;
                sampleCount++;
            }

            if (sampleCount == 0) return 0f;

            float averageWristY = wristYSum / sampleCount;
            float averageElbowY = elbowYSum / sampleCount;
            if (averageWristY >= averageElbowY) return 0f; // wrist must be raised above the elbow on average

            return GestureMath.AccumulatedRotation(relative);
        }
    }
}
