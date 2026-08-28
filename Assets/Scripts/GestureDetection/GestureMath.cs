using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    public static class GestureMath
    {
        // Counts direction reversals (pivots) in a series using hysteresis: a pivot is
        // only confirmed once the series has moved at least minAmplitude AWAY from a
        // running high/low candidate, at which point that candidate becomes the pivot
        // and tracking restarts in the opposite direction. This is standard "zigzag"
        // peak detection - critically, a small dip/spike inside a larger monotonic move
        // (e.g. one noisy landmark sample during a steady rise) does NOT reset or count
        // as a reversal, because the running candidate keeps extending past it instead
        // of committing a pivot at the small dip itself.
        // Used to detect repeated back-and-forth motion (shaking, stomping, rubbing).
        public static int CountReversals(IReadOnlyList<float> values, float minAmplitude)
        {
            if (values.Count < 2) return 0;

            int reversals = 0;
            float extreme = values[0];
            int trend = 0; // 0 = direction not yet established, 1 = tracking a high, -1 = tracking a low

            for (int i = 1; i < values.Count; i++)
            {
                float v = values[i];

                if (trend >= 0 && v > extreme)
                {
                    extreme = v;
                    trend = 1;
                }
                else if (trend <= 0 && v < extreme)
                {
                    extreme = v;
                    trend = -1;
                }
                else if (trend == 1 && extreme - v >= minAmplitude)
                {
                    reversals++;
                    trend = -1;
                    extreme = v;
                }
                else if (trend == -1 && v - extreme >= minAmplitude)
                {
                    reversals++;
                    trend = 1;
                    extreme = v;
                }
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
