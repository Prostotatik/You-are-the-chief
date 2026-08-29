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

        [Test]
        public void ToBlitTransform_TopRegion_SamplesPhysicalTopOfSourceTexture()
        {
            // Worked example from the task: a region near the top of a y-down frame
            // (small Center.y = "above") must, once fed to Graphics.Blit (which samples
            // in y-UP UV space, v=0 bottom / v=1 top), land near v=1 (physical top),
            // not v=0 (physical bottom).
            var region = new PoseCropRegion(new Vector2(0.5f, 0.2f), 0.3f);
            var (uvScale, uvOffset) = PoseCrop.ToUvTransform(region);
            var (blitScale, blitOffset) = PoseCrop.ToBlitTransform(uvScale, uvOffset);

            // x axis is untouched by the flip.
            Assert.AreEqual(uvScale.x, blitScale.x, 0.0001f);
            Assert.AreEqual(uvOffset.x, blitOffset.x, 0.0001f);

            // blit-space t=0 (first row written into the crop texture, i.e. the crop's
            // own y-down "top") must sample source texture-v = 1 - (Center.y - Size/2)
            // = 0.95 - close to v=1, the source texture's physical top edge.
            float vAtBlitTop = 0f * blitScale.y + blitOffset.y;
            Assert.AreEqual(0.95f, vAtBlitTop, 0.001f);

            // blit-space t=1 (the crop's own y-down "bottom") must sample source
            // texture-v = 1 - (Center.y + Size/2) = 0.65 - still in the upper half
            // (v > 0.5), confirming the whole region samples the physical top-ish strip.
            float vAtBlitBottom = 1f * blitScale.y + blitOffset.y;
            Assert.AreEqual(0.65f, vAtBlitBottom, 0.001f);
            Assert.Greater(vAtBlitBottom, 0.5f);
        }

        [Test]
        public void BlitTransform_RoundTrip_CropYMapsBackToOriginalYDownSourcePosition()
        {
            // Pins the Critical y-axis fix end-to-end: a known y-down PoseCropRegion is
            // converted for Graphics.Blit sampling (ToBlitTransform, y-up), then a
            // synthetic landmarker cropY (y-down, MediaPipe pixel-row convention) is
            // mapped back into source space using the ORIGINAL y-down transform
            // (ToUvTransform) - exactly as SentisPoseProvider.RunLandmarker does. The
            // round trip must reproduce the same y-down source coordinate regardless of
            // the y-flip applied only at the Blit call site.
            var region = new PoseCropRegion(new Vector2(0.5f, 0.2f), 0.3f);
            var (uvScale, uvOffset) = PoseCrop.ToUvTransform(region);
            var (blitScale, blitOffset) = PoseCrop.ToBlitTransform(uvScale, uvOffset);

            // A joint sitting at the physical top-left of the crop, i.e. crop-space
            // (cropX, cropY) = (0, 0) in y-down terms (top-left origin, per
            // PoseLandmark's contract).
            Vector2 cropPosition = new Vector2(0f, 0f);

            // The provider maps this back using the UNFLIPPED y-down transform.
            Vector2 sourcePosition = cropPosition * uvScale + uvOffset;

            // Expected: the region's own top-left corner in y-down source space.
            Vector2 expectedTopLeft = region.Center - new Vector2(region.Size, region.Size) * 0.5f;
            Assert.AreEqual(expectedTopLeft.x, sourcePosition.x, 0.0001f);
            Assert.AreEqual(expectedTopLeft.y, sourcePosition.y, 0.0001f);

            // Cross-check against the physical texture row Graphics.Blit would actually
            // read for this same corner: blit-space t=0 (dest row 0, the crop's y-down
            // "top") samples source texture-v = blitOffset.y. Converting that v back to
            // a y-down coordinate (yd = 1 - v) must equal sourcePosition.y - i.e. the
            // pixel Blit physically sampled is the same one the back-projection assumes.
            float physicalV = 0f * blitScale.y + blitOffset.y;
            float physicalYDown = 1f - physicalV;
            Assert.AreEqual(sourcePosition.y, physicalYDown, 0.0001f);
        }
    }
}
