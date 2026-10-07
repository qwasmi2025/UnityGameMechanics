using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

namespace RopeLab.EditorTools
{
    /// <summary>Tools > Rope Lab > Build Test Scene: ProBuilder blockout with 5 TLOU2-style rope tests.</summary>
    public static class RopeLabBuilder
    {
        const string MatDir = "Assets/RopeLab/Materials";
        const float BayW = 22f, BayD = 18f;

        static Material _floor, _wall, _accent, _goal, _rope, _cable, _plug, _machine, _crate, _dark, _brass, _elecBox, _goalFloor;
        const string GridDir = "Assets/Thirdparty/Ciathyza/Gridbox Prototype Materials/Materials/URP/";

        [MenuItem("Tools/Rope Lab/Build Test Scene")]
        public static void Build()
        {
            var old = GameObject.Find("RopeLab");
            if (old) Object.DestroyImmediate(old);
            var oldHud = GameObject.Find("RopeHUD");
            if (oldHud) Object.DestroyImmediate(oldHud);

            MakeMaterials();
            var root = new GameObject("RopeLab").transform;
            var geo = Group("Blockout", root);

            // Shared floor + hallway along -Z
            const int bays = 6;
            float mid = BayW * bays * 0.5f - 1f, span = BayW * bays + 4f;
            Box("Floor", new Vector3(mid, -0.25f, 5f), new Vector3(span, 0.5f, 30f), _floor, geo);
            Box("Hall_BackWall", new Vector3(mid, 1.5f, -10f), new Vector3(span, 3f, 0.4f), _wall, geo);

            Box("Outer_BackWall", new Vector3(mid, 3f, 20.2f), new Vector3(span, 6f, 0.4f), _wall, geo);
            Box("Outer_WallL", new Vector3(-3.2f, 3f, 5f), new Vector3(0.4f, 6f, 30f), _wall, geo);
            Box("Outer_WallR", new Vector3(BayW * bays + 1.2f, 3f, 5f), new Vector3(0.4f, 6f, 30f), _wall, geo);

            for (int i = 0; i <= bays; i++)
            {
                float x = i * BayW - 1f;
                Box($"Divider_{i}", new Vector3(x, 2.5f, BayD * 0.5f + 1f), new Vector3(0.4f, 5f, BayD + 2f), _wall, geo);
            }

            var zones = Group("Tests", root);
            Test1_DragAndDrape(zones, 0f);
            Test2_ThrowOver(zones, BayW * 1);
            Test3_PowerCable(zones, BayW * 2);
            Test4_TieAndClimb(zones, BayW * 3);
            Test5_PullCrate(zones, BayW * 4);
            Test6_CableOverFence(zones, BayW * 5);
            AddLeashedDog(root);

            // HUD
            var hud = new GameObject("RopeHUD");
            hud.AddComponent<RopeHUD>();
            hud.AddComponent<RopeDebugView>();       // G: show rope nodes
            hud.AddComponent<FrameRateLock>();       // 60 FPS cap

            // Player
            var cc = Object.FindFirstObjectByType<CharacterController>();
            if (cc)
            {
                if (!cc.GetComponent<RopeInteractor>()) cc.gameObject.AddComponent<RopeInteractor>();
                var spawn = GameObject.Find("RopeLab/Tests/Test01/Spawn");
                if (spawn) cc.transform.root.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
                // The player prefab brings its own camera; turn off the loose scene camera.
                foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (c.transform.root != cc.transform.root && c.CompareTag("MainCamera")) c.gameObject.SetActive(false);
            }
            else Debug.LogWarning("[RopeLab] No CharacterController player found — add RopeInteractor to your player manually.");

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeObject = root.gameObject;
            Debug.Log("[RopeLab] Built 6 rope tests. Press Play. Keys: E grab/drop, hold E coil, LMB throw, F tie/plug, Q climb, 1-6 teleport, R reset, H help.");
        }

