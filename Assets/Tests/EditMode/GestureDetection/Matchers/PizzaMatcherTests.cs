using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class PizzaMatcherTests
    {
        private static Dictionary<PoseJoint, Vector2> RightArm(Vector2 elbow, Vector2 wrist) =>
            new Dictionary<PoseJoint, Vector2> { { PoseJoint.RightElbow, elbow }, { PoseJoint.RightWrist, wrist } };

        [Test]
        public void Evaluate_WristTracesFullCircleAroundElbow_Matches()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            const float radius = 0.2f;
            var builder = new LandmarkSequenceBuilder();
            // 8 steps of 45 degrees = one full monotonic 360-degree loop around the elbow.
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                var wrist = elbow + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                builder.AddFrame(0.1f, RightArm(elbow, wrist));
            }

            var matcher = new PizzaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(1f, result.Progress, 0.01f);
        }

        [Test]
        public void Evaluate_WristStaysStill_DoesNotMatch()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            var wrist = elbow + new Vector2(0f, -0.2f);
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
                builder.AddFrame(0.1f, RightArm(elbow, wrist));

            var matcher = new PizzaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
