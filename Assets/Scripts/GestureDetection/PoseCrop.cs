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
        // to physically sample the SAME region.
        //
        // Graphics.Blit samples the source texture in UV space, which is y-UP (v=0 is
        // the texture's bottom edge, v=1 is its top edge) - the opposite of this
        // project's y-down convention that ToUvTransform's offset/scale are expressed
        // in. Feeding a y-down (scale, offset) straight into Blit samples the vertically
        // mirrored region. This performs the one corrective y-flip needed, keeping the
        // x axis (which has no convention mismatch) untouched.
        //
        // Derivation: a y-down region spans yd in [offset.y, offset.y + scale.y] (top
        // edge to bottom edge). The corresponding texture-v coordinate for a y-down
        // value yd is v = 1 - yd (v grows upward, yd grows downward). To have
        // blit-space t=0 sample the region's physical TOP edge (v = 1 - offset.y) and
        // blit-space t=1 sample its physical BOTTOM edge (v = 1 - (offset.y +
        // scale.y)), solve v = t * scale'.y + offset'.y for the two endpoints:
        //   offset'.y = 1 - offset.y
        //   scale'.y  = (1 - (offset.y + scale.y)) - (1 - offset.y) = -scale.y
        // Worked example: region Center=(0.5, 0.2), Size=0.3 (near the top of a y-down
        // frame) -> ToUvTransform gives offset=(0.35, 0.05), scale=(0.3, 0.3). This
        // method gives offset'=(0.35, 0.95), scale'=(0.3, -0.3), so blit-space t=0 samples
        // v=0.95 and t=1 samples v=0.65 - both in the texture's upper half (v close to
        // 1), i.e. physically the top-ish strip of the source texture, matching y-down
        // intuition. See PoseCropTests for the algebraic round-trip that pins this.
        public static (Vector2 scale, Vector2 offset) ToBlitTransform(Vector2 scale, Vector2 offset)
        {
            Vector2 blitScale = new Vector2(scale.x, -scale.y);
            Vector2 blitOffset = new Vector2(offset.x, 1f - offset.y);
            return (blitScale, blitOffset);
        }
    }
}
