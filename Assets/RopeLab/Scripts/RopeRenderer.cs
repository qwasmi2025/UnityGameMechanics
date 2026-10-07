using UnityEngine;

namespace RopeLab
{
    /// <summary>Builds a smooth tube mesh through the rope nodes every frame (Catmull-Rom, parallel-transport frames).</summary>
    [RequireComponent(typeof(RopeSim))]
    [DefaultExecutionOrder(200)]
    public class RopeRenderer : MonoBehaviour
    {
        public Material material;
        public int sides = 6;
        public int subdivisions = 2;
        public float radiusScale = 1f;
        [Tooltip("Optional mesh shown at the last node (cable plug).")]
        public Transform endCap;

        RopeSim _rope;
        Mesh _mesh;
        Vector3[] _pts, _verts, _norms;
        Vector2[] _uvs;
        int[] _tris;
        GameObject _meshGo;
        bool _trisSet;
        int _lastVersion = -1;

        void Start()
        {
            _rope = GetComponent<RopeSim>();
            _meshGo = new GameObject(name + "_Mesh");
            _meshGo.transform.SetParent(transform, false);
            _meshGo.AddComponent<MeshFilter>().sharedMesh = _mesh = new Mesh { name = "RopeTube" };
            _mesh.MarkDynamic();
            var mr = _meshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Allocate();
        }

        void Allocate()
        {
            int n = _rope.NodeCount;
            int rings = (n - 1) * subdivisions + 1;
            _pts = new Vector3[rings];
            _verts = new Vector3[rings * (sides + 1)];
            _norms = new Vector3[_verts.Length];
            _uvs = new Vector2[_verts.Length];
            _tris = new int[(rings - 1) * sides * 6];
            int t = 0;
            for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * (sides + 1) + s, b = a + sides + 1;
                _tris[t++] = a; _tris[t++] = b; _tris[t++] = a + 1;
                _tris[t++] = a + 1; _tris[t++] = b; _tris[t++] = b + 1;
            }
        }

        static readonly Unity.Profiling.ProfilerMarker s_meshMarker = new Unity.Profiling.ProfilerMarker("RopeLab.Mesh");

        void LateUpdate()
        {
            using var _m = s_meshMarker.Auto();
            if (_rope == null || _rope.Positions == null) return;
            // Nothing moved since the last mesh (sleeping rope): keep the mesh as is.
            if (_rope.Version == _lastVersion) return;
            _lastVersion = _rope.Version;
            var p = _rope.Positions;
            int n = _rope.NodeCount;
            _meshGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _meshGo.transform.localScale = Vector3.one;
            if (transform.lossyScale != Vector3.one) _meshGo.transform.SetParent(null, true);

            int k = 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, n - 1)];
                // At sharp bends (rope wrapped over an edge) a spline swings inside the corner and the tube
                // disappears into the geometry. There, follow the simulated polyline instead, which the solver
                // keeps outside the surface.
                float bend = Mathf.Max(Vector3.Angle(p1 - p0, p2 - p1), Vector3.Angle(p2 - p1, p3 - p2));
                float toLinear = Mathf.Clamp01((bend - 20f) / 40f);
                for (int s = 0; s < subdivisions; s++)
                {
                    float t = s / (float)subdivisions;
                    Vector3 smooth = CatmullRom(p0, p1, p2, p3, t);
                    _pts[k++] = toLinear > 0f ? Vector3.Lerp(smooth, Vector3.Lerp(p1, p2, t), toLinear) : smooth;
                }
            }
            _pts[k] = p[n - 1];

            float r = _rope.radius * radiusScale;
            Vector3 tangent = (_pts[1] - _pts[0]).normalized;
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float v = 0f;
            for (int ring = 0; ring < _pts.Length; ring++)
            {
                Vector3 next = ring < _pts.Length - 1 ? _pts[ring + 1] : _pts[ring] + tangent;
                Vector3 prev = ring > 0 ? _pts[ring - 1] : _pts[ring] - tangent;
                Vector3 newT = (next - prev);
                if (newT.sqrMagnitude > 1e-10f)
                {
                    newT.Normalize();
                    normal = Vector3.ProjectOnPlane(normal, newT);          // parallel transport
                    if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(newT, Vector3.up);
                    normal.Normalize();
                    tangent = newT;
                }
                Vector3 bin = Vector3.Cross(tangent, normal);
                if (ring > 0) v += Vector3.Distance(_pts[ring], _pts[ring - 1]) / (r * 12f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    Vector3 dir = normal * Mathf.Cos(a) + bin * Mathf.Sin(a);
                    int idx = ring * (sides + 1) + s;
                    _verts[idx] = _pts[ring] + dir * r;
                    _norms[idx] = dir;
                    _uvs[idx] = new Vector2(s / (float)sides, v);
                }
            }
            _mesh.vertices = _verts;
            _mesh.normals = _norms;
            _mesh.uv = _uvs;
            if (!_trisSet) { _mesh.triangles = _tris; _trisSet = true; }
            _mesh.RecalculateBounds();

            if (endCap)
            {
                endCap.position = p[n - 1];
                var pin = _rope.GetPin(n - 1);
                if (pin.kind == PinKind.Socket && pin.target)
                    endCap.rotation = pin.target.rotation;          // seated straight in the socket
                else
                {
                    // Ease toward the cable direction: following the last segment exactly made the plug spin
                    // wildly while a thrown bundle unwinds.
                    Vector3 dir = (p[n - 1] - p[n - 2]);
                    if (dir.sqrMagnitude > 1e-8f)
                    {
                        var target = Quaternion.LookRotation(dir.normalized);
                        endCap.rotation = Quaternion.Slerp(endCap.rotation, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
                    }
                }
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
