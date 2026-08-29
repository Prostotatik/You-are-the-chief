using UnityEngine;

namespace GestureDetection
{
    // An axis-aligned square region of the source webcam texture to crop and feed to
    // the 256x256 landmarker model. Center and Size are normalized to the source
    // texture's own [0,1] UV space (Size as a fraction of max(sourceWidth, sourceHeight)).
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

            Vector2 center = (min + max) * 0.5f;
            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            float size = span * (1f + paddingFraction);
            return new PoseCropRegion(center, size);
        }

        // Converts a region into the (scale, offset) pair used to map a normalized
        // position within the crop (blit-space, 0..1) back to a position in the full
        // source texture - both sides of this transform are in this project's native
        // y-down convention (PoseLandmark.Position: (0,0) top-left, y grows downward).
        // sourcePosition = cropPosition * scale + offset.
        public static (Vector2 scale, Vector2 offset) ToUvTransform(PoseCropRegion region)
        {
            Vector2 scale = new Vector2(region.Size, region.Size);
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
        // Worked example: region Center=(0.5, 0.2), Size=0.3 -> ToUvTransform gives
        // scale=(0.3, 0.3), offset=(0.35, 0.05). This method gives blitScale=(0.3, 0.3),
        // blitOffset=(0.35, 0.65). Landmarker cropY=0 -> t = 1 -> v_src = 0.3 + 0.65 =
        // 0.95 -> y-down 0.05 = offset.y. Landmarker cropY=1 -> t = 0 -> v_src = 0.65 ->
        // y-down 0.35 = offset.y + scale.y. Both ends round-trip. See PoseCropTests for
        // the test that models both hops independently and pins these numbers.
        public static (Vector2 scale, Vector2 offset) ToBlitTransform(Vector2 scale, Vector2 offset)
        {
            Vector2 blitScale = new Vector2(scale.x, scale.y);
            Vector2 blitOffset = new Vector2(offset.x, 1f - offset.y - scale.y);
            return (blitScale, blitOffset);
        }
    }
}
