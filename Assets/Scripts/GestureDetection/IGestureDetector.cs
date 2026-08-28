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
