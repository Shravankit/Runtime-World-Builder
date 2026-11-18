using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    public sealed class LakeData
    {
        public Vector2 Center;               // world X,Z
        public float Radius, Level, Depth, Seed;

        /// Organic shoreline: radius varies smoothly with angle (seamless around the circle).
        public float RadiusAt(float ang)
        {
            float n = Mathf.PerlinNoise(Seed + 5f + Mathf.Cos(ang) * 1.5f, Seed + 5f + Mathf.Sin(ang) * 1.5f);
            return Radius * (1f + (n - 0.5f) * 0.6f);
        }
    }

    public sealed class RiverData
    {
        public List<Vector3> Points = new();  // x/z world, y = water surface (metres)
        public float HalfWidth;
        public Vector2 Min, Max;               // XZ bounds of Points, set by WaterRegistry.Add

        public bool NearBounds(float x, float z, float margin)
        {
            float m = HalfWidth + margin;
            return x >= Min.x - m && x <= Max.x + m && z >= Min.y - m && z <= Max.y + m;
        }
    }

    /// Lets the road builder ask "is there water here?" so it can decide to place a bridge.
    public static class WaterRegistry
    {
        static readonly List<LakeData> lakes = new();
        static readonly List<RiverData> rivers = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        public static void Add(LakeData l) => lakes.Add(l);
        public static void Add(RiverData r)
        {
            r.Min = new Vector2(float.MaxValue, float.MaxValue);
            r.Max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in r.Points)
            {
                r.Min = Vector2.Min(r.Min, new Vector2(p.x, p.z));
                r.Max = Vector2.Max(r.Max, new Vector2(p.x, p.z));
            }
            rivers.Add(r);
        }
        public static void Clear() { lakes.Clear(); rivers.Clear(); }

        public static bool TryGet(float x, float z, float margin, out float level)
        {
            level = float.MinValue;
            bool hit = false;

            foreach (var l in lakes)
            {
                float dx = x - l.Center.x, dz = z - l.Center.y;
                float quick = l.Radius * 1.3f + margin;           // cheap reject before the Perlin lookup
                if (dx * dx + dz * dz > quick * quick) continue;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= l.RadiusAt(Mathf.Atan2(dz, dx)) + margin) { level = Mathf.Max(level, l.Level); hit = true; }
            }

            foreach (var r in rivers)
                if (r.NearBounds(x, z, margin))
                    for (int i = 0; i < r.Points.Count - 1; i++)
                    {
                        Vector3 a = r.Points[i], b = r.Points[i + 1];
                        float abx = b.x - a.x, abz = b.z - a.z, len2 = abx * abx + abz * abz;
                        float t = len2 > 1e-8f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.z) * abz) / len2) : 0f;
                        float dx = x - (a.x + abx * t), dz = z - (a.z + abz * t);
                        if (dx * dx + dz * dz <= (r.HalfWidth + margin) * (r.HalfWidth + margin))
                        {
                            level = Mathf.Max(level, Mathf.Lerp(a.y, b.y, t));
                            hit = true;
                        }
                    }
            return hit;
        }
    }

    /// Assign real materials in the inspector. Fallbacks are created for quick testing (URP Lit).
    [Serializable]
    public class FeatureMaterials
    {
        public Material road, bridge, water;

        public Material Road => road != null ? road : road = Make(new Color(0.18f, 0.18f, 0.2f), false);
        public Material Bridge => bridge != null ? bridge : bridge = Make(new Color(0.55f, 0.55f, 0.52f), false);
        public Material Water => water != null ? water : water = Make(new Color(0.1f, 0.35f, 0.5f, 0.65f), true);

        static Material Make(Color c, bool transparent)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", transparent ? 0.95f : 0.15f);

            if (transparent && sh.name.Contains("Universal"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return m;
        }
    }
}