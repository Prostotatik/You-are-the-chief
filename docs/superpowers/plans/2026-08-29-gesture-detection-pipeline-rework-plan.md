# Gesture Detection Pipeline Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the jittery/face-clustered webcam pose detection by adding the missing person-detector crop stage, per-joint temporal smoothing (OneEuroFilter), a confidence-scale fix, and a skeleton (joint+bone) debug overlay — without touching gesture matchers, `GestureDetector`, or the calibration flow, none of which are the source of the problem.

**Architecture:** `SentisPoseProvider` grows a first inference pass (`pose_detection.onnx`, 224x224 NHWC input, ArgMax-selected single best detection across 2254 pre-baked anchors, no NMS needed) that finds a bounding region around the player. A pure-math step (`PoseCrop`) turns that region into an axis-aligned crop (center, size) of the source webcam texture; the crop is rendered into an intermediate `RenderTexture` via `Graphics.Blit`'s UV scale/offset overload and fed to the existing landmarker model (`pose_landmarks_detector_full.onnx`, unchanged, 256x256 NHWC) exactly as before, just on a cropped region instead of the full squashed frame. Raw landmarker output is passed through a per-joint `PoseLandmarkSmoother` (backed by a pure `OneEuroFilter`) before being broadcast as a `LandmarkFrame`. The previous frame's landmark bounding box is reused as next frame's crop region (skipping the detector) as long as tracking confidence stays high, falling back to the detector when it drops or no region exists yet — mirroring MediaPipe's own re-detect-on-loss behavior.

**Tech Stack:** Unity 6000.5.8f1, C#, `com.unity.ai.inference` 2.6.1 (Unity Inference Engine, namespace `Unity.InferenceEngine`), `com.unity.test-framework` 1.7.0 (NUnit, EditMode tests).

## Global Constraints

- All source files and code comments in English. Chat/commit conversation may be in Russian.
- Coordinate convention: `PoseLandmark.Position` is normalized viewport space, `(0,0)` = top-left, `(1,1)` = bottom-right — y grows downward.
- This subsystem must not reference gameplay, scoring, or networking types.
- No physical webcam is available in this development environment. Every task except Task 5 (Sentis/webcam integration) and Task 6 (debug overlay visual result) must be fully verifiable by EditMode unit tests with synthetic data. Task 5 is verified by code review + Console log inspection; Task 6 is verified by manual keyboard/webcam-driven interaction in the Editor once hardware is available.
- Run EditMode tests via the `unity-cli` skill's test action (Unity Test Framework, EditMode, filtered to the relevant test class) after every test-writing/implementation step pair.
- Do not modify `GestureDetector.cs`, any file under `Matchers/`, `CalibrationController.cs`, `CalibrationSequencer.cs`, `LandmarkBuffer.cs`, `GestureMath.cs`, or their tests — out of scope per design spec `docs/superpowers/specs/2026-08-29-gesture-detection-pipeline-rework-design.md`.

---

## File Structure Overview

```
Assets/Scripts/GestureDetection/
    OneEuroFilter.cs                  (new)
    PoseCrop.cs                       (new)
    PoseLandmarkSmoother.cs           (new)
    SentisPoseProvider.cs             (modified)
    GestureDetectionDebugOverlay.cs   (modified)

Assets/Models/
    pose_detection.onnx               (new, sourced)
    pose_detection_anchors.csv        (new, sourced, as TextAsset)

Assets/Tests/EditMode/GestureDetection/
    OneEuroFilterTests.cs             (new)
    PoseCropTests.cs                  (new)
    PoseLandmarkSmootherTests.cs      (new)
```

---

### Task 1: OneEuroFilter

**Files:**
- Create: `Assets/Scripts/GestureDetection/OneEuroFilter.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/OneEuroFilterTests.cs`

**Interfaces:**
- Consumes: nothing (pure C#, no project dependencies).
- Produces: `OneEuroFilter` class — `OneEuroFilter(float minCutoff = 1f, float beta = 0f, float derivateCutoff = 1f)` constructor, `float Filter(float value, float timestamp)` method (first call seeds and returns `value` unchanged), `void Reset()` (drops all internal state so the next `Filter` call reseeds instead of blending against a stale value).

- [ ] **Step 1: Write the failing OneEuroFilter tests**

Create `Assets/Tests/EditMode/GestureDetection/OneEuroFilterTests.cs`:

```csharp
using GestureDetection;
using NUnit.Framework;

namespace GestureDetection.Tests
{
    public class OneEuroFilterTests
    {
        [Test]
        public void Filter_FirstCall_ReturnsInputUnchanged()
        {
            var filter = new OneEuroFilter();
            float result = filter.Filter(0.5f, timestamp: 0f);
            Assert.AreEqual(0.5f, result);
        }

        [Test]
        public void Filter_NoisySignalAroundConstant_OutputVariesLessThanInput()
        {
            var filter = new OneEuroFilter(minCutoff: 1f, beta: 0f, derivateCutoff: 1f);
            float[] noisy = { 0.50f, 0.54f, 0.47f, 0.53f, 0.48f, 0.52f, 0.49f, 0.55f, 0.46f, 0.51f };

            float t = 0f;
            float first = filter.Filter(noisy[0], t);
            float minOut = first, maxOut = first;

            for (int i = 1; i < noisy.Length; i++)
            {
                t += 1f / 30f; // simulate 30fps
                float output = filter.Filter(noisy[i], t);
                minOut = UnityEngine.Mathf.Min(minOut, output);
                maxOut = UnityEngine.Mathf.Max(maxOut, output);
            }

            float inputSpread = 0.55f - 0.46f;
            float outputSpread = maxOut - minOut;
            Assert.Less(outputSpread, inputSpread, "Filtered output should vary less than the noisy input.");
        }

        [Test]
        public void Filter_SteppedSignal_EventuallyTracksNewValue()
        {
            var filter = new OneEuroFilter(minCutoff: 1f, beta: 0f, derivateCutoff: 1f);
            float t = 0f;
            filter.Filter(0f, t);

            float lastOutput = 0f;
            for (int i = 0; i < 60; i++)
            {
                t += 1f / 30f;
                lastOutput = filter.Filter(1f, t);
            }

            Assert.Greater(lastOutput, 0.9f, "After 2 seconds of a held new value, the filter should have converged close to it.");
        }

        [Test]
        public void Reset_ThenFilter_ReturnsNewValueUnchangedLikeFirstCall()
        {
            var filter = new OneEuroFilter();
            filter.Filter(0.2f, 0f);
            filter.Filter(0.2f, 1f / 30f);

            filter.Reset();
            float result = filter.Filter(0.9f, 2f);

            Assert.AreEqual(0.9f, result);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.OneEuroFilterTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `OneEuroFilter` does not exist yet.

- [ ] **Step 3: Implement OneEuroFilter**

Create `Assets/Scripts/GestureDetection/OneEuroFilter.cs`:

```csharp
using UnityEngine;

namespace GestureDetection
{
    // Standard One-Euro filter (Casiez, Roussel, Vogel 2012): a low-pass filter whose
    // cutoff frequency adapts to signal speed, so it smooths noise at rest but tracks
    // fast motion with low lag. Operates on a single scalar over time; the caller keeps
    // one instance per tracked value (e.g. one per landmark axis).
    public class OneEuroFilter
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _derivateCutoff;

        private bool _hasPrevious;
        private float _previousValue;
        private float _previousDerivative;
        private float _previousTimestamp;

        public OneEuroFilter(float minCutoff = 1f, float beta = 0f, float derivateCutoff = 1f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _derivateCutoff = derivateCutoff;
        }

        public float Filter(float value, float timestamp)
        {
            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _previousValue = value;
                _previousDerivative = 0f;
                _previousTimestamp = timestamp;
                return value;
            }

            float dt = Mathf.Max(timestamp - _previousTimestamp, 1e-6f);
            float rate = 1f / dt;

            float dValue = (value - _previousValue) * rate;
            float derivativeAlpha = SmoothingFactor(rate, _derivateCutoff);
            float derivative = Lerp(_previousDerivative, dValue, derivativeAlpha);

            float cutoff = _minCutoff + _beta * Mathf.Abs(derivative);
            float valueAlpha = SmoothingFactor(rate, cutoff);
            float filtered = Lerp(_previousValue, value, valueAlpha);

            _previousValue = filtered;
            _previousDerivative = derivative;
            _previousTimestamp = timestamp;

            return filtered;
        }

        public void Reset()
        {
            _hasPrevious = false;
        }

        private static float SmoothingFactor(float rate, float cutoff)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            float te = 1f / rate;
            return 1f / (1f + tau / te);
        }

        private static float Lerp(float previous, float current, float alpha) =>
            alpha * current + (1f - alpha) * previous;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.OneEuroFilterTests` via the `unity-cli` skill.
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/OneEuroFilter.cs Assets/Tests/EditMode/GestureDetection/OneEuroFilterTests.cs
git commit -m "feat(gesture-detection): add OneEuroFilter for landmark smoothing"
```

