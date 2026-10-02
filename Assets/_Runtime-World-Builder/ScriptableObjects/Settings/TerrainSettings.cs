using UnityEngine;

namespace RuntimeWorldBuilder.SO.Settings
{
    [CreateAssetMenu(fileName = "TerrainSettings", menuName = "Runtime Terrain Builder/TerrainSettings")]
    public class TerrainSettings : ScriptableObject
    {
        [Header("Chunk grid (do NOT change after terrain is created)")]
        [Min(16f)] public float chunkSize = 256f;
        [Tooltip("Must be 2^n + 1 (129, 257, 513...)")]
        public int heightmapResolution = 257;
        [Min(1f)] public float maxHeight = 200f;

        [Header("Initial extent (chunk coords, inclusive)")]
        public Vector2Int initialMin = new(0, 0);
        public Vector2Int initialMax = new(3, 3);

        [Header("Base generation (deterministic by world position)")]
        public int seed = 12345;
        public float noiseScale = 0.002f;
        [Range(1, 8)] public int octaves = 4;
        [Range(0f, 1f)] public float persistence = 0.5f;
        public float lacunarity = 2f;
        [Range(0f, 1f)] public float baseHeight01 = 0.1f;
        [Range(0f, 1f)] public float amplitude01 = 0.3f;

        public float CellSpacing => chunkSize / (heightmapResolution - 1);

        public Vector2Int WorldToChunk(Vector3 p) =>
            new(Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.z / chunkSize));

        public Vector3 ChunkOrigin(Vector2Int c) => new(c.x * chunkSize, 0f, c.y * chunkSize);

        public float SampleBase01(float worldX, float worldZ)
        {
            float amp = 1f, freq = noiseScale, sum = 0f, norm = 0f;
            var rng = new System.Random(seed);
            float ox = rng.Next(-10000, 10000), oz = rng.Next(-10000, 10000);
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise((worldX + ox) * freq, (worldZ + oz) * freq) * amp;
                norm += amp; amp *= persistence; freq *= lacunarity;
            }
            return baseHeight01 + (sum / norm) * amplitude01;
        }

        void OnValidate()
        {
            // snap to 2^n + 1
            int n = Mathf.Max(5, Mathf.RoundToInt(Mathf.Log(Mathf.Max(heightmapResolution - 1, 1), 2)));
            heightmapResolution = (1 << n) + 1;
        }
    }
}