using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class PoseLandmarkSmootherTests
    {
        private static LandmarkFrame FrameWithWrist(float timestamp, Vector2 wristPosition, float confidence)
        {
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);
            joints[(int)PoseJoint.RightWrist] = new PoseLandmark(wristPosition, confidence);
            return new LandmarkFrame(timestamp, joints);
        }

        [Test]
        public void Smooth_FirstFrame_ReturnsPositionUnchanged()
        {
            var smoother = new PoseLandmarkSmoother();
            var raw = FrameWithWrist(0f, new Vector2(0.5f, 0.5f), confidence: 1f);

            var smoothed = smoother.Smooth(raw);

            Assert.AreEqual(new Vector2(0.5f, 0.5f), smoothed.Get(PoseJoint.RightWrist).Position);
        }

        [Test]
        public void Smooth_NoisySuccessiveFrames_ReducesJitterVersusRaw()
        {
            var smoother = new PoseLandmarkSmoother();
            float[] noisyX = { 0.50f, 0.54f, 0.47f, 0.53f, 0.48f, 0.52f, 0.49f, 0.55f, 0.46f, 0.51f };

            float t = 0f;
            Vector2 first = smoother.Smooth(FrameWithWrist(t, new Vector2(noisyX[0], 0.5f), 1f)).Get(PoseJoint.RightWrist).Position;
            float minX = first.x, maxX = first.x;

            for (int i = 1; i < noisyX.Length; i++)
            {
                t += 1f / 30f;
                var smoothed = smoother.Smooth(FrameWithWrist(t, new Vector2(noisyX[i], 0.5f), 1f));
                float x = smoothed.Get(PoseJoint.RightWrist).Position.x;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }

            Assert.Less(maxX - minX, 0.55f - 0.46f);
        }

        [Test]
        public void Smooth_ConfidenceBelowThreshold_PassesThroughUnfiltered()
        {
            var smoother = new PoseLandmarkSmoother();
            smoother.Smooth(FrameWithWrist(0f, new Vector2(0.5f, 0.5f), confidence: 1f));

            var lowConfidence = FrameWithWrist(1f / 30f, new Vector2(0.9f, 0.9f), confidence: 0.1f);
            var smoothed = smoother.Smooth(lowConfidence, minConfidenceToFilter: 0.4f);

            Assert.AreEqual(new Vector2(0.9f, 0.9f), smoothed.Get(PoseJoint.RightWrist).Position);
        }

        [Test]
        public void Smooth_JointReappearsAfterGap_ReseedsInsteadOfBlendingTowardStaleValue()
        {
            var smoother = new PoseLandmarkSmoother();
            smoother.Smooth(FrameWithWrist(0f, new Vector2(0.1f, 0.1f), confidence: 1f));

            // Joint drops below confidence for a while (passes through unfiltered per the
            // previous test), then reappears far away - it must jump straight there, not
            // ease in from the stale 0.1,0.1 filter state.
            smoother.Smooth(FrameWithWrist(1f / 30f, new Vector2(0.9f, 0.9f), confidence: 0.1f));
            var reappeared = smoother.Smooth(FrameWithWrist(2f / 30f, new Vector2(0.9f, 0.9f), confidence: 1f));

            Assert.AreEqual(new Vector2(0.9f, 0.9f), reappeared.Get(PoseJoint.RightWrist).Position);
        }

        [Test]
        public void Reset_ThenSmooth_ReturnsUnchangedLikeFirstFrame()
        {
            var smoother = new PoseLandmarkSmoother();
            smoother.Smooth(FrameWithWrist(0f, new Vector2(0.2f, 0.2f), confidence: 1f));
            smoother.Smooth(FrameWithWrist(1f / 30f, new Vector2(0.2f, 0.2f), confidence: 1f));

            smoother.Reset();
            var smoothed = smoother.Smooth(FrameWithWrist(2f, new Vector2(0.7f, 0.7f), confidence: 1f));

            Assert.AreEqual(new Vector2(0.7f, 0.7f), smoothed.Get(PoseJoint.RightWrist).Position);
        }
    }
}
