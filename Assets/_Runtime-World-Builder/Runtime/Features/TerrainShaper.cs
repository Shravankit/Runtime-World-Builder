using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Quick terrain edits: hills, valleys, flatten. All undoable (one step each).
    public static class TerrainShaper
    {
        /// Smooth lumpy mound (raise) or pit (lower). amount = peak change in metres.
        public static void Hill(TerrainWorld world, TerrainSettings s, Vector2 c, float radius, float amount, bool raise)
        {
            float maxH = s.maxHeight, seed = Random.Range(0f, 500f), sign = raise ? 1f : -1f, r2 = radius * radius;
            world.ModifyHeights(raise ? "Hill" : "Valley", c - Vector2.one * radius, c + Vector2.one * radius,
                (wx, wz, cur01) =>
                {
                    float dx = wx - c.x, dz = wz - c.y, d2 = dx * dx + dz * dz;
                    if (d2 >= r2) return cur01;
                    float t = Mathf.Sqrt(d2) / radius;
                    float f = 1f - t * t * (3f - 2f * t);                                              // smooth falloff
                    float n = 0.6f + 0.8f * Mathf.PerlinNoise(seed + wx * 0.03f, seed + wz * 0.03f);   // lumpy, not a perfect dome
                    return cur01 + sign * amount * f * n / maxH;
                });
        }

        /// Levels the ground to the height at the click point, with a soft edge.
        public static bool Flatten(TerrainWorld world, TerrainSettings s, Vector2 c, float radius)
        {
            if (!world.TrySampleHeight01(c.x, c.y, out float target01)) return false;
            float r2 = radius * radius;
            world.ModifyHeights("Flatten", c - Vector2.one * radius, c + Vector2.one * radius,
                (wx, wz, cur01) =>
                {
                    float dx = wx - c.x, dz = wz - c.y, d2 = dx * dx + dz * dz;
                    if (d2 >= r2) return cur01;
                    float t = Mathf.Sqrt(d2) / radius;
                    float w = t <= 0.6f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f);
                    return Mathf.Lerp(cur01, target01, w);
                });
            return true;
        }
    }
}