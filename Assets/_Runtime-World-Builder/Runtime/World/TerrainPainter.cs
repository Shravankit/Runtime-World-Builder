using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public static class TerrainPainter
    {
        public static void PaintChunk(TerrainChunkData chunk, TerrainSettings s, TerrainBiomeSettings biome)
        {
            int last = s.heightmapResolution - 1;
            PaintRegion(chunk, s, biome, 0, 0, last, last);
        }

        /// Repaints only the alphamap pixels covering heightmap samples [hx0..hx1] x [hz0..hz1].
        public static void PaintRegion(TerrainChunkData chunk, TerrainSettings s, TerrainBiomeSettings biome,
            int hx0, int hz0, int hx1, int hz1)
        {
            if (biome == null || biome.layers == null || biome.layers.Length == 0) return;

            var td = chunk.Terrain.terrainData;
            var layers = biome.layers;
            int aRes = biome.alphamapResolution, nL = layers.Length;
            int hRes = s.heightmapResolution, hLast = hRes - 1;

            if (td.terrainLayers.Length != nL)
            {
                var tl = new TerrainLayer[nL];
                for (int i = 0; i < nL; i++) tl[i] = layers[i].terrainLayer;
                td.terrainLayers = tl;
            }
            if (td.alphamapResolution != aRes) td.alphamapResolution = aRes;   // setting it resamples, so only when needed

            float a2h = hLast / (float)(aRes - 1);     // alpha index -> heightmap index
            float h2a = (aRes - 1) / (float)hLast;     // heightmap index -> alpha index

            // alpha rect, with a small margin because slope reads neighbouring samples
            int ax0 = Mathf.Max(0, Mathf.FloorToInt(hx0 * h2a) - 2);
            int az0 = Mathf.Max(0, Mathf.FloorToInt(hz0 * h2a) - 2);
            int ax1 = Mathf.Min(aRes - 1, Mathf.CeilToInt(hx1 * h2a) + 2);
            int az1 = Mathf.Min(aRes - 1, Mathf.CeilToInt(hz1 * h2a) + 2);
            int w = ax1 - ax0 + 1, hgt = az1 - az0 + 1;

            // slope (degrees) at every heightmap node the rect touches, computed once
            int sx0 = Mathf.Clamp(Mathf.FloorToInt(ax0 * a2h), 0, hLast - 1);
            int sz0 = Mathf.Clamp(Mathf.FloorToInt(az0 * a2h), 0, hLast - 1);
            int sx1 = Mathf.Min(hLast, Mathf.CeilToInt(ax1 * a2h) + 1);
            int sz1 = Mathf.Min(hLast, Mathf.CeilToInt(az1 * a2h) + 1);

            var H = chunk.Heights;
            var slope = new float[sz1 - sz0 + 1, sx1 - sx0 + 1];
            float sp = s.CellSpacing;
            float k = s.maxHeight / (2f * sp);
            for (int z = sz0; z <= sz1; z++)
            {
                int zl = Mathf.Max(z - 1, 0), zh = Mathf.Min(z + 1, hLast);
                for (int x = sx0; x <= sx1; x++)
                {
                    int xl = Mathf.Max(x - 1, 0), xh = Mathf.Min(x + 1, hLast);
                    float dx = (H[z, xh] - H[z, xl]) * k;
                    float dz = (H[zh, x] - H[zl, x]) * k;
                    slope[z - sz0, x - sx0] = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                }
            }

            int baseX = chunk.Coord.x * hLast, baseZ = chunk.Coord.y * hLast;
            var alpha = new float[hgt, w, nL];

            for (int az = az0; az <= az1; az++)
            {
                float fz = az * a2h;
                int z0 = Mathf.Min((int)fz, hLast - 1);
                float tz = Mathf.Clamp01(fz - z0);
                float wz = (baseZ + fz) * sp;
                int lz = z0 - sz0, oz = az - az0;

                for (int ax = ax0; ax <= ax1; ax++)
                {
                    float fx = ax * a2h;
                    int x0 = Mathf.Min((int)fx, hLast - 1);
                    float tx = Mathf.Clamp01(fx - x0);
                    float wx = (baseX + fx) * sp;
                    int lx = x0 - sx0, ox = ax - ax0;

                    float h01 = Mathf.Lerp(
                        Mathf.Lerp(H[z0, x0], H[z0, x0 + 1], tx),
                        Mathf.Lerp(H[z0 + 1, x0], H[z0 + 1, x0 + 1], tx), tz);
                    float slopeDeg = Mathf.Lerp(
                        Mathf.Lerp(slope[lz, lx], slope[lz, lx + 1], tx),
                        Mathf.Lerp(slope[lz + 1, lx], slope[lz + 1, lx + 1], tx), tz);

                    float sum = 0f;
                    for (int l = 0; l < nL; l++)
                    {
                        float wgt = layers[l].EvaluateWeight(h01, slopeDeg, wx, wz);
                        alpha[oz, ox, l] = wgt;
                        sum += wgt;
                    }
                    if (sum < 1e-4f) { alpha[oz, ox, 0] = 1f; sum = 1f; }

                    float inv = 1f / sum;
                    for (int l = 0; l < nL; l++) alpha[oz, ox, l] *= inv;
                }
            }

            td.SetAlphamaps(ax0, az0, alpha);
        }
    }
}