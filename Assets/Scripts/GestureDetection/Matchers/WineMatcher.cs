using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Wine: stomp feet repeatedly, as if stomping grapes.
    // Detected as vertical strikes (direction reversals) on EACH ankle, gated on that
    // ankle being below its own hip (a standing/stepping posture, not e.g. a raised
    // Mac&Cheese heel). Both feet must contribute at least one strike each - a single
    // foot bouncing while the other stays still no longer satisfies this on its own.
    public class WineMatcher : IGestureMatcher
    {
        public const int RequiredStrikesPerFoot = 1;
        public const int RequiredCombinedStrikes = 2;
        public const float BaseMinStrikeAmplitude = 0.05f;

        public GestureType GestureType => GestureType.Wine;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float amplitude = BaseMinStrikeAmplitude * Mathf.Max(calibration.BodyScale, 0.01f);

            var leftY = CollectGroundedAnkleY(window, PoseJoint.LeftAnkle, PoseJoint.LeftHip);
            var rightY = CollectGroundedAnkleY(window, PoseJoint.RightAnkle, PoseJoint.RightHip);

            int leftStrikes = GestureMath.CountReversals(leftY, amplitude);
            int rightStrikes = GestureMath.CountReversals(rightY, amplitude);
            int totalStrikes = leftStrikes + rightStrikes;

            bool bothFeetContributed = leftStrikes >= RequiredStrikesPerFoot && rightStrikes >= RequiredStrikesPerFoot;
            bool isMatch = bothFeetContributed && totalStrikes >= RequiredCombinedStrikes;

            // Cap progress at 0.5 until both feet have contributed, so a single bouncing
            // foot can't read as "almost there" on its own.
            float progress = Mathf.Clamp01((float)totalStrikes / RequiredCombinedStrikes);
            if (!bothFeetContributed) progress = Mathf.Min(progress, 0.5f);

            return new MatchResult(isMatch, progress);
        }

        private static List<float> CollectGroundedAnkleY(IReadOnlyList<LandmarkFrame> window, PoseJoint ankleJoint, PoseJoint hipJoint)
        {
            var values = new List<float>();
            foreach (var frame in window)
            {
                bool hasAnkle = JointFilter.TryGet(frame, ankleJoint, out var ankle);
                bool hasHip = JointFilter.TryGet(frame, hipJoint, out var hip);
                if (!hasAnkle || !hasHip) continue;
                if (ankle.y <= hip.y) continue; // ankle must be below the hip (standing posture)

                values.Add(ankle.y);
            }
            return values;
        }
    }
}