        // ------------------------------------------------------------------ Tests
        static void Test1_DragAndDrape(Transform parent, float x0)
        {
            var t = Bay(parent, 1, x0, "Drag & Drape",
                "Grab the rope anywhere (E) and drag it around the pillars.\nDrop the free end inside the green square. Watch it drape and catch on edges.");
            var geo = t.Find("Geo");

            Box("Pillar_A", new Vector3(x0 + 8f, 1.5f, 11f), new Vector3(0.7f, 3f, 0.7f), _wall, geo);
            Box("Pillar_B", new Vector3(x0 + 12.5f, 1.5f, 13.5f), new Vector3(0.7f, 3f, 0.7f), _wall, geo);
            Box("Pillar_C", new Vector3(x0 + 15f, 1.5f, 8f), new Vector3(0.7f, 3f, 0.7f), _wall, geo);
            // Low bar to drape over + a knee-high ledge
            Box("Bar_PostL", new Vector3(x0 + 4f, 0.65f, 8f), new Vector3(0.2f, 1.3f, 0.2f), _wall, geo);
            Box("Bar_PostR", new Vector3(x0 + 8f, 0.65f, 8f), new Vector3(0.2f, 1.3f, 0.2f), _wall, geo);
            Box("Bar", new Vector3(x0 + 6f, 1.35f, 8f), new Vector3(4.2f, 0.12f, 0.12f), _accent, geo);
            Box("Ledge", new Vector3(x0 + 16.5f, 0.4f, 15.5f), new Vector3(4f, 0.8f, 3f), _wall, geo);

            var anchorPost = Box("AnchorPost", new Vector3(x0 + 4f, 0.4f, 15f), new Vector3(0.5f, 0.8f, 0.5f), _machine, geo);
            var anchor = Empty("RopeAnchor", t, new Vector3(x0 + 4f, 0.85f, 15f));

            var rope = MakeRope(t, "Rope", 14f, false, new Vector3(x0 + 4f, 0.85f, 15f), new Vector3(1f, 0f, -0.3f), anchor, null);
            var goal = GoalZone(t, "GoalZone", new Vector3(x0 + 12f, 0.5f, 5.5f), new Vector3(2.5f, 1.2f, 2.5f));

            // Loose rope near the entrance: practise gathering (hold E) and throwing.
            var loose = MakeRope(t, "LooseRope", 8f, false, new Vector3(x0 + 13f, 0.1f, 1.5f), Vector3.right, null, null);
            loose.layoutMeander = 80f;

            var zone = t.GetComponent<RopeTestZone>();
            zone.goal = TestGoal.RopeEndInZone; zone.rope = rope; zone.goalZone = goal;
            zone.ropesToReset = new[] { rope, loose };
        }

        static void Test2_ThrowOver(Transform parent, float x0)
        {
            var t = Bay(parent, 2, x0, "Throw Over",
                "The wall is too high to climb. Pick up the rope's free end,\nhold LMB to aim and throw it over the wall into the green zone.");
            var geo = t.Find("Geo");

            Box("HighWall", new Vector3(x0 + 10f, 1.6f, 9.5f), new Vector3(BayW - 0.4f, 3.2f, 0.5f), _wall, geo);
            Box("Crate_Step", new Vector3(x0 + 15f, 0.5f, 7.5f), new Vector3(1f, 1f, 1f), _crate, geo);
            Box("AnchorPost", new Vector3(x0 + 4f, 0.4f, 3f), new Vector3(0.5f, 0.8f, 0.5f), _machine, geo);
            var anchor = Empty("RopeAnchor", t, new Vector3(x0 + 4f, 0.85f, 3f));

            var rope = MakeRope(t, "Rope", 14f, false, new Vector3(x0 + 4f, 0.85f, 3f), Vector3.right, anchor, null);
            var goal = GoalZone(t, "GoalZone", new Vector3(x0 + 10f, 1f, 14f), new Vector3(12f, 2f, 7f));

            var zone = t.GetComponent<RopeTestZone>();
            zone.goal = TestGoal.RopeEndInZone; zone.rope = rope; zone.goalZone = goal;
            zone.ropesToReset = new[] { rope };
        }

