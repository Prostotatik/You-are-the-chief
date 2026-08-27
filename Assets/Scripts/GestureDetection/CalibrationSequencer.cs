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
                bool hasShoulders = JointFilter.TryGet(frame, PoseJoint.LeftShoulder, out var leftShoulder)
                    && JointFilter.TryGet(frame, PoseJoint.RightShoulder, out var rightShoulder);
                bool hasHips = JointFilter.TryGet(frame, PoseJoint.LeftHip, out var leftHip)
                    && JointFilter.TryGet(frame, PoseJoint.RightHip, out var rightHip);
                if (!hasShoulders || !hasHips) continue;

                scaleSum += Vector2.Distance(leftShoulder, rightShoulder);
                centerSum += (leftHip + rightHip) * 0.5f;
                sampleCount++;
            }

            if (sampleCount == 0) return CalibrationData.Identity;

            return new CalibrationData(scaleSum / sampleCount, centerSum / sampleCount);
        }
    }
}
