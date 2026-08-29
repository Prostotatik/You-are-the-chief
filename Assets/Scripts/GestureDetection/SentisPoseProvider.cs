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
        // Per-anchor box channels: [dx, dy, dw, dh, kp1x, kp1y, kp2x, kp2y, ...] (12
        // total per row; only the first 8 are read here). dx/dy (indices 0-1) are the
        // box-center offset from the anchor, used below for boxCenter. dw/dh (indices
        // 2-3) are the box width/height offset and are NOT used - crop size instead
        // comes from the distance between the two alignment keypoints (kp1, kp2 at
        // indices 4-7), matching Unity's own BlazeDetectionSample.
        private const int DetectorBoxStride = 12;
        private const float DetectorScoreThreshold = 0.5f;

        private const int LandmarkerInputSize = 256;
        private const int LandmarkerOutputStride = 5;
        private const int LandmarkerVisibilityOffset = 3;

        // Explicit names for PoseCrop.FromLandmarkBounds's default arguments (used in
        // Update() below) so the tuning knobs are visible at the call site instead of
        // hidden behind method-default values.
        private const float LandmarkBoundsMinConfidence = 0.4f;
        private const float LandmarkBoundsPaddingFraction = 0.35f;

        // Once a landmark-derived crop region has been used this many consecutive
        // frames, force a fresh detector pass rather than drifting indefinitely on
        // landmark-bounds-only crops (mirrors MediaPipe's periodic re-detect safety net).
        private const int MaxFramesBetweenDetections = 30;

        // If the detector is forced to run this many consecutive frames in a row (e.g.
        // because FromLandmarkBounds keeps failing to find 2 confident joints even
        // though the detector keeps finding a person), log once so this expensive
        // failure mode isn't silent.
        private const int DetectorEveryFrameWarnThreshold = 15;

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
        private int _consecutiveForcedDetections;
        private bool _hasWarnedDetectorEveryFrame;

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
            _cropTexture = new RenderTexture(LandmarkerInputSize, LandmarkerInputSize, 0)
            {
                // A crop region can extend past [0,1] (PoseCrop.FromDetection's 1.25x
                // scale and FromLandmarkBounds's 1.35x padding both can push past the
                // frame edge) - clamp so Graphics.Blit samples the edge pixel instead of
                // wrapping around to the opposite side of the source texture.
                wrapMode = TextureWrapMode.Clamp,
            };
            _cropTexture.Create();
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

            // _framesSinceDetection only ever counts consecutive frames that ran the
            // landmarker against a landmark-derived region (never a frame that ran the
            // detector) - see the increment site below, after RunLandmarker.
            bool wasRegionLost = !_currentRegion.HasValue;
            bool needsDetection = wasRegionLost || _framesSinceDetection >= MaxFramesBetweenDetections;
            if (needsDetection)
            {
                _currentRegion = RunDetector();
                _framesSinceDetection = 0;

                // Track the "detector forced every frame with no backoff" failure mode:
                // a region that was lost (not the normal periodic re-detect) and stays
                // lost/never regains 2 confident joints will hit this branch every
                // single Update(). The periodic MaxFramesBetweenDetections re-detect
                // resets this counter below once a landmark-derived region resumes.
                if (wasRegionLost)
                {
                    _consecutiveForcedDetections++;
                    if (_consecutiveForcedDetections >= DetectorEveryFrameWarnThreshold && !_hasWarnedDetectorEveryFrame)
                    {
                        _hasWarnedDetectorEveryFrame = true;
                        Debug.LogWarning($"{nameof(SentisPoseProvider)}: detector has run {_consecutiveForcedDetections} consecutive frames because landmark bounds keep failing to find 2 confident joints - falling back to detector-every-frame.", this);
                    }
                }
            }

            if (!_currentRegion.HasValue)
            {
                // No person found by the detector and no prior region to fall back on -
                // nothing to feed the landmarker this frame.
                return;
            }

            var rawFrame = RunLandmarker(_currentRegion.Value);

            // Prefer next frame's crop from this frame's own landmarks (cheap, matches
            // MediaPipe's re-detect-on-loss behavior) - but only if enough joints were
            // confidently found; otherwise force a fresh detector pass next frame.
            var boundsRegion = PoseCrop.FromLandmarkBounds(rawFrame, LandmarkBoundsMinConfidence, LandmarkBoundsPaddingFraction);
            _currentRegion = boundsRegion.Size > 0f ? boundsRegion : (PoseCropRegion?)null;

            if (_currentRegion.HasValue)
            {
                // A usable landmark-derived region was produced for next frame - this
                // counts toward the MaxFramesBetweenDetections budget, and the region is
                // no longer "lost" so the forced-detection backoff counter resets.
                _framesSinceDetection++;
                _consecutiveForcedDetections = 0;
                _hasWarnedDetectorEveryFrame = false;
            }

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

            // Guard against the detector model's actual output shape ever differing from
            // the hardcoded DetectorAnchorCount/DetectorBoxStride assumption above - a
            // mismatch here would otherwise throw IndexOutOfRangeException every single
            // Update() call. Degrade to "no detection this frame" instead.
            if (scores.Length < DetectorAnchorCount || boxes.Length < DetectorAnchorCount * DetectorBoxStride)
            {
                Debug.LogError($"{nameof(SentisPoseProvider)}: detector output shape mismatch (scores={scores.Length}, boxes={boxes.Length}, expected scores>={DetectorAnchorCount}, boxes>={DetectorAnchorCount * DetectorBoxStride}) - skipping detection this frame.", this);
                return null;
            }

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

            // Graphics.Blit samples its source in y-UP UV space, but uvScale/uvOffset
            // above are in this project's native y-DOWN convention (see PoseLandmark's
            // doc comment and PoseCrop.ToBlitTransform for the full derivation). Flip
            // once here, at the Blit call site only - sourcePosition below keeps using
            // the unflipped y-down uvScale/uvOffset since cropX/cropY are also y-down.
            var (blitScale, blitOffset) = PoseCrop.ToBlitTransform(uvScale, uvOffset);
            Graphics.Blit(_webcamTexture, _cropTexture, blitScale, blitOffset);

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

                // Map from the crop's own y-down [0,1] space back into full-source-texture
                // y-down [0,1] space using the same axis-aligned region used to build the
                // crop (uv * scale + offset, see PoseCrop.ToUvTransform). Deliberately uses
                // the UNFLIPPED uvScale/uvOffset here, not blitScale/blitOffset: cropX/cropY
                // are y-down (MediaPipe pixel-row convention) and uvScale/uvOffset are also
                // y-down, so this axis matches directly - the y-flip only applies at the
                // Graphics.Blit call site above, which samples in y-up UV space. uvScale/
                // uvOffset are loop-invariant (same region every iteration) so they're
                // computed once above, not per-joint.
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
            if (_cropTexture != null)
            {
                _cropTexture.Release();
                UnityEngine.Object.Destroy(_cropTexture);
                _cropTexture = null;
            }
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
