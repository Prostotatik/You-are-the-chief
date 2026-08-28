// Pose model sourced from https://github.com/Unity-Technologies/sentis-samples
// (BlazeDetectionSample/Pose/Assets/Models/pose_landmarks_detector_full.onnx),
// Unity's own Inference Engine (Sentis) sample repository, per Task 10 Step 1.
// NOTE: that file is distributed under Unity's Sentis sample license (Unity
// Terms of Service, "Experimental / Evaluation" — see the repo's License.md),
// not Apache-2.0/MIT. Confirm this is acceptable for the target project's
// licensing needs before shipping; swap in a differently-licensed ONNX export
// of pose_landmark_full if not.
using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace GestureDetection
{
    // Captures the local webcam and runs a BlazePose-family model through the
    // Inference Engine to produce a LandmarkFrame every tick.
    //
    // Model contract (verified directly against the sourced ONNX asset with the
    // Python `onnx` package, and cross-checked against Unity's own sample script
    // BlazeDetectionSample/Pose/Assets/Scripts/PoseDetection.cs in the
    // sentis-samples repo — see Task 10 Step 2 of the implementation plan):
    //   Input:  tensor named "input_1", shape (1, InputSize, InputSize, 3) —
    //           NHWC, RGB, normalized [0,1]. NOTE this is NHWC, not the NCHW
    //           layout Inference Engine defaults to, so the input tensor must
    //           be constructed with H and W before C, and the TextureTransform
    //           must explicitly opt into TensorLayout.NHWC.
    //   Output: tensor named "Identity", flat float buffer (1, 195) = 39
    //           landmark blocks of OutputStride(5) floats each:
    //           [x, y, z, visibility, presence] in pixel units of the
    //           InputSize x InputSize input tensor. Only the first 33 blocks
    //           (PoseJointCount.Value) correspond to the standard MediaPipe
    //           BlazePose body joints in PoseJoint enum order; the remaining 6
    //           are auxiliary ROI-tracking points and are ignored here.
    //           x/y are divided by InputSize below to convert them into the
    //           normalized [0,1] viewport space that PoseLandmark expects.
    //
    // KNOWN LIMITATION: the real MediaPipe/BlazePose pipeline runs a separate,
    // lightweight pose-DETECTOR model first to find and crop/rotate/scale the
    // body region before feeding it to this landmarker model (see the sample's
    // two-model, two-stage Detect() flow). This provider is single-stage: it
    // feeds the resized full webcam frame directly to the landmarker for
    // simplicity, per this task's brief. Expect degraded accuracy versus the
    // full two-stage pipeline, especially when the subject doesn't fill most
    // of the frame; a future task can add the detector + affine-crop stage if
    // needed.
    public class SentisPoseProvider : MonoBehaviour, IPoseProvider
    {
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private int webcamRequestWidth = 640;
        [SerializeField] private int webcamRequestHeight = 480;

        // How long to go without a fresh webcam frame before treating the camera as
        // disconnected mid-session (as opposed to no device at all, which is caught in
        // Start()).
        private const float DisconnectTimeoutSeconds = 3f;

        private const int InputSize = 256;
        private const int OutputStride = 5;
        private const int VisibilityOffset = 3;

        public event Action<LandmarkFrame> OnLandmarkFrame;
        public event Action OnCameraUnavailable;

        public bool IsCameraUnavailable { get; private set; }

        private WebCamTexture _webcamTexture;
        private Worker _worker;
        private Tensor<float> _inputTensor;
        private float _timeSinceLastFrame;

        private void Start()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                RaiseCameraUnavailable();
                return;
            }

            if (modelAsset == null)
            {
                Debug.LogError($"{nameof(SentisPoseProvider)}: no ModelAsset assigned - disabling.", this);
                enabled = false;
                return;
            }

            _webcamTexture = new WebCamTexture(webcamRequestWidth, webcamRequestHeight);
            _webcamTexture.Play();

            var model = ModelLoader.Load(modelAsset);
            _worker = new Worker(model, BackendType.GPUCompute);
            // NHWC: (batch, height, width, channels) — matches the sourced
            // model's verified input shape, not Inference Engine's NCHW default.
            _inputTensor = new Tensor<float>(new TensorShape(1, InputSize, InputSize, 3));
        }

        private void Update()
        {
            if (_webcamTexture == null) return;

            if (!_webcamTexture.didUpdateThisFrame)
            {
                _timeSinceLastFrame += Time.deltaTime;
                if (_timeSinceLastFrame >= DisconnectTimeoutSeconds && !IsCameraUnavailable)
                {
                    RaiseCameraUnavailable();
                }
                return;
            }

            _timeSinceLastFrame = 0f;

            var transform = new TextureTransform()
                .SetDimensions(InputSize, InputSize, 3)
                .SetTensorLayout(TensorLayout.NHWC);
            TextureConverter.ToTensor(_webcamTexture, _inputTensor, transform);
            _worker.Schedule(_inputTensor);
            // Do NOT Dispose() this tensor: PeekOutput returns a reference into the worker's
            // own pooled storage, not a copy - disposing it here frees memory the worker still
            // considers in-use and corrupts state on the next Schedule() call.
            var output = _worker.PeekOutput("Identity") as Tensor<float>;
            if (output == null) return;

            var downloaded = output.DownloadToArray();
            var joints = new PoseLandmark[PoseJointCount.Value];
            for (int i = 0; i < PoseJointCount.Value; i++)
            {
                int baseIndex = i * OutputStride;
                float x = downloaded[baseIndex] / InputSize;
                float y = downloaded[baseIndex + 1] / InputSize;
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

        private void RaiseCameraUnavailable()
        {
            IsCameraUnavailable = true;
            OnCameraUnavailable?.Invoke();
            enabled = false;
        }
    }
}
