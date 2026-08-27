using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    public static class GestureMath
    {
        // Counts direction reversals in a series whose swing exceeds minAmplitude.
        // Used to detect repeated back-and-forth motion (shaking, stomping, rubbing).
        public static int CountReversals(IReadOnlyList<float> values, float minAmplitude)
        {
            if (values.Count < 2) return 0;

            int reversals = 0;
            int direction = 0;
            float lastExtreme = values[0];

            for (int i = 1; i < values.Count; i++)
            {
                float delta = values[i] - values[i - 1];
                if (Mathf.Abs(delta) < 1e-5f) continue;

                int newDirection = delta > 0f ? 1 : -1;
                if (direction != 0 && newDirection != direction)
                {
                    if (Mathf.Abs(values[i - 1] - lastExtreme) >= minAmplitude)
                    {
                        reversals++;
                        lastExtreme = values[i - 1];
                    }
                }

                direction = newDirection;
            }

            return reversals;
        }

        // Sums the signed angular delta between consecutive vectors (treated as
        // offsets from a pivot) and returns the absolute total in degrees.
        // Used to detect a hand tracing a circular path (e.g. twirling dough).
        public static float AccumulatedRotation(IReadOnlyList<Vector2> pivotRelativeVectors)
        {
            if (pivotRelativeVectors.Count < 2) return 0f;

            float total = 0f;
            for (int i = 1; i < pivotRelativeVectors.Count; i++)
            {
                float angleA = Mathf.Atan2(pivotRelativeVectors[i - 1].y, pivotRelativeVectors[i - 1].x) * Mathf.Rad2Deg;
                float angleB = Mathf.Atan2(pivotRelativeVectors[i].y, pivotRelativeVectors[i].x) * Mathf.Rad2Deg;
                total += Mathf.DeltaAngle(angleA, angleB);
            }

            return Mathf.Abs(total);
        }
    }
}
