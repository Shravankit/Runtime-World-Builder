using System.Collections.Generic;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public class TerrainChunkData
    {
        public Vector2Int Coord;
        public float[,] Heights;
        public Terrain Terrain;
    }

    public class TerrainWorld
    {
        readonly TerrainSettings s;
        readonly Dictionary<Vector2Int, TerrainChunkData> chunks = new();

        public Vector2Int Min { get; private set; }
        public Vector2Int Max { get; private set; }

        public TerrainWorld(TerrainSettings terrainSettings)
        {
            s = terrainSettings;
            Min = s.initialMin;
            Max = s.initialMax;

            for (int z = Min.y; z <= Max.y; z++)
                for (int x = Min.x; x <= Max.x; x++)
                    CreateChunk(new Vector2Int(x, z));
        }

        public bool TryGetChunk(Vector2Int c, out TerrainChunkData d) => chunks.TryGetValue(c, out d);

        /// Grow extent by N chunks per side. Existing chunks are never modified.
        public void Extend(int left, int right, int down, int up)
        {
            var newMin = new Vector2Int(Min.x - left, Min.y - down);
            var newMax = new Vector2Int(Max.x + right, Max.y + up);

            for (int z = newMin.y; z <= newMax.y; z++)
                for (int x = newMin.x; x <= newMax.x; x++)
                {
                    var c = new Vector2Int(x, z);
                    if (!chunks.ContainsKey(c)) CreateChunk(c);
                }

            Min = newMin; Max = newMax;
            // e.g. ServiceRegistry.Resolve<EventBus>().Publish(new TerrainExtentChanged(Min, Max));
        }

        void CreateChunk(Vector2Int c)
        {
            int res = s.heightmapResolution;
            var h = new float[res, res];
            Vector3 origin = s.ChunkOrigin(c);

            // 1) fill from deterministic base
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = s.SampleBase01(origin.x + x * s.CellSpacing, origin.z + z * s.CellSpacing);

            // 2) stitch shared edges from existing (possibly edited) neighbours
            int last = res - 1;
            if (chunks.TryGetValue(c + Vector2Int.left, out var L))
                for (int z = 0; z < res; z++) h[z, 0] = L.Heights[z, last];
            if (chunks.TryGetValue(c + Vector2Int.right, out var R))
                for (int z = 0; z < res; z++) h[z, last] = R.Heights[z, 0];
            if (chunks.TryGetValue(c + Vector2Int.down, out var D))
                for (int x = 0; x < res; x++) h[0, x] = D.Heights[last, x];
            if (chunks.TryGetValue(c + Vector2Int.up, out var U))
                for (int x = 0; x < res; x++) h[last, x] = U.Heights[0, x];

            var data = new TerrainChunkData { Coord = c, Heights = h };
            data.Terrain = BuildTerrainObject(data, origin);
            chunks[c] = data;
        }

        Terrain BuildTerrainObject(TerrainChunkData d, Vector3 origin)
        {
            var td = new TerrainData { heightmapResolution = s.heightmapResolution };
            td.size = new Vector3(s.chunkSize, s.maxHeight, s.chunkSize);
            td.SetHeights(0, 0, d.Heights);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = $"Chunk_{d.Coord.x}_{d.Coord.y}";
            go.transform.position = origin;
            return go.GetComponent<Terrain>();
        }
    }
}