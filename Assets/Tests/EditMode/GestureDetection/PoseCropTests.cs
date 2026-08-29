using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class PoseCropTests
    {
        [Test]
        public void FromDetection_ComputesCenterAndSizeFromKeypoints()
        {
            var keypoint1 = new Vector2(0.4f, 0.5f);
            var keypoint2 = new Vector2(0.6f, 0.5f); // horizontal pair, distance 0.2

            var region = PoseCrop.FromDetection(keypoint1, keypoint2, boxCenter: new Vector2(0.5f, 0.5f), scale: 1.25f);

            Assert.AreEqual(0.5f, region.Center.x, 0.001f);
            Assert.AreEqual(0.5f, region.Center.y, 0.001f);
            Assert.AreEqual(0.25f, region.Size, 0.001f); // 1.25 * 0.2
        }

        private static LandmarkFrame FrameFromJoints(Dictionary<PoseJoint, Vector2> positions, float confidence = 1f)
        {
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);
            foreach (var pair in positions)
                joints[(int)pair.Key] = new PoseLandmark(pair.Value, confidence);
            return new LandmarkFrame(0f, joints);
        }

        [Test]
        public void FromLandmarkBounds_TightCluster_ProducesPaddedSquareAroundIt()
        {
            var frame = FrameFromJoints(new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
                { PoseJoint.LeftHip, new Vector2(0.4f, 0.6f) },
                { PoseJoint.RightHip, new Vector2(0.6f, 0.6f) },
            });

            var region = PoseCrop.FromLandmarkBounds(frame, minConfidence: 0.4f, paddingFraction: 0.35f);

            // Bounding box is x:[0.4,0.6] y:[0.3,0.6] -> center (0.5, 0.45), largest span 0.3.
            Assert.AreEqual(0.5f, region.Center.x, 0.001f);
            Assert.AreEqual(0.45f, region.Center.y, 0.001f);
            Assert.Greater(region.Size, 0.3f); // padded, so bigger than the raw span
        }

        [Test]
        public void FromLandmarkBounds_FewerThanTwoConfidentJoints_ReturnsZeroSizeRegion()
        {
            var frame = FrameFromJoints(new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.Nose, new Vector2(0.5f, 0.2f) },
            });

            var region = PoseCrop.FromLandmarkBounds(frame);

            Assert.AreEqual(0f, region.Size);
        }

        [Test]
        public void ToUvTransform_CenteredRegion_OffsetsByHalfSizeFromCenter()
        {
            var region = new PoseCropRegion(new Vector2(0.5f, 0.5f), 0.3f);
            var (scale, offset) = PoseCrop.ToUvTransform(region);

            Assert.AreEqual(0.3f, scale.x, 0.001f);
            Assert.AreEqual(0.3f, scale.y, 0.001f);
            Assert.AreEqual(0.35f, offset.x, 0.001f); // 0.5 - 0.3*0.5
            Assert.AreEqual(0.35f, offset.y, 0.001f);
        }

        [Test]
        public void ToUvTransform_OffCenterRegion_SamplingCornerAndOppositeCornerBoundTheRegion()
        {
            var region = new PoseCropRegion(new Vector2(0.6f, 0.4f), 0.2f);
            var (scale, offset) = PoseCrop.ToUvTransform(region);

            // uv=(0,0) must land on the region's min corner, uv=(1,1) on its max corner.
            Vector2 minCorner = Vector2.zero * scale + offset;
            Vector2 maxCorner = Vector2.one * scale + offset;
            Assert.AreEqual(0.5f, minCorner.x, 0.001f); // 0.6 - 0.1
            Assert.AreEqual(0.3f, minCorner.y, 0.001f); // 0.4 - 0.1
            Assert.AreEqual(0.7f, maxCorner.x, 0.001f); // 0.6 + 0.1
            Assert.AreEqual(0.5f, maxCorner.y, 0.001f); // 0.4 + 0.1
        }
    }
}
