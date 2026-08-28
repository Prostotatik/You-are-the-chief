# Gesture Detection Subsystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the webcam pose-based gesture detection subsystem that recognizes 5 full-body gestures (Pizza, Mac&Cheese, Rocket Soda, Wine, Spicy Spice) from a player's own webcam, exposing them as local events (`IGestureDetector`) that the future core gameplay loop, tutorial overlay, and multiplayer layer will consume.

**Architecture:** `WebCamTexture` → Unity Inference Engine (Sentis) pose model → per-frame `LandmarkFrame` → per-player `LandmarkBuffer` (2s rolling window) → 5 independent rule-based `IGestureMatcher` implementations → `IGestureDetector` events (`OnGestureRecognized`, `OnGestureProgress`, `OnCameraUnavailable`). The detection layer has zero knowledge of gameplay/scoring/networking.

**Tech Stack:** Unity 6000.5.8f1, C#, `com.unity.ai.inference` 2.6.1 (Unity Inference Engine, formerly Sentis — runtime assembly `Unity.InferenceEngine`, namespace `Unity.InferenceEngine`), `com.unity.test-framework` 1.7.0 (NUnit, EditMode tests), `com.unity.inputsystem` 1.20.0 (already installed, used only for the calibration/demo key triggers).

## Global Constraints

