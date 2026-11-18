using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Catmull-Rom through control points, resampled at an even spacing (metres).
    public static class PathSpline
    {
        public static List<Vector2> Sample(IList<Vector2> p, float spacing)
        {
            var res = new List<Vector2>();
            if (p.Count < 2) { res.AddRange(p); return res; }

            var dense = new List<Vector2>();
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector2 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Count - 1)];
                for (int k = 0; k < 16; k++) dense.Add(CR(p0, p1, p2, p3, k / 16f));
            }
            dense.Add(p[p.Count - 1]);

            res.Add(dense[0]);
            float next = spacing, run = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                float len = Vector2.Distance(dense[i - 1], dense[i]);
                if (len < 1e-5f) continue;
                while (run + len >= next)
                {
                    res.Add(Vector2.Lerp(dense[i - 1], dense[i], (next - run) / len));
                    next += spacing;
                }
                run += len;
            }
            if (Vector2.Distance(res[res.Count - 1], dense[dense.Count - 1]) > spacing * 0.25f)
                res.Add(dense[dense.Count - 1]);
            return res;
        }

        static Vector2 CR(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }

    /// Rasterizes "distance to path + target height" onto a grid aligned with the terrain sample grid,
    /// so a deform function can look up the closest path point per terrain sample in O(1).
    public sealed class PathRaster
    {
        public readonly Vector2 Min, Max;
        readonly float sp, invSp;
        readonly int w, h;
        readonly float[] dist, yv;

        /// pts: x/z = world position, y = target height in metres. segEnabled[i] controls segment i->i+1.
        public PathRaster(IList<Vector3> pts, float reach, float spacing, bool[] segEnabled = null)
        {
            sp = spacing; invSp = 1f / spacing;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in pts)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            minX -= reach + sp; minZ -= reach + sp; maxX += reach + sp; maxZ += reach + sp;

            Min = new Vector2(Mathf.Floor(minX * invSp) * sp, Mathf.Floor(minZ * invSp) * sp);
            w = Mathf.CeilToInt((maxX - Min.x) * invSp) + 2;
            h = Mathf.CeilToInt((maxZ - Min.y) * invSp) + 2;
            Max = Min + new Vector2(w * sp, h * sp);

            dist = new float[w * h];
            yv = new float[w * h];
            System.Array.Fill(dist, float.MaxValue);

            float reach2 = reach * reach;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (segEnabled != null && !segEnabled[i]) continue;
                Vector3 a = pts[i], b = pts[i + 1];
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - Min.x) * invSp));
                int ix1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - Min.x) * invSp));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach - Min.y) * invSp));
                int iz1 = Mathf.Min(h - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + reach - Min.y) * invSp));

                float abx = b.x - a.x, abz = b.z - a.z, len2 = abx * abx + abz * abz;
                float invLen2 = len2 > 1e-8f ? 1f / len2 : 0f;
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    float pz = Min.y + iz * sp;
                    int row = iz * w;
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float px = Min.x + ix * sp;
                        float t = Mathf.Clamp01(((px - a.x) * abx + (pz - a.z) * abz) * invLen2);
                        float dx = px - (a.x + abx * t), dz = pz - (a.z + abz * t);
                        float d2 = dx * dx + dz * dz;
                        if (d2 > reach2) continue;                       // compare squared, sqrt only for accepted cells
                        float d = Mathf.Sqrt(d2);
                        int idx = row + ix;
                        if (d < dist[idx]) { dist[idx] = d; yv[idx] = Mathf.Lerp(a.y, b.y, t); }
                    }
                }
            }
        }

        public bool TrySample(float wx, float wz, out float d, out float y)
        {
            int ix = Mathf.RoundToInt((wx - Min.x) * invSp), iz = Mathf.RoundToInt((wz - Min.y) * invSp);
            d = float.MaxValue; y = 0f;
            if (ix < 0 || iz < 0 || ix >= w || iz >= h) return false;
            int idx = iz * w + ix;
            if (dist[idx] == float.MaxValue) return false;
            d = dist[idx]; y = yv[idx];
            return true;
        }
    }

    /// Strip mesh along a polyline: flat ribbon, or a box-section (deck) when thickness > 0 and sides = true.
    public static class RibbonMesh
    {
        /// Builds points [from, to) of c. Tangents use the full list, so split meshes join without cracks.
        public static Mesh Build(IList<Vector3> c, float width, float thickness, float uvPerMeter, bool sides,
            int from = 0, int to = -1, float distOffset = 0f)
        {
            if (to < 0) to = c.Count;
            int n = to - from;
            var m = new Mesh { name = "Ribbon" };
            if (n < 2) return m;

            var TL = new Vector3[n]; var TR = new Vector3[n];
            var BL = new Vector3[n]; var BR = new Vector3[n];
            var dist = new float[n];
            float hw = width * 0.5f;
            var down = Vector3.down * thickness;

            for (int i = 0; i < n; i++)
            {
                int g = from + i;
                Vector3 t = c[Mathf.Min(g + 1, c.Count - 1)] - c[Mathf.Max(g - 1, 0)];
                t.y = 0f;
                t = t.sqrMagnitude < 1e-6f ? Vector3.forward : t.normalized;
                Vector3 r = new Vector3(t.z, 0f, -t.x);
                TL[i] = c[g] - r * hw; TR[i] = c[g] + r * hw;
                BL[i] = TL[i] + down; BR[i] = TR[i] + down;
                dist[i] = i > 0 ? dist[i - 1] + Vector3.Distance(c[g], c[g - 1]) : distOffset;
            }

            bool box = sides && thickness > 0f;
            int quads = (n - 1) * (box ? 4 : 1);
            var v = new List<Vector3>(quads * 4); var uv = new List<Vector2>(quads * 4); var tri = new List<int>(quads * 6);
            void Strip(Vector3[] a, Vector3[] b)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int k = v.Count;
                    v.Add(a[i]); v.Add(b[i]); v.Add(a[i + 1]); v.Add(b[i + 1]);
                    float v0 = dist[i] * uvPerMeter, v1 = dist[i + 1] * uvPerMeter;
                    uv.Add(new Vector2(0, v0)); uv.Add(new Vector2(1, v0));
                    uv.Add(new Vector2(0, v1)); uv.Add(new Vector2(1, v1));
                    tri.Add(k); tri.Add(k + 2); tri.Add(k + 1);
                    tri.Add(k + 1); tri.Add(k + 2); tri.Add(k + 3);
                }
            }

            Strip(TL, TR);                       // top
            if (box)
            {
                Strip(BL, TL);                   // left
                Strip(TR, BR);                   // right
                Strip(BR, BL);                   // bottom
            }

            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// Splits a long path into ~samplesPerMesh pieces so the camera can cull far pieces.
        /// A 3 km road as one mesh is drawn whenever any part is visible; in pieces it is not.
        public static GameObject SpawnChunked(string name, IList<Vector3> c, float width, float thickness, float uvPerMeter,
            bool sides, Material mat, Transform parent, bool collider, bool shadows, int samplesPerMesh = 48)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            float dist = 0f; int part = 0;
            for (int start = 0; start < c.Count - 1; start += samplesPerMesh - 1)
            {
                int end = Mathf.Min(start + samplesPerMesh, c.Count);
                var mesh = Build(c, width, thickness, uvPerMeter, sides, start, end, dist);
                mesh.name = name + "_" + part++;
                Spawn(mesh.name, mesh, mat, group.transform, collider, shadows);
                for (int i = start + 1; i < end; i++) dist += Vector3.Distance(c[i - 1], c[i]);
            }
            return group;
        }

        /// Copy of a polyline shifted sideways (+ = right of travel) and vertically.
        public static List<Vector3> Offset(IList<Vector3> c, float lateral, float dy)
        {
            var o = new List<Vector3>(c.Count);
            for (int i = 0; i < c.Count; i++)
            {
                Vector3 t = c[Mathf.Min(i + 1, c.Count - 1)] - c[Mathf.Max(i - 1, 0)];
                t.y = 0f;
                t = t.sqrMagnitude < 1e-6f ? Vector3.forward : t.normalized;
                o.Add(c[i] + new Vector3(t.z, 0f, -t.x) * lateral + Vector3.up * dy);
            }
            return o;
        }

        public static GameObject Spawn(string name, Mesh mesh, Material mat, Transform parent, bool collider, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (!shadows)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}