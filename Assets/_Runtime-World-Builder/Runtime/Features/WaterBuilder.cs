using System.Collections.Generic;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    public static class WaterBuilder
    {
        // ---------------- Lake / pond (a pond is just a small, shallow lake) ----------------
        public static LakeData CreateLake(TerrainWorld world, TerrainSettings s, Vector2 center, float radius, float depth,
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
            RibbonMesh.Spawn(depth > 4f ? "Lake" : "Pond", mesh, mats.Water, parent, false, false);

            WaterRegistry.Add(lake);
            return lake;
        }

        // ---------------- River ----------------
        /// control[0] should be the upstream (higher) end. The bed only ever slopes downhill along the path.
        public static RiverData CreateRiver(TerrainWorld world, TerrainSettings s, List<Vector2> control, float width, float depth,
            FeatureMaterials mats, Transform parent)
        {
            var pts = PathSpline.Sample(control, 3f);
            int n = pts.Count;
            if (n < 2) return null;
            float maxH = s.maxHeight, hw = width * 0.5f, bank = Mathf.Max(width, 4f);

            var hgt = new float[n]; float lastH = 0f;
            for (int i = 0; i < n; i++)
            {
                if (world.TrySampleHeight01(pts[i].x, pts[i].y, out float h01)) lastH = h01 * maxH;
                hgt[i] = lastH;
            }

            // profile: never rises going downstream, then smoothed, never above the ground
            var prof = (float[])hgt.Clone();
            for (int i = 1; i < n; i++) prof[i] = Mathf.Min(prof[i], prof[i - 1]);
            for (int pass = 0; pass < 6; pass++)
            {
                var tmp = (float[])prof.Clone();
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int k = -2; k <= 2; k++) sum += prof[Mathf.Clamp(i + k, 0, n - 1)];
                    tmp[i] = sum / 5f;
                }
                prof = tmp;
            }
            for (int i = 0; i < n; i++) prof[i] = Mathf.Min(prof[i], hgt[i]);
            for (int i = 1; i < n; i++) prof[i] = Mathf.Min(prof[i], prof[i - 1]);

            var bed = new List<Vector3>(n);
            var surface = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                bed.Add(new Vector3(pts[i].x, prof[i] - depth, pts[i].y));
                surface.Add(new Vector3(pts[i].x, prof[i] - depth * 0.3f, pts[i].y));
            }

            var raster = new PathRaster(bed, hw + bank, s.CellSpacing);
            world.ModifyHeights("River", raster.Min, raster.Max, (wx, wz, cur01) =>
            {
                if (!raster.TrySample(wx, wz, out float d, out float bedY)) return cur01;
                float curM = cur01 * maxH, target;
                if (d <= hw) target = bedY;
                else
                {
                    float t = (d - hw) / bank;
                    target = Mathf.Lerp(bedY, curM, t * t * (3f - 2f * t));   // smooth banks
                }
                return Mathf.Min(curM, target) / maxH;
            });

            RibbonMesh.SpawnChunked("River", surface, width + 0.6f, 0f, 0.1f, false, mats.Water, parent, false, false);

            var data = new RiverData { HalfWidth = hw };
            data.Points.AddRange(surface);
            WaterRegistry.Add(data);
            return data;
        }
    }
}