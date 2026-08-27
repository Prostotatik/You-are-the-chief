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
        public void Evaluate_WristCirclesAboveElbow_Matches()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            var builder = new LandmarkSequenceBuilder();
            // Wrist above elbow (smaller y), tracing a full circle around it.
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(0.1f, -0.2f)));
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(-0.1f, -0.2f)));
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(-0.2f, -0.1f)));
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(-0.1f, -0.2f)));
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(0.1f, -0.2f)));
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(0.2f, -0.1f)));
            builder.AddFrame(0.1f, RightArm(elbow, elbow + new Vector2(0.1f, -0.2f)));

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