---

### Task 2: PoseLandmarkSmoother

**Files:**
- Create: `Assets/Scripts/GestureDetection/PoseLandmarkSmoother.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/PoseLandmarkSmootherTests.cs`

**Interfaces:**
- Consumes: `OneEuroFilter` (Task 1), `LandmarkFrame`, `PoseLandmark`, `PoseJoint`, `PoseJointCount.Value` (existing).
- Produces: `PoseLandmarkSmoother` class — `LandmarkFrame Smooth(LandmarkFrame raw, float minConfidenceToFilter = 0.4f)` (returns a new `LandmarkFrame` with the same timestamp, each joint's position smoothed through that joint's own x/y `OneEuroFilter` pair; a joint below `minConfidenceToFilter` is passed through unfiltered and its filter pair is `Reset()` so the next time it reappears it reseeds instead of blending against a stale pre-gap value), `void Reset()` (resets every joint's filter pair, e.g. on camera reconnect).

- [ ] **Step 1: Write the failing PoseLandmarkSmoother tests**

Create `Assets/Tests/EditMode/GestureDetection/PoseLandmarkSmootherTests.cs`:

```csharp
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class PoseLandmarkSmootherTests
    {
        private static LandmarkFrame FrameWithWrist(float timestamp, Vector2 wristPosition, float confidence)
        {
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);
            joints[(int)PoseJoint.RightWrist] = new PoseLandmark(wristPosition, confidence);
            return new LandmarkFrame(timestamp, joints);
        }

        [Test]
        public void Smooth_FirstFrame_ReturnsPositionUnchanged()
        {
            var smoother = new PoseLandmarkSmoother();
            var raw = FrameWithWrist(0f, new Vector2(0.5f, 0.5f), confidence: 1f);

            var smoothed = smoother.Smooth(raw);

            Assert.AreEqual(new Vector2(0.5f, 0.5f), smoothed.Get(PoseJoint.RightWrist).Position);
        }

        [Test]
        public void Smooth_NoisySuccessiveFrames_ReducesJitterVersusRaw()
        {
            var smoother = new PoseLandmarkSmoother();
            float[] noisyX = { 0.50f, 0.54f, 0.47f, 0.53f, 0.48f, 0.52f, 0.49f, 0.55f, 0.46f, 0.51f };

            float t = 0f;
            Vector2 first = smoother.Smooth(FrameWithWrist(t, new Vector2(noisyX[0], 0.5f), 1f)).Get(PoseJoint.RightWrist).Position;
            float minX = first.x, maxX = first.x;

            for (int i = 1; i < noisyX.Length; i++)
            {
                t += 1f / 30f;
                var smoothed = smoother.Smooth(FrameWithWrist(t, new Vector2(noisyX[i], 0.5f), 1f));
                float x = smoothed.Get(PoseJoint.RightWrist).Position.x;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }

            Assert.Less(maxX - minX, 0.55f - 0.46f);
        }

        [Test]
        public void Smooth_ConfidenceBelowThreshold_PassesThroughUnfiltered()
        {
            var smoother = new PoseLandmarkSmoother();
            smoother.Smooth(FrameWithWrist(0f, new Vector2(0.5f, 0.5f), confidence: 1f));

            var lowConfidence = FrameWithWrist(1f / 30f, new Vector2(0.9f, 0.9f), confidence: 0.1f);
            var smoothed = smoother.Smooth(lowConfidence, minConfidenceToFilter: 0.4f);

            Assert.AreEqual(new Vector2(0.9f, 0.9f), smoothed.Get(PoseJoint.RightWrist).Position);
        }

        [Test]
        public void Smooth_JointReappearsAfterGap_ReseedsInsteadOfBlendingTowardStaleValue()
        {
            var smoother = new PoseLandmarkSmoother();
            smoother.Smooth(FrameWithWrist(0f, new Vector2(0.1f, 0.1f), confidence: 1f));

            // Joint drops below confidence for a while (passes through unfiltered per the
            // previous test), then reappears far away - it must jump straight there, not
            // ease in from the stale 0.1,0.1 filter state.
            smoother.Smooth(FrameWithWrist(1f / 30f, new Vector2(0.9f, 0.9f), confidence: 0.1f));
            var reappeared = smoother.Smooth(FrameWithWrist(2f / 30f, new Vector2(0.9f, 0.9f), confidence: 1f));

            Assert.AreEqual(new Vector2(0.9f, 0.9f), reappeared.Get(PoseJoint.RightWrist).Position);
        }

        [Test]
        public void Reset_ThenSmooth_ReturnsUnchangedLikeFirstFrame()
        {
            var smoother = new PoseLandmarkSmoother();
            smoother.Smooth(FrameWithWrist(0f, new Vector2(0.2f, 0.2f), confidence: 1f));
            smoother.Smooth(FrameWithWrist(1f / 30f, new Vector2(0.2f, 0.2f), confidence: 1f));

            smoother.Reset();
            var smoothed = smoother.Smooth(FrameWithWrist(2f, new Vector2(0.7f, 0.7f), confidence: 1f));

            Assert.AreEqual(new Vector2(0.7f, 0.7f), smoothed.Get(PoseJoint.RightWrist).Position);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.PoseLandmarkSmootherTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `PoseLandmarkSmoother` does not exist yet.

- [ ] **Step 3: Implement PoseLandmarkSmoother**

Create `Assets/Scripts/GestureDetection/PoseLandmarkSmoother.cs`:

```csharp
namespace GestureDetection
{
    // Applies a OneEuroFilter pair (x, y) per joint to a raw LandmarkFrame stream,
    // smoothing frame-to-frame jitter without materially lagging real motion. A joint
    // whose confidence drops below the threshold is passed through unfiltered and its
    // filter pair is reset, so a real occlusion gap never blends into a stale position
    // when the joint reappears - it reseeds at the new position instead.
    public class PoseLandmarkSmoother
    {
        private readonly OneEuroFilter[] _xFilters = new OneEuroFilter[PoseJointCount.Value];
        private readonly OneEuroFilter[] _yFilters = new OneEuroFilter[PoseJointCount.Value];

        public PoseLandmarkSmoother()
        {
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                _xFilters[i] = new OneEuroFilter();
                _yFilters[i] = new OneEuroFilter();
            }
        }

        public LandmarkFrame Smooth(LandmarkFrame raw, float minConfidenceToFilter = 0.4f)
        {
            var smoothedJoints = new PoseLandmark[PoseJointCount.Value];

            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                var joint = raw.Joints[i];

                if (joint.Confidence < minConfidenceToFilter)
                {
                    _xFilters[i].Reset();
                    _yFilters[i].Reset();
                    smoothedJoints[i] = joint;
                    continue;
                }

                float x = _xFilters[i].Filter(joint.Position.x, raw.Timestamp);
                float y = _yFilters[i].Filter(joint.Position.y, raw.Timestamp);
                smoothedJoints[i] = new PoseLandmark(new UnityEngine.Vector2(x, y), joint.Confidence);
            }

            return new LandmarkFrame(raw.Timestamp, smoothedJoints);
        }

        public void Reset()
        {
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                _xFilters[i].Reset();
                _yFilters[i].Reset();
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.PoseLandmarkSmootherTests` via the `unity-cli` skill.
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/PoseLandmarkSmoother.cs Assets/Tests/EditMode/GestureDetection/PoseLandmarkSmootherTests.cs
git commit -m "feat(gesture-detection): add per-joint landmark smoothing"
```

---

### Task 3: PoseCrop (detector-box-to-affine-crop math)

**Files:**
- Create: `Assets/Scripts/GestureDetection/PoseCrop.cs`
- Test: `Assets/Tests/EditMode/GestureDetection/PoseCropTests.cs`

**Interfaces:**
- Consumes: nothing beyond `UnityEngine.Vector2` (pure math, no Sentis/texture dependency so it's unit-testable without a live webcam).
- Produces: `PoseCropRegion` readonly struct (`Vector2 Center`, `float Size` — both normalized [0,1] source-texture space, `Size` as a fraction of `max(sourceWidth, sourceHeight)`; **axis-aligned only, no rotation** — see note below), `PoseCrop` static class:
  - `PoseCropRegion FromDetection(Vector2 keypoint1, Vector2 keypoint2, Vector2 boxCenter, float scale = 1.25f) -> PoseCropRegion` — replicates Unity's own `BlazeUtils`-style detector-to-crop sizing: `Center = boxCenter`, `Size = scale * Vector2.Distance(keypoint1, keypoint2)`. Unity's own sample also derives a rotation angle from these two keypoints to handle a person lying sideways or upside-down; this project's game has players seated/standing at a desk facing the camera, so that rotation is deliberately dropped to keep the crop step to a plain axis-aligned UV scale/offset (`Graphics.Blit`'s standard 4-argument overload, no custom shader/material needed) — see Task 5's `RunLandmarker` for where this pays off.
  - `PoseCropRegion FromLandmarkBounds(LandmarkFrame frame, float minConfidence = 0.4f, float paddingFraction = 0.35f) -> PoseCropRegion` — the cheap "reuse last frame's landmarks as next crop" path: axis-aligned bounding box of all joints above `minConfidence`, expanded by `paddingFraction` of its own size. Returns `PoseCropRegion` with `Size == 0` (`default`) if fewer than 2 joints are above `minConfidence` (nothing usable to bound).
  - `(Vector2 scale, Vector2 offset) ToUvTransform(PoseCropRegion region)` — converts a region into the `(scale, offset)` pair `Graphics.Blit(Texture, RenderTexture, Vector2 scale, Vector2 offset)` expects, where sampling `uv * scale + offset` over the source texture's own [0,1] UV space lands exactly on the region: `scale = (region.Size, region.Size)`, `offset = region.Center - region.Size * 0.5`.

- [ ] **Step 1: Write the failing PoseCrop tests**

Create `Assets/Tests/EditMode/GestureDetection/PoseCropTests.cs`:

```csharp
using System.Collections.Generic;
using GestureDetection;
using NUnit.Framework;
using UnityEngine;

namespace GestureDetection.Tests
{
    public class PoseCropTests
    {
        [Test]
        public void FromDetection_ComputesCenterAndSizeFromKeypoints()
        {
            var keypoint1 = new Vector2(0.4f, 0.5f);
            var keypoint2 = new Vector2(0.6f, 0.5f); // horizontal pair, distance 0.2

            var region = PoseCrop.FromDetection(keypoint1, keypoint2, boxCenter: new Vector2(0.5f, 0.5f), scale: 1.25f);

            Assert.AreEqual(0.5f, region.Center.x, 0.001f);
            Assert.AreEqual(0.5f, region.Center.y, 0.001f);
            Assert.AreEqual(0.25f, region.Size, 0.001f); // 1.25 * 0.2
        }

        private static LandmarkFrame FrameFromJoints(Dictionary<PoseJoint, Vector2> positions, float confidence = 1f)
        {
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new PoseLandmark(Vector2.zero, 0f);
            foreach (var pair in positions)
                joints[(int)pair.Key] = new PoseLandmark(pair.Value, confidence);
            return new LandmarkFrame(0f, joints);
        }

        [Test]
        public void FromLandmarkBounds_TightCluster_ProducesPaddedSquareAroundIt()
        {
            var frame = FrameFromJoints(new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.LeftShoulder, new Vector2(0.4f, 0.3f) },
                { PoseJoint.RightShoulder, new Vector2(0.6f, 0.3f) },
                { PoseJoint.LeftHip, new Vector2(0.4f, 0.6f) },
                { PoseJoint.RightHip, new Vector2(0.6f, 0.6f) },
            });

            var region = PoseCrop.FromLandmarkBounds(frame, minConfidence: 0.4f, paddingFraction: 0.35f);

            // Bounding box is x:[0.4,0.6] y:[0.3,0.6] -> center (0.5, 0.45), largest span 0.3.
            Assert.AreEqual(0.5f, region.Center.x, 0.001f);
            Assert.AreEqual(0.45f, region.Center.y, 0.001f);
            Assert.Greater(region.Size, 0.3f); // padded, so bigger than the raw span
        }

        [Test]
        public void FromLandmarkBounds_FewerThanTwoConfidentJoints_ReturnsZeroSizeRegion()
        {
            var frame = FrameFromJoints(new Dictionary<PoseJoint, Vector2>
            {
                { PoseJoint.Nose, new Vector2(0.5f, 0.2f) },
            });

            var region = PoseCrop.FromLandmarkBounds(frame);

            Assert.AreEqual(0f, region.Size);
        }

        [Test]
        public void ToUvTransform_CenteredRegion_OffsetsByHalfSizeFromCenter()
        {
            var region = new PoseCropRegion(new Vector2(0.5f, 0.5f), 0.3f);
            var (scale, offset) = PoseCrop.ToUvTransform(region);

            Assert.AreEqual(0.3f, scale.x, 0.001f);
            Assert.AreEqual(0.3f, scale.y, 0.001f);
            Assert.AreEqual(0.35f, offset.x, 0.001f); // 0.5 - 0.3*0.5
            Assert.AreEqual(0.35f, offset.y, 0.001f);
        }

        [Test]
        public void ToUvTransform_OffCenterRegion_SamplingCornerAndOppositeCornerBoundTheRegion()
        {
            var region = new PoseCropRegion(new Vector2(0.6f, 0.4f), 0.2f);
            var (scale, offset) = PoseCrop.ToUvTransform(region);

            // uv=(0,0) must land on the region's min corner, uv=(1,1) on its max corner.
            Vector2 minCorner = Vector2.zero * scale + offset;
            Vector2 maxCorner = Vector2.one * scale + offset;
            Assert.AreEqual(0.5f, minCorner.x, 0.001f); // 0.6 - 0.1
            Assert.AreEqual(0.3f, minCorner.y, 0.001f); // 0.4 - 0.1
            Assert.AreEqual(0.7f, maxCorner.x, 0.001f); // 0.6 + 0.1
            Assert.AreEqual(0.5f, maxCorner.y, 0.001f); // 0.4 + 0.1
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode tests for `GestureDetection.Tests.PoseCropTests` via the `unity-cli` skill.
Expected: compile error / FAIL — `PoseCrop` and `PoseCropRegion` do not exist yet.

- [ ] **Step 3: Implement PoseCropRegion and PoseCrop**

Create `Assets/Scripts/GestureDetection/PoseCrop.cs`:

```csharp
using UnityEngine;

namespace GestureDetection
{
    // An axis-aligned square region of the source webcam texture to crop and feed to
    // the 256x256 landmarker model. Center and Size are normalized to the source
    // texture's own [0,1] UV space (Size as a fraction of max(sourceWidth, sourceHeight)).
    //
    // Unlike Unity's own BlazePose sample, this deliberately carries no rotation: that
    // sample rotates its crop to handle a person lying sideways or upside-down, which
    // this project's game never needs (players are seated/standing at a desk, upright,
    // facing the camera). Staying axis-aligned keeps the crop step a plain UV scale/
    // offset blit - see PoseCrop.ToUvTransform and SentisPoseProvider.RunLandmarker.
    public readonly struct PoseCropRegion
    {
        public readonly Vector2 Center;
        public readonly float Size;

        public PoseCropRegion(Vector2 center, float size)
        {
            Center = center;
            Size = size;
        }
    }

    // Turns either a fresh detector result or the previous frame's landmark spread into
    // a PoseCropRegion for the next landmarker inference. Mirrors the two data sources
    // Unity's own BlazePose sample uses: a full detector pass (FromDetection) when no
    // recent region is trusted, and a cheap reuse of last frame's landmark bounds
    // (FromLandmarkBounds) otherwise - see SentisPoseProvider for which path is chosen
    // each frame.
    public static class PoseCrop
    {
        public static PoseCropRegion FromDetection(Vector2 keypoint1, Vector2 keypoint2, Vector2 boxCenter, float scale = 1.25f)
        {
            float size = scale * Vector2.Distance(keypoint1, keypoint2);
            return new PoseCropRegion(boxCenter, size);
        }

        public static PoseCropRegion FromLandmarkBounds(LandmarkFrame frame, float minConfidence = 0.4f, float paddingFraction = 0.35f)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            int count = 0;

            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                if (!JointFilter.TryGet(frame, (PoseJoint)i, out var position, minConfidence)) continue;
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                count++;
            }

            if (count < 2) return default;

            Vector2 center = (min + max) * 0.5f;
            float span = Mathf.Max(max.x - min.x, max.y - min.y);
            float size = span * (1f + paddingFraction);
            return new PoseCropRegion(center, size);
        }

        // Converts a region into the (scale, offset) pair Graphics.Blit(Texture,
        // RenderTexture, Vector2 scale, Vector2 offset) expects: it samples the source
        // texture at uv * scale + offset, so this maps blit-space uv=(0,0)..(1,1) onto
        // the region's own min..max corners in source-texture UV space.
        public static (Vector2 scale, Vector2 offset) ToUvTransform(PoseCropRegion region)
        {
            Vector2 scale = new Vector2(region.Size, region.Size);
            Vector2 offset = region.Center - scale * 0.5f;
            return (scale, offset);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the EditMode tests for `GestureDetection.Tests.PoseCropTests` via the `unity-cli` skill.
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/GestureDetection/PoseCrop.cs Assets/Tests/EditMode/GestureDetection/PoseCropTests.cs
git commit -m "feat(gesture-detection): add detector-to-crop-region math"
```

---

### Task 4: Source the detector model and anchors data

**Files:**
- Create (binary/data, sourced not authored): `Assets/Models/pose_detection.onnx`
- Create (data, sourced not authored): `Assets/Models/pose_detection_anchors.csv`

**Interfaces:**
- Consumes: nothing.
- Produces: the two asset files Task 5 wires into `SentisPoseProvider`. `pose_detection_anchors.csv` is 2254 rows of `x_center,y_center,w,h` normalized-[0,1] floats (w/h are constant `1.0` per row in the source data and unused for scaling — only x_center/y_center matter for decoding).

- [ ] **Step 1: Download the detector model and anchors file**

Download from the `Unity-Technologies/sentis-samples` repository (same repo and license terms already documented in `SentisPoseProvider.cs` for the existing landmarker model — "Unity's own Inference Engine (Sentis) sample repository... Unity Terms of Service, 'Experimental / Evaluation'"):

- `BlazeDetectionSample/Pose/Assets/Models/pose_detection.onnx` → save as `Assets/Models/pose_detection.onnx`
- `BlazeDetectionSample/Pose/Assets/Data/anchors.csv` → save as `Assets/Models/pose_detection_anchors.csv`

```bash
curl -L -o "Assets/Models/pose_detection.onnx" "https://raw.githubusercontent.com/Unity-Technologies/sentis-samples/main/BlazeDetectionSample/Pose/Assets/Models/pose_detection.onnx"
curl -L -o "Assets/Models/pose_detection_anchors.csv" "https://raw.githubusercontent.com/Unity-Technologies/sentis-samples/main/BlazeDetectionSample/Pose/Assets/Data/anchors.csv"
```

- [ ] **Step 2: Verify the anchors file has 2254 rows**

```bash
wc -l "Assets/Models/pose_detection_anchors.csv"
```
Expected: `2254` (or `2255` if the source file has a trailing newline/header — if there's a header row, note it for Task 5's CSV parsing step).

- [ ] **Step 3: In the Unity Editor, let both files import, then set the CSV's import type**

Open the Unity Editor (or trigger an asset database refresh via the `unity-cli` skill) so `pose_detection.onnx` imports as a `ModelAsset` and `pose_detection_anchors.csv` imports as a `TextAsset` (Unity's default for `.csv`, no import-settings change needed).

- [ ] **Step 4: Commit**

```bash
git add Assets/Models/pose_detection.onnx Assets/Models/pose_detection.onnx.meta Assets/Models/pose_detection_anchors.csv Assets/Models/pose_detection_anchors.csv.meta
git commit -m "chore(gesture-detection): source pose_detection.onnx and its anchors data"
```

---

### Task 5: Wire the two-stage pipeline, smoothing, and confidence fix into SentisPoseProvider

**Files:**
- Modify: `Assets/Scripts/GestureDetection/SentisPoseProvider.cs`

**Interfaces:**
- Consumes: `PoseCrop`, `PoseCropRegion`, `PoseLandmarkSmoother` (Tasks 1-3), `Assets/Models/pose_detection.onnx`, `Assets/Models/pose_detection_anchors.csv` (Task 4), existing `LandmarkFrame`, `PoseLandmark`, `PoseJoint`, `PoseJointCount.Value`, `IPoseProvider`.
- Produces: same public surface as before (`IPoseProvider` — `OnLandmarkFrame`, `OnCameraUnavailable`, `IsCameraUnavailable`, plus the existing `Texture` debug property) — this task changes internals only, no interface change, so `GestureDetectionBootstrap` and `GestureDetectionDebugOverlay` need no changes for wiring (Task 6 changes the overlay for a different reason: drawing bones).

This task has no unit tests of its own — per the Global Constraints, Sentis/webcam integration is verified by code review + Console log inspection, consistent with how the original single-stage version of this file was verified. All the math it depends on (`PoseCrop`, `PoseLandmarkSmoother`) is already unit-tested in Tasks 1-3.

- [ ] **Step 1: Replace SentisPoseProvider with the two-stage pipeline**

Replace the full contents of `Assets/Scripts/GestureDetection/SentisPoseProvider.cs`:

```csharp
// Pose models sourced from https://github.com/Unity-Technologies/sentis-samples
// (BlazeDetectionSample/Pose/Assets/Models/), Unity's own Inference Engine (Sentis)
// sample repository. NOTE: distributed under Unity's Sentis sample license (Unity
// Terms of Service, "Experimental / Evaluation" — see the repo's License.md), not
// Apache-2.0/MIT. Confirm this is acceptable for the target project's licensing needs
// before shipping.
using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace GestureDetection
{
    // Captures the local webcam and runs it through a two-stage BlazePose-family
    // pipeline: a lightweight detector finds the player and a rough crop region, then
    // the landmarker model runs on just that cropped region to produce a LandmarkFrame
    // every tick. This two-stage shape (as opposed to feeding the whole frame to the
    // landmarker) matches Unity's own sample architecture and is what fixes the
    // landmarker receiving a squashed, mostly-background image - see
    // docs/superpowers/specs/2026-08-29-gesture-detection-pipeline-rework-design.md.
    // Unlike Unity's own sample, the crop here is axis-aligned (no rotation) - see
    // PoseCrop's doc comment for why.
    //
    // Detector model contract (pose_detection.onnx, verified against Unity's own
    // BlazeDetectionSample/Pose/Assets/Scripts/PoseDetection.cs):
    //   Input:  224x224x3 NHWC, RGB, normalized [0,1].
    //   Output: "boxes" (1, 2254, 12) and "scores" (1, 2254, 1) - 2254 pre-baked
    //           anchors (see pose_detection_anchors.csv, x_center/y_center per row,
    //           normalized [0,1]; w/h columns are constant and unused). Per anchor,
    //           the 12 box channels are [dx, dy, dw, dh, kp1x, kp1y, kp2x, kp2y, ...]
    //           - only the first 8 are used here (box offset + 2 alignment keypoints).
    //           No NMS: the single highest-scoring anchor (after sigmoid) is used
    //           directly, matching Unity's own sample (ArgMax across all anchors).
    //
    // Landmarker model contract (pose_landmarks_detector_full.onnx, unchanged from the
    // original single-stage version of this file):
    //   Input:  tensor named "input_1", shape (1, 256, 256, 3) NHWC, RGB, [0,1].
    //   Output: tensor named "Identity", flat float buffer (1, 195) = 39 landmark
    //           blocks of 5 floats [x, y, z, visibility, presence] in pixel units of
    //           the 256x256 input. Only the first 33 blocks (PoseJointCount.Value)
    //           are used; x/y are divided by 256 to land in the crop's own normalized
    //           [0,1] space, then mapped back into full-source-texture normalized
    //           space via the same PoseCropRegion used to build the crop.
    public class SentisPoseProvider : MonoBehaviour, IPoseProvider
    {
        [SerializeField] private ModelAsset detectorModelAsset;
        [SerializeField] private ModelAsset landmarkerModelAsset;
        [SerializeField] private TextAsset detectorAnchorsCsv;
        [SerializeField] private int webcamRequestWidth = 640;
        [SerializeField] private int webcamRequestHeight = 480;

        // How long to go without a fresh webcam frame before treating the camera as
        // disconnected mid-session (as opposed to no device at all, which is caught in
        // Start()).
        private const float DisconnectTimeoutSeconds = 3f;

        private const int DetectorInputSize = 224;
        private const int DetectorAnchorCount = 2254;
        private const int DetectorBoxStride = 12;
        private const float DetectorScoreThreshold = 0.5f;

        private const int LandmarkerInputSize = 256;
        private const int LandmarkerOutputStride = 5;
        private const int LandmarkerVisibilityOffset = 3;

        // Once a landmark-derived crop region has been used this many consecutive
        // frames, force a fresh detector pass rather than drifting indefinitely on
        // landmark-bounds-only crops (mirrors MediaPipe's periodic re-detect safety net).
        private const int MaxFramesBetweenDetections = 30;

        public event Action<LandmarkFrame> OnLandmarkFrame;
        public event Action OnCameraUnavailable;

        public bool IsCameraUnavailable { get; private set; }

        // Exposed for debug/preview UI only (e.g. GestureDetectionDebugOverlay) - not
        // part of the IPoseProvider gameplay contract.
        public WebCamTexture Texture => _webcamTexture;

        private WebCamTexture _webcamTexture;
        private Worker _detectorWorker;
        private Worker _landmarkerWorker;
        private Tensor<float> _detectorInputTensor;
        private Tensor<float> _landmarkerInputTensor;
        private Vector2[] _anchors;
        private RenderTexture _cropTexture;
        private readonly PoseLandmarkSmoother _smoother = new PoseLandmarkSmoother();

        private float _timeSinceLastFrame;
        private bool _hasReceivedFirstFrame;
        private PoseCropRegion? _currentRegion;
        private int _framesSinceDetection;

        private void Start()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                RaiseCameraUnavailable();
                return;
            }

            if (detectorModelAsset == null || landmarkerModelAsset == null || detectorAnchorsCsv == null)
            {
                Debug.LogError($"{nameof(SentisPoseProvider)}: detector/landmarker model or anchors CSV not assigned - disabling.", this);
                enabled = false;
                return;
            }

            _anchors = ParseAnchors(detectorAnchorsCsv.text);
            if (_anchors.Length != DetectorAnchorCount)
            {
                Debug.LogError($"{nameof(SentisPoseProvider)}: expected {DetectorAnchorCount} anchors, got {_anchors.Length} - disabling.", this);
                enabled = false;
                return;
            }

            _webcamTexture = new WebCamTexture(webcamRequestWidth, webcamRequestHeight);
            _webcamTexture.Play();

            _detectorWorker = new Worker(ModelLoader.Load(detectorModelAsset), BackendType.GPUCompute);
            _landmarkerWorker = new Worker(ModelLoader.Load(landmarkerModelAsset), BackendType.GPUCompute);

            _detectorInputTensor = new Tensor<float>(new TensorShape(1, DetectorInputSize, DetectorInputSize, 3));
            _landmarkerInputTensor = new Tensor<float>(new TensorShape(1, LandmarkerInputSize, LandmarkerInputSize, 3));
            _cropTexture = new RenderTexture(LandmarkerInputSize, LandmarkerInputSize, 0);
        }

        private void Update()
        {
            if (_webcamTexture == null) return;

            if (!_webcamTexture.didUpdateThisFrame)
            {
                // Only the disconnect watchdog (not camera warm-up) should ever disable
                // the provider here: a webcam can legitimately take longer than
                // DisconnectTimeoutSeconds to deliver its first frame.
                if (_hasReceivedFirstFrame)
                {
                    _timeSinceLastFrame += Time.deltaTime;
                    if (_timeSinceLastFrame >= DisconnectTimeoutSeconds && !IsCameraUnavailable)
                    {
                        RaiseCameraUnavailable();
                    }
                }
                return;
            }

            _hasReceivedFirstFrame = true;
            _timeSinceLastFrame = 0f;

            bool needsDetection = !_currentRegion.HasValue || _framesSinceDetection >= MaxFramesBetweenDetections;
            if (needsDetection)
            {
                _currentRegion = RunDetector();
                _framesSinceDetection = 0;
            }

            if (!_currentRegion.HasValue)
            {
                // No person found by the detector and no prior region to fall back on -
                // nothing to feed the landmarker this frame.
                return;
            }

            var rawFrame = RunLandmarker(_currentRegion.Value);
            _framesSinceDetection++;

            // Prefer next frame's crop from this frame's own landmarks (cheap, matches
            // MediaPipe's re-detect-on-loss behavior) - but only if enough joints were
            // confidently found; otherwise force a fresh detector pass next frame.
            var boundsRegion = PoseCrop.FromLandmarkBounds(rawFrame);
            _currentRegion = boundsRegion.Size > 0f ? boundsRegion : (PoseCropRegion?)null;

            var smoothedFrame = _smoother.Smooth(rawFrame);
            OnLandmarkFrame?.Invoke(smoothedFrame);
        }

        private PoseCropRegion? RunDetector()
        {
            var transform = new TextureTransform().SetTensorLayout(TensorLayout.NHWC);
            TextureConverter.ToTensor(_webcamTexture, _detectorInputTensor, transform);
            _detectorWorker.Schedule(_detectorInputTensor);

            var boxesOutput = _detectorWorker.PeekOutput("boxes") as Tensor<float>;
            var scoresOutput = _detectorWorker.PeekOutput("scores") as Tensor<float>;
            if (boxesOutput == null || scoresOutput == null) return null;

            var boxes = boxesOutput.DownloadToArray();
            var scores = scoresOutput.DownloadToArray();

            int bestIndex = -1;
            float bestScore = DetectorScoreThreshold;
            for (int i = 0; i < DetectorAnchorCount; i++)
            {
                // UNVERIFIED (same open question as the landmarker's visibility output,
                // per the design spec): assumes raw scores need a sigmoid to become a
                // [0,1] probability. Confirm against real webcam output once available.
                float score = Sigmoid(scores[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return null;

            int baseIndex = bestIndex * DetectorBoxStride;
            Vector2 anchor = _anchors[bestIndex];
            Vector2 anchorPixels = anchor * DetectorInputSize;

            float dx = boxes[baseIndex];
            float dy = boxes[baseIndex + 1];
            Vector2 boxCenterPixels = anchorPixels + new Vector2(dx, dy);
            Vector2 boxCenter = boxCenterPixels / DetectorInputSize;

            Vector2 keypoint1 = (anchorPixels + new Vector2(boxes[baseIndex + 4], boxes[baseIndex + 5])) / DetectorInputSize;
            Vector2 keypoint2 = (anchorPixels + new Vector2(boxes[baseIndex + 6], boxes[baseIndex + 7])) / DetectorInputSize;

            return PoseCrop.FromDetection(keypoint1, keypoint2, boxCenter);
        }

        private LandmarkFrame RunLandmarker(PoseCropRegion region)
        {
            var (uvScale, uvOffset) = PoseCrop.ToUvTransform(region);
            Graphics.Blit(_webcamTexture, _cropTexture, uvScale, uvOffset);

            var transform = new TextureTransform().SetTensorLayout(TensorLayout.NHWC);
            TextureConverter.ToTensor(_cropTexture, _landmarkerInputTensor, transform);
            _landmarkerWorker.Schedule(_landmarkerInputTensor);

            // Do NOT Dispose() this tensor: PeekOutput returns a reference into the worker's
            // own pooled storage, not a copy - disposing it here frees memory the worker still
            // considers in-use and corrupts state on the next Schedule() call.
            var output = _landmarkerWorker.PeekOutput("Identity") as Tensor<float>;
            var joints = new PoseLandmark[PoseJointCount.Value];
            if (output == null)
            {
                for (int i = 0; i < joints.Length; i++) joints[i] = new PoseLandmark(Vector2.zero, 0f);
                return new LandmarkFrame(Time.time, joints);
            }

            var downloaded = output.DownloadToArray();
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                int baseIndex = i * LandmarkerOutputStride;
                float cropX = downloaded[baseIndex] / LandmarkerInputSize;
                float cropY = downloaded[baseIndex + 1] / LandmarkerInputSize;

                // Map from the crop's own [0,1] space back into full-source-texture
                // [0,1] space using the same axis-aligned region used to build the crop
                // (uv * scale + offset - the inverse of Graphics.Blit's own sampling,
                // see PoseCrop.ToUvTransform).
                var (uvScale, uvOffset) = PoseCrop.ToUvTransform(region);
                Vector2 sourcePosition = new Vector2(cropX, cropY) * uvScale + uvOffset;

                // UNVERIFIED: assumes this graph's visibility output is already a [0,1]
                // probability. Confirm against real webcam output once a camera is
                // available (values outside [0,1] before clamping would prove it's a
                // raw logit) and apply Sigmoid here if so - see design spec.
                float visibility = downloaded[baseIndex + LandmarkerVisibilityOffset];
                joints[i] = new PoseLandmark(sourcePosition, Mathf.Clamp01(visibility));
            }

            return new LandmarkFrame(Time.time, joints);
        }

        private static Vector2[] ParseAnchors(string csvText)
        {
            var lines = csvText.Split('\n');
            var anchors = new System.Collections.Generic.List<Vector2>(lines.Length);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)) continue;
                anchors.Add(new Vector2(x, y));
            }
            return anchors.ToArray();
        }

        private static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        private void OnDestroy()
        {
            _webcamTexture?.Stop();
            _detectorWorker?.Dispose();
            _landmarkerWorker?.Dispose();
            _detectorInputTensor?.Dispose();
            _landmarkerInputTensor?.Dispose();
            if (_cropTexture != null) _cropTexture.Release();
        }

        private void RaiseCameraUnavailable()
        {
            IsCameraUnavailable = true;
            _webcamTexture?.Stop();
            OnCameraUnavailable?.Invoke();
            enabled = false;
        }
    }
}
```

- [ ] **Step 2: Update the GestureDetectionBootstrap scene wiring note**

`GestureDetectionBootstrap.cs` itself needs no code change (it only references `SentisPoseProvider` through its `MonoBehaviour` reference, not its serialized fields). In the Unity Editor, open the scene(s) that reference the `SentisPoseProvider` component (`GestureDetectionDemo.unity` and any bootstrap scene) and re-assign its inspector fields: `Detector Model Asset` → `pose_detection.onnx`, `Landmarker Model Asset` → the existing `pose_landmarks_detector_full.onnx` (previously assigned to the single `modelAsset` field, now renamed), `Detector Anchors Csv` → `pose_detection_anchors.csv`. This is a manual scene-asset step with no source diff to show; note it in the commit message.

- [ ] **Step 3: Run the full existing EditMode suite to confirm nothing else broke**

Run all EditMode tests under `GestureDetection.Tests` via the `unity-cli` skill (no filter, or filter to the `GestureDetection.EditMode.Tests` assembly).
Expected: every test from Tasks 1-3 plus every pre-existing test (LandmarkBuffer, GestureMath, CalibrationData, all 5 matchers, GestureDetector) still PASS — this task touches no file any of them depend on other than the untested `SentisPoseProvider`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/GestureDetection/SentisPoseProvider.cs
git commit -m "feat(gesture-detection): add two-stage detector+landmarker pipeline with smoothing"
```