        static void Test3_PowerCable(Transform parent, float x0)
        {
            var t = Bay(parent, 3, x0, "Power Cable",
                "The shutter needs power. Pick up the cable's plug end and route it\naround the wall to the socket (F). The cable is short — pick your path.");
            var geo = t.Find("Geo");

            Box("Partition", new Vector3(x0 + 10f, 1.5f, 13.5f), new Vector3(0.4f, 3f, 9f), _wall, geo);
            var gen = Box("Generator", new Vector3(x0 + 5f, 0.6f, 15.5f), new Vector3(1.6f, 1.2f, 1.1f), _machine, geo);
            Box("Generator_Top", new Vector3(x0 + 5f, 1.3f, 15.5f), new Vector3(1.2f, 0.2f, 0.8f), _dark, geo);
            var anchor = Empty("CableAnchor", t, new Vector3(x0 + 5.85f, 0.7f, 15.5f));

            // Electrical box on a post: green-grey housing, yellow socket plate, indicator lamp.
            // The socket faces -Z (toward the player); a plug goes in along +Z.
            float sx = x0 + 14.5f;
            Box("SocketPost", new Vector3(sx, 0.55f, 12.6f), new Vector3(0.12f, 1.1f, 0.12f), _wall, geo);
            Box("ElectricBox", new Vector3(sx, 1.25f, 12.48f), new Vector3(0.7f, 0.5f, 0.26f), _elecBox, geo);
            var plate = Box("SocketPlate", new Vector3(sx + 0.13f, 1.2f, 12.34f), new Vector3(0.18f, 0.18f, 0.02f), _plug, geo);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            Part(geo, PrimitiveType.Cylinder, "Receptacle", new Vector3(sx + 0.13f, 1.2f, 12.325f), new Vector3(0.075f, 0.012f, 0.075f), _dark);
            Part(geo, PrimitiveType.Cylinder, "DummyOutlet", new Vector3(sx - 0.14f, 1.2f, 12.34f), new Vector3(0.08f, 0.015f, 0.08f), _dark);
            var socketGo = Empty("Socket", t, new Vector3(sx + 0.13f, 1.2f, 12.315f));
            var lamp = Box("SocketLamp", new Vector3(sx + 0.13f, 1.38f, 12.34f), new Vector3(0.06f, 0.03f, 0.02f), _goal, socketGo.transform);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            var socket = socketGo.AddComponent<RopeSocket>();
            socket.indicator = lamp.GetComponent<Renderer>();

            // Shutter in the back wall + lights behind it.
            // Alcove against the back wall, closed by a powered shutter.
            Box("Alcove_L", new Vector3(x0 + 13.8f, 1.5f, 19f), new Vector3(0.4f, 3f, 2.4f), _wall, geo);
            Box("Alcove_R", new Vector3(x0 + 18.2f, 1.5f, 19f), new Vector3(0.4f, 3f, 2.4f), _wall, geo);
            Box("Alcove_Roof", new Vector3(x0 + 16f, 3.1f, 19f), new Vector3(4.8f, 0.2f, 2.4f), _wall, geo);
            var reward = Box("PowerCore", new Vector3(x0 + 16f, 0.75f, 19.2f), new Vector3(1f, 1.5f, 0.8f), _goal, geo);
            var door = Box("Shutter", new Vector3(x0 + 16f, 1.5f, 17.9f), new Vector3(4f, 3f, 0.2f), _accent, geo);
            var pd = door.gameObject.AddComponent<PoweredDoor>();
            var l = new GameObject("PowerLight").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.position = new Vector3(x0 + 16f, 2.4f, 18.8f);
            l.type = LightType.Point; l.range = 8f; l.intensity = 4f; l.color = new Color(0.5f, 1f, 0.6f);
            pd.lights = new[] { l };
            UnityEventTools.AddPersistentListener(socket.onPowered, pd.Open);
            UnityEventTools.AddPersistentListener(socket.onUnpowered, pd.Close);

            var cable = MakeRope(t, "Cable", 14f, true, anchor.transform.position, new Vector3(0.2f, 0f, -1f).normalized, anchor, null);

            var zone = t.GetComponent<RopeTestZone>();
            zone.goal = TestGoal.SocketPowered; zone.socket = socket;
            zone.ropesToReset = new[] { cable };
        }

