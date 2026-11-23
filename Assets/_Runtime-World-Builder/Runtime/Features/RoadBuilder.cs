using System;
using System.Collections.Generic;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    [Serializable]
    public class RoadStyle
    {
        public float width = 8f;
        public float shoulder = 6f;            // blend zone where terrain slopes from road edge back to natural ground
        [Range(0.02f, 0.4f)] public float maxGrade = 0.12f;   // rise / run
        public float bridgeClearance = 3f;     // deck height above the water surface
        public float deckThickness = 0.8f;
    }

    public static class RoadBuilder
    {
        const float Spacing = 2f;

        public static bool Build(TerrainWorld world, TerrainSettings s, List<Vector2> control, RoadStyle st,
    FeatureMaterials mats, Transform parent)
        {
            var feature = new GameObject("Road");
            feature.transform.SetParent(parent, false);

            bool ok = false;
            world.BeginEdit("Road");
            try { ok = BuildInternal(world, s, control, st, mats, feature.transform); }
            finally
            {
                if (ok)
                    world.AddEditHook(
                        () => { if (feature != null) feature.SetActive(false); },
                        () => { if (feature != null) feature.SetActive(true); },
                        () => { if (feature != null) UnityEngine.Object.Destroy(feature); });
                world.EndEdit();
            }
            if (!ok) UnityEngine.Object.Destroy(feature);
            return ok;
        }

        public static bool BuildInternal(TerrainWorld world, TerrainSettings s, List<Vector2> control, RoadStyle st,
            FeatureMaterials mats, Transform parent)
        {
            var pts = PathSpline.Sample(control, Spacing);
            int n = pts.Count;
            if (n < 2) return false;
            float maxH = s.maxHeight, hw = st.width * 0.5f;

            // 1) terrain height + water detection along the path (water must already exist to be detected)
            var h = new float[n]; var lvl = new float[n]; var water = new bool[n];
            float lastH = 0f;
            for (int i = 0; i < n; i++)
            {
                if (world.TrySampleHeight01(pts[i].x, pts[i].y, out float h01)) lastH = h01 * maxH;
                h[i] = lastH;
                lvl[i] = float.MinValue;
                if (WaterRegistry.TryGet(pts[i].x, pts[i].y, hw + 1.5f, out float lv)) { water[i] = true; lvl[i] = lv; }
            }

            // bridge = water samples + 2 samples of landing on each bank
            var isB = new bool[n];
            for (int i = 0; i < n; i++)
                if (water[i]) for (int k = -2; k <= 2; k++) isB[Mathf.Clamp(i + k, 0, n - 1)] = true;

            // 2) road profile: smooth over land
            var y = (float[])h.Clone();
            for (int pass = 0; pass < 4; pass++)
            {
                var tmp = (float[])y.Clone();
                for (int i = 0; i < n; i++)
                {
                    if (isB[i]) continue;
                    float sum = 0f;
                    for (int k = -2; k <= 2; k++) sum += y[Mathf.Clamp(i + k, 0, n - 1)];
                    tmp[i] = sum / 5f;
                }
                y = tmp;
            }

            // bridge decks: straight between the two banks, always above the water
            var bridgeRuns = new List<Vector2Int>();
            for (int i = 0; i < n;)
            {
                if (!isB[i]) { i++; continue; }
                int a = i;
                while (i < n && isB[i]) i++;
                int b = i - 1;
                bridgeRuns.Add(new Vector2Int(a, b));

                float runLvl = float.MinValue;
                for (int k = a; k <= b; k++) runLvl = Mathf.Max(runLvl, lvl[k]);
                float ya = a > 0 ? y[a - 1] : h[a], yb = b < n - 1 ? y[b + 1] : h[b];
                for (int k = a; k <= b; k++)
                {
                    float t = (k - a + 1) / (float)(b - a + 2);
                    y[k] = Mathf.Max(Mathf.Lerp(ya, yb, t), runLvl + st.bridgeClearance);
                }
            }

            // grade limit (forward + backward, twice) so the road never gets steeper than maxGrade
            float g = st.maxGrade * Spacing;
            for (int rep = 0; rep < 2; rep++)
            {
                for (int i = 1; i < n; i++) y[i] = Mathf.Clamp(y[i], y[i - 1] - g, y[i - 1] + g);
                for (int i = n - 2; i >= 0; i--) y[i] = Mathf.Clamp(y[i], y[i + 1] - g, y[i + 1] + g);
            }

            // 3) carve / fill terrain along land segments (cut-and-fill with sloped shoulders)
            var prof = new List<Vector3>(n);
            for (int i = 0; i < n; i++) prof.Add(new Vector3(pts[i].x, y[i], pts[i].y));

            var segOn = new bool[n - 1];
            for (int i = 0; i < n - 1; i++) segOn[i] = !isB[i] && !isB[i + 1];

            var raster = new PathRaster(prof, hw + st.shoulder, s.CellSpacing, segOn);
            float shoulder = Mathf.Max(0.5f, st.shoulder);
            world.ModifyHeights("Road", raster.Min, raster.Max, (wx, wz, cur01) =>
            {
                if (!raster.TrySample(wx, wz, out float d, out float roadY)) return cur01;
                float wgt = 1f;
                if (d > hw) { float t = (d - hw) / shoulder; wgt = 1f - t * t * (3f - 2f * t); }
                return Mathf.Lerp(cur01, roadY / maxH, wgt);
            });

            // 4) road surface meshes on land runs
            for (int i = 0; i < n;)
            {
                if (isB[i]) { i++; continue; }
                int a = i;
                while (i < n && !isB[i]) i++;
                int b = i - 1;
                if (b - a < 1) continue;

                var line = new List<Vector3>();
                for (int k = a; k <= b; k++) line.Add(prof[k] + Vector3.up * 0.08f);
                RibbonMesh.SpawnChunked("Road", line, st.width, 0f, 0.125f, false, mats.Road, parent, true, false);
            }

            // 5) bridges
            foreach (var run in bridgeRuns)
            {
                int a = Mathf.Max(run.x - 1, 0), b = Mathf.Min(run.y + 1, n - 1);
                var deck = new List<Vector3>();
                for (int k = a; k <= b; k++) deck.Add(prof[k]);

                var go = RibbonMesh.SpawnChunked("Bridge", deck, st.width, st.deckThickness, 0.125f, true,
                    mats.Bridge, parent, true, true);

                // railings
                float rail = hw - 0.1f;
                foreach (float side in new[] { -rail, rail })
                {
                    var rl = RibbonMesh.Offset(deck, side, 1f);
                    RibbonMesh.SpawnChunked("Rail", rl, 0.2f, 1f, 0.2f, true, mats.Bridge, go.transform, false, true);
                }

                // pillars down to the ground wherever there is a real gap
                for (int k = a; k <= b; k += 5)
                {
                    if (!world.TrySampleHeight01(pts[k].x, pts[k].y, out float h01)) continue;
                    float groundY = h01 * maxH - 0.5f;
                    float deckBottom = y[k] - st.deckThickness;
                    float height = deckBottom - groundY;
                    if (height < 1f) continue;

                    var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    p.name = "Pillar";
                    p.transform.SetParent(go.transform, false);
                    p.transform.position = new Vector3(pts[k].x, groundY + height * 0.5f, pts[k].y);
                    p.transform.localScale = new Vector3(1.4f, height * 0.5f, 1.4f);   // cylinder is 2 m tall
                    p.GetComponent<MeshRenderer>().sharedMaterial = mats.Bridge;
                }
            }
            return true;
        }
    }
}