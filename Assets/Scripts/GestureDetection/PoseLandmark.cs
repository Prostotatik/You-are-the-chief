using UnityEngine;

namespace GestureDetection
{
    // Position is normalized viewport space: (0,0) top-left, (1,1) bottom-right.
    // y grows downward — "above" on screen means a smaller y value.
    public readonly struct PoseLandmark
    {
        public readonly Vector2 Position;
        public readonly float Confidence;

        public PoseLandmark(Vector2 position, float confidence)
        {
            Position = position;
            Confidence = confidence;
        }
    }
}
