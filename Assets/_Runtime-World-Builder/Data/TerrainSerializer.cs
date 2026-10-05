using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Data
{
    public static class TerrainSerializer
    {
        const string Magic = "TCH1";

        public static string SlotPath(string slot) =>
            Path.Combine(Application.persistentDataPath, "TerrainSaves", slot);

        static string ChunkFile(Vector2Int c) => $"chunk_{c.x}_{c.y}.thc";

        public static void Save(TerrainWorld world, TerrainSettings s, string slot)
        {
            string dir = SlotPath(slot), chunkDir = Path.Combine(dir, "chunks");
            Directory.CreateDirectory(chunkDir);

            var data = TerrainSaveData.From(s, world);

            foreach (var d in world.Chunks)
            {
                if (!d.Dirty) continue;                       // untouched chunks are regenerated from the seed
                WriteChunk(Path.Combine(chunkDir, ChunkFile(d.Coord)), d.Heights, s.heightmapResolution);
                data.chunks.Add(d.Coord);
            }
            foreach (var st in world.Stamps) data.stamps.Add(StampSave.From(st));

            // manifest last, via temp file, so a crash can't leave a manifest pointing at missing chunks
            string tmp = Path.Combine(dir, "world.json.tmp"), final = Path.Combine(dir, "world.json");
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            if (File.Exists(final)) File.Delete(final);
            File.Move(tmp, final);

            Debug.Log($"Terrain saved: {data.chunks.Count} chunk files, {data.stamps.Count} stamps -> {dir}");
        }

        static void WriteChunk(string path, float[,] h, int res)
        {
            var u = new ushort[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    u[z * res + x] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(h[z, x]) * 65535f);

            var bytes = new byte[u.Length * 2];
            Buffer.BlockCopy(u, 0, bytes, 0, bytes.Length);

            using (var fs = File.Create(path))
            {
                fs.Write(Encoding.ASCII.GetBytes(Magic), 0, 4);
                fs.Write(BitConverter.GetBytes(res), 0, 4);
                using (var ds = new DeflateStream(fs, System.IO.Compression.CompressionLevel.Optimal, true))
                    ds.Write(bytes, 0, bytes.Length);
            }
        }

        //Load
        public static TerrainWorld Load(string slot, TerrainSettings s, TerrainBiomeSettings biome, HeightmapLibrary lib)
        {
            try
            {
                string dir = SlotPath(slot), jsonPath = Path.Combine(dir, "world.json");
                if (!File.Exists(jsonPath)) { Debug.LogWarning($"No save at {dir}"); return null; }

                var data = JsonUtility.FromJson<TerrainSaveData>(File.ReadAllText(jsonPath));
                if (data == null || data.version != 1) throw new InvalidDataException("Unknown save version");

                // read everything BEFORE touching the scene, so a bad file can't leave a half-loaded world
                var heights = new Dictionary<Vector2Int, float[,]>();
                foreach (var c in data.chunks)
                    heights[c] = ReadChunk(Path.Combine(dir, "chunks", ChunkFile(c)), data.heightmapResolution);

                var stamps = new List<HeightmapStamp>();
                foreach (var ss in data.stamps)
                {
                    var tex = lib != null ? lib.Find(ss.heightmapId) : null;
                    if (tex == null) { Debug.LogWarning($"Heightmap '{ss.heightmapId}' not in library, stamp skipped"); continue; }
                    stamps.Add(ss.ToStamp(tex));
                }

                data.ApplyTo(s);
                return new TerrainWorld(s, biome, data.extentMin, data.extentMax, heights, stamps);
            }
            catch (Exception e)
            {
                Debug.LogError($"Terrain load failed: {e.Message}");
                return null;
            }
        }

        static float[,] ReadChunk(string path, int expectedRes)
        {
            using var fs = File.OpenRead(path);
            var head = new byte[8];
            if (fs.Read(head, 0, 8) != 8 || Encoding.ASCII.GetString(head, 0, 4) != Magic)
                throw new InvalidDataException($"Bad chunk file {Path.GetFileName(path)}");
            int res = BitConverter.ToInt32(head, 4);
            if (res != expectedRes) throw new InvalidDataException($"Resolution mismatch in {Path.GetFileName(path)}");

            var bytes = new byte[res * res * 2];
            using (var ds = new DeflateStream(fs, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = ds.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) throw new EndOfStreamException($"Truncated chunk file {Path.GetFileName(path)}");
                    read += n;
                }
            }

            var u = new ushort[res * res];
            Buffer.BlockCopy(bytes, 0, u, 0, bytes.Length);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = u[z * res + x] / 65535f;
            return h;
        }
    }
}