using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Stamp
{
    public enum StampBlend
    {
        Add,
        Replace,
        Min,
        Max
    }
    [Serializable]
    public class HeightmapStamp
    {
        public Texture2D heightmap;          // needs Read/Write enabled, sRGB off
        public Vector2 position;             // world X,Z of the stamp center
        public float yawDegrees;             // rotation around Y
        public Vector2 size = new(200, 200); // footprint in meters (X, Z)
        public float heightMeters = 50f;     // white pixel = this many meters
        public float baseY = 0f;             // target height in meters for Replace/Max/Min
        public StampBlend blend = StampBlend.Add;
        [Range(0f, 0.5f)] public float edgeFalloff = 0.15f; // soft border, fraction of size
        [Range(0f, 1f)] public float strength = 1f;
        public bool invert;

        [NonSerialized] float[] cache;
        [NonSerialized] int cw, ch;
        [NonSerialized] float cos = 1f, sin;

        sealed class HeightCache { public float[] data; public int w, h; }
        static readonly Dictionary<Texture2D, HeightCache> shared = new();
        const int MaxCacheDim = 1024;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => shared.Clear();

        public static void Warm(Texture2D tex) { if (tex != null) GetShared(tex); }

        static HeightCache GetShared(Texture2D tex)
        {
            if (shared.TryGetValue(tex, out var hc)) return hc;

            int mip = 0, w = tex.width, h = tex.height;
            while (Mathf.Max(w, h) > MaxCacheDim && mip < tex.mipmapCount - 1)
            {
                mip++; w = Mathf.Max(1, w >> 1); h = Mathf.Max(1, h >> 1);
            }
            var px = tex.GetPixels(mip);
            var data = new float[px.Length];
            for (int i = 0; i < px.Length; i++) data[i] = px[i].r;

            return shared[tex] = new HeightCache { data = data, w = w, h = h };
        }
        public void BuildCache()
        {
            if (cache != null || heightmap == null) return;
            var hc = GetShared(heightmap);
            cache = hc.data; cw = hc.w; ch = hc.h;
        }

        public float Sample(float u, float v)        // bilinear, u,v in 0..1
        {
            float fx = Mathf.Clamp01(u) * (cw - 1), fz = Mathf.Clamp01(v) * (ch - 1);
            int x0 = (int)fx, z0 = (int)fz;
            int x1 = Mathf.Min(x0 + 1, cw - 1), z1 = Mathf.Min(z0 + 1, ch - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(cache[z0 * cw + x0], cache[z0 * cw + x1], tx);
            float b = Mathf.Lerp(cache[z1 * cw + x0], cache[z1 * cw + x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        public void GetWorldBounds(out Vector2 min, out Vector2 max)
        {
            float r = yawDegrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            float ex = Mathf.Abs(hx * c) + Mathf.Abs(hz * s);
            float ez = Mathf.Abs(hx * s) + Mathf.Abs(hz * c);
            min = position - new Vector2(ex, ez);
            max = position + new Vector2(ex, ez);
        }


        public void Prepare()
        {
            BuildCache();
            float r = yawDegrees * Mathf.Deg2Rad;
            cos = Mathf.Cos(r); sin = Mathf.Sin(r);
        }

        public bool TryEvaluate(float wx, float wz, float cur01, float maxH, out float result01, out float weight)
        {
            result01 = cur01; weight = 0f;

            float dx = wx - position.x, dz = wz - position.y;
            float lx = dx * cos - dz * sin;
            float lz = dx * sin + dz * cos;
            float u = lx / size.x + 0.5f, v = lz / size.y + 0.5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            float w = edgeFalloff <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, edge / edgeFalloff);
            w *= strength;
            if (w <= 0f) return false;

            float h01 = Sample(u, v);
            if (invert) h01 = 1f - h01;
            float add = h01 * heightMeters / maxH;
            float target = baseY / maxH + add;

            float nv;
            switch (blend)
            {
                case StampBlend.Add: nv = cur01 + add * w; break;
                case StampBlend.Replace: nv = Mathf.Lerp(cur01, target, w); break;
                case StampBlend.Max: nv = Mathf.Lerp(cur01, Mathf.Max(cur01, target), w); break;
                default: nv = Mathf.Lerp(cur01, Mathf.Min(cur01, target), w); break;
            }
            result01 = Mathf.Clamp01(nv);
            weight = w;
            return true;
        }
    }
}