        static void Test4_TieAndClimb(Transform parent, float x0)
        {
            var t = Bay(parent, 4, x0, "Tie & Climb",
                "Up the stairs. Hold E to coil the loose rope, tie it to the post (F),\nthrow it down into the pit, climb down (Q), touch the plate, climb back up.");
            var geo = t.Find("Geo");
            const float h = 4f;

            Box("Platform", new Vector3(x0 + 10f, h * 0.5f, 7.5f), new Vector3(BayW - 0.4f, h, 7f), _wall, geo);
            var stairs = ShapeGenerator.GenerateStair(PivotLocation.Center, new Vector3(3f, h, 8f), 16, true);
            stairs.name = "Stairs";
            stairs.transform.SetParent(geo, false);
            stairs.transform.position = new Vector3(x0 + 2.5f, h * 0.5f, 0f);
            Finish(stairs, _floor);
            stairs.gameObject.AddComponent<MeshCollider>();

            // Pit: enclosed by the platform, the back wall and the bay dividers.
            Box("Pit_BackWall", new Vector3(x0 + 10f, 3f, 18.2f), new Vector3(BayW, 6f, 0.4f), _wall, geo);
            Box("Pit_DividerL", new Vector3(x0 - 1f, 5f, 9f), new Vector3(0.4f, 2f, 18f), _wall, geo);
            Box("Pit_DividerR", new Vector3(x0 + BayW - 1f, 5f, 9f), new Vector3(0.4f, 2f, 18f), _wall, geo);
            Box("Lip_Trim", new Vector3(x0 + 10f, h + 0.05f, 10.95f), new Vector3(BayW - 0.4f, 0.1f, 0.1f), _accent, geo);

            var post = Box("TiePost", new Vector3(x0 + 10f, h + 0.55f, 9.6f), new Vector3(0.3f, 1.1f, 0.3f), _accent, geo);
            var tieGo = Empty("TiePoint", t, new Vector3(x0 + 10f, h + 0.75f, 9.6f));
            tieGo.AddComponent<RopeTiePoint>();

            var rope = MakeRope(t, "Rope", 10f, false, new Vector3(x0 + 5f, h + 0.1f, 7f), Vector3.right, null, null);
            rope.layoutMeander = 70f;   // loose, scattered rope to gather

            var plate = Box("PressurePlate", new Vector3(x0 + 14f, 0.03f, 15.5f), new Vector3(1.6f, 0.06f, 1.6f), _goal, geo);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            var goal = GoalZone(t, "GoalZone", new Vector3(x0 + 14f, 1f, 15.5f), new Vector3(1.6f, 2f, 1.6f));

            var zone = t.GetComponent<RopeTestZone>();
            zone.goal = TestGoal.PlayerInZone; zone.goalZone = goal;
            zone.ropesToReset = new[] { rope };
        }

