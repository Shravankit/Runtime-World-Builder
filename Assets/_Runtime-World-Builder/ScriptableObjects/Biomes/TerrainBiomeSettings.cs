using System;
using UnityEngine;

namespace RuntimeWorldBuilder.SO.Biomes
{
    [Serializable]
    public class TerrainBiomeRule
    {
        public string name = "Layer";
        public TerrainLayer terrainLayer;

        [Header("Altitude (Normalized 0..1)")]
        [Range(0f, 1f)] public float minHeight = 0f;
        [Range(0f, 1f)] public float maxHeight = 1f;
        [Range(0.001f, 0.3f)] public float heightTransition = 0.05f;

        [Header("Slope (Degrees 0..90)")]
        [Range(0f, 90f)] public float minSlope = 0f;
        [Range(0f, 90f)] public float maxSlope = 90f;
        [Range(0.1f, 20f)] public float slopeTransition = 5f;

        [Header("Noise Breakup")]
        public float noiseScale = 0.02f;
        public float noiseImpact = 0.15f;

        static float Band(float v, float min, float max, float t)
        {
            t = Mathf.Max(t, 0.001f);
            float lo = Smooth(min - t, min + t, v);
            float hi = 1f - Smooth(max - t, max + t, v);
            return lo * hi;
        }

        static float Smooth(float a, float b, float x)
        {
            float k = Mathf.Clamp01((x - a) / (b - a));
            return k * k * (3f - 2f * k);
        }
        public float EvaluateWeight(float h01, float slopeDeg, float wx, float wz)
        {
            float weight = Band(h01, minHeight, maxHeight, heightTransition)
                 * Band(slopeDeg, minSlope, maxSlope, slopeTransition);

            if (noiseImpact > 0f && weight > 0.001f)
            {
                float noise = Mathf.PerlinNoise(wx * noiseScale, wz * noiseScale) * 2f - 1f;
                weight = Mathf.Clamp01(weight + noise * noiseImpact);
            }
            return weight;
        }
    }

    [CreateAssetMenu(fileName = "TerrainBiomeSettings", menuName = "Runtime Terrain Builder/TerrainBiomeSettings")]
    public class TerrainBiomeSettings : ScriptableObject
    {
        public int alphamapResolution = 256;
        public TerrainBiomeRule[] layers;
    }
}
