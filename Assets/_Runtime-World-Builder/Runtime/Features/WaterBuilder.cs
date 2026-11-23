using System.Collections.Generic;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    public static class WaterBuilder
    {
        public static LakeData CreateLake(TerrainWorld world, TerrainSettings s, Vector2 center, float radius, float depth,
    FeatureMaterials mats, Transform parent)
        {
            string label = depth > 4f ? "Lake" : "Pond";
            var feature = new GameObject(label);
            feature.transform.SetParent(parent, false);

            LakeData lake = null;
            world.BeginEdit(label);
            try { lake = CreateLakeInternal(world, s, center, radius, depth, mats, feature.transform); }
            finally
            {
                if (lake != null)
                    world.AddEditHook(
                        () => { if (feature != null) feature.SetActive(false); WaterRegistry.Remove(lake); },
                        () => { if (feature != null) { feature.SetActive(true); WaterRegistry.Add(lake); } },
                        () => { if (feature != null) UnityEngine.Object.Destroy(feature); });
                world.EndEdit();
            }
            if (lake == null) UnityEngine.Object.Destroy(feature);
            return lake;
        }

        public static RiverData CreateRiver(TerrainWorld world, TerrainSettings s, List<Vector2> control, float width, float depth,
            FeatureMaterials mats, Transform parent)
        {
            var feature = new GameObject("River");
            feature.transform.SetParent(parent, false);

            RiverData river = null;
            world.BeginEdit("River");
            try { river = CreateRiverInternal(world, s, control, width, depth, mats, feature.transform); }
            finally
            {
                if (river != null)
                    world.AddEditHook(
                        () => { if (feature != null) feature.SetActive(false); WaterRegistry.Remove(river); },
                        () => { if (feature != null) { feature.SetActive(true); WaterRegistry.Add(river); } },
                        () => { if (feature != null) UnityEngine.Object.Destroy(feature); });
                world.EndEdit();
            }
            if (river == null) UnityEngine.Object.Destroy(feature);
            return river;
        }

        // ---------------- Lake / pond (a pond is just a small, shallow lake) ----------------
        public static LakeData CreateLakeInternal(TerrainWorld world, TerrainSettings s, Vector2 center, float radius, float depth,
            FeatureMaterials mats, Transform parent)
        {
            var lake = new LakeData { Center = center, Radius = radius, Depth = depth, Seed = Random.Range(0f, 500f) };
            float maxH = s.maxHeight;
            float bank = Mathf.Max(6f, radius * 0.5f);

            // water level = lowest point of the rim, so water never sits above the surrounding ground
            float minRim = float.MaxValue; int ok = 0;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                float r = lake.RadiusAt(a) + bank * 0.5f;
                if (world.TrySampleHeight01(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, out float h))
                { minRim = Mathf.Min(minRim, h * maxH); ok++; }
            }
            if (ok < 12) { Debug.LogWarning("Lake: too close to the terrain edge"); return null; }
            lake.Level = minRim - 0.3f;

            float reach = radius * 1.3f + bank;
            world.ModifyHeights("Lake", center - Vector2.one * reach, center + Vector2.one * reach, (wx, wz, cur01) =>
            {
                float dx = wx - center.x, dz = wz - center.y;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float r = lake.RadiusAt(Mathf.Atan2(dz, dx));
                if (d > r + bank) return cur01;

                float curM = cur01 * maxH, target;
                if (d <= r)
                {
                    float u = d / r;
                    target = lake.Level - depth * (1f - u * u);              // bowl, deepest in the middle
                }
                else
                {
                    float t = (d - r) / bank;
                    float shore = lake.Level + (d - r) * 0.3f;               // gentle bank rising from the waterline
                    target = Mathf.Lerp(shore, curM, t * t * (3f - 2f * t));
                }
                return Mathf.Min(curM, target) / maxH;
            });

            // water surface: triangle fan following the noisy shoreline
            const int seg = 64;
            var v = new Vector3[seg + 1];
            var uv = new Vector2[seg + 1];
            var tri = new int[seg * 3];
            v[0] = new Vector3(center.x, lake.Level, center.y); uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float r = lake.RadiusAt(a) + 0.5f;                            // tuck the edge slightly into the bank
                v[i + 1] = new Vector3(center.x + Mathf.Cos(a) * r, lake.Level, center.y + Mathf.Sin(a) * r);
                uv[i + 1] = new Vector2(v[i + 1].x * 0.05f, v[i + 1].z * 0.05f);
                tri[i * 3] = 0; tri[i * 3 + 1] = (i + 1) % seg + 1; tri[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "LakeWater", vertices = v, uv = uv, triangles = tri };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            RibbonMesh.Spawn(depth > 4f ? "Lake" : "Pond", mesh, mats.Lake, parent, false, false);

            WaterRegistry.Add(lake);
            return lake;
        }

        // ---------------- River ----------------
        /// control[0] should be the upstream (higher) end. The bed only ever slopes downhill along the path.
        public static RiverData CreateRiverInternal(TerrainWorld world, TerrainSettings s, List<Vector2> control, float width, float depth,
    FeatureMaterials mats, Transform parent)
        {
            const float Spacing = 3f;
            var raw = PathSpline.Sample(control, Spacing);
            int n = raw.Count;
            if (n < 2) return null;

            float maxH = s.maxHeight, hw = width * 0.5f;
            float valley = Mathf.Max(width * 3f, 20f);          // how far the valley sides blend into natural ground
            float d0 = Mathf.Min(depth, width * 0.2f);          // real rivers are shallow compared to their width
            int taperN = Mathf.Max(3, Mathf.CeilToInt(Mathf.Max(width * 2f, 20f) / Spacing));

            // 1) gentle meander so it doesn't look ruler-straight
            var pts = new List<Vector2>(n);
            float seed = Random.Range(0f, 500f), amp = width * 1.0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 t = raw[Mathf.Min(i + 1, n - 1)] - raw[Mathf.Max(i - 1, 0)];
                t = t.sqrMagnitude < 1e-6f ? Vector2.up : t.normalized;
                var nrm = new Vector2(-t.y, t.x);
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(i, n - 1 - i) / (float)taperN);
                float off = (Mathf.PerlinNoise(seed + i * Spacing * 0.012f, seed) - 0.5f) * 2f * amp * fade;
                pts.Add(raw[i] + nrm * off);
            }

            // 2) water level: never higher than the lower of the two banks, so it can never float
            float Ground(Vector2 p, float fallback) =>
                world.TrySampleHeight01(p.x, p.y, out float h) ? h * maxH : fallback;

            var surf = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 t = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
                t = t.sqrMagnitude < 1e-6f ? Vector2.up : t.normalized;
                var nrm = new Vector2(-t.y, t.x);
                float c = Ground(pts[i], 0f);
                float l = Ground(pts[i] + nrm * hw * 1.15f, c), r = Ground(pts[i] - nrm * hw * 1.15f, c);
                surf[i] = Mathf.Min(c, Mathf.Min(l, r)) - 0.15f;
            }
            var orig = (float[])surf.Clone();
            for (int pass = 0; pass < 10; pass++)
            {
                var tmp = (float[])surf.Clone();
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int k = -2; k <= 2; k++) sum += surf[Mathf.Clamp(i + k, 0, n - 1)];
                    tmp[i] = sum / 5f;
                }
                surf = tmp;
            }
            for (int i = 0; i < n; i++) surf[i] = Mathf.Min(surf[i], orig[i]);   // smoothing may only lower it
            for (int i = 1; i < n; i++) surf[i] = Mathf.Min(surf[i], surf[i - 1]); // always flows downhill

            // 3) bed and water-surface polylines (bed rises to the surface at both ends)
            var bed = new List<Vector3>(n);
            var water = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / (float)taperN));
                bed.Add(new Vector3(pts[i].x, surf[i] - d0 * f, pts[i].y));
                water.Add(new Vector3(pts[i].x, surf[i], pts[i].y));
            }

            // 4) carve ONLY (no raised banks): flat bed -> shoreline exactly at hw -> valley blend into natural ground
            float reach = hw + valley;
            var bedR = new PathRaster(bed, reach, s.CellSpacing);
            var surR = new PathRaster(water, reach, s.CellSpacing);
            float inner = hw * 0.5f;
            world.ModifyHeights("River", bedR.Min, bedR.Max, (wx, wz, cur01) =>
            {
                if (!bedR.TrySample(wx, wz, out float d, out float bedY)) return cur01;
                surR.TrySample(wx, wz, out _, out float surY);
                float curM = cur01 * maxH, target;
                if (d <= inner) target = bedY;
                else if (d <= hw) target = Mathf.Lerp(bedY, surY, Mathf.SmoothStep(0f, 1f, (d - inner) / (hw - inner)));
                else target = Mathf.Lerp(surY, curM, Mathf.SmoothStep(0f, 1f, (d - hw) / valley));
                return Mathf.Min(curM, target) / maxH;
            });

            // 5) water ribbon; slightly wider than the channel so its edges tuck under the shore
            var riverGo = RibbonMesh.SpawnChunked("River", water, width + 0.4f, 0f, 0.1f, false, mats.Water, parent, false, false);
            var block = new MaterialPropertyBlock();
            block.SetFloat("_Width", width + 0.4f);
            foreach (var r in riverGo.GetComponentsInChildren<MeshRenderer>()) r.SetPropertyBlock(block);

            var data = new RiverData { HalfWidth = hw };
            data.Points.AddRange(water);
            WaterRegistry.Add(data);
            return data;
        }
    }
}