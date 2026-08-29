# Gesture Detection Pipeline Rework — Design

**Date:** 2026-08-29
**Status:** Approved for planning

## Problem

The current webcam pose pipeline (`SentisPoseProvider`) is unusably jittery in
practice: landmarks flicker in and out, many land on the face instead of the
body, and there is no smoothing between frames.

Root cause, found via code audit + research (see below): `SentisPoseProvider`
feeds the **entire** webcam frame, resized to 256x256, directly into the
BlazePose *landmarker* model (`pose_landmarks_detector_full.onnx`). That model
is designed to receive a **tight crop of a single person**, produced by a
separate, lighter *detector* model run first. Unity's own sample repository
(`Unity-Technologies/sentis-samples`, `BlazeDetectionSample/Pose/`) ships both
models side by side — `pose_detection.onnx` (finds the person + a rough
bounding box) and `pose_landmarks_detector_{lite,full,heavy}.onnx` (takes a
crop and returns 33 landmarks) — and its own `PoseDetection.cs` sample script
runs them as a two-stage pipeline. Only the second model was ever brought into
this project; the first stage was skipped, so the landmarker receives a
squashed, un-cropped, mostly-background image every frame. That single gap
explains both symptoms: landmarks cluster near whatever face-shaped features
survive the squash, and because there's no per-frame stability (no ROI
tracking, no smoothing), predictions swing frame to frame.

Separately confirmed by code review: there is no temporal smoothing anywhere
in the pipeline (raw per-frame model output goes straight into
`LandmarkBuffer`), and the `visibility` output's scale is marked
`UNVERIFIED` in a code comment — it may be a raw pre-sigmoid logit rather
than a [0,1] probability, which would make the existing confidence-based
filtering (`JointFilter`, min-confidence 0.4) unreliable.

Research also confirmed (see conversation): MediaPipe's own reference
pipeline uses this same two-stage detector+landmarker architecture, plus a
hand-rollable `OneEuroFilter` (a well-known, ~50-line smoothing algorithm,
found in MediaPipe's own source at
`mediapipe/util/filtering/one_euro_filter.h`) for exactly this jitter
problem. Switching to a full MediaPipe Unity plugin
(`homuler/MediaPipeUnityPlugin`) was considered and rejected: it would fix
the same root cause but via a native cross-platform plugin dependency swap,
higher integration/maintenance risk, for no accuracy gain over fixing the
existing Sentis (Inference Engine) pipeline directly — Unity's own sample
already contains the correct two-model architecture, unused.

## Game Constraint

Players will not perform a T-pose or stand fully framed. Most gestures use
arms/upper body; leg gestures happen only when the player deliberately puts
their legs somewhere visible (e.g. up on a table), and are otherwise off
frame or hidden. The pipeline must tolerate partial-body visibility as the
normal case, not the exception.

This is already handled adequately by the existing code and needs no rework:
`CalibrationController` does not require a T-pose or any deliberate
calibration step — it passively (re)computes `CalibrationData` from shoulder/
hip landmarks in whatever ordinary frames those happen to be visible in,
recomputing roughly once a second in the background. Matchers already treat
absent/low-confidence joints as "not evaluable" per frame via
`JointFilter.TryGet` rather than requiring a fixed set of joints to be
visible. No changes to the calibration flow are needed.

## Design

### 1. Two-stage detection in `SentisPoseProvider`

Add the detector model as a first pass:

- Source `pose_detection.onnx` from the same `sentis-samples` repo/folder as
  the already-vendored landmarker model, and add it to `Assets/Models/`
  alongside a licensing note matching the existing one on
  `SentisPoseProvider`.
- Each frame (or every N frames once a person is being tracked — see below),
  run the detector on the full webcam frame (its own smaller input size,
  e.g. 224x224 per the sourced model) to get a bounding box + the two
  alignment keypoints it outputs.
- From the bounding box, compute a square crop region (with padding) in the
  source webcam texture, and use `TextureConverter`/`TextureTransform`'s
  crop support (or an intermediate `RenderTexture` blit) to produce the
  256x256 input the landmarker already expects — replacing today's
  "squash whole frame" step with "squash the cropped region".
