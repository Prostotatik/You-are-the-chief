using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Pizza: rotate a hand as if twirling dough. Detected as a wrist tracing a
    // circular path around its elbow while raised above it.
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
            foreach (var frame in window)
            {
                bool hasElbow = JointFilter.TryGet(frame, elbowJoint, out var elbow);
                bool hasWrist = JointFilter.TryGet(frame, wristJoint, out var wrist);
                if (!hasElbow || !hasWrist) continue;
                if (wrist.y >= elbow.y) continue; // wrist must be raised above the elbow

                relative.Add(wrist - elbow);
            }

            return GestureMath.AccumulatedRotation(relative);
        }
    }
}
