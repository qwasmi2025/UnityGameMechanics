using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RopeLab.EditorTools
{
    /// <summary>
    /// Tools > Rope Lab > Upgrade Rope Look
    /// Generates a tileable 3-strand twisted rope texture (+ normal map from its height), applies it to M_Rope,
    /// and makes every rope in the open scene thinner and smoother.
    /// </summary>
    public static class RopeLookSetup
    {
        const string Dir = "Assets/RopeLab/Materials";
        const string AlbedoPath = Dir + "/T_Rope_Albedo.png";
        const string HeightPath = Dir + "/T_Rope_Normal.png";

        public const float RopeRadius = 0.012f;    // ~2.4 cm thick, like a climbing/utility rope
        public const float CableRadius = 0.009f;
        public static readonly Color RopeColor = new Color(0.45f, 0.78f, 1.0f);   // sky blue (rope and cable)

        [MenuItem("Tools/Rope Lab/Upgrade Rope Look")]
        public static string Upgrade()
        {
            MakeTextures();
            var ropeMat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/M_Rope.mat");
            if (ropeMat) ApplyRopeMaterial(ropeMat);
            var cableMat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/M_Cable.mat");
            if (cableMat)
            {
                cableMat.SetColor("_BaseColor", RopeColor);
                cableMat.color = RopeColor;
                EditorUtility.SetDirty(cableMat);
            }

            int count = 0;
            foreach (var r in Object.FindObjectsByType<RopeSim>(FindObjectsSortMode.None))
            {
                Undo.RecordObject(r, "Rope look");
                r.radius = r.isCable ? CableRadius : RopeRadius;
                var rr = r.GetComponent<RopeRenderer>();
                if (rr)
                {
                    Undo.RecordObject(rr, "Rope look");
                    rr.sides = 8;
                    rr.subdivisions = 3;
                    rr.radiusScale = 1f;
                }
                count++;
            }
            if (count > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            return $"Upgraded {count} ropes, material {(ropeMat ? "updated" : "missing")}";
        }

        public static void ApplyRopeMaterial(Material m)
        {
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(HeightPath);
            m.SetColor("_BaseColor", Color.white);
            m.color = Color.white;
            if (albedo) { m.SetTexture("_BaseMap", albedo); m.mainTexture = albedo; }
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1.2f);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ Texture
        static void MakeTextures()
        {
            const int W = 128, H = 256;       // u = around the rope, v = along it
            var albedo = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var height = new Texture2D(W, H, TextureFormat.RGBA32, true);
            // Sky-blue synthetic rope: bright strand crowns, deeper blue in the grooves.
            Color hemp = RopeColor;
            Color dark = new Color(0.08f, 0.26f, 0.40f);
            var rng = new System.Random(7);
            var noise = new float[W * H];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)rng.NextDouble();

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                // Three strands winding as a helix: the strand boundary moves across u as v increases.
                float s = Frac(u + v) * 3f;
                float across = Frac(s);
                float strand = Mathf.Sin(across * Mathf.PI);            // 0 in the groove, 1 on the crown
                float crown = Mathf.Pow(strand, 0.6f);
                // Fibres inside each strand, twisted the opposite way (integer frequencies keep it tileable).
                float fibre = 0.5f + 0.5f * Mathf.Sin((u * 3f - v * 2f) * Mathf.PI * 2f * 9f + noise[y * W + x] * 1.5f);
                float grain = noise[((y * 7) % H) * W + (x * 3) % W] * 0.12f;

                float h = crown * 0.85f + fibre * 0.15f * crown;
                Color c = Color.Lerp(dark, hemp, 0.25f + 0.75f * crown) * (0.88f + 0.12f * fibre) * (0.94f + grain);
                c.a = 1f;
                albedo.SetPixel(x, y, c);
                height.SetPixel(x, y, new Color(h, h, h, 1f));
            }
            albedo.Apply(); height.Apply();
            File.WriteAllBytes(AlbedoPath, albedo.EncodeToPNG());
            File.WriteAllBytes(HeightPath, height.EncodeToPNG());
            Object.DestroyImmediate(albedo); Object.DestroyImmediate(height);

            AssetDatabase.ImportAsset(AlbedoPath);
            AssetDatabase.ImportAsset(HeightPath);

            var ai = (TextureImporter)AssetImporter.GetAtPath(AlbedoPath);
            ai.textureType = TextureImporterType.Default;
            ai.sRGBTexture = true;
            ai.wrapMode = TextureWrapMode.Repeat;
            ai.mipmapEnabled = true;
            ai.anisoLevel = 4;
            ai.SaveAndReimport();

            var hi = (TextureImporter)AssetImporter.GetAtPath(HeightPath);
            hi.textureType = TextureImporterType.NormalMap;
            hi.convertToNormalmap = true;
            hi.heightmapScale = 0.12f;
            hi.normalmapFilter = TextureImporterNormalFilter.Standard;
            hi.wrapMode = TextureWrapMode.Repeat;
            hi.SaveAndReimport();
        }

        static float Frac(float f) => f - Mathf.Floor(f);
    }
}
