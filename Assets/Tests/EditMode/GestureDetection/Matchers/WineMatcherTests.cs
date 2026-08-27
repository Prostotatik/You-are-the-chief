using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class WineMatcherTests
    {
        [Test]
        public void Evaluate_AlternatingFootStomps_Matches()
        {
            var builder = new LandmarkSequenceBuilder();
            float[] leftYs = { 0.7f, 0.9f, 0.7f, 0.9f };
            float[] rightYs = { 0.9f, 0.7f, 0.9f, 0.7f };
            for (int i = 0; i < leftYs.Length; i++)
            {
                builder.AddFrame(0.15f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, new Vector2(0.4f, leftYs[i]) },
                    { PoseJoint.RightAnkle, new Vector2(0.6f, rightYs[i]) },
                });
            }

            var matcher = new WineMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
        }

        [Test]
        public void Evaluate_FeetStandingStill_DoesNotMatch()
        {
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 4; i++)
            {
                builder.AddFrame(0.15f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, new Vector2(0.4f, 0.8f) },
                    { PoseJoint.RightAnkle, new Vector2(0.6f, 0.8f) },
                });
            }

            var matcher = new WineMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
