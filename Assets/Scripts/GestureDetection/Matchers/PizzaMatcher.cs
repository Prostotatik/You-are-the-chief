using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Pizza: rotate a hand as if twirling dough. Detected as a wrist tracing a
    // circular path around its elbow.
    //
    // No per-frame height gate: a full loop around the elbow necessarily has the
    // wrist below elbow height for part of the loop, so gating frames by
    // "wrist above elbow" would drop exactly the frames needed to keep the angle
    // sweep continuous and make RequiredRotationDegrees unreachable.
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

                relative.Add(wrist - elbow);
            }

            return GestureMath.AccumulatedRotation(relative);
        }
    }
}
