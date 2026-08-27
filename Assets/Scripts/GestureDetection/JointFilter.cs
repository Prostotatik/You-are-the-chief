using UnityEngine;

namespace GestureDetection
{
    public static class JointFilter
    {
        public const float DefaultMinConfidence = 0.4f;

        public static bool TryGet(LandmarkFrame frame, PoseJoint joint, out Vector2 position, float minConfidence = DefaultMinConfidence)
        {
            var landmark = frame.Get(joint);
            if (landmark.Confidence < minConfidence)
            {
                position = default;
                return false;
            }

            position = landmark.Position;
            return true;
        }
    }
}
