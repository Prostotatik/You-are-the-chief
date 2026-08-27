using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection.Tests
{
    // Builds synthetic LandmarkFrame sequences for matcher unit tests.
    // Joints not passed to AddFrame are left at zero confidence (i.e. filtered out
    // by JointFilter), matching how a real pose model reports low-confidence joints.
    public class LandmarkSequenceBuilder
    {
        private readonly List<LandmarkFrame> _frames = new List<LandmarkFrame>();
        private float _time;

        public LandmarkSequenceBuilder AddFrame(float dt, Dictionary<PoseJoint, Vector2> positions, float confidence = 1f)
        {
            _time += dt;
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);

            foreach (var pair in positions)
                joints[(int)pair.Key] = new PoseLandmark(pair.Value, confidence);

            _frames.Add(new LandmarkFrame(_time, joints));
            return this;
        }

        public List<LandmarkFrame> Build() => _frames;
    }
}
