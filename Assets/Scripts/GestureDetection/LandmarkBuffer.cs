using System.Collections.Generic;

namespace GestureDetection
{
    public class LandmarkBuffer
    {
        private readonly List<LandmarkFrame> _frames = new List<LandmarkFrame>();
        private readonly float _maxAgeSeconds;

        public LandmarkBuffer(float maxAgeSeconds = 2.5f)
        {
            _maxAgeSeconds = maxAgeSeconds;
        }

        public void Add(LandmarkFrame frame)
        {
            _frames.Add(frame);
            float cutoff = frame.Timestamp - _maxAgeSeconds;
            while (_frames.Count > 0 && _frames[0].Timestamp < cutoff)
            {
                _frames.RemoveAt(0);
            }
        }

        public IReadOnlyList<LandmarkFrame> GetWindow(float seconds)
        {
            if (_frames.Count == 0) return System.Array.Empty<LandmarkFrame>();

            float latest = _frames[_frames.Count - 1].Timestamp;
            float cutoff = latest - seconds;
            var result = new List<LandmarkFrame>();
            foreach (var frame in _frames)
            {
                if (frame.Timestamp >= cutoff) result.Add(frame);
            }
            return result;
        }

        public void Clear() => _frames.Clear();
    }
}
