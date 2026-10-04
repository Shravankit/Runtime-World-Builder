using System;
using System.Collections.Generic;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Data
{
    [Serializable]
    public class TerrainSaveData
    {
        public int version = 1;

        //grid - must match to the chunk data
        public float chunkSize;
        public int heightmapResolution;
        public float maxHeight;

        //base generation
        public int seed;
        public float noiseScale, persistence, lacunarity, baseHeight01, amplitude01;
        public int octaves;

        public Vector2Int extentMin, extentMax;
        public List<Vector2Int> chunks = new();   // chunks that have a .thc file
        public List<StampSave> stamps = new();

        public static TerrainSaveData From(TerrainSettings s, TerrainWorld w) => new()
        {
            chunkSize = s.chunkSize,
            heightmapResolution = s.heightmapResolution,
            maxHeight = s.maxHeight,
            seed = s.seed,
            noiseScale = s.noiseScale,
            octaves = s.octaves,
            persistence = s.persistence,
            lacunarity = s.lacunarity,
            baseHeight01 = s.baseHeight01,
            amplitude01 = s.amplitude01,
            extentMin = w.Min,
            extentMax = w.Max,
        };

        public void ApplyTo(TerrainSettings s)
        {
            s.chunkSize = chunkSize;
            s.heightmapResolution = heightmapResolution;
            s.maxHeight = maxHeight;
            s.seed = seed;
            s.noiseScale = noiseScale;
            s.octaves = octaves;
            s.persistence = persistence;
            s.lacunarity = lacunarity;
            s.baseHeight01 = baseHeight01;
            s.amplitude01 = amplitude01;
        }
    }

    [Serializable]
    public class StampSave
    {
        public string heightmapId;            // texture name, resolved via HeightmapLibrary
        public Vector2 position, size;
        public float yawDegrees, heightMeters, baseY, edgeFalloff, strength;
        public int blend;
        public bool invert;

        public static StampSave From(HeightmapStamp s) => new()
        {
            heightmapId = s.heightmap != null ? s.heightmap.name : "",
            position = s.position,
            size = s.size,
            yawDegrees = s.yawDegrees,
            heightMeters = s.heightMeters,
            baseY = s.baseY,
            blend = (int)s.blend,
            edgeFalloff = s.edgeFalloff,
            strength = s.strength,
            invert = s.invert,
        };

        public HeightmapStamp ToStamp(Texture2D tex)
        {
            var st = new HeightmapStamp
            {
                heightmap = tex,
                position = position,
                size = size,
                yawDegrees = yawDegrees,
                heightMeters = heightMeters,
                baseY = baseY,
                blend = (StampBlend)blend,
                edgeFalloff = edgeFalloff,
                strength = strength,
                invert = invert,
            };
            st.Prepare();
            return st;
        }
    }
}