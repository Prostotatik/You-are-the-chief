using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class LandmarkBufferTests
    {
        private static LandmarkFrame Frame(float timestamp)
        {
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 1f);
            return new LandmarkFrame(timestamp, joints);
        }

        [Test]
        public void Add_TrimsFramesOlderThanMaxAge()
        {
            var buffer = new LandmarkBuffer(maxAgeSeconds: 1f);
            buffer.Add(Frame(0f));
            buffer.Add(Frame(0.5f));
            buffer.Add(Frame(2f)); // cutoff = 2 - 1 = 1, so 0f and 0.5f must be trimmed

            var window = buffer.GetWindow(10f);
            Assert.AreEqual(1, window.Count);
            Assert.AreEqual(2f, window[0].Timestamp);
        }

        [Test]
        public void GetWindow_ReturnsOnlyFramesWithinRequestedSeconds()
        {
            var buffer = new LandmarkBuffer(maxAgeSeconds: 5f);
            buffer.Add(Frame(0f));
            buffer.Add(Frame(1f));
            buffer.Add(Frame(2f));
            buffer.Add(Frame(3f));

            var window = buffer.GetWindow(1.5f); // latest = 3, cutoff = 1.5
            Assert.AreEqual(2, window.Count);
            Assert.AreEqual(2f, window[0].Timestamp);
            Assert.AreEqual(3f, window[1].Timestamp);
        }

        [Test]
        public void GetWindow_OnEmptyBuffer_ReturnsEmpty()
        {
            var buffer = new LandmarkBuffer();
            Assert.AreEqual(0, buffer.GetWindow(2f).Count);
        }

        [Test]
        public void Clear_RemovesAllFrames()
        {
            var buffer = new LandmarkBuffer();
            buffer.Add(Frame(0f));
            buffer.Clear();
            Assert.AreEqual(0, buffer.GetWindow(10f).Count);
        }
    }
}
