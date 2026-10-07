using UnityEngine;

namespace VehicleToolkit
{
    /// <summary>
    /// Simple arcade-style car controller built on Unity's WheelCollider. Motor/steer/brake are driven directly
    /// from input each FixedUpdate - no gearbox or engine curve, just enough to drive a 4x4 around convincingly
    /// and let WheelCollider's friction curves handle slope/mud traction.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ArcadeCarController : MonoBehaviour
    {
        [System.Serializable]
        public struct Wheel
        {
            public WheelCollider collider;
            public Transform visual;
            public bool isSteering;
            public bool isMotor;
        }

        [Header("Wheels")]
        public Wheel frontLeft;
        public Wheel frontRight;
        public Wheel rearLeft;
        public Wheel rearRight;

        [Header("Input")]
        public string verticalAxis = "Vertical";
        public string horizontalAxis = "Horizontal";
        public KeyCode brakeKey = KeyCode.Space;

        [Header("Arcade Tuning")]
        public float motorTorque = 1500.0f;
        public float maxSteerAngle = 30.0f;
        public float brakeTorque = 3000.0f;
        [Tooltip("Overrides the rigidbody's center of mass (local space) - lower is more stable on slopes.")]
        public Vector3 centerOfMass = new Vector3(0.0f, -0.5f, 0.0f);

        protected Rigidbody carBody;

        public void Awake()
        {
            carBody = GetComponent<Rigidbody>();
            carBody.centerOfMass = centerOfMass;
        }

        public void FixedUpdate()
        {
            var vertical = Input.GetAxis(verticalAxis);
            var horizontal = Input.GetAxis(horizontalAxis);
            var brake = Input.GetKey(brakeKey) ? brakeTorque : 0.0f;
            var steerAngle = horizontal * maxSteerAngle;
            var torque = vertical * motorTorque;

            ApplyWheel(ref frontLeft, torque, steerAngle, brake);
            ApplyWheel(ref frontRight, torque, steerAngle, brake);
            ApplyWheel(ref rearLeft, torque, steerAngle, brake);
            ApplyWheel(ref rearRight, torque, steerAngle, brake);
        }

        protected static void ApplyWheel(ref Wheel wheel, float torque, float steerAngle, float brake)
        {
            if (!wheel.collider)
            {
                return;
            }
            wheel.collider.motorTorque = wheel.isMotor ? torque : 0.0f;
            wheel.collider.steerAngle = wheel.isSteering ? steerAngle : 0.0f;
            wheel.collider.brakeTorque = brake;
        }

        public void Update()
        {
            SyncVisual(frontLeft);
            SyncVisual(frontRight);
            SyncVisual(rearLeft);
            SyncVisual(rearRight);
        }

        protected static void SyncVisual(Wheel wheel)
        {
            if (!wheel.collider || !wheel.visual)
            {
                return;
            }
            wheel.collider.GetWorldPose(out var position, out var rotation);
            wheel.visual.SetPositionAndRotation(position, rotation);
        }
    }
}
