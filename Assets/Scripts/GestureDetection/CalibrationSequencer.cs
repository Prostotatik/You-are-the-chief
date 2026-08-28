using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Pure function: turns a set of sampled landmark frames (typically a ~3s T-pose
    // window) into a CalibrationData baseline. Kept separate from CalibrationController
    // so it is testable without a MonoBehaviour/coroutine.
    public static class CalibrationSequencer
    {
        public static CalibrationData Compute(IReadOnlyList<LandmarkFrame> frames)
        {
            float scaleSum = 0f;
            Vector2 centerSum = Vector2.zero;
            int sampleCount = 0;

            foreach (var frame in frames)
            {
                // Each TryGet is called unconditionally (not short-circuited via &&) so the
                // compiler can prove leftShoulder/rightShoulder/leftHip/rightHip are always
                // definitely assigned below - JointFilter.TryGet assigns its out parameter
                // on both the true and false paths.
                bool hasLeftShoulder = JointFilter.TryGet(frame, PoseJoint.LeftShoulder, out var leftShoulder);
                bool hasRightShoulder = JointFilter.TryGet(frame, PoseJoint.RightShoulder, out var rightShoulder);
                bool hasLeftHip = JointFilter.TryGet(frame, PoseJoint.LeftHip, out var leftHip);
                bool hasRightHip = JointFilter.TryGet(frame, PoseJoint.RightHip, out var rightHip);
                if (!hasLeftShoulder || !hasRightShoulder || !hasLeftHip || !hasRightHip) continue;

                scaleSum += Vector2.Distance(leftShoulder, rightShoulder);
                centerSum += (leftHip + rightHip) * 0.5f;
                sampleCount++;
            }

            if (sampleCount == 0) return CalibrationData.Identity;

            float averageShoulderWidth = scaleSum / sampleCount;
            float bodyScale = averageShoulderWidth / CalibrationData.ReferenceBodyScale;
            return new CalibrationData(bodyScale, centerSum / sampleCount);
        }
    }
}
