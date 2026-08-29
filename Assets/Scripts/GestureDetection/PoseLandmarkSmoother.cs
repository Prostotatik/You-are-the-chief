namespace GestureDetection
{
    // Applies a OneEuroFilter pair (x, y) per joint to a raw LandmarkFrame stream,
    // smoothing frame-to-frame jitter without materially lagging real motion. A joint
    // whose confidence drops below the threshold is passed through unfiltered and its
    // filter pair is reset, so a real occlusion gap never blends into a stale position
    // when the joint reappears - it reseeds at the new position instead.
    public class PoseLandmarkSmoother
    {
        private readonly OneEuroFilter[] _xFilters = new OneEuroFilter[PoseJointCount.Value];
        private readonly OneEuroFilter[] _yFilters = new OneEuroFilter[PoseJointCount.Value];

        public PoseLandmarkSmoother()
        {
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                _xFilters[i] = new OneEuroFilter();
                _yFilters[i] = new OneEuroFilter();
            }
        }

        public LandmarkFrame Smooth(LandmarkFrame raw, float minConfidenceToFilter = 0.4f)
        {
            var smoothedJoints = new PoseLandmark[PoseJointCount.Value];

            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                var joint = raw.Joints[i];

                if (joint.Confidence < minConfidenceToFilter)
                {
                    _xFilters[i].Reset();
                    _yFilters[i].Reset();
                    smoothedJoints[i] = joint;
                    continue;
                }

                float x = _xFilters[i].Filter(joint.Position.x, raw.Timestamp);
                float y = _yFilters[i].Filter(joint.Position.y, raw.Timestamp);
                smoothedJoints[i] = new PoseLandmark(new UnityEngine.Vector2(x, y), joint.Confidence);
            }

            return new LandmarkFrame(raw.Timestamp, smoothedJoints);
        }

        public void Reset()
        {
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                _xFilters[i].Reset();
                _yFilters[i].Reset();
            }
        }
    }
}
