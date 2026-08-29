using UnityEngine;

namespace GestureDetection
{
    // Standard One-Euro filter (Casiez, Roussel, Vogel 2012): a low-pass filter whose
    // cutoff frequency adapts to signal speed, so it smooths noise at rest but tracks
    // fast motion with low lag. Operates on a single scalar over time; the caller keeps
    // one instance per tracked value (e.g. one per landmark axis).
    public class OneEuroFilter
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _derivateCutoff;

        private bool _hasPrevious;
        private float _previousValue;
        private float _previousDerivative;
        private float _previousTimestamp;

        public OneEuroFilter(float minCutoff = 1f, float beta = 0f, float derivateCutoff = 1f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _derivateCutoff = derivateCutoff;
        }

        public float Filter(float value, float timestamp)
        {
            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _previousValue = value;
                _previousDerivative = 0f;
                _previousTimestamp = timestamp;
                return value;
            }

            float dt = Mathf.Max(timestamp - _previousTimestamp, 1e-6f);
            float rate = 1f / dt;

            float dValue = (value - _previousValue) * rate;
            float derivativeAlpha = SmoothingFactor(rate, _derivateCutoff);
            float derivative = Lerp(_previousDerivative, dValue, derivativeAlpha);

            float cutoff = _minCutoff + _beta * Mathf.Abs(derivative);
            float valueAlpha = SmoothingFactor(rate, cutoff);
            float filtered = Lerp(_previousValue, value, valueAlpha);

            _previousValue = filtered;
            _previousDerivative = derivative;
            _previousTimestamp = timestamp;

            return filtered;
        }

        public void Reset()
        {
            _hasPrevious = false;
        }

        private static float SmoothingFactor(float rate, float cutoff)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            float te = 1f / rate;
            return 1f / (1f + tau / te);
        }

        private static float Lerp(float previous, float current, float alpha) =>
            alpha * current + (1f - alpha) * previous;
    }
}
