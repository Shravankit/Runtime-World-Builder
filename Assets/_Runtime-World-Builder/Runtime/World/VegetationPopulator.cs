using System.Collections;
using System.Collections.Generic;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.Settings;
using RuntimeWorldBuilder.SO.Vegetation;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public static class VegetationPopulator
    {
        const int RowsPerSlice = 16;        // grass rows per time slice
        const int SlopeRowsPerSlice = 64;   // slope rows per time slice

        // ---------- cached / reused data ----------
        sealed class ProtoSet
        {
            public TreePrototype[] trees;
            public DetailPrototype[] details;
            public int[] treeIdx, detailIdx;
            public int layerCount;
        }

        /// Evaluates the normalized biome layer weight at a point, using the same rules as TerrainPainter,
        /// so vegetation matches the painted texture without reading alphamaps back from the terrain.
        sealed class Sampler
        {
            readonly TerrainBiomeRule[] layers;
            readonly float ox, oz, size;
            readonly float[] w;

            public Sampler(TerrainBiomeSettings biome, Vector2Int coord, float chunkSize)
            {
                layers = biome != null ? biome.layers : null;
                ox = coord.x * chunkSize; oz = coord.y * chunkSize; size = chunkSize;
                w = new float[layers != null ? Mathf.Max(1, layers.Length) : 1];
            }

            public float Weight(int layer, float nx, float nz, float h01, float slopeDeg)
            {
                if (layers == null || layers.Length == 0) return layer == 0 ? 1f : 0f;
                if (layer < 0 || layer >= layers.Length) return 0f;

                float wx = ox + nx * size, wz = oz + nz * size, sum = 0f;
                for (int l = 0; l < layers.Length; l++)
                {
                    float v = layers[l].EvaluateWeight(h01, slopeDeg, wx, wz);
                    w[l] = v; sum += v;
                }
                if (sum < 1e-4f) return layer == 0 ? 1f : 0f;   // same fallback as TerrainPainter
                return w[layer] / sum;
            }
        }

        static readonly Dictionary<VegetationSettings, ProtoSet> protoCache = new();
        static readonly List<TreeInstance> treeBuffer = new();
        static int[,] layerBuffer;      // only one populate job runs at a time (see TerrainWorld.Tick)
        static float[,] slopeBuffer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { protoCache.Clear(); treeBuffer.Clear(); layerBuffer = null; slopeBuffer = null; }

        /// Call if you edit rules/prefabs at runtime so prototypes are rebuilt.
        public static void InvalidateCache() => protoCache.Clear();

        /// Synchronous version (runs everything in one go).
        public static void Populate(TerrainChunkData chunk, TerrainSettings s, TerrainBiomeSettings biome, VegetationSettings veg)
        {
            var it = PopulateSteps(chunk, s, biome, veg);
            while (it.MoveNext()) { }
        }

        /// Time-sliced version. Step it with MoveNext() from TerrainWorld.Tick.
        public static IEnumerator PopulateSteps(TerrainChunkData chunk, TerrainSettings s,
            TerrainBiomeSettings biome, VegetationSettings veg)
        {
            if (veg == null || veg.vegetationRules == null || veg.vegetationRules.Length == 0) yield break;
            var terrain = chunk.Terrain;
            if (terrain == null) yield break;
            var td = terrain.terrainData;

            terrain.treeDistance = veg.treeDistance;
            terrain.treeBillboardDistance = veg.treeBillboardDistance;
            terrain.detailObjectDistance = veg.detailDistance;
            terrain.detailObjectDensity = veg.detailDensityScale;

            int nL = biome != null && biome.layers != null ? Mathf.Max(1, biome.layers.Length) : 1;
            var proto = GetPrototypes(veg, nL);

            // ---- prototypes (only touched when something actually changed) ----
            if (td.treePrototypes.Length != proto.trees.Length) td.treePrototypes = proto.trees;
            yield return null;
            if (terrain == null) yield break;

            if (proto.details.Length > 0)
            {
                if (td.detailResolution != veg.detailResolution)
                    td.SetDetailResolution(veg.detailResolution, veg.detailResolutionPerPatch);   // before SetDetailLayer
                if (td.detailPrototypes.Length != proto.details.Length) td.detailPrototypes = proto.details;
                yield return null;
                if (terrain == null) yield break;
            }

            // ---- slope map once per chunk, from our own height array, sliced into row blocks ----
            int hRes = s.heightmapResolution, last = hRes - 1;
            float[,] H = chunk.Heights;
            if (slopeBuffer == null || slopeBuffer.GetLength(0) != hRes) slopeBuffer = new float[hRes, hRes];
            var slope = slopeBuffer;
            float k = s.maxHeight / (2f * s.CellSpacing);

            for (int zStart = 0; zStart < hRes; zStart += SlopeRowsPerSlice)
            {
                int zEnd = Mathf.Min(zStart + SlopeRowsPerSlice, hRes);
                for (int z = zStart; z < zEnd; z++)
                {
                    int zl = Mathf.Max(z - 1, 0), zh = Mathf.Min(z + 1, last);
                    for (int x = 0; x < hRes; x++)
                    {
                        int xl = Mathf.Max(x - 1, 0), xh = Mathf.Min(x + 1, last);
                        float dx = (H[z, xh] - H[z, xl]) * k;
                        float dz = (H[zh, x] - H[zl, x]) * k;
                        slope[z, x] = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                    }
                }
                yield return null;
                if (terrain == null) yield break;
            }

            // ---- rules ----
            var sampler = new Sampler(biome, chunk.Coord, s.chunkSize);
            treeBuffer.Clear();
            int dRes = veg.detailResolution;

            for (int i = 0; i < veg.vegetationRules.Length; i++)
            {
                var r = veg.vegetationRules[i];
                if (r == null) continue;
                var rng = new System.Random(Hash(veg.seed, chunk.Coord, i));

                if (proto.treeIdx[i] >= 0)
                {
                    PlaceTrees(r, sampler, s.chunkSize, H, slope, proto.treeIdx[i], rng, treeBuffer, veg.maxTreesPerChunk);
                }
                else if (proto.detailIdx[i] >= 0)
                {
                    var it = PaintGrass(td, r, sampler, veg, dRes, H, slope, proto.detailIdx[i], rng);
                    while (true)
                    {
                        if (terrain == null) yield break;
                        if (!it.MoveNext()) break;
                        yield return null;
                    }
                }

                yield return null;
                if (terrain == null) yield break;
            }

            if (proto.trees.Length > 0)
                td.SetTreeInstances(treeBuffer.ToArray(), true);   // snapToHeightmap: y follows the terrain
            treeBuffer.Clear();
            // no terrain.Flush(): it forces a heavy sync. Call it yourself only if you need colliders immediately.
        }

        // ---------- prototypes ----------
        static ProtoSet GetPrototypes(VegetationSettings veg, int nL)
        {
            if (protoCache.TryGetValue(veg, out var cached)
                && cached.treeIdx.Length == veg.vegetationRules.Length && cached.layerCount == nL)
                return cached;

            var treeProtos = new List<TreePrototype>();
            var detailProtos = new List<DetailPrototype>();
            int n = veg.vegetationRules.Length;
            var treeIdx = new int[n];
            var detailIdx = new int[n];

            for (int i = 0; i < n; i++)
            {
                var r = veg.vegetationRules[i];
                treeIdx[i] = detailIdx[i] = -1;
                if (r == null || r.biomeLayerIndex < 0 || r.biomeLayerIndex >= nL) continue;

                if (r.vegetationKind == VegetationKind.Tree && r.prefab != null)
                {
                    treeIdx[i] = treeProtos.Count;
                    treeProtos.Add(new TreePrototype { prefab = r.prefab });
                }
                else if (r.vegetationKind == VegetationKind.GrassMesh && r.prefab != null)
                {
                    detailIdx[i] = detailProtos.Count;
                    detailProtos.Add(MakeDetail(r, true));
                }
                else if (r.vegetationKind == VegetationKind.GrassTexture && r.texture != null)
                {
                    detailIdx[i] = detailProtos.Count;
                    detailProtos.Add(MakeDetail(r, false));
                }
            }

            var set = new ProtoSet
            {
                trees = treeProtos.ToArray(),
                details = detailProtos.ToArray(),
                treeIdx = treeIdx,
                detailIdx = detailIdx,
                layerCount = nL,
            };
            protoCache[veg] = set;
            return set;
        }

        static DetailPrototype MakeDetail(VegetationRule r, bool mesh)
        {
            var w = r.EffWidthScale; var h = r.EffHeightScale;
            return new DetailPrototype
            {
                usePrototypeMesh = mesh,
                prototype = mesh ? r.prefab : null,
                prototypeTexture = mesh ? null : r.texture,
                useInstancing = mesh,                              // mesh grass needs an instancing-enabled material
                renderMode = mesh ? DetailRenderMode.VertexLit : DetailRenderMode.GrassBillboard,
                minWidth = w.x,
                maxWidth = w.y,
                minHeight = h.x,
                maxHeight = h.y,
                healthyColor = r.EffHealthyColor,
                dryColor = r.EffDryColor,
                noiseSpread = r.noiseSpread * 10f,
                density = 1f,
            };
        }

        // ---------- trees: jittered grid, accepted by biome-layer weight ----------
        static void PlaceTrees(VegetationRule r, Sampler sampler, float chunkSize,
            float[,] H, float[,] slope, int protoIndex, System.Random rng, List<TreeInstance> output, int maxTrees)
        {
            if (r.density <= 0f) return;
            float step = Mathf.Sqrt(10000f / r.density);          // metres between grid points
            int n = Mathf.Max(1, Mathf.CeilToInt(chunkSize / step));
            float cell = chunkSize / n;

            float minH = r.minHeight01, maxH = r.EffMaxHeight01, maxSlope = r.EffMaxSlope, power = r.EffWeightPower;
            var ws = r.EffWidthScale; var hs = r.EffHeightScale;

            for (int gz = 0; gz < n; gz++)
                for (int gx = 0; gx < n; gx++)
                {
                    // always consume the RNG so results don't shift when filters change
                    float jx = (float)rng.NextDouble(), jz = (float)rng.NextDouble();
                    float accept = (float)rng.NextDouble();
                    float rot = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float wScale = Mathf.Lerp(ws.x, ws.y, (float)rng.NextDouble());
                    float hScale = Mathf.Lerp(hs.x, hs.y, (float)rng.NextDouble());

                    if (output.Count >= maxTrees) continue;

                    float nx = (gx + jx) * cell / chunkSize, nz = (gz + jz) * cell / chunkSize;

                    // cheap rejections first
                    float h01 = Bilinear(H, nx, nz);
                    if (h01 < minH || h01 > maxH) continue;
                    float slopeDeg = Bilinear(slope, nx, nz);
                    if (slopeDeg > maxSlope) continue;

                    float w = sampler.Weight(r.biomeLayerIndex, nx, nz, h01, slopeDeg);
                    if (w < r.minLayerWeight) continue;
                    if (accept > Mathf.Pow(w, power)) continue;

                    output.Add(new TreeInstance
                    {
                        prototypeIndex = protoIndex,
                        position = new Vector3(nx, 0f, nz),       // y snapped by SetTreeInstances
                        widthScale = wScale,
                        heightScale = hScale,
                        rotation = rot,
                        color = Color.white,
                        lightmapColor = Color.white,
                    });
                }
        }

        // ---------- grass: per detail cell, count from layer weight (time-sliced) ----------
        static IEnumerator PaintGrass(TerrainData td, VegetationRule r, Sampler sampler, VegetationSettings veg,
            int dRes, float[,] H, float[,] slope, int detailIndex, System.Random rng)
        {
            float maxCount = Mathf.Clamp(r.density, 0f, Mathf.Min(16, veg.maxDetailPerCell));
            if (maxCount <= 0f) yield break;

            if (layerBuffer == null || layerBuffer.GetLength(0) != dRes)
                layerBuffer = new int[dRes, dRes];
            else
                System.Array.Clear(layerBuffer, 0, layerBuffer.Length);
            var layer = layerBuffer;                              // [z, x]

            float minH = r.minHeight01, maxH = r.EffMaxHeight01, maxSlope = r.EffMaxSlope, power = r.EffWeightPower;

            for (int z = 0; z < dRes; z++)
            {
                if ((z & (RowsPerSlice - 1)) == 0)
                {
                    yield return null;
                    if (td == null) yield break;
                }

                float nz = (z + 0.5f) / dRes;
                for (int x = 0; x < dRes; x++)
                {
                    float jitter = (float)rng.NextDouble();
                    float nx = (x + 0.5f) / dRes;

                    float h01 = Bilinear(H, nx, nz);
                    if (h01 < minH || h01 > maxH) continue;
                    float slopeDeg = Bilinear(slope, nx, nz);
                    if (slopeDeg > maxSlope) continue;

                    float w = sampler.Weight(r.biomeLayerIndex, nx, nz, h01, slopeDeg);
                    if (w < r.minLayerWeight) continue;

                    float f = Mathf.Pow(w, power) * maxCount;
                    int count = Mathf.FloorToInt(f);
                    if (jitter < f - count) count++;              // stochastic rounding keeps edges soft
                    layer[z, x] = Mathf.Min(count, 16);
                }
            }

            td.SetDetailLayer(0, 0, detailIndex, layer);
        }

        // ---------- helpers ----------
        /// Bilinear sample of a [z, x] array with normalized 0..1 coordinates.
        static float Bilinear(float[,] a, float nx, float nz)
        {
            int resZ = a.GetLength(0), resX = a.GetLength(1);
            float fx = Mathf.Clamp01(nx) * (resX - 1), fz = Mathf.Clamp01(nz) * (resZ - 1);
            int x0 = (int)fx, z0 = (int)fz;
            int x1 = Mathf.Min(x0 + 1, resX - 1), z1 = Mathf.Min(z0 + 1, resZ - 1);
            float tx = fx - x0, tz = fz - z0;
            float top = Mathf.Lerp(a[z0, x0], a[z0, x1], tx);
            float bot = Mathf.Lerp(a[z1, x0], a[z1, x1], tx);
            return Mathf.Lerp(top, bot, tz);
        }

        static int Hash(int seed, Vector2Int c, int rule) =>
            unchecked(seed * 16777619 ^ c.x * 73856093 ^ c.y * 19349663 ^ rule * 83492791);
    }
}