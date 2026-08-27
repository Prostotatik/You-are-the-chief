using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class CalibrationDataTests
    {
        [Test]
        public void Identity_HasBodyScaleOne()
        {
            Assert.AreEqual(1f, CalibrationData.Identity.BodyScale);
            Assert.AreEqual(Vector2.zero, CalibrationData.Identity.ReferenceCenter);
        }

        [Test]
        public void Constructor_StoresValues()
        {
            var data = new CalibrationData(0.25f, new Vector2(0.5f, 0.6f));
            Assert.AreEqual(0.25f, data.BodyScale);
            Assert.AreEqual(new Vector2(0.5f, 0.6f), data.ReferenceCenter);
        }
    }
}
