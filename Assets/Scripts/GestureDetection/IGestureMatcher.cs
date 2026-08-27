using System.Collections.Generic;

namespace GestureDetection
{
    public interface IGestureMatcher
    {
        GestureType GestureType { get; }
        MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration);
    }
}
