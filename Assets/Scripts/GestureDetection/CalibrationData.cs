using UnityEngine;

namespace GestureDetection
{
    // BodyScale is a dimensionless ratio: the player's measured shoulder width divided
    // by ReferenceBodyScale. Matchers multiply their base thresholds (all tuned assuming
    // a "typical" player, i.e. BodyScale == 1) by this ratio, so a player who is closer
    // to/further from the camera - or simply bigger/smaller on screen - gets
    // proportionally scaled thresholds instead of the untuned raw shoulder-width value.
    public readonly struct CalibrationData
    {
        // Typical shoulder width in normalized viewport units at a comfortable webcam
        // distance. Raw measured shoulder widths are divided by this to produce
        // BodyScale, so BodyScale == 1 means "matches the assumption every matcher's
        // base threshold was tuned against."
        public const float ReferenceBodyScale = 0.2f;

        public readonly float BodyScale;
        public readonly Vector2 ReferenceCenter;

        public CalibrationData(float bodyScale, Vector2 referenceCenter)
        {
            BodyScale = bodyScale;
            ReferenceCenter = referenceCenter;
        }

        public static readonly CalibrationData Identity = new CalibrationData(1f, Vector2.zero);
    }
}
