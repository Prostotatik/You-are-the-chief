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

        // The two tests below use literal span thresholds mirroring PoseCrop.
        // FromLandmarkBounds's private MinUsableSpan (0.02f) - if that constant's value
        // changes, these thresholds (just inside/outside it) need to move with it.
        [Test]
        public void FromLandmarkBounds_JointsClusteredWithinMinSpan_ReturnsZeroSizeRegion()
        {
            // Two confident joints, but collapsed onto nearly identical positions - span
            // (0.01) is below MinUsableSpan (0.02), so this should be treated the same as
            // "fewer than two confident joints": a degenerate detection, not a real one.
            var frame = FrameFromJoints(new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.50f, 0.30f) },
                { PoseJoint.RightShoulder, new Vector2(0.51f, 0.30f) }, // span 0.01 < 0.02
            });

            var region = PoseCrop.FromLandmarkBounds(frame, minConfidence: 0.4f, paddingFraction: 0.35f);

            Assert.AreEqual(0f, region.Size);
        }

        [Test]
        public void FromLandmarkBounds_JointsJustOutsideMinSpan_ReturnsValidNonZeroRegion()
        {
            // Span (0.03) is just outside MinUsableSpan (0.02) - should produce a normal,
            // valid padded region rather than being treated as degenerate.
            var frame = FrameFromJoints(new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.50f, 0.30f) },
                { PoseJoint.RightShoulder, new Vector2(0.53f, 0.30f) }, // span 0.03 > 0.02
            });

            var region = PoseCrop.FromLandmarkBounds(frame, minConfidence: 0.4f, paddingFraction: 0.35f);

            Assert.Greater(region.Size, 0f);
            Assert.AreEqual(0.03f * 1.35f, region.Size, 0.001f);
        }

        [Test]
        public void ToUvTransform_CenteredRegion_OffsetsByHalfSizeFromCenter()
        {
            var region = new PoseCropRegion(new Vector2(0.5f, 0.5f), 0.3f);
            var (scale, offset) = PoseCrop.ToUvTransform(region, sourceWidth: 100, sourceHeight: 100);

            Assert.AreEqual(0.3f, scale.x, 0.001f);
            Assert.AreEqual(0.3f, scale.y, 0.001f);
            Assert.AreEqual(0.35f, offset.x, 0.001f); // 0.5 - 0.3*0.5
            Assert.AreEqual(0.35f, offset.y, 0.001f);
        }

        [Test]
        public void ToUvTransform_OffCenterRegion_SamplingCornerAndOppositeCornerBoundTheRegion()
        {
            var region = new PoseCropRegion(new Vector2(0.6f, 0.4f), 0.2f);
            var (scale, offset) = PoseCrop.ToUvTransform(region, sourceWidth: 100, sourceHeight: 100);

            // uv=(0,0) must land on the region's min corner, uv=(1,1) on its max corner.
            Vector2 minCorner = Vector2.zero * scale + offset;
            Vector2 maxCorner = Vector2.one * scale + offset;
            Assert.AreEqual(0.5f, minCorner.x, 0.001f); // 0.6 - 0.1
            Assert.AreEqual(0.3f, minCorner.y, 0.001f); // 0.4 - 0.1
            Assert.AreEqual(0.7f, maxCorner.x, 0.001f); // 0.6 + 0.1
            Assert.AreEqual(0.5f, maxCorner.y, 0.001f); // 0.4 + 0.1
        }

        [Test]
        public void ToUvTransform_NonSquareSource_CorrectsNarrowerAxisToBePhysicallySquare()
        {
            // Worked example (matches PoseCrop.ToUvTransform's doc comment): 640x480
            // source (4:3, wider than tall), Size=0.3. Intended physical square is
            // Size * max(640,480) = 192x192 px. Uncorrected UV scale (0.3, 0.3) would
            // sample 192px horizontally but only 144px vertically. Corrected: x keeps
            // span 0.3 (already 192px, the max dimension), y-span becomes
            // 0.3 * (640/480) = 0.4 (192px).
            var region = new PoseCropRegion(new Vector2(0.5f, 0.5f), 0.3f);
            var (scale, _) = PoseCrop.ToUvTransform(region, sourceWidth: 640, sourceHeight: 480);

            Assert.AreEqual(0.3f, scale.x, 0.0001f);
            Assert.AreEqual(0.4f, scale.y, 0.0001f);

            // Confirm both axes now cover the same PHYSICAL pixel span.
            float physicalWidthPx = scale.x * 640;
            float physicalHeightPx = scale.y * 480;
            Assert.AreEqual(192f, physicalWidthPx, 0.01f);
            Assert.AreEqual(192f, physicalHeightPx, 0.01f);
            Assert.AreEqual(physicalWidthPx, physicalHeightPx, 0.01f);
        }

        [Test]
        public void ToUvTransform_TallerThanWideSource_CorrectsXAxisInstead()
        {
            // Mirror case: source taller than wide (e.g. a portrait-oriented capture).
            // Now height is the max dimension, so y keeps span Size and x is inflated.
            var region = new PoseCropRegion(new Vector2(0.5f, 0.5f), 0.3f);
            var (scale, _) = PoseCrop.ToUvTransform(region, sourceWidth: 480, sourceHeight: 640);

            Assert.AreEqual(0.3f, scale.y, 0.0001f);
            Assert.AreEqual(0.4f, scale.x, 0.0001f); // 0.3 * (640/480)

            float physicalWidthPx = scale.x * 480;
            float physicalHeightPx = scale.y * 640;
            Assert.AreEqual(physicalWidthPx, physicalHeightPx, 0.01f);
        }

        // Simulates the REAL two-hop pipeline in SentisPoseProvider.RunLandmarker,
        // modelled here from first principles rather than by reusing any formula from
        // the code under test:
        //
        //   Hop 2 (TextureConverter.ToTensor, CoordOrigin.TopLeft - the default left in
        //   place by `new TextureTransform().SetTensorLayout(TensorLayout.NHWC)`): the
        //   conversion shader does `O_pos.y = O_size.y - 1 - O_pos.y`, so tensor row 0
        //   (landmarker cropY = 0) reads the crop texture's PHYSICAL TOP row. Normalized,
        //   the crop-texture UV read for a given cropY is t = 1 - cropY.
        //
        //   Hop 1 (Graphics.Blit): the destination UV t samples the source at
        //   v = t * blitScale.y + blitOffset.y (identity for scale=1/offset=0).
        //
        // Then source UV -> this project's y-down convention: yd = 1 - v.
        //
        // Returns the y-down source coordinate a landmark reported at `cropY` actually
        // came from, given the blit transform the provider used.
        private static float SourceYDownForLandmarkerCropY(float cropY, Vector2 blitScale, Vector2 blitOffset)
        {
            float cropTextureT = 1f - cropY;                              // Hop 2: TopLeft flip
            float sourceV = cropTextureT * blitScale.y + blitOffset.y;    // Hop 1: Blit
            return 1f - sourceV;                                          // UV -> y-down
        }

        [Test]
        public void BlitTransform_BothHopsComposed_CropYExtremesLandOnRegionTopAndBottomEdges()
        {
            // Worked example: a region near the top of a y-down frame (small Center.y =
            // "above"), sampled from a 640x480 (4:3) source so uvScale.y is
            // aspect-corrected to Size * (640/480) = 0.3 * 1.33333... = 0.4 (not 0.3).
            // Its y-down top edge is 0.2 - 0.4*0.5 = 0.0, its bottom edge
            // 0.2 + 0.4*0.5 = 0.4. These two expected numbers are computed by hand here,
            // NOT derived from ToBlitTransform, so a wrong blit transform cannot make
            // the assertions move with it.
            var region = new PoseCropRegion(new Vector2(0.5f, 0.2f), 0.3f);
            const float expectedTopEdgeYDown = 0.0f;
            const float expectedBottomEdgeYDown = 0.4f;

            var (uvScale, uvOffset) = PoseCrop.ToUvTransform(region, sourceWidth: 640, sourceHeight: 480);
            var (blitScale, blitOffset) = PoseCrop.ToBlitTransform(uvScale, uvOffset);

            // The landmarker's cropY = 0 is the crop's own y-down top row; pushed back
            // through both hops it must land on the region's top edge in the source.
            Assert.AreEqual(
                expectedTopEdgeYDown,
                SourceYDownForLandmarkerCropY(0f, blitScale, blitOffset),
                0.0001f,
                "cropY=0 must trace back through ToTensor's TopLeft flip and the Blit to the region's y-down top edge");

            // cropY = 1 is the crop's y-down bottom row -> the region's bottom edge.
            Assert.AreEqual(
                expectedBottomEdgeYDown,
                SourceYDownForLandmarkerCropY(1f, blitScale, blitOffset),
                0.0001f,
                "cropY=1 must trace back through ToTensor's TopLeft flip and the Blit to the region's y-down bottom edge");

            // x axis has no convention mismatch on either hop and must pass through.
            Assert.AreEqual(uvScale.x, blitScale.x, 0.0001f);
            Assert.AreEqual(uvOffset.x, blitOffset.x, 0.0001f);
        }

        [Test]
        public void BlitTransform_BothHopsComposed_AgreesWithBackProjectionAcrossTheWholeCropForSeveralRegions()
        {
            // The provider back-projects landmarks with `cropPos * uvScale + uvOffset`
            // (plain y-down). For the pipeline to be correct, that must equal what the
            // two physical hops actually sampled, for EVERY cropY - not just one point,
            // which a single-sample test could satisfy by accident (e.g. a sign error
            // whose fixed point happens to be the sampled coordinate).
            var regions = new[]
            {
                new PoseCropRegion(new Vector2(0.5f, 0.2f), 0.3f),   // near the top
                new PoseCropRegion(new Vector2(0.5f, 0.5f), 0.4f),   // centered
                new PoseCropRegion(new Vector2(0.3f, 0.75f), 0.25f), // near the bottom
                new PoseCropRegion(new Vector2(0.7f, 0.6f), 0.5f),   // large
            };

            foreach (var region in regions)
            {
                // Non-square source so this also exercises the aspect-corrected,
                // non-uniform scale.x != scale.y case end-to-end.
                var (uvScale, uvOffset) = PoseCrop.ToUvTransform(region, sourceWidth: 640, sourceHeight: 480);
                var (blitScale, blitOffset) = PoseCrop.ToBlitTransform(uvScale, uvOffset);

                foreach (float cropY in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    float backProjected = cropY * uvScale.y + uvOffset.y;
                    float actuallySampled = SourceYDownForLandmarkerCropY(cropY, blitScale, blitOffset);
                    Assert.AreEqual(
                        backProjected,
                        actuallySampled,
                        0.0001f,
                        $"region center={region.Center} size={region.Size}, cropY={cropY}: the y-down source coordinate the two hops physically sampled must match what RunLandmarker's back-projection reports");
                }
            }
        }
    }
}