- All source files and code comments in English (per project owner's instruction). Chat/commit conversation may be in Russian; files are not.
- Coordinate convention: `PoseLandmark.Position` is normalized viewport space, `(0,0)` = top-left, `(1,1)` = bottom-right — i.e. **y grows downward** ("above" on screen = smaller y).
- Landmark indexing follows the standard MediaPipe BlazePose 33-point order (see `PoseJoint` enum in Task 1) — this is the layout the sourced pose model in Task 10 must be mapped to.
- This subsystem must not reference gameplay, scoring, or networking types. Its only public surface is `IGestureDetector`, `IPoseProvider`, `GestureType`, and the data types needed to implement those (`LandmarkFrame`, `PoseLandmark`, `PoseJoint`, `CalibrationData`).
- No physical webcam is available in this development environment. Every task except Task 10 (model/webcam integration) and Task 11 (demo scene) must be fully verifiable by EditMode unit tests with synthetic data. Task 10 is verified by code review + Console log inspection; Task 11 is verified by manual keyboard-driven interaction in the Editor.
- Run EditMode tests via the `unity-cli` skill's test action (Unity Test Framework, EditMode, filtered to the relevant test class) after every test-writing/implementation step pair. Do not invent a raw Editor executable command — this project's Unity install path is not assumed known.

---

## File Structure Overview

```
Assets/Scripts/GestureDetection/
    GestureDetection.asmdef
    GestureType.cs
    PoseJoint.cs
    PoseLandmark.cs
    LandmarkFrame.cs
    JointFilter.cs
    LandmarkBuffer.cs
    MatchResult.cs
    IGestureMatcher.cs
    CalibrationData.cs
    GestureMath.cs
    Matchers/
        PizzaMatcher.cs
        MacAndCheeseMatcher.cs
        RocketSodaMatcher.cs
        WineMatcher.cs
        SpicySpiceMatcher.cs
    IPoseProvider.cs
    IGestureDetector.cs
    GestureDetector.cs
    StubGestureDetector.cs
    CalibrationSequencer.cs
    CalibrationController.cs
    SentisPoseProvider.cs

Assets/Tests/EditMode/GestureDetection/
    GestureDetection.EditMode.Tests.asmdef
    LandmarkBufferTests.cs
    GestureMathTests.cs
    CalibrationDataTests.cs
    CalibrationSequencerTests.cs
    GestureDetectorTests.cs
    Matchers/
        PizzaMatcherTests.cs
        MacAndCheeseMatcherTests.cs
        RocketSodaMatcherTests.cs
        WineMatcherTests.cs
        SpicySpiceMatcherTests.cs
    TestFixtures/
        LandmarkSequenceBuilder.cs
        FakePoseProvider.cs

Assets/Scenes/
    GestureDetectionDemo.unity
```

---

### Task 1: Assembly setup, core landmark types, and LandmarkBuffer

**Files:**
- Create: `Assets/Scripts/GestureDetection/GestureDetection.asmdef`
- Create: `Assets/Tests/EditMode/GestureDetection/GestureDetection.EditMode.Tests.asmdef`
- Create: `Assets/Scripts/GestureDetection/PoseJoint.cs`
- Create: `Assets/Scripts/GestureDetection/PoseLandmark.cs`
- Create: `Assets/Scripts/GestureDetection/LandmarkFrame.cs`
- Create: `Assets/Scripts/GestureDetection/JointFilter.cs`
- Create: `Assets/Scripts/GestureDetection/LandmarkBuffer.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/LandmarkBufferTests.cs`

**Interfaces:**
- Produces: `PoseJoint` enum (33 values matching MediaPipe BlazePose order), `PoseLandmark` struct (`Vector2 Position`, `float Confidence`), `LandmarkFrame` struct (`float Timestamp`, `PoseLandmark[] Joints`, `PoseLandmark Get(PoseJoint)`), `JointFilter.TryGet(LandmarkFrame, PoseJoint, out Vector2, float minConfidence = 0.4f)`, `LandmarkBuffer` class (`void Add(LandmarkFrame)`, `IReadOnlyList<LandmarkFrame> GetWindow(float seconds)`, `void Clear()`).

- [ ] **Step 1: Create the runtime assembly definition**

Create `Assets/Scripts/GestureDetection/GestureDetection.asmdef`:

```json
{
    "name": "GestureDetection",
    "rootNamespace": "GestureDetection",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create the EditMode test assembly definition**

Create `Assets/Tests/EditMode/GestureDetection/GestureDetection.EditMode.Tests.asmdef`:

```json
{
    "name": "GestureDetection.EditMode.Tests",
    "rootNamespace": "GestureDetection.Tests",
    "references": [
        "GestureDetection",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Write PoseJoint, PoseLandmark, LandmarkFrame**

Create `Assets/Scripts/GestureDetection/PoseJoint.cs`:

```csharp
namespace GestureDetection
{
    // Matches the standard MediaPipe BlazePose 33-landmark output order.
    // The pose model sourced in Task 10 must map its output to this order.
    public enum PoseJoint
    {
        Nose = 0,
        LeftEyeInner = 1,
        LeftEye = 2,
        LeftEyeOuter = 3,
        RightEyeInner = 4,
        RightEye = 5,
        RightEyeOuter = 6,
        LeftEar = 7,
        RightEar = 8,
        MouthLeft = 9,
        MouthRight = 10,
        LeftShoulder = 11,
        RightShoulder = 12,
        LeftElbow = 13,
        RightElbow = 14,
        LeftWrist = 15,
        RightWrist = 16,
        LeftPinky = 17,
        RightPinky = 18,
        LeftIndex = 19,
        RightIndex = 20,
        LeftThumb = 21,
        RightThumb = 22,
        LeftHip = 23,
        RightHip = 24,
        LeftKnee = 25,
        RightKnee = 26,
        LeftAnkle = 27,
        RightAnkle = 28,
        LeftHeel = 29,
        RightHeel = 30,
        LeftFootIndex = 31,
        RightFootIndex = 32,
    }

    public static class PoseJointCount
    {
        public const int Value = 33;
    }
}
```

Create `Assets/Scripts/GestureDetection/PoseLandmark.cs`:

```csharp
using UnityEngine;

namespace GestureDetection
{
    // Position is normalized viewport space: (0,0) top-left, (1,1) bottom-right.
    // y grows downward — "above" on screen means a smaller y value.
    public readonly struct PoseLandmark
    {
        public readonly Vector2 Position;
        public readonly float Confidence;

        public PoseLandmark(Vector2 position, float confidence)
        {
            Position = position;
            Confidence = confidence;
        }
    }
}
```

Create `Assets/Scripts/GestureDetection/LandmarkFrame.cs`:

```csharp
namespace GestureDetection
{
    public readonly struct LandmarkFrame
    {
        public readonly float Timestamp;
        public readonly PoseLandmark[] Joints;

        public LandmarkFrame(float timestamp, PoseLandmark[] joints)
        {
            Timestamp = timestamp;
            Joints = joints;
        }

        public PoseLandmark Get(PoseJoint joint) => Joints[(int)joint];
    }
}
```

- [ ] **Step 4: Write JointFilter**

Create `Assets/Scripts/GestureDetection/JointFilter.cs`:

```csharp
using UnityEngine;

namespace GestureDetection
{
    public static class JointFilter
    {
        public const float DefaultMinConfidence = 0.4f;

        public static bool TryGet(LandmarkFrame frame, PoseJoint joint, out Vector2 position, float minConfidence = DefaultMinConfidence)
        {
            var landmark = frame.Get(joint);
            if (landmark.Confidence < minConfidence)
            {
                position = default;
                return false;
            }

            position = landmark.Position;
            return true;
        }
    }
}
```

- [ ] **Step 5: Write the failing LandmarkBuffer tests**

Create `Assets/Tests/EditMode/GestureDetection/LandmarkBufferTests.cs`:

```csharp
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
```

- [ ] **Step 6: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.LandmarkBufferTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `LandmarkBuffer` does not exist yet.

- [ ] **Step 7: Implement LandmarkBuffer**

Create `Assets/Scripts/GestureDetection/LandmarkBuffer.cs`:

```csharp
using System.Collections.Generic;

namespace GestureDetection
{
    public class LandmarkBuffer
    {
        private readonly List<LandmarkFrame> _frames = new List<LandmarkFrame>();
        private readonly float _maxAgeSeconds;

        public LandmarkBuffer(float maxAgeSeconds = 2.5f)
        {
            _maxAgeSeconds = maxAgeSeconds;
        }

        public void Add(LandmarkFrame frame)
        {
            _frames.Add(frame);
            float cutoff = frame.Timestamp - _maxAgeSeconds;
            while (_frames.Count > 0 && _frames[0].Timestamp < cutoff)
            {
                _frames.RemoveAt(0);
            }
        }

        public IReadOnlyList<LandmarkFrame> GetWindow(float seconds)
        {
            if (_frames.Count == 0) return System.Array.Empty<LandmarkFrame>();

            float latest = _frames[_frames.Count - 1].Timestamp;
            float cutoff = latest - seconds;
            var result = new List<LandmarkFrame>();
            foreach (var frame in _frames)
            {
                if (frame.Timestamp >= cutoff) result.Add(frame);
            }
            return result;
        }

        public void Clear() => _frames.Clear();
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.LandmarkBufferTests` via the `unity-cli` skill.
Expected: 4 tests PASS.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/GestureDetection Assets/Tests/EditMode/GestureDetection
git commit -m "feat(gesture-detection): add landmark data types and rolling buffer"
```

---

### Task 2: Gesture matcher shared infrastructure

**Files:**
- Create: `Assets/Scripts/GestureDetection/GestureType.cs`
- Create: `Assets/Scripts/GestureDetection/MatchResult.cs`
- Create: `Assets/Scripts/GestureDetection/CalibrationData.cs`
- Create: `Assets/Scripts/GestureDetection/GestureMath.cs`
- Create: `Assets/Scripts/GestureDetection/IGestureMatcher.cs`
- Create: `Assets/Tests/EditMode/GestureDetection/TestFixtures/LandmarkSequenceBuilder.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/GestureMathTests.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/CalibrationDataTests.cs`

**Interfaces:**
- Consumes: `PoseJoint`, `PoseLandmark`, `LandmarkFrame`, `PoseJointCount.Value` (Task 1).
- Produces: `GestureType` enum (`Pizza, MacAndCheese, RocketSoda, Wine, SpicySpice`), `MatchResult` struct (`bool IsMatch`, `float Progress`, static `MatchResult.None`), `CalibrationData` struct (`float BodyScale`, `Vector2 ReferenceCenter`, static `CalibrationData.Identity`), `GestureMath.CountReversals(IReadOnlyList<float>, float minAmplitude) -> int`, `GestureMath.AccumulatedRotation(IReadOnlyList<Vector2>) -> float` (degrees), `IGestureMatcher` interface (`GestureType GestureType { get; }`, `MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)`), test helper `LandmarkSequenceBuilder` (`AddFrame(float dt, Dictionary<PoseJoint, Vector2> positions, float confidence = 1f)`, `Build() -> List<LandmarkFrame>`).

- [ ] **Step 1: Write GestureType, MatchResult, CalibrationData, IGestureMatcher**

Create `Assets/Scripts/GestureDetection/GestureType.cs`:

```csharp
namespace GestureDetection
{
    public enum GestureType
    {
        Pizza,
        MacAndCheese,
        RocketSoda,
        Wine,
        SpicySpice,
    }
}
```

Create `Assets/Scripts/GestureDetection/MatchResult.cs`:

```csharp
using UnityEngine;

namespace GestureDetection
{
    public readonly struct MatchResult
    {
        public readonly bool IsMatch;
        public readonly float Progress;

        public MatchResult(bool isMatch, float progress)
        {
            IsMatch = isMatch;
            Progress = Mathf.Clamp01(progress);
        }

        public static readonly MatchResult None = new MatchResult(false, 0f);
    }
}
```

Create `Assets/Scripts/GestureDetection/CalibrationData.cs`:

```csharp
using UnityEngine;

namespace GestureDetection
{
    // BodyScale is a dimensionless ratio: the player's measured shoulder width divided
    // by ReferenceBodyScale. Matchers multiply their base thresholds (all tuned assuming
    // a "typical" player, i.e. BodyScale == 1) by this ratio, so a player who is closer
    // to/further from the camera - or simply bigger/smaller on screen - gets
    // proportionally scaled thresholds instead of the untuned raw shoulder-width value.
    public readonly struct CalibrationData
    {
        // Typical shoulder width in normalized viewport units at a comfortable webcam
        // distance. Raw measured shoulder widths are divided by this to produce
        // BodyScale, so BodyScale == 1 means "matches the assumption every matcher's
        // base threshold was tuned against."
        public const float ReferenceBodyScale = 0.2f;

        public readonly float BodyScale;
        public readonly Vector2 ReferenceCenter;

        public CalibrationData(float bodyScale, Vector2 referenceCenter)
        {
            BodyScale = bodyScale;
            ReferenceCenter = referenceCenter;
        }

        public static readonly CalibrationData Identity = new CalibrationData(1f, Vector2.zero);
    }
}
```

Create `Assets/Scripts/GestureDetection/IGestureMatcher.cs`:

```csharp
using System.Collections.Generic;

namespace GestureDetection
{
    public interface IGestureMatcher
    {
        GestureType GestureType { get; }
        MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration);
    }
}
```

- [ ] **Step 2: Write the failing GestureMath tests**

Create `Assets/Tests/EditMode/GestureDetection/GestureMathTests.cs`:

```csharp
using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class GestureMathTests
    {
        [Test]
        public void CountReversals_OscillatingSeries_CountsEachDirectionChange()
        {
            var values = new List<float> { 0.5f, 0.6f, 0.5f, 0.6f, 0.5f, 0.6f };
            int reversals = GestureMath.CountReversals(values, minAmplitude: 0.05f);
            Assert.AreEqual(4, reversals);
        }

        [Test]
        public void CountReversals_FlatSeries_ReturnsZero()
        {
            var values = new List<float> { 0.5f, 0.5f, 0.5f, 0.5f };
            int reversals = GestureMath.CountReversals(values, minAmplitude: 0.05f);
            Assert.AreEqual(0, reversals);
        }

        [Test]
        public void CountReversals_BelowAmplitudeThreshold_IsIgnored()
        {
            var values = new List<float> { 0.5f, 0.51f, 0.5f, 0.51f };
            int reversals = GestureMath.CountReversals(values, minAmplitude: 0.05f);
            Assert.AreEqual(0, reversals);
        }

        [Test]
        public void AccumulatedRotation_FullCircle_ReturnsAbout360()
        {
            var points = new List<Vector2>
            {
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(-1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(1f, 0f),
            };
            float rotation = GestureMath.AccumulatedRotation(points);
            Assert.AreEqual(360f, rotation, 1f);
        }

        [Test]
        public void AccumulatedRotation_SinglePoint_ReturnsZero()
        {
            var points = new List<Vector2> { new Vector2(1f, 0f) };
            Assert.AreEqual(0f, GestureMath.AccumulatedRotation(points));
        }
    }
}
```

Create `Assets/Tests/EditMode/GestureDetection/CalibrationDataTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.GestureMathTests` and `GestureDetection.Tests.CalibrationDataTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `GestureMath` does not exist yet.

- [ ] **Step 4: Implement GestureMath**

Create `Assets/Scripts/GestureDetection/GestureMath.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    public static class GestureMath
    {
        // Counts direction reversals in a series whose swing exceeds minAmplitude.
        // Used to detect repeated back-and-forth motion (shaking, stomping, rubbing).
        public static int CountReversals(IReadOnlyList<float> values, float minAmplitude)
        {
            if (values.Count < 2) return 0;

            int reversals = 0;
            int direction = 0;
            float lastExtreme = values[0];

            for (int i = 1; i < values.Count; i++)
            {
                float delta = values[i] - values[i - 1];
                if (Mathf.Abs(delta) < 1e-5f) continue;

                int newDirection = delta > 0f ? 1 : -1;
                if (direction != 0 && newDirection != direction)
                {
                    if (Mathf.Abs(values[i - 1] - lastExtreme) >= minAmplitude)
                    {
                        reversals++;
                        lastExtreme = values[i - 1];
                    }
                }

                direction = newDirection;
            }

            return reversals;
        }

        // Sums the signed angular delta between consecutive vectors (treated as
        // offsets from a pivot) and returns the absolute total in degrees.
        // Used to detect a hand tracing a circular path (e.g. twirling dough).
        public static float AccumulatedRotation(IReadOnlyList<Vector2> pivotRelativeVectors)
        {
            if (pivotRelativeVectors.Count < 2) return 0f;

            float total = 0f;
            for (int i = 1; i < pivotRelativeVectors.Count; i++)
            {
                float angleA = Mathf.Atan2(pivotRelativeVectors[i - 1].y, pivotRelativeVectors[i - 1].x) * Mathf.Rad2Deg;
                float angleB = Mathf.Atan2(pivotRelativeVectors[i].y, pivotRelativeVectors[i].x) * Mathf.Rad2Deg;
                total += Mathf.DeltaAngle(angleA, angleB);
            }

            return Mathf.Abs(total);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.GestureMathTests` and `GestureDetection.Tests.CalibrationDataTests` via the `unity-cli` skill.
Expected: 7 tests PASS.

- [ ] **Step 6: Add the LandmarkSequenceBuilder test helper**

Create `Assets/Tests/EditMode/GestureDetection/TestFixtures/LandmarkSequenceBuilder.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection.Tests
{
    // Builds synthetic LandmarkFrame sequences for matcher unit tests.
    // Joints not passed to AddFrame are left at zero confidence (i.e. filtered out
    // by JointFilter), matching how a real pose model reports low-confidence joints.
    public class LandmarkSequenceBuilder
    {
        private readonly List<LandmarkFrame> _frames = new List<LandmarkFrame>();
        private float _time;

        public LandmarkSequenceBuilder AddFrame(float dt, Dictionary<PoseJoint, Vector2> positions, float confidence = 1f)
        {
            _time += dt;
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);

            foreach (var pair in positions)
                joints[(int)pair.Key] = new PoseLandmark(pair.Value, confidence);

            _frames.Add(new LandmarkFrame(_time, joints));
            return this;
        }

        public List<LandmarkFrame> Build() => _frames;
    }
}
```

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/GestureDetection Assets/Tests/EditMode/GestureDetection
git commit -m "feat(gesture-detection): add gesture matcher shared infrastructure"
```

---

### Task 3: PizzaMatcher

**Files:**
- Create: `Assets/Scripts/GestureDetection/Matchers/PizzaMatcher.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/Matchers/PizzaMatcherTests.cs`

**Interfaces:**
- Consumes: `IGestureMatcher`, `MatchResult`, `CalibrationData`, `LandmarkFrame`, `JointFilter.TryGet`, `PoseJoint`, `GestureMath.AccumulatedRotation` (Tasks 1-2), `LandmarkSequenceBuilder` (Task 2).
- Produces: `PizzaMatcher : IGestureMatcher` (`GestureType.Pizza`, matches when either wrist traces ≥300° around its elbow while raised above it, within the given window).

- [ ] **Step 1: Write the failing PizzaMatcher tests**

Create `Assets/Tests/EditMode/GestureDetection/Matchers/PizzaMatcherTests.cs`:

```csharp
using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class PizzaMatcherTests
    {
        private static Dictionary<PoseJoint, Vector2> RightArm(Vector2 elbow, Vector2 wrist) =>
            new Dictionary<PoseJoint, Vector2> { { PoseJoint.RightElbow, elbow }, { PoseJoint.RightWrist, wrist } };

        [Test]
        public void Evaluate_WristTracesFullCircleRaisedAboveElbow_Matches()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            // Circle's center sits above the elbow (smaller y) but still encloses it,
            // so the loop winds a full 360 degrees around the elbow while keeping the
            // wrist's average height above the elbow's.
            var center = elbow + new Vector2(0f, -0.05f);
            const float radius = 0.2f;
            var builder = new LandmarkSequenceBuilder();
            // 8 steps of 45 degrees = one full monotonic 360-degree loop around the elbow.
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                var wrist = center + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                builder.AddFrame(0.1f, RightArm(elbow, wrist));
            }

            var matcher = new PizzaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
            Assert.AreEqual(1f, result.Progress, 0.01f);
        }

        [Test]
        public void Evaluate_WristTracesFullCircleBelowElbow_DoesNotMatch()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            // Same full 360-degree loop, but centered below the elbow (larger y) —
            // the rotation requirement is satisfied but the height requirement isn't.
            var center = elbow + new Vector2(0f, 0.05f);
            const float radius = 0.2f;
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                var wrist = center + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                builder.AddFrame(0.1f, RightArm(elbow, wrist));
            }

            var matcher = new PizzaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }

        [Test]
        public void Evaluate_WristStaysStill_DoesNotMatch()
        {
            var elbow = new Vector2(0.5f, 0.5f);
            var wrist = elbow + new Vector2(0f, -0.2f);
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
                builder.AddFrame(0.1f, RightArm(elbow, wrist));

            var matcher = new PizzaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.PizzaMatcherTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `PizzaMatcher` does not exist yet.

- [ ] **Step 3: Implement PizzaMatcher**

Create `Assets/Scripts/GestureDetection/Matchers/PizzaMatcher.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Pizza: rotate a hand as if twirling dough. Detected as a wrist tracing a
    // circular path around its elbow while raised above it on average.
    //
    // The "raised above the elbow" check is a window-average, not a per-frame
    // gate: a full loop around the elbow necessarily has the wrist below elbow
    // height for part of the loop, so gating individual frames by "wrist above
    // elbow" would drop exactly the frames needed to keep the angle sweep
    // continuous and make RequiredRotationDegrees unreachable.
    public class PizzaMatcher : IGestureMatcher
    {
        public const float RequiredRotationDegrees = 300f;

        public GestureType GestureType => GestureType.Pizza;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float rightRotation = EvaluateArm(window, PoseJoint.RightElbow, PoseJoint.RightWrist);
            float leftRotation = EvaluateArm(window, PoseJoint.LeftElbow, PoseJoint.LeftWrist);
            float rotation = Mathf.Max(rightRotation, leftRotation);

            float progress = Mathf.Clamp01(rotation / RequiredRotationDegrees);
            return new MatchResult(rotation >= RequiredRotationDegrees, progress);
        }

        private static float EvaluateArm(IReadOnlyList<LandmarkFrame> window, PoseJoint elbowJoint, PoseJoint wristJoint)
        {
            var relative = new List<Vector2>();
            float wristYSum = 0f;
            float elbowYSum = 0f;
            int sampleCount = 0;

            foreach (var frame in window)
            {
                bool hasElbow = JointFilter.TryGet(frame, elbowJoint, out var elbow);
                bool hasWrist = JointFilter.TryGet(frame, wristJoint, out var wrist);
                if (!hasElbow || !hasWrist) continue;

                relative.Add(wrist - elbow);
                wristYSum += wrist.y;
                elbowYSum += elbow.y;
                sampleCount++;
            }

            if (sampleCount == 0) return 0f;

            float averageWristY = wristYSum / sampleCount;
            float averageElbowY = elbowYSum / sampleCount;
            if (averageWristY >= averageElbowY) return 0f; // wrist must be raised above the elbow on average

            return GestureMath.AccumulatedRotation(relative);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.PizzaMatcherTests` via the `unity-cli` skill.
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/Matchers/PizzaMatcher.cs Assets/Tests/EditMode/GestureDetection/Matchers/PizzaMatcherTests.cs
git commit -m "feat(gesture-detection): add PizzaMatcher"
```

---

### Task 4: MacAndCheeseMatcher

**Files:**
- Create: `Assets/Scripts/GestureDetection/Matchers/MacAndCheeseMatcher.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/Matchers/MacAndCheeseMatcherTests.cs`

**Interfaces:**
- Consumes: same shared infra as Task 3, plus `GestureMath.CountReversals`.
- Produces: `MacAndCheeseMatcher : IGestureMatcher` (`GestureType.MacAndCheese`, matches when one ankle is raised above its knee and the opposite wrist stays close to that ankle while oscillating, ≥2 oscillations).

- [ ] **Step 1: Write the failing MacAndCheeseMatcher tests**

Create `Assets/Tests/EditMode/GestureDetection/Matchers/MacAndCheeseMatcherTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.MacAndCheeseMatcherTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `MacAndCheeseMatcher` does not exist yet.

- [ ] **Step 3: Implement MacAndCheeseMatcher**

Create `Assets/Scripts/GestureDetection/Matchers/MacAndCheeseMatcher.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Mac&Cheese: raise a heel and rub a fist against it (grating parmesan).
    // Detected as one ankle raised above its own knee, with the opposite wrist
    // staying close to that ankle and oscillating (the rubbing motion).
    public class MacAndCheeseMatcher : IGestureMatcher
    {
        public const float BaseProximityThreshold = 0.18f;
        public const int RequiredOscillations = 2;

        public GestureType GestureType => GestureType.MacAndCheese;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float left = EvaluateSide(window, PoseJoint.LeftAnkle, PoseJoint.LeftKnee, PoseJoint.RightWrist, calibration);
            float right = EvaluateSide(window, PoseJoint.RightAnkle, PoseJoint.RightKnee, PoseJoint.LeftWrist, calibration);
            float progress = Mathf.Max(left, right);
            return new MatchResult(progress >= 1f, progress);
        }

        private static float EvaluateSide(IReadOnlyList<LandmarkFrame> window, PoseJoint ankleJoint, PoseJoint kneeJoint, PoseJoint wristJoint, CalibrationData calibration)
        {
            float proximityThreshold = BaseProximityThreshold * Mathf.Max(calibration.BodyScale, 0.01f);
            var distances = new List<float>();

            foreach (var frame in window)
            {
                bool hasAnkle = JointFilter.TryGet(frame, ankleJoint, out var ankle);
                bool hasKnee = JointFilter.TryGet(frame, kneeJoint, out var knee);
                bool hasWrist = JointFilter.TryGet(frame, wristJoint, out var wrist);
                if (!hasAnkle || !hasKnee || !hasWrist) continue;
                if (ankle.y >= knee.y) continue; // leg must be raised: ankle above the knee

                distances.Add(Vector2.Distance(ankle, wrist));
            }

            if (distances.Count == 0) return 0f;

            foreach (var distance in distances)
            {
                if (distance > proximityThreshold) return 0f;
            }

            int reversals = GestureMath.CountReversals(distances, proximityThreshold * 0.2f);
            return Mathf.Clamp01((float)reversals / RequiredOscillations);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.MacAndCheeseMatcherTests` via the `unity-cli` skill.
Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/Matchers/MacAndCheeseMatcher.cs Assets/Tests/EditMode/GestureDetection/Matchers/MacAndCheeseMatcherTests.cs
git commit -m "feat(gesture-detection): add MacAndCheeseMatcher"
```

---

### Task 5: RocketSodaMatcher

**Files:**
- Create: `Assets/Scripts/GestureDetection/Matchers/RocketSodaMatcher.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/Matchers/RocketSodaMatcherTests.cs`

**Interfaces:**
- Consumes: same shared infra as Task 3.
- Produces: `RocketSodaMatcher : IGestureMatcher` (`GestureType.RocketSoda`, matches when both wrists are close together and below chest height, oscillating vertically ≥3 times).

- [ ] **Step 1: Write the failing RocketSodaMatcher tests**

Create `Assets/Tests/EditMode/GestureDetection/Matchers/RocketSodaMatcherTests.cs`:

```csharp
using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class RocketSodaMatcherTests
    {
        private static Dictionary<PoseJoint, Vector2> Frame(Vector2 leftWrist, Vector2 rightWrist) =>
            new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftWrist, leftWrist },
                { PoseJoint.RightWrist, rightWrist },
                { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
            };

        [Test]
        public void Evaluate_BothFistsShakingBelowChest_Matches()
        {
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
            {
                float y = i % 2 == 0 ? 0.55f : 0.65f;
                builder.AddFrame(0.1f, Frame(new Vector2(0.48f, y), new Vector2(0.52f, y)));
            }

            var matcher = new RocketSodaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsTrue(result.IsMatch);
        }

        [Test]
        public void Evaluate_HandsHeldStillAboveChest_DoesNotMatch()
        {
            var builder = new LandmarkSequenceBuilder();
            for (int i = 0; i < 6; i++)
                builder.AddFrame(0.1f, Frame(new Vector2(0.48f, 0.1f), new Vector2(0.52f, 0.1f)));

            var matcher = new RocketSodaMatcher();
            var result = matcher.Evaluate(builder.Build(), CalibrationData.Identity);

            Assert.IsFalse(result.IsMatch);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.RocketSodaMatcherTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `RocketSodaMatcher` does not exist yet.

- [ ] **Step 3: Implement RocketSodaMatcher**

Create `Assets/Scripts/GestureDetection/Matchers/RocketSodaMatcher.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Rocket Soda: shake two fists together low near the belly, like shaking a bottle.
    // Detected as both wrists close together and below chest height, oscillating
    // vertically together.
    public class RocketSodaMatcher : IGestureMatcher
    {
        public const float BaseProximityThreshold = 0.15f;
        public const float BaseChestOffset = 0.05f;
        public const int RequiredOscillations = 3;

        public GestureType GestureType => GestureType.RocketSoda;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float proximityThreshold = BaseProximityThreshold * Mathf.Max(calibration.BodyScale, 0.01f);
            float chestOffset = BaseChestOffset * Mathf.Max(calibration.BodyScale, 0.01f);
            var midpointY = new List<float>();

            foreach (var frame in window)
            {
                bool hasLeftWrist = JointFilter.TryGet(frame, PoseJoint.LeftWrist, out var leftWrist);
                bool hasRightWrist = JointFilter.TryGet(frame, PoseJoint.RightWrist, out var rightWrist);
                bool hasLeftShoulder = JointFilter.TryGet(frame, PoseJoint.LeftShoulder, out var leftShoulder);
                bool hasRightShoulder = JointFilter.TryGet(frame, PoseJoint.RightShoulder, out var rightShoulder);
                if (!hasLeftWrist || !hasRightWrist || !hasLeftShoulder || !hasRightShoulder) continue;

                float chestY = (leftShoulder.y + rightShoulder.y) * 0.5f;
                bool belowChest = leftWrist.y > chestY + chestOffset && rightWrist.y > chestY + chestOffset;
                bool closeTogether = Vector2.Distance(leftWrist, rightWrist) <= proximityThreshold;
                if (!belowChest || !closeTogether) continue;

                midpointY.Add((leftWrist.y + rightWrist.y) * 0.5f);
            }

            if (midpointY.Count == 0) return MatchResult.None;

            int reversals = GestureMath.CountReversals(midpointY, chestOffset);
            float progress = Mathf.Clamp01((float)reversals / RequiredOscillations);
            return new MatchResult(reversals >= RequiredOscillations, progress);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.RocketSodaMatcherTests` via the `unity-cli` skill.
Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/Matchers/RocketSodaMatcher.cs Assets/Tests/EditMode/GestureDetection/Matchers/RocketSodaMatcherTests.cs
git commit -m "feat(gesture-detection): add RocketSodaMatcher"
```

---

### Task 6: WineMatcher

**Files:**
- Create: `Assets/Scripts/GestureDetection/Matchers/WineMatcher.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/Matchers/WineMatcherTests.cs`

**Interfaces:**
- Consumes: same shared infra as Task 3.
- Produces: `WineMatcher : IGestureMatcher` (`GestureType.Wine`, matches when the ankles produce ≥2 combined vertical strikes — stepping in place).

- [ ] **Step 1: Write the failing WineMatcher tests**

Create `Assets/Tests/EditMode/GestureDetection/Matchers/WineMatcherTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.WineMatcherTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `WineMatcher` does not exist yet.

- [ ] **Step 3: Implement WineMatcher**

Create `Assets/Scripts/GestureDetection/Matchers/WineMatcher.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Wine: stomp feet repeatedly, as if stomping grapes.
    // Detected as combined vertical strikes (direction reversals) across both ankles.
    public class WineMatcher : IGestureMatcher
    {
        public const int RequiredStrikes = 2;
        public const float BaseMinStrikeAmplitude = 0.05f;

        public GestureType GestureType => GestureType.Wine;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float amplitude = BaseMinStrikeAmplitude * Mathf.Max(calibration.BodyScale, 0.01f);
            var leftY = new List<float>();
            var rightY = new List<float>();

            foreach (var frame in window)
            {
                if (JointFilter.TryGet(frame, PoseJoint.LeftAnkle, out var leftAnkle)) leftY.Add(leftAnkle.y);
                if (JointFilter.TryGet(frame, PoseJoint.RightAnkle, out var rightAnkle)) rightY.Add(rightAnkle.y);
            }

            int leftStrikes = GestureMath.CountReversals(leftY, amplitude);
            int rightStrikes = GestureMath.CountReversals(rightY, amplitude);
            int totalStrikes = leftStrikes + rightStrikes;

            float progress = Mathf.Clamp01((float)totalStrikes / RequiredStrikes);
            return new MatchResult(totalStrikes >= RequiredStrikes, progress);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.WineMatcherTests` via the `unity-cli` skill.
Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/Matchers/WineMatcher.cs Assets/Tests/EditMode/GestureDetection/Matchers/WineMatcherTests.cs
git commit -m "feat(gesture-detection): add WineMatcher"
```

---

### Task 7: SpicySpiceMatcher

**Files:**
- Create: `Assets/Scripts/GestureDetection/Matchers/SpicySpiceMatcher.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/Matchers/SpicySpiceMatcherTests.cs`

**Interfaces:**
- Consumes: same shared infra as Task 3.
- Produces: `SpicySpiceMatcher : IGestureMatcher` (`GestureType.SpicySpice`, matches when both wrists stay at face height and their distance to the nose oscillates ≥2 times — moving toward/away from the face).

- [ ] **Step 1: Write the failing SpicySpiceMatcher tests**

Create `Assets/Tests/EditMode/GestureDetection/Matchers/SpicySpiceMatcherTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.SpicySpiceMatcherTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `SpicySpiceMatcher` does not exist yet.

- [ ] **Step 3: Implement SpicySpiceMatcher**

Create `Assets/Scripts/GestureDetection/Matchers/SpicySpiceMatcher.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Spicy Spice: raise both fists to face height, move them toward/away from the face.
    // Detected as both wrists staying near the nose's height while their distance to
    // the nose oscillates.
    public class SpicySpiceMatcher : IGestureMatcher
    {
        public const float BaseFaceHeightTolerance = 0.12f;
        public const int RequiredOscillations = 2;

        public GestureType GestureType => GestureType.SpicySpice;

        public MatchResult Evaluate(IReadOnlyList<LandmarkFrame> window, CalibrationData calibration)
        {
            float faceTolerance = BaseFaceHeightTolerance * Mathf.Max(calibration.BodyScale, 0.01f);
            var wristToNoseDistance = new List<float>();

            foreach (var frame in window)
            {
                bool hasNose = JointFilter.TryGet(frame, PoseJoint.Nose, out var nose);
                bool hasLeftWrist = JointFilter.TryGet(frame, PoseJoint.LeftWrist, out var leftWrist);
                bool hasRightWrist = JointFilter.TryGet(frame, PoseJoint.RightWrist, out var rightWrist);
                if (!hasNose || !hasLeftWrist || !hasRightWrist) continue;

                bool leftAtFace = Mathf.Abs(leftWrist.y - nose.y) <= faceTolerance;
                bool rightAtFace = Mathf.Abs(rightWrist.y - nose.y) <= faceTolerance;
                if (!leftAtFace || !rightAtFace) continue;

                float avgDistance = (Vector2.Distance(leftWrist, nose) + Vector2.Distance(rightWrist, nose)) * 0.5f;
                wristToNoseDistance.Add(avgDistance);
            }

            if (wristToNoseDistance.Count == 0) return MatchResult.None;

            int reversals = GestureMath.CountReversals(wristToNoseDistance, faceTolerance * 0.3f);
            float progress = Mathf.Clamp01((float)reversals / RequiredOscillations);
            return new MatchResult(reversals >= RequiredOscillations, progress);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.SpicySpiceMatcherTests` via the `unity-cli` skill.
Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/Matchers/SpicySpiceMatcher.cs Assets/Tests/EditMode/GestureDetection/Matchers/SpicySpiceMatcherTests.cs
git commit -m "feat(gesture-detection): add SpicySpiceMatcher"
```

---

### Task 8: IGestureDetector, GestureDetector, StubGestureDetector

**Files:**
- Create: `Assets/Scripts/GestureDetection/IPoseProvider.cs`
- Create: `Assets/Scripts/GestureDetection/IGestureDetector.cs`
- Create: `Assets/Scripts/GestureDetection/GestureDetector.cs`
- Create: `Assets/Scripts/GestureDetection/StubGestureDetector.cs`
- Create: `Assets/Tests/EditMode/GestureDetection/TestFixtures/FakePoseProvider.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/GestureDetectorTests.cs`

**Interfaces:**
- Consumes: `IGestureMatcher`, `GestureType`, `MatchResult`, `CalibrationData`, `LandmarkBuffer`, `LandmarkFrame`, all 5 matchers (Tasks 1-7), `LandmarkSequenceBuilder` (Task 2).
- Produces: `IPoseProvider` (`event Action<LandmarkFrame> OnLandmarkFrame`, `event Action OnCameraUnavailable`), `IGestureDetector` (`event Action<GestureType> OnGestureRecognized`, `event Action<GestureType, float> OnGestureProgress`, `event Action OnCameraUnavailable`), `GestureDetector : MonoBehaviour, IGestureDetector` (`void Initialize(IPoseProvider)`, `void SetCalibration(CalibrationData)`, `void ResetLock()`), `StubGestureDetector : MonoBehaviour, IGestureDetector` (`void SimulateGesture(GestureType)`, `void SimulateProgress(GestureType, float)`, `void SimulateCameraUnavailable()`) — this is the interface implementation the future core gameplay loop can use to develop and test against before Task 10's real webcam provider exists.

- [ ] **Step 1: Write IPoseProvider, IGestureDetector, and the FakePoseProvider test double**

Create `Assets/Scripts/GestureDetection/IPoseProvider.cs`:

```csharp
using System;

namespace GestureDetection
{
    public interface IPoseProvider
    {
        event Action<LandmarkFrame> OnLandmarkFrame;
        event Action OnCameraUnavailable;
    }
}
```

Create `Assets/Scripts/GestureDetection/IGestureDetector.cs`:

```csharp
using System;

namespace GestureDetection
{
    public interface IGestureDetector
    {
        event Action<GestureType> OnGestureRecognized;
        event Action<GestureType, float> OnGestureProgress;
        event Action OnCameraUnavailable;

        // Re-arms the detector after a match so it can recognize the next gesture.
        // Without calling this, a detector locks onto its first recognized gesture for
        // the rest of the session - callers must call this once they're done reacting
        // to an OnGestureRecognized event (e.g. after assigning it to an order) so the
        // player can perform another gesture afterward.
        void ResetLock();
    }
}
```

Create `Assets/Tests/EditMode/GestureDetection/TestFixtures/FakePoseProvider.cs`:

```csharp
using System;

namespace GestureDetection.Tests
{
    public class FakePoseProvider : IPoseProvider
    {
        public event Action<LandmarkFrame> OnLandmarkFrame;
        public event Action OnCameraUnavailable;

        public void PushFrame(LandmarkFrame frame) => OnLandmarkFrame?.Invoke(frame);
        public void PushCameraUnavailable() => OnCameraUnavailable?.Invoke();
    }
}
```

- [ ] **Step 2: Write the failing GestureDetector tests**

Create `Assets/Tests/EditMode/GestureDetection/GestureDetectorTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.GestureDetectorTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `GestureDetector` does not exist yet.

- [ ] **Step 4: Implement GestureDetector and StubGestureDetector**

Create `Assets/Scripts/GestureDetection/GestureDetector.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    public class GestureDetector : MonoBehaviour, IGestureDetector
    {
        [SerializeField] private float windowSeconds = 1.5f;

        public event Action<GestureType> OnGestureRecognized;
        public event Action<GestureType, float> OnGestureProgress;
        public event Action OnCameraUnavailable;

        private IPoseProvider _poseProvider;
        private readonly LandmarkBuffer _buffer = new LandmarkBuffer();
        private readonly List<IGestureMatcher> _matchers = new List<IGestureMatcher>
        {
            new PizzaMatcher(),
            new MacAndCheeseMatcher(),
            new RocketSodaMatcher(),
            new WineMatcher(),
            new SpicySpiceMatcher(),
        };

        private CalibrationData _calibration = CalibrationData.Identity;
        private GestureType? _lockedGesture;

        public void Initialize(IPoseProvider poseProvider)
        {
            if (_poseProvider != null)
            {
                _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
                _poseProvider.OnCameraUnavailable -= HandleCameraUnavailable;
            }

            _poseProvider = poseProvider;
            _poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
            _poseProvider.OnCameraUnavailable += HandleCameraUnavailable;
        }

        public void SetCalibration(CalibrationData calibration)
        {
            _calibration = calibration;
        }

        public void ResetLock()
        {
            _lockedGesture = null;
            // Without this, frames already in the buffer from the just-recognized gesture
            // are still inside the next evaluation window and immediately re-match,
            // firing OnGestureRecognized again on the very next incoming frame.
            _buffer.Clear();
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _buffer.Add(frame);
            if (_lockedGesture.HasValue) return;

            var window = _buffer.GetWindow(windowSeconds);
            foreach (var matcher in _matchers)
            {
                var result = matcher.Evaluate(window, _calibration);
                OnGestureProgress?.Invoke(matcher.GestureType, result.Progress);
                if (result.IsMatch)
                {
                    _lockedGesture = matcher.GestureType;
                    OnGestureRecognized?.Invoke(matcher.GestureType);
                    break;
                }
            }
        }

        private void HandleCameraUnavailable()
        {
            OnCameraUnavailable?.Invoke();
        }

        private void OnDestroy()
        {
            if (_poseProvider == null) return;
            _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
            _poseProvider.OnCameraUnavailable -= HandleCameraUnavailable;
        }
    }
}
```

Create `Assets/Scripts/GestureDetection/StubGestureDetector.cs`:

```csharp
using System;
using UnityEngine;

namespace GestureDetection
{
    // Manually-driven IGestureDetector for developing and testing the gameplay
    // layer before the real webcam-based detector (Task 10) is wired in.
    public class StubGestureDetector : MonoBehaviour, IGestureDetector
    {
        public event Action<GestureType> OnGestureRecognized;
        public event Action<GestureType, float> OnGestureProgress;
        public event Action OnCameraUnavailable;

        public void SimulateGesture(GestureType gesture) => OnGestureRecognized?.Invoke(gesture);

        public void SimulateProgress(GestureType gesture, float progress) =>
            OnGestureProgress?.Invoke(gesture, Mathf.Clamp01(progress));

        public void SimulateCameraUnavailable() => OnCameraUnavailable?.Invoke();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.GestureDetectorTests` via the `unity-cli` skill.
Expected: 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/GestureDetection/IPoseProvider.cs Assets/Scripts/GestureDetection/IGestureDetector.cs Assets/Scripts/GestureDetection/GestureDetector.cs Assets/Scripts/GestureDetection/StubGestureDetector.cs Assets/Tests/EditMode/GestureDetection/GestureDetectorTests.cs Assets/Tests/EditMode/GestureDetection/TestFixtures/FakePoseProvider.cs
git commit -m "feat(gesture-detection): add GestureDetector wiring and stub for downstream development"
```

---

### Task 9: Calibration

**Files:**
- Create: `Assets/Scripts/GestureDetection/CalibrationSequencer.cs`
- Create: `Assets/Scripts/GestureDetection/CalibrationController.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/CalibrationSequencerTests.cs`

**Interfaces:**
- Consumes: `CalibrationData`, `LandmarkFrame`, `JointFilter.TryGet`, `PoseJoint`, `IPoseProvider`, `LandmarkBuffer` (Tasks 1-2, 8), `LandmarkSequenceBuilder` (Task 2).
- Produces: `CalibrationSequencer.Compute(IReadOnlyList<LandmarkFrame>) -> CalibrationData` (pure function, averages shoulder-width and hip-midpoint over the given samples), `CalibrationController : MonoBehaviour` (`event Action<CalibrationData> OnCalibrationComplete`, `void BeginCalibration(IPoseProvider)`, `const float DurationSeconds = 3f`).

- [ ] **Step 1: Write the failing CalibrationSequencer tests**

Create `Assets/Tests/EditMode/GestureDetection/CalibrationSequencerTests.cs`:

```csharp
using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class CalibrationSequencerTests
    {
        [Test]
        public void Compute_AveragesShoulderWidthAndHipMidpoint()
        {
            var builder = new LandmarkSequenceBuilder();
            builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
                { PoseJoint.LeftHip, new Vector2(0.45f, 0.6f) },
                { PoseJoint.RightHip, new Vector2(0.55f, 0.6f) },
            });
            builder.AddFrame(0.1f, new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
                { PoseJoint.LeftHip, new Vector2(0.45f, 0.6f) },
                { PoseJoint.RightHip, new Vector2(0.55f, 0.6f) },
            });

            var result = CalibrationSequencer.Compute(builder.Build());

            Assert.AreEqual(0.2f, result.BodyScale, 0.001f); // shoulder width
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.CalibrationSequencerTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `CalibrationSequencer` does not exist yet.

- [ ] **Step 3: Implement CalibrationSequencer and CalibrationController**

Create `Assets/Scripts/GestureDetection/CalibrationSequencer.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GestureDetection
{
    // Pure function: turns a set of sampled landmark frames (typically a ~3s T-pose
    // window) into a CalibrationData baseline. Kept separate from CalibrationController
    // so it is testable without a MonoBehaviour/coroutine.
    public static class CalibrationSequencer
    {
        public static CalibrationData Compute(IReadOnlyList<LandmarkFrame> frames)
        {
            float scaleSum = 0f;
            Vector2 centerSum = Vector2.zero;
            int sampleCount = 0;

            foreach (var frame in frames)
            {
                // Each TryGet is called unconditionally (not short-circuited via &&) so the
                // compiler can prove leftShoulder/rightShoulder/leftHip/rightHip are always
                // definitely assigned below - JointFilter.TryGet assigns its out parameter
                // on both the true and false paths.
                bool hasLeftShoulder = JointFilter.TryGet(frame, PoseJoint.LeftShoulder, out var leftShoulder);
                bool hasRightShoulder = JointFilter.TryGet(frame, PoseJoint.RightShoulder, out var rightShoulder);
                bool hasLeftHip = JointFilter.TryGet(frame, PoseJoint.LeftHip, out var leftHip);
                bool hasRightHip = JointFilter.TryGet(frame, PoseJoint.RightHip, out var rightHip);
                if (!hasLeftShoulder || !hasRightShoulder || !hasLeftHip || !hasRightHip) continue;

                scaleSum += Vector2.Distance(leftShoulder, rightShoulder);
                centerSum += (leftHip + rightHip) * 0.5f;
                sampleCount++;
            }

            if (sampleCount == 0) return CalibrationData.Identity;

            float averageShoulderWidth = scaleSum / sampleCount;
            float bodyScale = averageShoulderWidth / CalibrationData.ReferenceBodyScale;
            return new CalibrationData(bodyScale, centerSum / sampleCount);
        }
    }
}
```

Create `Assets/Scripts/GestureDetection/CalibrationController.cs`:

```csharp
using System;
using System.Collections;
using UnityEngine;

namespace GestureDetection
{
    // Runs a short T-pose calibration window against a live IPoseProvider,
    // then reports the resulting CalibrationData.
    public class CalibrationController : MonoBehaviour
    {
        public const float DurationSeconds = 3f;

        public event Action<CalibrationData> OnCalibrationComplete;

        private IPoseProvider _poseProvider;
        private readonly LandmarkBuffer _samples = new LandmarkBuffer(maxAgeSeconds: DurationSeconds + 1f);

        public void BeginCalibration(IPoseProvider poseProvider)
        {
            _poseProvider = poseProvider;
            _samples.Clear();
            _poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
            StartCoroutine(FinishAfterDuration());
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _samples.Add(frame);
        }

        private IEnumerator FinishAfterDuration()
        {
            yield return new WaitForSeconds(DurationSeconds);

            _poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
            var result = CalibrationSequencer.Compute(_samples.GetWindow(DurationSeconds));
            OnCalibrationComplete?.Invoke(result);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.CalibrationSequencerTests` via the `unity-cli` skill.
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/CalibrationSequencer.cs Assets/Scripts/GestureDetection/CalibrationController.cs Assets/Tests/EditMode/GestureDetection/CalibrationSequencerTests.cs
git commit -m "feat(gesture-detection): add T-pose calibration"
```

---

### Task 10: Pose model acquisition and SentisPoseProvider

**Files:**
- Modify: `Assets/Scripts/GestureDetection/GestureDetection.asmdef` (add Inference Engine reference)
- Create: `Assets/Models/pose_landmark_full.onnx` (or `.sentis`) — sourced, not authored
- Create: `Assets/Scripts/GestureDetection/SentisPoseProvider.cs`

**Interfaces:**
- Consumes: `IPoseProvider` (Task 8), `LandmarkFrame`, `PoseLandmark`, `PoseJoint`, `PoseJointCount.Value` (Task 1).
- Produces: `SentisPoseProvider : MonoBehaviour, IPoseProvider` — the concrete webcam + ML implementation. Nothing downstream depends on its internals, only on `IPoseProvider`.

This task cannot be covered by automated tests (no webcam or model asset exists in this environment). Verification is: (a) code compiles and matches the `IPoseProvider` contract, (b) manual Console log inspection once run on a machine with a webcam, done together with Task 11's demo scene.

- [ ] **Step 1: Source a BlazePose pose-landmark model as a Sentis-compatible asset**

Unity's official Inference Engine samples repository has historically shipped a ready-to-use BlazePose pose-estimation sample with a pre-converted model asset. Clone it and copy the model asset into this project:

```bash
git clone --depth 1 https://github.com/Unity-Technologies/sentis-samples "$TMPDIR/sentis-samples"
find "$TMPDIR/sentis-samples" -iname "*blazepose*" -o -iname "*pose*landmark*"
```

Copy the located model file (`.onnx` or `.sentis`) into `Assets/Models/` in this project (create the folder if needed), keeping its original filename.

**If that repository or sample no longer exists or has no usable model:** use a web search for a permissively-licensed (Apache-2.0/MIT) ONNX export of MediaPipe's `pose_landmark_full` model, download it into `Assets/Models/pose_landmark_full.onnx`, and note the source URL in a one-line comment at the top of `SentisPoseProvider.cs`.

- [ ] **Step 2: Import the model and inspect its real input/output shape**

In the Unity Editor, select the imported model asset and open it (double-click), or use the Inference Engine model visualizer, to confirm:
- Input tensor shape (expected default assumption below: `(1, 3, 256, 256)`, NCHW, RGB).
- Output tensor shape and layout (expected default assumption below: a single flat float output where each of the 33 landmarks occupies a fixed stride containing at least x, y, and a visibility/confidence value).

If the actual shapes differ from the assumptions in Step 4 below, update the constants (`InputSize`, `OutputStride`, `VisibilityOffset`) in `SentisPoseProvider.cs` accordingly before proceeding — do not leave mismatched constants in place, this will silently produce garbage landmark positions.

- [ ] **Step 3: Add the Inference Engine assembly reference**

Modify `Assets/Scripts/GestureDetection/GestureDetection.asmdef`, changing `"references": []` to:

```json
"references": [
    "Unity.InferenceEngine"
],
```

- [ ] **Step 4: Implement SentisPoseProvider**

Create `Assets/Scripts/GestureDetection/SentisPoseProvider.cs`:

```csharp
using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace GestureDetection
{
    // Captures the local webcam and runs a BlazePose-family model through the
    // Inference Engine to produce a LandmarkFrame every tick.
    //
    // Assumed model contract (verify against the actual imported asset — see
    // Task 10 Step 2 of the implementation plan — and adjust the constants below
    // if they don't match):
    //   Input:  (1, 3, InputSize, InputSize) RGB, normalized [0,1].
    //   Output: flat float buffer, one PoseJointCount.Value block of OutputStride
    //           floats each: [x, y, ..., visibility at VisibilityOffset].
    public class SentisPoseProvider : MonoBehaviour, IPoseProvider
    {
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private int webcamRequestWidth = 640;
        [SerializeField] private int webcamRequestHeight = 480;

        private const int InputSize = 256;
        private const int OutputStride = 5;
        private const int VisibilityOffset = 3;

        public event Action<LandmarkFrame> OnLandmarkFrame;
        public event Action OnCameraUnavailable;

        private WebCamTexture _webcamTexture;
        private Worker _worker;
        private Tensor<float> _inputTensor;

        private void Start()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                OnCameraUnavailable?.Invoke();
                enabled = false;
                return;
            }

            _webcamTexture = new WebCamTexture(webcamRequestWidth, webcamRequestHeight);
            _webcamTexture.Play();

            var model = ModelLoader.Load(modelAsset);
            _worker = new Worker(model, BackendType.GPUCompute);
            _inputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize));
        }

        private void Update()
        {
            if (_webcamTexture == null || !_webcamTexture.didUpdateThisFrame) return;

            TextureConverter.ToTensor(_webcamTexture, _inputTensor, new TextureTransform().SetDimensions(InputSize, InputSize, 3));
            _worker.Schedule(_inputTensor);
            // Do NOT Dispose() this tensor: PeekOutput returns a reference into the worker's
            // own pooled storage, not a copy - disposing it here frees memory the worker still
            // considers in-use and corrupts state on the next Schedule() call.
            var output = _worker.PeekOutput() as Tensor<float>;
            if (output == null) return;

            var downloaded = output.DownloadToArray();
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                int baseIndex = i * OutputStride;
                float x = downloaded[baseIndex];
                float y = downloaded[baseIndex + 1];
                // UNVERIFIED: assumes this graph's visibility output is already a [0,1]
                // probability. MediaPipe-family models sometimes output a raw pre-sigmoid
                // logit here instead, which Clamp01 would silently binarize. Confirm against
                // real webcam output (values outside [0,1] before clamping would prove it's a
                // logit) once a camera is available, and apply Sigmoid here if so.
                float visibility = downloaded[baseIndex + VisibilityOffset];
                joints[i] = new PoseLandmark(new Vector2(x, y), Mathf.Clamp01(visibility));
            }

            OnLandmarkFrame?.Invoke(new LandmarkFrame(Time.time, joints));
        }

        private void OnDestroy()
        {
            _webcamTexture?.Stop();
            _worker?.Dispose();
            _inputTensor?.Dispose();
        }
    }
}
```

- [ ] **Step 5: Verify it compiles and runs without a webcam attached**

Run the EditMode test suite for the whole `GestureDetection.EditMode.Tests` assembly via the `unity-cli` skill to confirm the new file didn't break compilation.
Expected: all previously-passing tests (Tasks 1-9) still PASS; `SentisPoseProvider` itself has no automated test.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/GestureDetection/GestureDetection.asmdef Assets/Scripts/GestureDetection/SentisPoseProvider.cs Assets/Models
git commit -m "feat(gesture-detection): add webcam pose provider via Inference Engine"
```