---

### Task 6: Skeleton bones in the debug overlay

**Files:**
- Modify: `Assets/Scripts/GestureDetection/GestureDetectionDebugOverlay.cs`

**Interfaces:**
- Consumes: existing `LandmarkFrame`, `PoseJoint`, `PoseJointCount.Value` fields already used by this file.
- Produces: no new public surface — visual-only change to an already dev-only `MonoBehaviour`.

- [ ] **Step 1: Add bone connections and draw them in OnGUI**

In `Assets/Scripts/GestureDetection/GestureDetectionDebugOverlay.cs`, add a static bone list and a draw call. First, add the field after the existing `_dotTexture` field:

```csharp
        private Texture2D _dotTexture;
        private Texture2D _lineTexture;

        // Standard BlazePose skeletal adjacency (upper body + legs); face landmarks
        // (indices 0-10) are intentionally excluded from bones since they're only
        // used as single confidence-gated dots, not a connected skeleton.
        private static readonly (PoseJoint, PoseJoint)[] Bones =
        {
            (PoseJoint.LeftShoulder, PoseJoint.RightShoulder),
            (PoseJoint.LeftShoulder, PoseJoint.LeftElbow),
            (PoseJoint.LeftElbow, PoseJoint.LeftWrist),
            (PoseJoint.RightShoulder, PoseJoint.RightElbow),
            (PoseJoint.RightElbow, PoseJoint.RightWrist),
            (PoseJoint.LeftShoulder, PoseJoint.LeftHip),
            (PoseJoint.RightShoulder, PoseJoint.RightHip),
            (PoseJoint.LeftHip, PoseJoint.RightHip),
            (PoseJoint.LeftHip, PoseJoint.LeftKnee),
            (PoseJoint.LeftKnee, PoseJoint.LeftAnkle),
            (PoseJoint.RightHip, PoseJoint.RightKnee),
            (PoseJoint.RightKnee, PoseJoint.RightAnkle),
        };
```

