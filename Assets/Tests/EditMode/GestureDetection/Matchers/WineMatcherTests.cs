using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class WineMatcherTests
    {
        private static readonly Vector2 LeftHip = new Vector2(0.4f, 0.5f);
        private static readonly Vector2 RightHip = new Vector2(0.6f, 0.5f);

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
                    { PoseJoint.LeftHip, LeftHip },
                    { PoseJoint.RightHip, RightHip },
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
                    { PoseJoint.LeftHip, LeftHip },
                    { PoseJoint.RightHip, RightHip },
                });
            }

            var matcher = new WineMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }

        [Test]
        public void Evaluate_OnlyOneFootBouncing_DoesNotMatch()
        {
            // Left ankle oscillates enough to satisfy the old (pre-fix) combined-only
            // threshold on its own; the right foot never moves. This must NOT match,
            // since a real stomp needs both feet to contribute.
            var builder = new LandmarkSequenceBuilder();
            float[] leftYs = { 0.7f, 0.9f, 0.7f, 0.9f, 0.7f, 0.9f };
            for (int i = 0; i < leftYs.Length; i++)
            {
                builder.AddFrame(0.15f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, new Vector2(0.4f, leftYs[i]) },
                    { PoseJoint.RightAnkle, new Vector2(0.6f, 0.8f) },
                    { PoseJoint.LeftHip, LeftHip },
                    { PoseJoint.RightHip, RightHip },
                });
            }

            var matcher = new WineMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }

        [Test]
        public void Evaluate_AnkleAboveHip_DoesNotContributeStrikes()
        {
            // Same oscillation as the positive test, but the ankle is drawn above the hip
            // (e.g. a raised-leg pose) on every frame for one foot - that foot's strikes
            // must not count, so with only one foot left contributing this should not match.
            var builder = new LandmarkSequenceBuilder();
            float[] leftYs = { 0.2f, 0.1f, 0.2f, 0.1f }; // above LeftHip.y (0.5): ungrounded
            float[] rightYs = { 0.9f, 0.7f, 0.9f, 0.7f };
            for (int i = 0; i < leftYs.Length; i++)
            {
                builder.AddFrame(0.15f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, new Vector2(0.4f, leftYs[i]) },
                    { PoseJoint.RightAnkle, new Vector2(0.6f, rightYs[i]) },
                    { PoseJoint.LeftHip, LeftHip },
                    { PoseJoint.RightHip, RightHip },
                });
            }

            var matcher = new WineMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
