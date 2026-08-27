using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class MacAndCheeseMatcherTests
    {
        [Test]
        public void Evaluate_RaisedHeelWithRubbingFist_Matches()
        {
            var knee = new Vector2(0.5f, 0.6f);
            var ankle = new Vector2(0.5f, 0.4f); // raised above the knee (smaller y)

            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                // Alternates the wrist's distance from the ankle (0.02 vs 0.08) so the
                // rubbing motion actually oscillates. A symmetric +-offset around the
                // ankle would keep the distance constant and never trigger a reversal.
                var wrist = ankle + new Vector2(0f, (i % 2 == 0 ? 0.02f : 0.08f));
                builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, ankle },
                    { PoseJoint.LeftKnee, knee },
                    { PoseJoint.RightWrist, wrist },
                });
            }

            var matcher = new MacAndCheeseMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
        }

        [Test]
        public void Evaluate_LegNotRaised_DoesNotMatch()
        {
            var knee = new Vector2(0.5f, 0.6f);
            var ankle = new Vector2(0.5f, 0.9f); // below the knee: leg not raised

            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                var wrist = ankle + new Vector2(0f, (i % 2 == 0 ? 0.05f : -0.05f));
                builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, ankle },
                    { PoseJoint.LeftKnee, knee },
                    { PoseJoint.RightWrist, wrist },
                });
            }

            var matcher = new MacAndCheeseMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