Then update `OnEnable` to also create `_lineTexture` (same 1x1 white texture as `_dotTexture` — reused for scaled/rotated line rects):

```csharp
        private void OnEnable()
        {
            poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
            poseProvider.OnCameraUnavailable += () => _statusText = "Camera unavailable";
            gestureDetector.OnGestureRecognized += g => _statusText = $"RECOGNIZED: {g}";
            gestureDetector.OnGestureProgress += (g, p) => _statusText = $"{g}: {p:P0}";

            _dotTexture = new Texture2D(1, 1);
            _dotTexture.SetPixel(0, 0, Color.white);
            _dotTexture.Apply();
            _lineTexture = _dotTexture;
        }
```

Then add bone-drawing inside `OnGUI`, right before the existing per-joint dot loop (so bones render underneath the dots), inside the `if (_latestFrame.HasValue)` block:

```csharp
            if (_latestFrame.HasValue)
            {
                var frame = _latestFrame.Value;

                foreach (var (jointA, jointB) in Bones)
                {
                    var a = frame.Get(jointA);
                    var b = frame.Get(jointB);
                    if (a.Confidence < minConfidenceToDraw || b.Confidence < minConfidenceToDraw) continue;

                    Vector2 pointA = new Vector2(
                        previewRect.x + a.Position.x * previewRect.width,
                        previewRect.y + a.Position.y * previewRect.height);
                    Vector2 pointB = new Vector2(
                        previewRect.x + b.Position.x * previewRect.width,
                        previewRect.y + b.Position.y * previewRect.height);

                    DrawLine(pointA, pointB, Color.cyan, thickness: 2f);
                }

                for (int i = 0; i < PoseJointCount.Value; i++)
                {
                    var landmark = frame.Joints[i];
                    if (landmark.Confidence < minConfidenceToDraw) continue;

                    // PoseLandmark.Position is normalized [0,1], y grows downward - same
                    // convention as this screen-space preview rect, so no flip needed.
                    float x = previewRect.x + landmark.Position.x * previewRect.width;
                    float y = previewRect.y + landmark.Position.y * previewRect.height;
                    var dotRect = new Rect(x - 4, y - 4, 8, 8);

                    var prevColor = GUI.color;
                    GUI.color = Color.Lerp(Color.red, Color.green, landmark.Confidence);
                    GUI.DrawTexture(dotRect, _dotTexture);
                    GUI.color = prevColor;
                }
            }
```

