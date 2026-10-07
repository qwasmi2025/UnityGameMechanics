#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PlagueRats.EditorTools
{
    /// <summary>
    /// Builds an AnimatorController from a set of loose AnimationClips and attaches an
    /// Animator to a model prefab, so the prefab becomes valid input for GPU Instancer
    /// Pro - Crowd Animations (which requires an Animator to discover & bake clips).
    ///
    /// The rat model ships with 6 in-place clips: Idle, Idle 2, Death, Attack 1, Run, Hit.
    /// This tool auto-detects them (by name, fuzzy) or lets you assign them manually,
    /// then generates a controller and wires everything up.
    ///
    /// Two controller modes:
    ///   - "GPUI (flat states)"     : every clip is a standalone state, no transitions.
    ///        This is all GPUI Compute Animator needs (clips are played from code via
    ///        StartAnimation). Recommended for the swarm.
    ///   - "State machine"          : Idle/Run loop with bool 'Moving', plus triggers
    ///        'Attack','Hit','Death'. Use if you ever drive a single rat with Mecanim.
    ///
    /// Menu: Tools > Plague Rats > Rat Animator Builder
    /// </summary>
    public class RatAnimatorBuilder : EditorWindow
    {
        GameObject modelPrefab;
        string outputFolder = "Assets/PlagueRats/Data";

        // the 6 clips
        AnimationClip clipIdle, clipIdle2, clipRun, clipAttack, clipHit, clipDeath;

        enum Mode { GpuiFlatStates, StateMachine }
        Mode mode = Mode.GpuiFlatStates;

        bool attachAnimatorToPrefab = true;
        bool addCrowdInstanceHint = true;

        [MenuItem("Tools/Plague Rats/Rat Animator Builder")]
        static void Open()
        {
            var w = GetWindow<RatAnimatorBuilder>("Rat Animator Builder");
            w.minSize = new Vector2(420, 520);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Rat Animator Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds an AnimatorController from the 6 rat clips and attaches an Animator " +
                "to the prefab, so GPU Instancer Pro - Crowd Animations can bake them.\n\n" +
                "1) Assign the rat model/prefab.\n" +
                "2) Auto-Detect (or assign the 6 clips manually).\n" +
                "3) Build.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            modelPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Rat Model / Prefab", modelPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && modelPrefab != null)
                AutoDetectClips();

            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            mode = (Mode)EditorGUILayout.EnumPopup("Controller Mode", mode);
            attachAnimatorToPrefab = EditorGUILayout.Toggle("Attach Animator to Prefab", attachAnimatorToPrefab);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            if (GUILayout.Button("Auto-Detect Clips from Prefab/Model"))
                AutoDetectClips();

            clipIdle   = ClipField("Idle",     clipIdle);
            clipIdle2  = ClipField("Idle 2",   clipIdle2);
            clipRun    = ClipField("Run",      clipRun);
            clipAttack = ClipField("Attack 1", clipAttack);
            clipHit    = ClipField("Hit",      clipHit);
            clipDeath  = ClipField("Death",    clipDeath);

            EditorGUILayout.Space();

            int assigned = CountAssigned();
            EditorGUILayout.LabelField($"Assigned clips: {assigned} / 6");

            GUI.enabled = modelPrefab != null && assigned > 0;
            if (GUILayout.Button("Build Animator Controller", GUILayout.Height(36)))
                Build();
            GUI.enabled = true;

            if (mode == Mode.GpuiFlatStates)
                EditorGUILayout.HelpBox(
                    "GPUI mode: each clip becomes a standalone state. Clips are played from " +
                    "code (RatDirector -> StartAnimation). This is what the swarm uses.",
                    MessageType.None);
            else
                EditorGUILayout.HelpBox(
                    "State-machine mode: Idle/Run via bool 'Moving'; 'Attack','Hit','Death' " +
                    "are triggers. For driving a single rat with Mecanim.", MessageType.None);
        }

        AnimationClip ClipField(string label, AnimationClip c) =>
            (AnimationClip)EditorGUILayout.ObjectField(label, c, typeof(AnimationClip), false);

        int CountAssigned()
        {
            int n = 0;
            if (clipIdle) n++; if (clipIdle2) n++; if (clipRun) n++;
            if (clipAttack) n++; if (clipHit) n++; if (clipDeath) n++;
            return n;
        }

        void AutoDetectClips()
        {
            if (modelPrefab == null) return;

            var found = new List<AnimationClip>();
            // from the model asset itself
            string path = AssetDatabase.GetAssetPath(modelPrefab);
            if (!string.IsNullOrEmpty(path))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview"))
                        found.Add(c);

            // also any clips sitting in the same folder
            if (!string.IsNullOrEmpty(path))
            {
                string dir = Path.GetDirectoryName(path);
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { dir }))
                {
                    var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    if (c != null && !found.Contains(c) && !c.name.StartsWith("__preview"))
                        found.Add(c);
                }
            }

            // fuzzy match by name
            foreach (var c in found)
            {
                string n = c.name.ToLowerInvariant();
                if (n.Contains("idle") && (n.Contains("2") || n.Contains("ii") || n.Contains("b")))
                    clipIdle2 = clipIdle2 ? clipIdle2 : c;
                else if (n.Contains("idle"))
                    clipIdle = clipIdle ? clipIdle : c;
                else if (n.Contains("run") || n.Contains("walk") || n.Contains("move"))
                    clipRun = clipRun ? clipRun : c;
                else if (n.Contains("attack") || n.Contains("bite"))
                    clipAttack = clipAttack ? clipAttack : c;
                else if (n.Contains("hit") || n.Contains("damage") || n.Contains("hurt"))
                    clipHit = clipHit ? clipHit : c;
                else if (n.Contains("death") || n.Contains("die") || n.Contains("dead"))
                    clipDeath = clipDeath ? clipDeath : c;
            }

            Debug.Log($"[RatAnimatorBuilder] Auto-detected from {found.Count} clips found near the model.");
            Repaint();
        }

        void Build()
        {
            EnsureFolder(outputFolder);
            string ctrlPath = $"{outputFolder}/{modelPrefab.name}_Animator.controller";

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = controller.layers[0].stateMachine;

            // collect (name -> clip, loop) in a stable order
            var clips = new List<(string name, AnimationClip clip, bool loop)>
            {
                ("Idle",     clipIdle,   true),
                ("Idle 2",   clipIdle2,  true),
                ("Run",      clipRun,    true),
                ("Attack 1", clipAttack, false),
                ("Hit",      clipHit,    false),
                ("Death",    clipDeath,  false),
            };
            clips.RemoveAll(t => t.clip == null);

            // set each clip's import loop flag to match (so Mecanim + baking behave)
            foreach (var (name, clip, loop) in clips)
                SetClipLoop(clip, loop);

            if (mode == Mode.GpuiFlatStates)
                BuildFlatStates(sm, clips);
            else
                BuildStateMachine(controller, sm, clips);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            if (attachAnimatorToPrefab)
                AttachAnimator(controller);

            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(controller);
            Debug.Log($"[RatAnimatorBuilder] Built '{ctrlPath}' with {clips.Count} clips " +
                      $"(mode: {mode}). " +
                      (attachAnimatorToPrefab ? "Animator attached to prefab." : ""));
        }

        // ---- GPUI flat states: each clip is its own state, no transitions ----
        void BuildFlatStates(AnimatorStateMachine sm, List<(string name, AnimationClip clip, bool loop)> clips)
        {
            float y = 0;
            foreach (var (name, clip, loop) in clips)
            {
                var st = sm.AddState(name, new Vector3(300, y, 0));
                st.motion = clip;
                st.writeDefaultValues = true;
                y += 60;
                if (name == "Idle") sm.defaultState = st;
            }
        }

        // ---- Mecanim state machine: Idle/Run + triggers ----
        void BuildStateMachine(AnimatorController controller, AnimatorStateMachine sm,
                               List<(string name, AnimationClip clip, bool loop)> clips)
        {
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit",    AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death",  AnimatorControllerParameterType.Trigger);

            AnimatorState idle = null, run = null, attack = null, hit = null, death = null;
            foreach (var (name, clip, loop) in clips)
            {
                var st = sm.AddState(name.Replace(" ", ""), RandPos());
                st.motion = clip;
                switch (name)
                {
                    case "Idle":     idle = st; sm.defaultState = st; break;
                    case "Run":      run = st; break;
                    case "Attack 1": attack = st; break;
                    case "Hit":      hit = st; break;
                    case "Death":    death = st; break;
                }
            }

            if (idle && run)
            {
                var toRun = idle.AddTransition(run);
                toRun.AddCondition(AnimatorConditionMode.If, 0, "Moving");
                toRun.hasExitTime = false; toRun.duration = 0.1f;

                var toIdle = run.AddTransition(idle);
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");
                toIdle.hasExitTime = false; toIdle.duration = 0.1f;
            }

            // triggers from Any State
            if (attack) AddAnyStateTrigger(sm, attack, "Attack", returnTo: idle);
            if (hit)    AddAnyStateTrigger(sm, hit,    "Hit",    returnTo: idle);
            if (death)  AddAnyStateTrigger(sm, death,  "Death",  returnTo: null);
        }

        void AddAnyStateTrigger(AnimatorStateMachine sm, AnimatorState target, string trigger,
                                AnimatorState returnTo)
        {
            var t = sm.AddAnyStateTransition(target);
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.hasExitTime = false; t.duration = 0.05f;
            t.canTransitionToSelf = false;

            if (returnTo != null)
            {
                var back = target.AddTransition(returnTo);
                back.hasExitTime = true; back.exitTime = 0.95f; back.duration = 0.1f;
            }
        }

        Vector3 RandPos() => new Vector3(Random.Range(250, 500), Random.Range(0, 360), 0);

        void AttachAnimator(AnimatorController controller)
        {
            // Operate on the prefab contents
            string prefabPath = AssetDatabase.GetAssetPath(modelPrefab);
            bool isModelAsset = (PrefabUtility.GetPrefabAssetType(modelPrefab) == PrefabAssetType.Model);

            if (isModelAsset)
            {
                // can't add components to an imported model prefab directly:
                // create a user prefab variant/copy first.
                string variantPath = $"{outputFolder}/{modelPrefab.name}_RatCrowd.prefab";
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                var anim = instance.GetComponent<Animator>();
                if (anim == null) anim = instance.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                Object.DestroyImmediate(instance);
                EditorGUIUtility.PingObject(saved);
                Debug.Log($"[RatAnimatorBuilder] Created user prefab '{variantPath}' with Animator. " +
                          "Use THIS prefab in RatDirector (add GPUI Crowd Instance to it).");
            }
            else
            {
                // editable user prefab: open, modify, save
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                var anim = root.GetComponent<Animator>();
                if (anim == null) anim = root.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log("[RatAnimatorBuilder] Animator attached to prefab.");
            }
        }

        static void SetClipLoop(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime == loop) return;
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
