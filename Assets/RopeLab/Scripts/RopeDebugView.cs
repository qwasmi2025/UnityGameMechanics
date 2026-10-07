using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RopeLab
{
    /// <summary>
    /// Press G to show every rope node in the Game view (no Gizmos button needed), colour-coded:
    /// yellow = pinned (hand, tie, anchor, socket, coil), red = touching geometry, cyan = free / in the air,
    /// violet = rope asleep (resting, not simulated). A small legend appears bottom-left.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class RopeDebugView : MonoBehaviour
    {
        public Key toggleKey = Key.G;
        public bool show;
        public float nodeSize = 0.05f;

        static readonly Color Pinned = new Color(1f, 0.85f, 0.1f);
        static readonly Color Contact = new Color(1f, 0.25f, 0.2f);
        static readonly Color Free = new Color(0.2f, 0.9f, 1f);
        static readonly Color Asleep = new Color(0.85f, 0.35f, 1f);   // violet: stands out on the grey Gridbox floor

        Mesh _sphere;
        readonly Material[] _mats = new Material[4];
        readonly List<Matrix4x4>[] _batches = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        RopeSim[] _ropes;
        GUIStyle _label;

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Color[] cols = { Pinned, Contact, Free, Asleep };
            for (int i = 0; i < 4; i++)
            {
                _mats[i] = new Material(shader) { enableInstancing = true };
                _mats[i].SetColor("_BaseColor", cols[i]);
                _mats[i].color = cols[i];
            }
            _ropes = FindObjectsByType<RopeSim>(FindObjectsSortMode.None);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame) show = !show;
        }

        void LateUpdate()
        {
            if (!show || _sphere == null) return;
            foreach (var b in _batches) b.Clear();
            Vector3 scale = Vector3.one * nodeSize;
            foreach (var r in _ropes)
            {
                if (!r || r.Positions == null) continue;
                bool asleep = r.IsSleeping;
                for (int i = 0; i < r.NodeCount; i++)
                {
                    int c = r.IsPinned(i) ? 0 : asleep ? 3 : r.InContact(i) ? 1 : 2;
                    _batches[c].Add(Matrix4x4.TRS(r.GetNode(i), Quaternion.identity, scale));
                }
            }
            for (int c = 0; c < 4; c++)
            {
                var list = _batches[c];
                if (list.Count == 0) continue;
                var rp = new RenderParams(_mats[c]) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                for (int start = 0; start < list.Count; start += 1023)
                {
                    int count = Mathf.Min(1023, list.Count - start);
                    Graphics.RenderMeshInstanced(rp, _sphere, 0, list, count, start);
                }
            }
        }

        void OnGUI()
        {
            if (!show) return;
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            float x = 20f, y = Screen.height - 120f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 8, y - 8, 350, 108), Texture2D.whiteTexture);
            Row(ref y, x, Pinned, "Pinned (hand / tie / anchor / socket)");
            Row(ref y, x, Contact, "Touching geometry");
            Row(ref y, x, Free, "Free / in the air");
            Row(ref y, x, Asleep, "Rope asleep (not simulated)");
            GUI.color = Color.white;
        }

        void Row(ref float y, float x, Color c, string text)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y + 4, 14, 14), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 22, y, 320, 22), text, _label);
            y += 24f;
        }
    }
}