Finally, add the `DrawLine` helper method (uses `GUIUtility.RotateAroundPivot` to draw a rotated thin rect between two screen points, a standard OnGUI line-drawing technique):

```csharp
        private void DrawLine(Vector2 pointA, Vector2 pointB, Color color, float thickness)
        {
            Vector2 delta = pointB - pointA;
            float length = delta.magnitude;
            if (length < 0.001f) return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            var prevColor = GUI.color;
            var prevMatrix = GUI.matrix;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - thickness * 0.5f, length, thickness), _lineTexture);

            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }
```

- [ ] **Step 2: Manual verification (once a webcam is available)**

This file has no automated tests (dev-only OnGUI tool, consistent with its existing untested status). Once a physical webcam is available, run the demo scene and confirm: cyan bone lines connect confidence-passing joint pairs, no line is drawn when either endpoint is below `minConfidenceToDraw`, and — combined with Task 5's smoothing/crop fix — joints no longer flicker or cluster on the face. Note this manual check's outcome in the PR/commit description rather than as an automated test result.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/GestureDetection/GestureDetectionDebugOverlay.cs
git commit -m "feat(gesture-detection): draw skeleton bones in the debug overlay"
```

---

## Summary of what remains manual (no webcam in this dev environment)

- Task 4: confirming the downloaded `pose_detection.onnx`/anchors CSV actually match the documented contract (spot-check via Python `onnx` package if available, matching how the original landmarker model was verified per the prior plan).
- Task 5: confirming the detector's `"boxes"`/`"scores"` output tensor names are correct (this plan uses the names from Unity's own sample; if `ModelLoader`/`PeekOutput` logs a "no output named X" error in the Console, use `Unity_GetConsoleLogs`/the model's actual output names to correct them), and resolving the two `UNVERIFIED` sigmoid comments (detector score, landmarker visibility) against real camera output.
- Task 6: the actual "no more flicker/face-clustering, bones visible" visual acceptance check.
