using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class GestureMathTests
    {
        [Test]
        public void CountReversals_OscillatingSeries_CountsEachDirectionChange()
        {
            var values = new List<float> { 0.5f, 0.6f, 0.5f, 0.6f, 0.5f, 0.6f };
            int reversals = GestureMath.CountReversals(values, minAmplitude: 0.05f);
            Assert.AreEqual(4, reversals);
        }

        [Test]
        public void CountReversals_FlatSeries_ReturnsZero()
        {
            var values = new List<float> { 0.5f, 0.5f, 0.5f, 0.5f };
            int reversals = GestureMath.CountReversals(values, minAmplitude: 0.05f);
            Assert.AreEqual(0, reversals);
        }

        [Test]
        public void CountReversals_BelowAmplitudeThreshold_IsIgnored()
        {
            var values = new List<float> { 0.5f, 0.51f, 0.5f, 0.51f };
            int reversals = GestureMath.CountReversals(values, minAmplitude: 0.05f);
            Assert.AreEqual(0, reversals);
        }

        [Test]
        public void AccumulatedRotation_FullCircle_ReturnsAbout360()
        {
            var points = new List<Vector2>
            {
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(-1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(1f, 0f),
            };
            float rotation = GestureMath.AccumulatedRotation(points);
            Assert.AreEqual(360f, rotation, 1f);
        }

        [Test]
        public void AccumulatedRotation_SinglePoint_ReturnsZero()
        {
            var points = new List<Vector2> { new Vector2(1f, 0f) };
            Assert.AreEqual(0f, GestureMath.AccumulatedRotation(points));
        }
    }
}
