using UnityEngine;

namespace GestureDetection
{
    // BodyScale is the player's shoulder width (normalized viewport units) measured
    // during calibration. Matchers scale their distance thresholds by this value so
    // detection isn't biased by the player's height or distance from the camera.
    public readonly struct CalibrationData
    {
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
