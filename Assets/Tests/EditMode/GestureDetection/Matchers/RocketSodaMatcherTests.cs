using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class RocketSodaMatcherTests
    {
        private static Dictionary<PoseJoint, Vector2> Frame(Vector2 leftWrist, Vector2 rightWrist) =>
            new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftWrist, leftWrist },
                { PoseJoint.RightWrist, rightWrist },
                { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
            };

        [Test]
        public void Evaluate_BothFistsShakingBelowChest_Matches()
        {
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                float y = i % 2 == 0 ? 0.55f : 0.65f;
                builder.AddFrame(0.1f, Frame(new Vector2(0.48f, y), new Vector2(0.52f, y)));
            }

            var matcher = new RocketSodaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
        }

        [Test]
        public void Evaluate_HandsHeldStillAboveChest_DoesNotMatch()
        {
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
                builder.AddFrame(0.1f, Frame(new Vector2(0.48f, 0.1f), new Vector2(0.52f, 0.1f)));

            var matcher = new RocketSodaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
