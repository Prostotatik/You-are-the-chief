using UnityEngine;

namespace GestureDetection
{
    public readonly struct MatchResult
    {
        public readonly bool IsMatch;
        public readonly float Progress;

        public MatchResult(bool isMatch, float progress)
        {
            IsMatch = isMatch;
            Progress = Mathf.Clamp01(progress);
        }

        public static readonly MatchResult None = new MatchResult(false, 0f);
    }
}
