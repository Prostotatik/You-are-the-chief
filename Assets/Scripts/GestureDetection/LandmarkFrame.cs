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
