using UnityEditor;
using UnityEngine;
using VehicleToolkit;

namespace VehicleToolkit.Editor
{
    public class ArcadeCarSetupWindow : EditorWindow
    {
        public enum Drivetrain
        {
            RWD,
            FWD,
            AWD,
        }

        private GameObject car;
        private Transform wheelFL;
        private Transform wheelFR;
        private Transform wheelRL;
        private Transform wheelRR;

        private Drivetrain drivetrain = Drivetrain.AWD;
        private float carMass = 1500.0f;
        private Vector3 centerOfMass = new Vector3(0.0f, -0.5f, 0.0f);

        private float wheelRadius = 0.4f;
        private float wheelMass = 20.0f;
        private float suspensionDistance = 0.3f;
        private float springForce = 35000.0f;
        private float damperForce = 4500.0f;
        private float gripMultiplier = 1.0f;

        private float motorTorque = 1500.0f;
        private float maxSteerAngle = 30.0f;
        private float brakeTorque = 3000.0f;

        [MenuItem("Tools/Vehicle Toolkit/Arcade Car Setup")]
        public static void Open()
        {
            GetWindow<ArcadeCarSetupWindow>("Arcade Car Setup");
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Arcade 4x4 Car Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assign the car body and its four wheel meshes, then click Create Arcade Car Rig. This adds a " +
                "WheelCollider per wheel and an ArcadeCarController wired up to drive them.",
                MessageType.Info);

            EditorGUILayout.Space();
            car = (GameObject)EditorGUILayout.ObjectField("Car", car, typeof(GameObject), true);
            wheelFL = (Transform)EditorGUILayout.ObjectField("Wheel Front Left", wheelFL, typeof(Transform), true);
            wheelFR = (Transform)EditorGUILayout.ObjectField("Wheel Front Right", wheelFR, typeof(Transform), true);
            wheelRL = (Transform)EditorGUILayout.ObjectField("Wheel Rear Left", wheelRL, typeof(Transform), true);
            wheelRR = (Transform)EditorGUILayout.ObjectField("Wheel Rear Right", wheelRR, typeof(Transform), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Body", EditorStyles.boldLabel);
            drivetrain = (Drivetrain)EditorGUILayout.EnumPopup("Drivetrain", drivetrain);
            carMass = EditorGUILayout.Slider("Mass (kg)", carMass, 300.0f, 5000.0f);
            centerOfMass = EditorGUILayout.Vector3Field("Center Of Mass", centerOfMass);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Wheels", EditorStyles.boldLabel);
            wheelRadius = EditorGUILayout.Slider("Radius", wheelRadius, 0.1f, 1.0f);
            wheelMass = EditorGUILayout.Slider("Mass (kg)", wheelMass, 1.0f, 80.0f);
            suspensionDistance = EditorGUILayout.Slider("Suspension Distance", suspensionDistance, 0.05f, 0.8f);
            springForce = EditorGUILayout.Slider("Suspension Spring", springForce, 5000.0f, 100000.0f);
            damperForce = EditorGUILayout.Slider("Suspension Damper", damperForce, 500.0f, 15000.0f);
            gripMultiplier = EditorGUILayout.Slider("Grip Multiplier", gripMultiplier, 0.2f, 3.0f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Arcade Tuning", EditorStyles.boldLabel);
            motorTorque = EditorGUILayout.Slider("Motor Torque", motorTorque, 200.0f, 5000.0f);
            maxSteerAngle = EditorGUILayout.Slider("Max Steer Angle", maxSteerAngle, 10.0f, 45.0f);
            brakeTorque = EditorGUILayout.Slider("Brake Torque", brakeTorque, 500.0f, 10000.0f);

            EditorGUILayout.Space();
            var canCreate = car && wheelFL && wheelFR && wheelRL && wheelRR;
            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button("Create Arcade Car Rig", GUILayout.Height(32)))
                {
                    CreateRig();
                }
            }
            if (!canCreate)
            {
                EditorGUILayout.HelpBox("Assign the car and all four wheel meshes first.", MessageType.Warning);
            }
        }

        private void CreateRig()
        {
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Arcade Car Rig");

            var carBody = car.GetComponent<Rigidbody>();
            if (!carBody)
            {
                carBody = Undo.AddComponent<Rigidbody>(car);
                carBody.mass = carMass;
            }

            var flWheel = CreateWheel("WC_FrontLeft", wheelFL, isSteering: true, isMotor: drivetrain != Drivetrain.RWD);
            var frWheel = CreateWheel("WC_FrontRight", wheelFR, isSteering: true, isMotor: drivetrain != Drivetrain.RWD);
            var rlWheel = CreateWheel("WC_RearLeft", wheelRL, isSteering: false, isMotor: drivetrain != Drivetrain.FWD);
            var rrWheel = CreateWheel("WC_RearRight", wheelRR, isSteering: false, isMotor: drivetrain != Drivetrain.FWD);

            var controller = car.GetComponent<ArcadeCarController>();
            if (!controller)
            {
                controller = Undo.AddComponent<ArcadeCarController>(car);
            }
            controller.frontLeft = flWheel;
            controller.frontRight = frWheel;
            controller.rearLeft = rlWheel;
            controller.rearRight = rrWheel;
            controller.motorTorque = motorTorque;
            controller.maxSteerAngle = maxSteerAngle;
            controller.brakeTorque = brakeTorque;
            controller.centerOfMass = centerOfMass;

            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = car;
            Debug.Log($"Arcade car rig created on '{car.name}' ({drivetrain}).", car);
        }

        private ArcadeCarController.Wheel CreateWheel(string name, Transform visual, bool isSteering, bool isMotor)
        {
            var wheelGO = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(wheelGO, "Create Arcade Car Rig");
            wheelGO.transform.SetParent(car.transform, false);
            wheelGO.transform.position = visual.position;
            wheelGO.transform.rotation = car.transform.rotation;

            var wheelCollider = Undo.AddComponent<WheelCollider>(wheelGO);
            wheelCollider.radius = wheelRadius;
            wheelCollider.mass = wheelMass;
            wheelCollider.suspensionDistance = suspensionDistance;
            wheelCollider.suspensionSpring = new JointSpring()
            {
                spring = springForce,
                damper = damperForce,
                targetPosition = 0.5f,
            };

            var forwardFriction = wheelCollider.forwardFriction;
            forwardFriction.stiffness *= gripMultiplier;
            wheelCollider.forwardFriction = forwardFriction;

            var sidewaysFriction = wheelCollider.sidewaysFriction;
            sidewaysFriction.stiffness *= gripMultiplier;
            wheelCollider.sidewaysFriction = sidewaysFriction;

            return new ArcadeCarController.Wheel()
            {
                collider = wheelCollider,
                visual = visual,
                isSteering = isSteering,
                isMotor = isMotor,
            };
        }
    }
}
