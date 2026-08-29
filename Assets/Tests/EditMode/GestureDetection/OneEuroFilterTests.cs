using GestureDetection;
using NUnit.Framework;

namespace GestureDetection.Tests
{
    public class OneEuroFilterTests
    {
        [Test]
        public void Filter_FirstCall_ReturnsInputUnchanged()
        {
            var filter = new OneEuroFilter();
            float result = filter.Filter(0.5f, timestamp: 0f);
            Assert.AreEqual(0.5f, result);
        }

        [Test]
        public void Filter_NoisySignalAroundConstant_OutputVariesLessThanInput()
        {
            var filter = new OneEuroFilter(minCutoff: 1f, beta: 0f, derivateCutoff: 1f);
            float[] noisy = { 0.50f, 0.54f, 0.47f, 0.53f, 0.48f, 0.52f, 0.49f, 0.55f, 0.46f, 0.51f };

            float t = 0f;
            float first = filter.Filter(noisy[0], t);
            float minOut = first, maxOut = first;

            for (int i = 1; i < noisy.Length; i++)
            {
                t += 1f / 30f; // simulate 30fps
                float output = filter.Filter(noisy[i], t);
                minOut = UnityEngine.Mathf.Min(minOut, output);
                maxOut = UnityEngine.Mathf.Max(maxOut, output);
            }

            float inputSpread = 0.55f - 0.46f;
            float outputSpread = maxOut - minOut;
            Assert.Less(outputSpread, inputSpread, "Filtered output should vary less than the noisy input.");
        }

        [Test]
        public void Filter_SteppedSignal_EventuallyTracksNewValue()
        {
            var filter = new OneEuroFilter(minCutoff: 1f, beta: 0f, derivateCutoff: 1f);
            float t = 0f;
            filter.Filter(0f, t);

            float lastOutput = 0f;
            for (int i = 0; i < 60; i++)
            {
                t += 1f / 30f;
                lastOutput = filter.Filter(1f, t);
            }

            Assert.Greater(lastOutput, 0.9f, "After 2 seconds of a held new value, the filter should have converged close to it.");
        }

        [Test]
        public void Reset_ThenFilter_ReturnsNewValueUnchangedLikeFirstCall()
        {
            var filter = new OneEuroFilter();
            filter.Filter(0.2f, 0f);
            filter.Filter(0.2f, 1f / 30f);

            filter.Reset();
            float result = filter.Filter(0.9f, 2f);

            Assert.AreEqual(0.9f, result);
        }
    }
}
