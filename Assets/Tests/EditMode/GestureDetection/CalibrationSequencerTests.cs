using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class CalibrationSequencerTests
    {
        [Test]
        public void Compute_AveragesShoulderWidthAndHipMidpoint_AsRatioToReference()
        {
            // Shoulder width here is 0.3, 1.5x CalibrationData.ReferenceBodyScale (0.2),
            // so this also verifies BodyScale is a ratio, not the raw measured width.
            var builder = new LandmarkSequenceBuilder();
            builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.35f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.65f, 0.3f) },
                { PoseJoint.LeftHip, new Vector2(0.45f, 0.6f) },
                { PoseJoint.RightHip, new Vector2(0.55f, 0.6f) },
            });
            builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.35f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.65f, 0.3f) },
                { PoseJoint.LeftHip, new Vector2(0.45f, 0.6f) },
                { PoseJoint.RightHip, new Vector2(0.55f, 0.6f) },
            });

            var result = CalibrationSequencer.Compute(builder.Build());

            Assert.AreEqual(1.5f, result.BodyScale, 0.001f);
            Assert.AreEqual(new Vector2(0.5f, 0.6f), result.ReferenceCenter); // hip midpoint
        }

        [Test]
        public void Compute_NoUsableFrames_ReturnsIdentity()
        {
            var builder = new LandmarkSequenceBuilder();
            builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>()); // no joints set -> zero confidence

            var result = CalibrationSequencer.Compute(builder.Build());

            Assert.AreEqual(CalibrationData.Identity.BodyScale, result.BodyScale);
        }

        [Test]
        public void Compute_EmptyList_ReturnsIdentity()
        {
            var result = CalibrationSequencer.Compute(new List<LandmarkFrame>());
            Assert.AreEqual(CalibrationData.Identity.BodyScale, result.BodyScale);
        }
    }
}
