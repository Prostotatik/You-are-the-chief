using UnityEngine;

namespace GestureDetection
{
    // An axis-aligned square region of the source webcam texture to crop and feed to
    // the 256x256 landmarker model. Center and Size are normalized to the source
    // texture's own [0,1] UV space, with Size a fraction of max(sourceWidth,
    // sourceHeight) - i.e. Center/Size describe a PHYSICALLY square region, not a
    // UV-square one. On a non-square source (e.g. 640x480) a UV-equal-on-both-axes
    // square is not physically square: a fixed UV delta covers more physical pixels on
    // the wider axis. ToUvTransform is where this gets corrected into the actual
    // (possibly non-equal) UV scale needed to sample a physically-square region - see
    // its doc comment. FromDetection/FromLandmarkBounds below compute Size from UV-space
    // distances and do NOT apply this correction themselves (they're also subject to the
    // same distortion if the source keypoints/bounds aren't purely horizontal), so Size
    // as produced by them is an approximation of the physical span; ToUvTransform is the
    // single point where physical-square correctness is actually enforced, since that's
    // where pixels are physically sampled.
    //
    // Unlike Unity's own BlazePose sample, this deliberately carries no rotation: that
    // sample rotates its crop to handle a person lying sideways or upside-down, which
    // this project's game never needs (players are seated/standing at a desk, upright,
    // facing the camera). Staying axis-aligned keeps the crop step a plain UV scale/
    // offset blit - see PoseCrop.ToUvTransform and SentisPoseProvider.RunLandmarker.
    public readonly struct PoseCropRegion
    {
        public readonly Vector2 Center;
        public readonly float Size;

        public PoseCropRegion(Vector2 center, float size)
        {
            Center = center;
            Size = size;
        }
    }

    // Turns either a fresh detector result or the previous frame's landmark spread into
    // a PoseCropRegion for the next landmarker inference. Mirrors the two data sources
    // Unity's own BlazePose sample uses: a full detector pass (FromDetection) when no
    // recent region is trusted, and a cheap reuse of last frame's landmark bounds
    // (FromLandmarkBounds) otherwise - see SentisPoseProvider for which path is chosen
    // each frame.
    public static class PoseCrop
    {
        public static PoseCropRegion FromDetection(Vector2 keypoint1, Vector2 keypoint2, Vector2 boxCenter, float scale = 1.25f)
        {
            float size = scale * Vector2.Distance(keypoint1, keypoint2);
            return new PoseCropRegion(boxCenter, size);
        }

        // Below this, a landmark-bounds span isn't a real detection - it's noise (e.g.
        // the landmarker output collapsing onto near-identical positions for >=2
        // joints). Without this guard a tiny non-zero span still passes Update()'s
        // `boundsRegion.Size > 0f` check and gets fed to the landmarker as a
        // sub-pixel-wide crop for up to MaxFramesBetweenDetections frames.
        private const float MinUsableSpan = 0.02f; // ~2% of frame

        public static PoseCropRegion FromLandmarkBounds(LandmarkFrame frame, float minConfidence = 0.4f, float paddingFraction = 0.35f)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            int count = 0;

            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                if (!JointFilter.TryGet(frame, (PoseJoint)i, out var position, minConfidence)) continue;
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                count++;
            }

            if (count < 2) return default;

            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            if (span < MinUsableSpan) return default;

