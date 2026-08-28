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
    }
}
