using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    // Spec-mandated coverage (docs/superpowers/specs/2026-08-28-gesture-detection-design.md,
    // Testing Strategy): each matcher's tests should include "a different gesture sequence
    // (should not cross-trigger another matcher)". No single matcher's own test file can
    // prove that on its own - it takes evaluating all 5 matchers against all 5 fixtures
    // together. Each fixture below is the same clean, known-good positive fixture already
    // proven in that gesture's own *MatcherTests.cs file.
    public class CrossGestureMatrixTests
    {
        private static Dictionary<GestureType, List<LandmarkFrame>> BuildFixtures()
        {
            return new Dictionary<GestureType, List<LandmarkFrame>>
            {
                { GestureType.Pizza, BuildPizzaFixture() },
                { GestureType.MacAndCheese, BuildMacAndCheeseFixture() },
                { GestureType.RocketSoda, BuildRocketSodaFixture() },
                { GestureType.Wine, BuildWineFixture() },
                { GestureType.SpicySpice, BuildSpicySpiceFixture() },
            };
        }

        private static List<IGestureMatcher> BuildMatchers()
        {
            return new List<IGestureMatcher>
            {
                new PizzaMatcher(),
                new MacAndCheeseMatcher(),
                new RocketSodaMatcher(),
                new WineMatcher(),
                new SpicySpiceMatcher(),
            };
        }

        [Test]
        public void EachMatcher_OnlyMatchesItsOwnCleanFixture()
        {
            var fixtures = BuildFixtures();
            var matchers = BuildMatchers();

            foreach (var matcher in matchers)
            {
                foreach (var pair in fixtures)
                {
                    var result = matcher.Evaluate(pair.Value, CalibrationData.Identity);
                    bool expectedMatch = pair.Key == matcher.GestureType;

                    Assert.AreEqual(
                        expectedMatch,
                        result.IsMatch,
                        $"{matcher.GestureType} matcher evaluated against the {pair.Key} fixture: " +
                        $"expected IsMatch={expectedMatch}, got {result.IsMatch} (progress={result.Progress})");
                }
            }
        }

        private static List<LandmarkFrame> BuildPizzaFixture()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            var center = elbow + new Vector2(0f, -0.05f);
            const float radius = 0.2f;
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                var wrist = center + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.RightElbow, elbow },
                    { PoseJoint.RightWrist, wrist },
                });
            }
            return builder.Build();
        }

        private static List<LandmarkFrame> BuildMacAndCheeseFixture()
        {
            var knee = new Vector2(0.5f, 0.6f);
            var ankle = new Vector2(0.5f, 0.4f);
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                var wrist = ankle + new Vector2(0f, i % 2 == 0 ? 0.02f : 0.08f);
                builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftAnkle, ankle },
                    { PoseJoint.LeftKnee, knee },
                    { PoseJoint.RightWrist, wrist },
                });
            }
            return builder.Build();
        }

        private static List<LandmarkFrame> BuildRocketSodaFixture()
        {
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                float y = i % 2 == 0 ? 0.55f : 0.65f;
                builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.LeftWrist, new Vector2(0.48f, y) },
                    { PoseJoint.RightWrist, new Vector2(0.52f, y) },
                    { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                    { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
                });
            }
            return builder.Build();
        }

        private static List<LandmarkFrame> BuildWineFixture()
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
                    { PoseJoint.LeftHip, new Vector2(0.4f, 0.5f) },
                    { PoseJoint.RightHip, new Vector2(0.6f, 0.5f) },
                });
            }
            return builder.Build();
        }

        private static List<LandmarkFrame> BuildSpicySpiceFixture()
        {
            var nose = new Vector2(0.5f, 0.2f);
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                float offset = i % 2 == 0 ? 0.05f : 0.2f;
                builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
                {
                    { PoseJoint.Nose, nose },
                    { PoseJoint.LeftWrist, nose + new Vector2(-offset, 0f) },
                    { PoseJoint.RightWrist, nose + new Vector2(offset, 0f) },
                });
            }
            return builder.Build();
        }
    }
}