            Vector2 center = (min + max) * 0.5f;
            float size = span * (1f + paddingFraction);
            return new PoseCropRegion(center, size);
        }

        // Converts a region into the (scale, offset) pair used to map a normalized
        // position within the crop (blit-space, 0..1) back to a position in the full
        // source texture - both sides of this transform are in this project's native
        // y-down convention (PoseLandmark.Position: (0,0) top-left, y grows downward).
        // sourcePosition = cropPosition * scale + offset.
        //
        // sourceWidth/sourceHeight correct for a non-square source so the UV rect
        // sampled is PHYSICALLY square (matching PoseCropRegion's doc comment: Size is a
        // fraction of max(sourceWidth, sourceHeight)), not merely UV-square. A fixed UV
        // delta covers more physical pixels on the wider axis, so the narrower axis's UV
        // span must be inflated by the source's aspect ratio to cover the same physical
        // distance as the wider axis.
        //
        // Worked example: sourceWidth=640, sourceHeight=480 (4:3), Size=0.3. The intended
        // physical square is Size * max(640,480) = 192x192 px. Uncorrected UV scale
        // (0.3, 0.3) would sample 0.3*640=192px horizontally but only 0.3*480=144px
        // vertically - a 192x144 rect, not square. Corrected: x keeps span 0.3 (192px,
        // already right since width is the max dimension); y-span becomes
        // 0.3 * (640/480) = 0.4, i.e. 0.4*480=192px - now physically square. In general,
        // for source wider than tall (sourceWidth >= sourceHeight): scale.x = Size,
        // scale.y = Size * (sourceWidth / sourceHeight). For source taller than wide:
        // scale.y = Size, scale.x = Size * (sourceHeight / sourceWidth). Both reduce to
        // scale = Size * maxDimension / axisDimension.
        public static (Vector2 scale, Vector2 offset) ToUvTransform(PoseCropRegion region, int sourceWidth, int sourceHeight)
        {
            float maxDimension = Mathf.Max(sourceWidth, sourceHeight);
            Vector2 scale = new Vector2(
                region.Size * (maxDimension / sourceWidth),
                region.Size * (maxDimension / sourceHeight));
            Vector2 offset = region.Center - scale * 0.5f;
            return (scale, offset);
        }

        // Converts a y-down ToUvTransform() pair into the (scale, offset) that must be
        // passed to Graphics.Blit(Texture, RenderTexture, Vector2 scale, Vector2 offset)
        // so that, AFTER the whole two-hop pipeline in SentisPoseProvider.RunLandmarker,
        // the landmarker's y-down crop output back-projects through the ORIGINAL y-down
        // ToUvTransform pair onto the correct source pixel.
        //
        // The pipeline has TWO hops, and both must be accounted for:
        //
        //   _webcamTexture --Graphics.Blit(scale,offset)--> _cropTexture
        //                  --TextureConverter.ToTensor--> _landmarkerInputTensor
        //
        // Hop 2 (ToTensor) already flips. RunLandmarker calls it with
        // `new TextureTransform().SetTensorLayout(TensorLayout.NHWC)`, which leaves
        // coordOrigin at its default CoordOrigin.TopLeft (= 0; verified in the Inference
        // Engine package source, Runtime/Core/Converters/TextureTransform.cs - coordOrigin
        // is an auto-property never touched by SetTensorLayout). The conversion shader
        // (Runtime/Core/Resources/Sentis/TextureConversion/TextureToTensor.compute and
        // .shader, both ~line 45) does:
        //     if (CoordOrigin == 0) // CoordOrigin.TopLeft
        //         O_pos.y = O_size.y - 1 - O_pos.y;
        // i.e. tensor row 0 (the landmarker's cropY = 0) reads the crop texture's
        // PHYSICAL TOP row. In normalized terms the crop-texture UV sampled for a given
        // landmarker cropY is v_crop = 1 - cropY.
        //
        // Hop 1 (Graphics.Blit) introduces NO flip of its own: it draws a fullscreen quad
        // whose destination UV t runs bottom-to-top and samples the source at
        // v_src = t * blitScale.y + blitOffset.y. With scale=1, offset=0 that is an
        // identity copy (physical top -> physical top).
        //
        // Composing, with t = v_crop = 1 - cropY:
        //     v_src = (1 - cropY) * blitScale.y + blitOffset.y
        // The back-projection in RunLandmarker computes, in y-down space,
        //     sourceYDown = cropY * scale.y + offset.y
        // and y-down relates to source UV by v_src = 1 - sourceYDown. Substituting and
        // matching coefficients of cropY:
        //     -blitScale.y = -scale.y                 =>  blitScale.y = scale.y
        //     blitScale.y + blitOffset.y = 1 - offset.y
        //                                             =>  blitOffset.y = 1 - offset.y - scale.y
        //
        // NOTE the two easy mistakes this formula deliberately avoids: blitScale.y is NOT
        // negated (the negation would double up on the flip ToTensor already performs),
        // and blitOffset.y carries the "- scale.y" term (without it, only one end of the
        // range round-trips).
        //
        // The x axis needs no correction - neither hop touches it.
        //
        // Worked example: region Center=(0.5, 0.2), Size=0.3, source 640x480 (4:3) ->
        // ToUvTransform gives scale=(0.3, 0.4) (aspect-corrected per its own doc
        // comment), offset=(0.35, 0.0). This method gives blitScale=(0.3, 0.4),
        // blitOffset=(0.35, 0.6). Landmarker cropY=0 -> t = 1 -> v_src = 0.4 + 0.6 = 1.0
        // -> y-down 0.0 = offset.y. Landmarker cropY=1 -> t = 0 -> v_src = 0.6 ->
        // y-down 0.4 = offset.y + scale.y. Both ends round-trip. See PoseCropTests for
        // the test that models both hops independently and pins these numbers.
        // (This formula's derivation is agnostic to whether scale.x == scale.y - it only
        // reasons about the y axis in isolation - so it is unaffected by ToUvTransform's
        // aspect correction; only the numbers above changed to reflect a non-square
        // scale.)
        public static (Vector2 scale, Vector2 offset) ToBlitTransform(Vector2 scale, Vector2 offset)
        {
            Vector2 blitScale = new Vector2(scale.x, scale.y);
            Vector2 blitOffset = new Vector2(offset.x, 1f - offset.y - scale.y);
            return (blitScale, blitOffset);
        }
    }
}