---

### Task 11: Manual demo scene

**Files:**
- Create: `Assets/Scenes/GestureDetectionDemo.unity`
- Create: `Assets/Scripts/GestureDetection/GestureDetectionDemoController.cs`

**Interfaces:**
- Consumes: `StubGestureDetector`, `IGestureDetector` (Task 8).
- Produces: a scene an engineer can open and press Play on to see gesture events firing, without needing a webcam — the closing manual-verification deliverable for this sub-project.

- [ ] **Step 1: Write the demo controller**

Create `Assets/Scripts/GestureDetection/GestureDetectionDemoController.cs`:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace GestureDetection
{
    // Press 1-5 in Play mode to simulate each gesture and confirm the
    // IGestureDetector event wiring works end-to-end without a webcam.
    //
    // Uses the new Input System (UnityEngine.InputSystem.Keyboard), not the
    // legacy UnityEngine.Input class: this project's Active Input Handling
    // (Project Settings > Player) is set to "Input System Package (New)" only,
    // under which UnityEngine.Input.GetKeyDown throws InvalidOperationException.
    public class GestureDetectionDemoController : MonoBehaviour
    {
        [SerializeField] private StubGestureDetector stubDetector;

        private void OnEnable()
        {
            stubDetector.OnGestureRecognized += gesture => Debug.Log($"[GestureDetectionDemo] Recognized: {gesture}");
            stubDetector.OnCameraUnavailable += () => Debug.Log("[GestureDetectionDemo] Camera unavailable");
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) stubDetector.SimulateGesture(GestureType.Pizza);
            if (keyboard.digit2Key.wasPressedThisFrame) stubDetector.SimulateGesture(GestureType.MacAndCheese);
            if (keyboard.digit3Key.wasPressedThisFrame) stubDetector.SimulateGesture(GestureType.RocketSoda);
            if (keyboard.digit4Key.wasPressedThisFrame) stubDetector.SimulateGesture(GestureType.Wine);
            if (keyboard.digit5Key.wasPressedThisFrame) stubDetector.SimulateGesture(GestureType.SpicySpice);
            if (keyboard.cKey.wasPressedThisFrame) stubDetector.SimulateCameraUnavailable();
        }
    }
}
```

- [ ] **Step 2: Create the demo scene and wire it up via Unity MCP**

Use the Unity MCP tools (already connected in this environment) rather than hand-editing a `.unity` YAML file:

1. `mcp__unity-mcp__Unity_ManageScene` with `Action: "Create"`, `Name: "GestureDetectionDemo"`, `Path: "Assets/Scenes"`.
2. `mcp__unity-mcp__Unity_ManageGameObject` to create an empty GameObject named `DemoRoot`.
3. Add component `StubGestureDetector` to `DemoRoot`.
4. Add component `GestureDetectionDemoController` to `DemoRoot`, and set its `stubDetector` field to reference the `StubGestureDetector` component on the same object.
5. `mcp__unity-mcp__Unity_ManageScene` with `Action: "Save"`.

- [ ] **Step 3: Manually verify in the Editor**

Open `Assets/Scenes/GestureDetectionDemo.unity`, enter Play mode, press keys `1`-`5` and `C`, and confirm each press logs the matching line in the Console (use `mcp__unity-mcp__Unity_ReadConsole` or `Unity_GetConsoleLogs` to check programmatically, or read the Console window directly).
Expected: 6 distinct log lines, one per key, each matching the gesture/camera-unavailable label.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/GestureDetectionDemo.unity Assets/Scripts/GestureDetection/GestureDetectionDemoController.cs
git commit -m "feat(gesture-detection): add manual demo scene for keyboard-simulated gestures"
```

