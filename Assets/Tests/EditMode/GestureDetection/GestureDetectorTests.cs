using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class GestureDetectorTests
    {
        private static LandmarkFrame WineStompFrame(float timestamp, float leftY, float rightY)
        {
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);
            joints[(int)PoseJoint.LeftAnkle] = new PoseLandmark(new Vector2(0.4f, leftY), 1f);
            joints[(int)PoseJoint.RightAnkle] = new PoseLandmark(new Vector2(0.6f, rightY), 1f);
            joints[(int)PoseJoint.LeftHip] = new PoseLandmark(new Vector2(0.4f, 0.5f), 1f);
            joints[(int)PoseJoint.RightHip] = new PoseLandmark(new Vector2(0.6f, 0.5f), 1f);
            return new LandmarkFrame(timestamp, joints);
        }

        [Test]
        public void HandleLandmarkFrame_CleanWineSequence_FiresOnGestureRecognized()
        {
            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();
            var poseProvider = new FakePoseProvider();
            detector.Initialize(poseProvider);

            GestureType? recognized = null;
            detector.OnGestureRecognized += g => recognized = g;

            float[] leftYs = { 0.7f, 0.9f, 0.7f, 0.9f };
            float[] rightYs = { 0.9f, 0.7f, 0.9f, 0.7f };
            for (int i = 0; i < leftYs.Length; i++)
                poseProvider.PushFrame(WineStompFrame(i * 0.15f, leftYs[i], rightYs[i]));

            Assert.AreEqual(GestureType.Wine, recognized);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HandleLandmarkFrame_EmitsAtMostOneProgressEventPerFrame_AndFinalOneOnMatch()
        {
            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();
            var poseProvider = new FakePoseProvider();
            detector.Initialize(poseProvider);

            var allProgressEvents = new List<(GestureType gesture, float progress)>();
            detector.OnGestureProgress += (g, p) => allProgressEvents.Add((g, p));

            float[] leftYs = { 0.7f, 0.9f, 0.7f, 0.9f };
            float[] rightYs = { 0.9f, 0.7f, 0.9f, 0.7f };
            for (int i = 0; i < leftYs.Length; i++)
            {
                int countBefore = allProgressEvents.Count;
                poseProvider.PushFrame(WineStompFrame(i * 0.15f, leftYs[i], rightYs[i]));

                // Never more than one matcher's progress reported for a single frame -
                // not one event per matcher (5 matchers exist).
                Assert.LessOrEqual(allProgressEvents.Count - countBefore, 1);
            }

            // Somewhere in the sequence, the frame that produced the match must have
            // reported progress 1f for the matched gesture, not some fractional value
            // from mid-evaluation - it doesn't have to be the very last frame pushed,
            // since WineMatcher can satisfy its threshold before the sequence ends.
            Assert.IsTrue(allProgressEvents.Count > 0);
            var lastEvent = allProgressEvents[allProgressEvents.Count - 1];
            Assert.AreEqual(GestureType.Wine, lastEvent.gesture);
            Assert.AreEqual(1f, lastEvent.progress);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HandleLandmarkFrame_ProgressFallingBelowFloor_RetractsToZeroInsteadOfStaying()
        {
            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();
            var poseProvider = new FakePoseProvider();
            detector.Initialize(poseProvider);

            var events = new List<(GestureType gesture, float progress)>();
            detector.OnGestureProgress += (g, p) => events.Add((g, p));

            // Left ankle alone produces one strike (not enough feet to match Wine, but
            // enough for its progress to clear the report floor).
            poseProvider.PushFrame(WineStompFrame(0f, 0.7f, 0.8f));
            poseProvider.PushFrame(WineStompFrame(0.15f, 0.9f, 0.8f));
            poseProvider.PushFrame(WineStompFrame(0.3f, 0.7f, 0.8f));

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(GestureType.Wine, events[0].gesture);
            Assert.Greater(events[0].progress, 0f);

            // Push enough later, static frames that the 1.5s evaluation window slides
            // past the oscillating frames above entirely - progress should retract to 0
            // rather than staying frozen at its last reported value.
            poseProvider.PushFrame(WineStompFrame(5f, 0.7f, 0.8f));
            poseProvider.PushFrame(WineStompFrame(5.15f, 0.7f, 0.8f));

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(GestureType.Wine, events[1].gesture);
            Assert.AreEqual(0f, events[1].progress);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HandleLandmarkFrame_AfterMatch_LocksAndIgnoresFurtherFrames()
        {
            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();
            var poseProvider = new FakePoseProvider();
            detector.Initialize(poseProvider);

            int recognizedCount = 0;
            detector.OnGestureRecognized += _ => recognizedCount++;

            float[] leftYs = { 0.7f, 0.9f, 0.7f, 0.9f, 0.7f, 0.9f };
            float[] rightYs = { 0.9f, 0.7f, 0.9f, 0.7f, 0.9f, 0.7f };
            for (int i = 0; i < leftYs.Length; i++)
                poseProvider.PushFrame(WineStompFrame(i * 0.15f, leftYs[i], rightYs[i]));

            Assert.AreEqual(1, recognizedCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ResetLock_ClearsBufferSoStaleFramesDoNotImmediatelyRefire_ThenAllowsANewMatch()
        {
            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();
            var poseProvider = new FakePoseProvider();
            detector.Initialize(poseProvider);

            int recognizedCount = 0;
            detector.OnGestureRecognized += _ => recognizedCount++;

            float[] leftYs = { 0.7f, 0.9f, 0.7f, 0.9f };
            float[] rightYs = { 0.9f, 0.7f, 0.9f, 0.7f };
            for (int i = 0; i < leftYs.Length; i++)
                poseProvider.PushFrame(WineStompFrame(i * 0.15f, leftYs[i], rightYs[i]));
            Assert.AreEqual(1, recognizedCount);

            detector.ResetLock();

            // A single static frame right after reset: if the buffer weren't cleared,
            // the still-buffered stomp frames above would still be inside the window
            // and would immediately re-match on this frame alone.
            poseProvider.PushFrame(WineStompFrame(0.6f, 0.8f, 0.8f));
            Assert.AreEqual(1, recognizedCount);

            // A fresh clean stomp sequence should now be able to match again.
            for (int i = 0; i < leftYs.Length; i++)
                poseProvider.PushFrame(WineStompFrame(1f + i * 0.15f, leftYs[i], rightYs[i]));
            Assert.AreEqual(2, recognizedCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void OnCameraUnavailable_ForwardsFromPoseProvider()
        {
            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();
            var poseProvider = new FakePoseProvider();
            detector.Initialize(poseProvider);

            bool fired = false;
            detector.OnCameraUnavailable += () => fired = true;

            poseProvider.PushCameraUnavailable();

            Assert.IsTrue(fired);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void OnCameraUnavailable_AlreadyUnavailableBeforeInitialize_FiresOnCatchUp()
        {
            // Simulates the provider's Start() firing OnCameraUnavailable before
            // whoever composes the scene gets a chance to call Initialize() and
            // subscribe - Unity's Start() order between components isn't guaranteed.
            var poseProvider = new FakePoseProvider();
            poseProvider.PushCameraUnavailable();

            var go = new GameObject("GestureDetector");
            var detector = go.AddComponent<GestureDetector>();

            bool fired = false;
            detector.OnCameraUnavailable += () => fired = true;
            detector.Initialize(poseProvider);

            Assert.IsTrue(fired);

            Object.DestroyImmediate(go);
        }
    }
}
