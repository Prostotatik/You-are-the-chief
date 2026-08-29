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

            var infoRect = new Rect(previewRect.x, previewRect.yMax + 5, previewWidth, 80);
            GUI.Box(infoRect, GUIContent.none);
            GUI.Label(new Rect(infoRect.x + 5, infoRect.y + 5, infoRect.width - 10, infoRect.height - 10),
                $"Status: {_statusText}\n" +
                $"Landmark frames received: {_framesReceived}\n" +
                $"Camera unavailable: {poseProvider.IsCameraUnavailable}");
        }

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
    }
}
