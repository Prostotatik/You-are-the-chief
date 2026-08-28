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