        static void Test6_CableOverFence(Transform parent, float x0)
        {
            var t = Bay(parent, 6, x0, "Cable Over Fence",
                "The cable is tied to the generator and is too short to carry around the fence.\nThrow the plug over the fence (LMB), walk round through the gap, pick it up (E) and plug it in (F).");
            var geo = t.Find("Geo");

            // Tall fence across the bay with a walk-around gap at the far right.
            const float fz = 9f, fh = 3.2f, gapStart = 16.5f;
            float fenceLen = gapStart + 0.8f;
            Box("Fence", new Vector3(x0 - 0.8f + fenceLen * 0.5f, fh * 0.5f, fz), new Vector3(fenceLen, fh, 0.12f), _fence, geo);
            for (float px = -0.5f; px <= gapStart; px += 3f)
                Box("FencePost", new Vector3(x0 + px, (fh + 0.1f) * 0.5f, fz), new Vector3(0.16f, fh + 0.1f, 0.2f), _elecBox, geo);
            Box("FenceEndPost", new Vector3(x0 + gapStart, (fh + 0.1f) * 0.5f, fz), new Vector3(0.2f, fh + 0.1f, 0.24f), _elecBox, geo);

            // Generator on the near side; the cable is fixed to it for good.
            Box("Generator", new Vector3(x0 + 4f, 0.6f, 4f), new Vector3(1.6f, 1.2f, 1.1f), _machine, geo);
            Box("Generator_Top", new Vector3(x0 + 4f, 1.3f, 4f), new Vector3(1.2f, 0.2f, 0.8f), _dark, geo);
            var anchor = Empty("CableAnchor", t, new Vector3(x0 + 4.85f, 0.7f, 4f));
            var cable = MakeRope(t, "Cable", 14f, true, anchor.transform.position, new Vector3(1f, 0f, -0.4f).normalized, anchor, null);

            // Socket box on the far side, facing the fence.
            float sx = x0 + 4f, sz = 15.6f;
            Box("SocketPost", new Vector3(sx, 0.55f, sz + 0.12f), new Vector3(0.12f, 1.1f, 0.12f), _wall, geo);
            Box("ElectricBox", new Vector3(sx, 1.25f, sz), new Vector3(0.7f, 0.5f, 0.26f), _elecBox, geo);
            var plate = Box("SocketPlate", new Vector3(sx + 0.13f, 1.2f, sz - 0.14f), new Vector3(0.18f, 0.18f, 0.02f), _plug, geo);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            Part(geo, PrimitiveType.Cylinder, "Receptacle", new Vector3(sx + 0.13f, 1.2f, sz - 0.155f), new Vector3(0.075f, 0.012f, 0.075f), _dark);
            var socketGo = Empty("Socket", t, new Vector3(sx + 0.13f, 1.2f, sz - 0.165f));
            var lamp = Box("SocketLamp", new Vector3(sx + 0.13f, 1.38f, sz - 0.14f), new Vector3(0.06f, 0.03f, 0.02f), _goal, socketGo.transform);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            var socket = socketGo.AddComponent<RopeSocket>();
            socket.indicator = lamp.GetComponent<Renderer>();

            // Reward: a floodlight on the far side comes on when powered.
            var indicator = Box("PowerFlag", new Vector3(sx - 0.2f, 1.6f, sz), new Vector3(0.08f, 0.3f, 0.08f), _goal, geo);
            Object.DestroyImmediate(indicator.GetComponent<Collider>());
            var pd = indicator.gameObject.AddComponent<PoweredDoor>();
            pd.openOffset = new Vector3(0f, 0.35f, 0f);
            var l = new GameObject("FloodLight").AddComponent<Light>();
            l.transform.SetParent(t, false);
            l.transform.position = new Vector3(sx, 3.5f, sz - 2f);
            l.type = LightType.Point; l.range = 10f; l.intensity = 5f; l.color = new Color(0.6f, 0.9f, 1f);
            pd.lights = new[] { l };
            UnityEventTools.AddPersistentListener(socket.onPowered, pd.Open);
            UnityEventTools.AddPersistentListener(socket.onUnpowered, pd.Close);

            var zone = t.GetComponent<RopeTestZone>();
            zone.goal = TestGoal.SocketPowered; zone.socket = socket;
            zone.ropesToReset = new[] { cable };
        }

        const string DogPrefab = "Assets/PolygonDog/Prefabs/Dogs/Unity_SK_Animals_Dog_GermanShepherd_Collar_01.prefab";
        const string DogController = "Assets/PolygonDog/Animations/Animation_Dog.controller";

