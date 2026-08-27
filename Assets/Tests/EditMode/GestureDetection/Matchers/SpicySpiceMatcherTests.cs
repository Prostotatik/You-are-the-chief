using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class SpicySpiceMatcherTests
    {
        private static Dictionary<PoseJoint, Vector2> Frame(Vector2 nose, Vector2 leftWrist, Vector2 rightWrist) =>
            new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.Nose, nose },
                { PoseJoint.LeftWrist, leftWrist },
                { PoseJoint.RightWrist, rightWrist },
            };

        [Test]
        public void Evaluate_FistsAtFaceMovingInAndOut_Matches()
        {
            var nose = new Vector2(0.5f, 0.2f);
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                float offset = i % 2 == 0 ? 0.05f : 0.2f;
                builder.AddFrame(0.1f, Frame(nose, nose + new Vector2(-offset, 0f), nose + new Vector2(offset, 0f)));
            }

            var matcher = new SpicySpiceMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
        }

        [Test]
        public void Evaluate_FistsFarBelowFace_DoesNotMatch()
        {
            var nose = new Vector2(0.5f, 0.2f);
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                float offset = i % 2 == 0 ? 0.05f : 0.2f;
                builder.AddFrame(0.1f, Frame(nose, nose + new Vector2(-offset, 0.6f), nose + new Vector2(offset, 0.6f)));
            }

            var matcher = new SpicySpiceMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
