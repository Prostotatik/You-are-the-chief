using UnityEngine;

namespace GestureDetection
{
    // Development-only visual debug overlay: draws the raw webcam feed plus the
    // detected pose landmarks and live gesture-progress feedback on top of it, via
    // OnGUI. Not part of the shipping gameplay surface - references SentisPoseProvider
    // and GestureDetector directly rather than through IPoseProvider/IGestureDetector,
    // since this is a dev tool, not something the gameplay loop depends on.
    public class GestureDetectionDebugOverlay : MonoBehaviour
    {
        [SerializeField] private SentisPoseProvider poseProvider;
        [SerializeField] private GestureDetector gestureDetector;
        [SerializeField] private float minConfidenceToDraw = 0.4f;
        [SerializeField] private float previewWidth = 480f;
        [SerializeField] private float previewHeight = 360f;

        private LandmarkFrame? _latestFrame;
        private int _framesReceived;
        private string _statusText = "Waiting for calibration...";
        private Texture2D _dotTexture;

        private void OnEnable()
        {
            poseProvider.OnLandmarkFrame += HandleLandmarkFrame;
            poseProvider.OnCameraUnavailable += () => _statusText = "Camera unavailable";
            gestureDetector.OnGestureRecognized += g => _statusText = $"RECOGNIZED: {g}";
            gestureDetector.OnGestureProgress += (g, p) => _statusText = $"{g}: {p:P0}";

            _dotTexture = new Texture2D(1, 1);
            _dotTexture.SetPixel(0, 0, Color.white);
            _dotTexture.Apply();
        }

        private void OnDisable()
        {
            if (poseProvider != null) poseProvider.OnLandmarkFrame -= HandleLandmarkFrame;
        }

        private void HandleLandmarkFrame(LandmarkFrame frame)
        {
            _latestFrame = frame;
            _framesReceived++;
        }

        private void OnGUI()
        {
            var previewRect = new Rect(10, 10, previewWidth, previewHeight);
            var texture = poseProvider.Texture;

            if (texture != null)
            {
                GUI.DrawTexture(previewRect, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Box(previewRect, "No webcam texture yet");
            }

            if (_latestFrame.HasValue)
            {
                var frame = _latestFrame.Value;
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

            var infoRect = new Rect(previewRect.x, previewRect.yMax + 5, previewWidth, 80);
            GUI.Box(infoRect, GUIContent.none);
            GUI.Label(new Rect(infoRect.x + 5, infoRect.y + 5, infoRect.width - 10, infoRect.height - 10),
                $"Status: {_statusText}\n" +
                $"Landmark frames received: {_framesReceived}\n" +
                $"Camera unavailable: {poseProvider.IsCameraUnavailable}");
        }
    }
}