        /// <summary>Tools > Rope Lab > Add Leashed Dog — an ambient PolygonDog tied to a post in the hallway.</summary>
        [MenuItem("Tools/Rope Lab/Add Leashed Dog")]
        public static void AddLeashedDogMenu()
        {
            var root = GameObject.Find("RopeLab");
            if (!root) { Debug.LogWarning("[RopeLab] Build the test scene first."); return; }
            MakeMaterials();
            AddLeashedDog(root.transform);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        static void AddLeashedDog(Transform root)
        {
            var old = root.Find("LeashedDog");
            if (old) Object.DestroyImmediate(old.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DogPrefab);
            if (!prefab) { Debug.LogWarning("[RopeLab] PolygonDog prefab not found: " + DogPrefab); return; }

            var group = new GameObject("LeashedDog").transform;
            group.SetParent(root, false);

            // Post in the hallway, left of the Test 01 start.
            Vector3 postPos = new Vector3(3.5f, 0f, -6f);
            // The tie point sits just ABOVE the post top: a pinned node inside a collider makes the next
            // nodes get pushed out while the pin stays in, which stretches the rope.
            Box("LeashPost", postPos + Vector3.up * 0.4f, new Vector3(0.14f, 0.8f, 0.14f), _machine, group);
            var tieTop = Empty("LeashTie", group, postPos + Vector3.up * 0.84f);

            var dog = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            dog.name = "Dog";
            dog.transform.position = postPos + new Vector3(1.8f, 0f, 0.3f);
            dog.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            var anim = dog.GetComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DogController);
            anim.applyRootMotion = true;
            var ai = dog.AddComponent<DogOnLeash>();
            ai.post = tieTop.transform;
            ai.leashLength = 3.5f;

            // Leash end sits on the collar (neck bone).
            Transform neck = null;
            foreach (var tr in dog.GetComponentsInChildren<Transform>(true))
                if (tr.name == "neck_C0_2_joint") { neck = tr; break; }
            var collar = new GameObject("LeashAttach").transform;
            collar.SetParent(neck ? neck : dog.transform, false);
            if (!neck) collar.localPosition = new Vector3(0f, 0.55f, 0.45f);

            var leash = MakeRope(group, "Leash", 4f, false, tieTop.transform.position,
                                 (collar.position - tieTop.transform.position).normalized, tieTop, null);
            leash.layoutCoiled = false;
            leash.endAnchor = collar;
            leash.segmentLength = 0.12f;
        }

        static void Test5_PullCrate(Transform parent, float x0)
        {
            var t = Bay(parent, 5, x0, "Pull the Crate",
                "The rope is tied to a heavy crate. Grab it and pull the crate\ninto the green zone. Use the pole as a pulley to change direction.");
            var geo = t.Find("Geo");

            var crate = Box("Crate", new Vector3(x0 + 13f, 0.61f, 14f), new Vector3(1.2f, 1.2f, 1.2f), _crate, t);
            var rb = crate.gameObject.AddComponent<Rigidbody>();
            rb.mass = 40f; rb.linearDamping = 0.5f; rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var pm = new PhysicsMaterial("CrateFriction") { dynamicFriction = 0.5f, staticFriction = 0.6f };
            crate.GetComponent<Collider>().sharedMaterial = pm;

            Box("Pole", new Vector3(x0 + 7f, 1.25f, 13f), new Vector3(0.35f, 2.5f, 0.35f), _accent, geo);
            Box("LowWall", new Vector3(x0 + 11f, 0.5f, 9f), new Vector3(7f, 1f, 0.4f), _wall, geo);

            // Rope laid out so its last node sits on the crate's front face.
            Vector3 attachWorld = crate.transform.position + new Vector3(-0.65f, -0.25f, 0f);
            float len = 9f;
            var rope = MakeRope(t, "Rope", len, false, attachWorld + Vector3.left * len, Vector3.right, null, rb);
            rope.endRigidbodyLocalPoint = crate.transform.InverseTransformPoint(attachWorld);
            // Re-layout so the end lands on the crate: start = attach - dir * len
            rope.startPoint.position = attachWorld - Vector3.right * (Mathf.Round(len / rope.segmentLength) * rope.segmentLength);

            var goal = GoalZone(t, "GoalZone", new Vector3(x0 + 4f, 0.75f, 4f), new Vector3(3.5f, 1.5f, 3.5f));

            var zone = t.GetComponent<RopeTestZone>();
            zone.goal = TestGoal.BodyInZone; zone.goalBody = rb; zone.goalZone = goal;
            zone.ropesToReset = new[] { rope };
            zone.bodiesToReset = new[] { rb };
        }

