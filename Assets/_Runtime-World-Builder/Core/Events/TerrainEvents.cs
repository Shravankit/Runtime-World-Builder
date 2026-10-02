using UnityEngine;

namespace RuntimeWorldBuilder.Core.Events
{
    public readonly struct TerrainExtentChanged
    {
        public readonly Vector2 Min, Max;
        public TerrainExtentChanged(Vector2Int min, Vector2Int max)
        {
            Min = min;
            Max = max;
        }
    }

    public readonly struct TerrainChunkCreated
    {
        public readonly Vector2Int Coord;
        public TerrainChunkCreated(Vector2Int c)
        {
            Coord = c;
        }
    }
}