---

## Plan Self-Review Notes

- **Spec coverage:** Pose Provider (Task 10), Landmark Buffer (Task 1), all 5 Gesture Matchers (Tasks 3-7), Calibration (Task 9), Public API / IGestureDetector boundary (Task 8), error handling — camera unavailable (Tasks 8, 10) and low-confidence filtering (Task 1's `JointFilter`, used throughout), testing strategy via synthetic fixtures (all tasks) plus a manual/keyboard path (Task 11) since no physical webcam exists in this environment. All spec sections are covered.
- **Type consistency:** `IGestureMatcher.Evaluate` signature (`IReadOnlyList<LandmarkFrame> window, CalibrationData calibration`) is identical across Task 2's interface definition and all 5 matcher implementations (Tasks 3-7). `IPoseProvider`/`IGestureDetector` event signatures defined in Task 8 are reused as-is by `SentisPoseProvider` (Task 10) and `StubGestureDetector`/demo (Tasks 8, 11).
- **No git repository currently exists in this project** (`e:/UnityProjects/You are the chief` is not yet a git repo). Before Task 1's first commit step, initialize one (`git init` and an initial commit of the existing project scaffold), or ask the user to confirm how they want version control handled — the commit steps in this plan assume a working repo.

## Post-Implementation: Final Whole-Branch Review Fixes

After all 11 tasks landed, a final whole-branch review (and one re-review of its fix round) found issues no single task's review could see, since every task's tests exercised each piece in isolation. Summarized here rather than reproduced in full — see the commit messages for complete detail:

- **`CalibrationData.BodyScale` was a raw shoulder-width measurement, not a ratio** — every matcher's threshold was tuned/tested against `Identity` (`BodyScale = 1`), but real calibration produced ~0.2, which would have shrunk 4 matchers' thresholds ~5x. Fixed by dividing the raw measurement by a new `CalibrationData.ReferenceBodyScale` (0.2) constant, so `Identity` now correctly represents "typical player" (commit `3f65937`).
- **`GestureDetector.ResetLock()` wasn't on `IGestureDetector`** (the interface's only public surface) and didn't clear the landmark buffer, so a consumer holding just the interface couldn't re-arm the detector, and calling the concrete method would immediately re-fire on stale buffered frames. Fixed by adding it to the interface and clearing the buffer (commit `3f65937`).
- **`GestureMath.CountReversals`** gated a reversal's amplitude check on the swing *leading into* a turn rather than the turn itself, so a single noisy sample during a monotonic move could count as a full reversal. Rewritten as hysteresis ("zigzag") peak detection, verified to produce identical counts on every pre-existing fixture (commit `d4cacb8`).
- **`MacAndCheeseMatcher`** zeroed its whole window on one out-of-range frame instead of skipping it; **`WineMatcher`** had no positional gating and let one bouncing foot satisfy it alone — both fixed (commit `d4cacb8`).
- **`OnCameraUnavailable`** could fire before a consumer subscribed (Unity `Start()` ordering isn't guaranteed) and had no mid-session disconnect detection. Added `IPoseProvider.IsCameraUnavailable` as a catch-up latch plus a watchdog in `SentisPoseProvider` (commit `d4cacb8`; the watchdog's warm-up false-positive was itself found and fixed in a re-review, commit `53e6f8a`).
- **`OnGestureProgress`** fired once per matcher per frame (5x/frame) and froze at a stale value once locked; narrowed to the single best-scoring matcher plus a floor, with an explicit retraction to `0f` when the leader changes or falls below floor (commit `d4cacb8`, retraction added in `53e6f8a`).
- Added the spec-mandated cross-gesture non-triggering test matrix (`CrossGestureMatrixTests.cs`, commit `d593e66`) and `GestureDetectionBootstrap` + a `GestureDetectionRealPipelineDemo` scene wiring `SentisPoseProvider`/`GestureDetector`/`CalibrationController` together for the first time (commit `ff3ccbe`) — a re-review then caught that `GestureDetectionBootstrap` subscribed to the detector's events *after* calling `Initialize()`, reintroducing the same dropped-catch-up-event bug in a different file (fixed in `53e6f8a`).
- **Known, deliberately deferred:** the cross-gesture matrix's fixtures are "safe by construction" — every off-diagonal cell is decided by a missing joint before any matcher's real gating logic runs, so it doesn't yet prove discrimination against a plausible full-body pose (e.g. it wouldn't catch Wine firing during Mac&Cheese's raised-heel phase). Strengthening it needs a shared base pose in the fixture builder; left as a follow-up rather than expanding scope further.