        // ------------------------------------------------------------------ Builders
        static Transform Bay(Transform parent, int number, float x0, string title, string desc)
        {
            var go = new GameObject($"Test{number:00}");
            go.transform.SetParent(parent, false);
            var geo = Group("Geo", go.transform);

            var trig = go.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.center = new Vector3(x0 + 10f, 3f, 5f);
            trig.size = new Vector3(BayW - 0.6f, 6f, 30f);

            var zone = go.AddComponent<RopeTestZone>();
            zone.number = number; zone.title = title; zone.description = desc;
            zone.ropesToReset = new RopeSim[0]; zone.bodiesToReset = new Rigidbody[0];

            var spawn = Empty("Spawn", go.transform, new Vector3(x0 + 10f, 0.05f, -6f));
            zone.spawnPoint = spawn.transform;

            // Floor number marker + wall sign
            var marker = Box("BayMarker", new Vector3(x0 + 10f, 0.01f, -2f), new Vector3(3f, 0.02f, 0.4f), _accent, geo);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            var sign = new GameObject("Sign");
            sign.transform.SetParent(geo, false);
            sign.transform.position = new Vector3(x0 + 10f, 5.6f, 0.2f);
            var tm = sign.AddComponent<TextMesh>();
            tm.text = $"{number:00}  {title.ToUpperInvariant()}";
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.12f; tm.fontSize = 64; tm.color = new Color(0.95f, 0.78f, 0.35f);
            return go.transform;
        }

        static RopeSim MakeRope(Transform parent, string name, float length, bool cable, Vector3 start, Vector3 dir, GameObject anchor, Rigidbody endBody)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var startT = Empty(name + "_Start", go.transform, start).transform;
            var rope = go.AddComponent<RopeSim>();
            rope.length = length;
            rope.segmentLength = cable ? 0.12f : 0.15f;   // short segments = smooth, supple curves
            rope.radius = cable ? RopeLookSetup.CableRadius : RopeLookSetup.RopeRadius;
            rope.isCable = cable;
            rope.startPoint = startT;
            rope.layoutDirection = dir;
            rope.permanentAnchor = anchor ? anchor.transform : null;
            rope.endRigidbody = endBody;
            rope.layoutCoiled = endBody == null;       // starts as a coil dropped on the floor (not the crate rope)
            // Per-step stiffness (see RopeSim): small values = soft, flexible rope/cord.
            rope.bendStiffness = cable ? 0.05f : 0.03f;
            rope.freeBendDegPerMeter = cable ? 70f : 90f;

            var rr = go.AddComponent<RopeRenderer>();
            rr.material = cable ? _cable : _rope;
            rr.sides = 8;
            rr.subdivisions = 3;
            if (cable)
            {
                rr.endCap = MakePlug(go.transform);
                rope.endCollisionRadius = 0.032f;   // the plug is ~6 cm wide: keep it out of floors and walls
                rope.endMassScale = 3f;             // weighted end: leads the throw like a real plug
            }
            return rope;
        }

