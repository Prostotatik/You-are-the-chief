# Gesture Detection Subsystem — Design Spec

Status: approved for planning
Date: 2026-08-28
Scope: first sub-project of "You Are The Chief" (motion-controlled cooking party game). This spec covers ONLY the webcam pose-based gesture detection subsystem. Core gameplay loop, table/tray presentation, tutorial modal visuals, and results/highlights are separate future sub-projects that will consume this subsystem's public API.

## Context

The game is an online multiplayer cooking party game. Each player sits at their own PC with their own webcam. A camera watches the player's body; the system must recognize when the player performs one of 5 specific full-body gestures, each mapped to a dish. No keyboard/mouse gameplay input — the webcam gesture is the only input mechanism for cooking actions.

Players are normally seated at a desk, but several gestures require the player to stand up or put their feet up on the desk near the camera — framing is not fixed to "seated upper body only."

Target platform: Desktop app (Windows/Mac build). This makes on-device ML inference via Unity Sentis viable (WebGL was ruled out due to browser ML inference limits).

## Gestures (source of truth)

| Dish | Physical action |
|---|---|
| Pizza | Rotate hand/index finger as if twirling pizza dough |
| Mac&Cheese | Raise heel, rub fist against it (grating parmesan) |
| Rocket Soda | Shake two fists together low near the belly (shaking a bottle) |
| Wine | Stomp feet repeatedly (stomping grapes) |
| Spicy Spice | Raise both fists to face height, thumbs inward, move hands toward/away from face |

## Architecture

```
WebCamTexture
     |
     v
Pose Provider (Unity Sentis inference, full-body landmark model)
     |  per-frame landmarks (x, y, confidence per joint)
     v
Landmark Buffer (per-player ring buffer, ~2s of history with timestamps)
     |
     v
5x Gesture Matcher (one per dish, rule-based, reads only its relevant joints)
     |  GestureType on match
     v
Gesture Event Bus (plain C# events on the player's detector instance)
     |
     v
(consumed later by: core gameplay loop, tutorial-hint overlay, highlight recorder)
```

This subsystem owns everything above the event bus. It has zero knowledge of gameplay, scoring, or networking — it only emits local events per player. This boundary lets the core gameplay loop be built and tested against a stub implementing the same interface, and lets multiplayer networking be added later without touching detection code.

## Components

### Pose Provider
- Captures frames from `WebCamTexture`.
- Runs a pretrained full-body pose model (BlazePose Full, 33 landmarks, includes wrists/hands/ankles/heels) through Unity Sentis.
- Outputs one landmark frame per tick: array of (x, y, confidence) in normalized viewport space.
- Runs fully offline/on-device — no network calls at runtime.
- **Risk / open implementation task:** sourcing and converting a BlazePose ONNX model into a Sentis-compatible asset is a nontrivial task to be handled explicitly in the implementation plan; exact model file is not yet in the project.

### Landmark Buffer
- One ring buffer per player, holding the last ~2 seconds of landmark frames with timestamps.
- Exists because gestures are temporal patterns (shaking, rotating, stomping), not single-frame poses. Matchers read a time window, not just the latest frame.

### Gesture Matcher (x5)
- One small class per dish (`PizzaMatcher`, `MacAndCheeseMatcher`, `RocketSodaMatcher`, `WineMatcher`, `SpicySpiceMatcher`), each implementing a common `IGestureMatcher` interface.
- Each matcher reads only the buffer's joints relevant to its gesture and evaluates a hand-tuned rule (position thresholds, velocity, oscillation count over the time window) — no trained classifier, no training data required.
- Draft matching rules (thresholds to be tuned during implementation/playtesting):
  - **Pizza:** wrist above elbow, hand traces a circular path, ≥1 full rotation within 1.5s.
  - **Mac&Cheese:** one ankle raised above the opposite knee, the opposite wrist oscillates near that ankle.
  - **Rocket Soda:** both wrists below chest height and close together, vertical oscillation ≥3 times within 1.5s.
  - **Wine:** both ankles alternate vertical downward strikes (stepping in place), ≥2 alternations within 1.5s.
  - **Spicy Spice:** both wrists at face height, alternating forward/backward motion toward the face.
- Rejected alternative: a single monolithic classifier handling all 5 gestures in one switch/case — rejected because it mixes 5 unrelated pattern logics in one place, harder to debug and extend when a 6th dish is added later.
- Rejected alternative: a trained ML classifier (e.g. LSTM/1D-CNN) over landmark sequences — rejected as overkill for 5 clearly distinct gestures; requires collecting a training dataset and a training pipeline outside Unity, and cannot be validated in this environment (no recorded gesture data, no webcam access in this session).

### Calibration
- Before gameplay starts, a short (~3s) T-pose calibration step: player stands in frame, arms out.
- Produces a per-player baseline (body scale, approximate distance from camera) used to normalize the position/velocity thresholds used by matchers, so gesture recognition is not miscalibrated by player height or camera distance.

### Public API (boundary for later subsystems)
```csharp
interface IGestureDetector
{
    event Action<GestureType> OnGestureRecognized;
    event Action<GestureType, float progress> OnGestureProgress; // 0..1, drives the tutorial-hint modal
    event Action OnCameraUnavailable;
}
```
- One `IGestureDetector` instance per local player.
- Consumers (core gameplay loop, tutorial overlay, future highlight recorder) depend only on this interface, never on Pose Provider/Buffer/Matcher internals.
- This subsystem does not talk to the network layer. Sending "gesture completed" to other players/server is the multiplayer subsystem's job, built on top of `OnGestureRecognized`.

## Error Handling

- No webcam present, or webcam disconnects mid-game: `OnCameraUnavailable` fires once; matchers stop evaluating (no crash, no false positives).
- Low-confidence landmark (poor lighting, occlusion): that landmark is excluded from matcher evaluation for that frame rather than trusted, to avoid false triggers.

## Testing Strategy

- No physical webcam is available in this development environment, so gesture matchers must be testable without one.
- Unit tests per matcher, driven by synthetic landmark-sequence fixtures: a "clean gesture" sequence (should match), a "near miss" sequence (should not match), and a "different gesture" sequence (should not cross-trigger another matcher).
- Manual end-to-end validation with a real webcam is left to the user once a build exists.

## Out of Scope (future sub-projects)

- Core gameplay loop (order rope, drag-and-drop assignment, scoring, fail condition).
- Multiplayer networking / relaying gesture-recognized events between players.
- Station/tray presentation, prop spawning, animations per dish.
- Tutorial-hint modal visuals (consumes `OnGestureProgress` but its own animation content is a separate design).
- Results screen and webcam highlight clip capture.