- Track re-detection cost: MediaPipe's approach only re-runs the heavier
  detector when tracking confidence drops (e.g. landmarker's own presence
  score falls below a threshold, or every fixed interval as a safety net);
  otherwise it reuses the previous frame's landmark bounding region
  (expanded by a margin) as the next crop directly, skipping the detector.
  Replicate this: track a "last known good bbox", derive next frame's crop
  from it, and only fall back to running the detector when presence drops
  or no bbox exists yet.
- When the detector finds no person at all (bbox confidence below
  threshold), emit a frame with all-zero confidence landmarks (or skip
  emitting) rather than feeding garbage into the landmarker — this is the
  correct behavior for partial-body frames where the player isn't in frame
  yet.

### 2. `OneEuroFilter` temporal smoothing

- New file `Assets/Scripts/GestureDetection/OneEuroFilter.cs`: a small,
  dependency-free C# implementation of the standard One-Euro filter
  (frequency, min-cutoff, beta, derivate-cutoff parameters), operating on a
  single scalar value over time. Pure logic, no Unity/MonoBehaviour
  dependency — unit-testable with synthetic time-series data the same way
  `GestureMath` is tested.
- `SentisPoseProvider` (or a small wrapper between it and the rest of the
  pipeline) keeps one `OneEuroFilter` pair (x, y) per `PoseJoint`, and
  smooths each landmark's position through its filter before constructing
  the `LandmarkFrame` that's broadcast via `OnLandmarkFrame`. Confidence is
  passed through unfiltered (it's not a jittery signal in the same sense,
  and matchers already threshold it).
- Filters reset (or are simply left to reconverge) when a joint's
  confidence drops out and comes back, since a stale filtered value
  bridging a real gap would be wrong; simplest correct approach is to key
  filters by joint and skip filtering (pass raw through, re-seed the
  filter) on the first frame a joint becomes visible again after being
  absent.

### 3. Confidence scale fix

- Once the two-stage pipeline is in and a real webcam is available for
  manual verification (per the plan's existing constraint that Task 10-
  equivalent work is verified by Console/manual inspection, not EditMode
  tests), check the landmarker's raw `visibility` output range. If values
  fall outside [0,1] before clamping, apply a sigmoid before use. Resolve
  the `UNVERIFIED` comment either way (confirm the current Clamp01 is
  correct, or add the sigmoid) so it stops being an open question.

### 4. Skeleton debug overlay

Extend `GestureDetectionDebugOverlay` (dev-only, already OnGUI-based) to
draw bone lines between joint pairs (standard BlazePose adjacency — e.g.
shoulder-elbow-wrist, hip-knee-ankle, shoulder-hip, shoulder-shoulder,
hip-hip) in addition to the existing per-joint dots, using the same
confidence-gated visibility (skip drawing a bone if either endpoint is
below `minConfidenceToDraw`). This is the visual "joints + bones, nothing
flickers" reference the project owner described seeing elsewhere, and
serves as the manual acceptance check for the jitter fix.

## Out of Scope

- No change to the 5 gesture matchers, `GestureDetector`, `LandmarkBuffer`,
  or the calibration flow — none of those are the source of the reported
  problems.
- No switch to MediaPipe Unity plugin or any non-Sentis runtime (considered
  and rejected — see Problem section).
- No change to which gestures exist or how they're defined; the project
  owner explicitly ruled out changing game design, only the detection
  implementation.

## Testing

- `OneEuroFilter`: EditMode unit tests with synthetic oscillating/noisy
  scalar sequences, verifying it reduces high-frequency noise while
  tracking a real underlying trend (same style as existing `GestureMathTests`).
- Crop/bbox math (converting a detector bounding box into a source-texture
  crop rect): EditMode unit tests with synthetic bounding boxes, pure
  functions extracted so they don't require a live `Texture2D`/webcam.
- The detector+landmarker Sentis integration itself and the debug overlay's
  visual bone drawing remain manual/Console-verified, consistent with the
  existing plan's constraint that no physical webcam is available in this
  development environment.