        /// <summary>
        /// Industrial cable plug built from primitives. Local +Z is the insertion direction; the tip sits at the
        /// origin (the cable's last node) and the body extends back along the cable.
        /// </summary>
        static Transform MakePlug(Transform parent)
        {
            var root = new GameObject("Plug").transform;
            root.SetParent(parent, false);
            Part(root, PrimitiveType.Cylinder, "Body", new Vector3(0, 0, -0.065f), new Vector3(0.056f, 0.05f, 0.056f), _plug);
            Part(root, PrimitiveType.Cylinder, "GripRing", new Vector3(0, 0, -0.1f), new Vector3(0.062f, 0.009f, 0.062f), _dark);
            Part(root, PrimitiveType.Cylinder, "Face", new Vector3(0, 0, -0.012f), new Vector3(0.05f, 0.006f, 0.05f), _dark);
            Part(root, PrimitiveType.Cylinder, "StrainRelief", new Vector3(0, 0, -0.135f), new Vector3(0.026f, 0.025f, 0.026f), _cable);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                Part(root, PrimitiveType.Cylinder, "Pin" + i, new Vector3(Mathf.Cos(a) * 0.013f, Mathf.Sin(a) * 0.013f, 0.004f),
                     new Vector3(0.006f, 0.01f, 0.006f), _brass);
            }
            return root;
        }

        static void Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            // Unity cylinders run along Y; lay them along Z (the plug axis).
            if (type == PrimitiveType.Cylinder) g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static BoxCollider GoalZone(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.size = size;
            // Visible floor square
            var decal = Box(name + "_Decal", new Vector3(center.x, 0.015f, center.z), new Vector3(size.x, 0.02f, size.z), _goalFloor, parent);
            Object.DestroyImmediate(decal.GetComponent<Collider>());
            if (center.y - size.y * 0.5f > 0.5f) decal.transform.position = new Vector3(center.x, center.y - size.y * 0.5f + 0.015f, center.z);
            return bc;
        }

        static ProBuilderMesh Box(string name, Vector3 center, Vector3 size, Material mat, Transform parent)
        {
            var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
            pb.name = name;
            pb.transform.SetParent(parent, false);
            pb.transform.position = center;
            Finish(pb, mat);
            pb.gameObject.AddComponent<BoxCollider>();
            return pb;
        }

        static void Finish(ProBuilderMesh pb, Material mat)
        {
            pb.SetMaterial(pb.faces, mat);
            pb.ToMesh();
            pb.Refresh();
            pb.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Empty(string name, Transform parent, Vector3 worldPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            return go;
        }

        static void MakeMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RopeLab")) AssetDatabase.CreateFolder("Assets", "RopeLab");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/RopeLab", "Materials");
            _floor = Mat("M_Floor", new Color(0.32f, 0.33f, 0.34f));
            _wall = Mat("M_Wall", new Color(0.62f, 0.62f, 0.6f));
            _accent = Mat("M_Accent", new Color(0.95f, 0.55f, 0.12f));
            _goal = Mat("M_Goal", new Color(0.2f, 0.85f, 0.35f), new Color(0.1f, 0.6f, 0.2f));
            _rope = Mat("M_Rope", new Color(0.62f, 0.47f, 0.28f), smooth: 0.1f);
            RopeLookSetup.ApplyRopeMaterial(_rope);    // twisted-strand texture if it has been generated
            // Worn off-white extension cord, yellow plug/socket plate, green-grey electrical box.
            _cable = Mat("M_Cable", RopeLookSetup.RopeColor, smooth: 0.35f);
            _plug = Mat("M_Plug", new Color(0.93f, 0.76f, 0.12f), smooth: 0.4f);
            _brass = Mat("M_Brass", new Color(0.78f, 0.62f, 0.3f), smooth: 0.6f);
            _elecBox = Mat("M_ElectricBox", new Color(0.29f, 0.34f, 0.29f), smooth: 0.3f);
            _machine = Mat("M_Machine", new Color(0.2f, 0.33f, 0.45f));
            _crate = Mat("M_Crate", new Color(0.55f, 0.36f, 0.2f));
            _dark = Mat("M_Dark", new Color(0.12f, 0.12f, 0.12f));
            _goalFloor = _goal;
            _fence = _dark;

            // Level blockout uses the Gridbox Prototype (URP) materials when present — used as-is, never modified.
            // Rope, cable, plug, brass and indicator lamps keep their own materials (their colours change at runtime).
            _floor = Grid("Grey2", _floor);
            _wall = Grid("Grey1", _wall);
            _accent = Grid("Orange", _accent);
            _machine = Grid("Blue2", _machine);
            _crate = Grid("Brown", _crate);
            _elecBox = Grid("Olive", _elecBox);
            _goalFloor = Grid("Green2", _goal);
            _fence = Grid("Grey4", _dark);
        }

        static Material _fence;

        static Material Grid(string colour, Material fallback)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(GridDir + "Prototype_512x512_" + colour + ".mat");
            return m ? m : fallback;
        }

        static Material Mat(string name, Color c, Color? emission = null, float smooth = 0.25f)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
