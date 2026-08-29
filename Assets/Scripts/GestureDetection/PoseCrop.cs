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

        // Converts a region into the (scale, offset) pair Graphics.Blit(Texture,
        // RenderTexture, Vector2 scale, Vector2 offset) expects: it samples the source
        // texture at uv * scale + offset, so this maps blit-space uv=(0,0)..(1,1) onto
        // the region's own min..max corners in source-texture UV space.
        public static (Vector2 scale, Vector2 offset) ToUvTransform(PoseCropRegion region)
        {
            Vector2 scale = new Vector2(region.Size, region.Size);
            Vector2 offset = region.Center - scale * 0.5f;
            return (scale, offset);
        }
    }
}
