using System;
using System.IO;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Import
{
    /// <summary>Runtime image decoder and persistence for terrain heightmaps.</summary>
    public static class HeightmapImporter
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "ImportedHeightmaps");

        public static Texture2D Load(string path, bool bigEndian, bool flipY, bool keepCopy, out string error)
        {
            error = null;
            Texture2D texture = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                { error = "File not found."; return null; }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                string name = Path.GetFileNameWithoutExtension(path);
                byte[] bytes = File.ReadAllBytes(path);

                switch (ext)
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
                        if (!ImageConversion.LoadImage(texture, bytes, false))
                        { UnityEngine.Object.Destroy(texture); error = "Could not decode PNG/JPG image."; return null; }
                        texture.Apply(true, false);
                        if (keepCopy) Persist(name + ext, bytes);
                        break;

                    case ".raw":
                    case ".r16":
                        texture = LoadRaw(bytes, bigEndian, flipY, out byte[] normalizedRaw, out error);
                        if (texture == null) return null;
                        if (keepCopy) Persist(name + ".r16", normalizedRaw);
                        break;

                    case ".exr":
#if RWB_ENABLE_TINYEXR
                        texture = LoadExr(path, out error);
                        if (texture == null) return null;
                        if (keepCopy) Persist(name + ext, bytes);
#else
                        error = "EXR decoder is not enabled. Install TinyEXR.NET, then add scripting define RWB_ENABLE_TINYEXR.";
                        return null;
#endif
                        break;

                    default:
                        error = $"Unsupported file type '{ext}'. Choose PNG, JPG, EXR, RAW or R16.";
                        return null;
                }

                texture.name = name;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch (Exception ex)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                error = ex.Message;
                return null;
            }
        }

        private static Texture2D LoadRaw(byte[] bytes, bool bigEndian, bool flipY, out byte[] normalizedLittleEndian, out string error)
        {
            normalizedLittleEndian = null;
            error = null;
            int length = bytes.Length;
            int res16 = Mathf.RoundToInt(Mathf.Sqrt(length / 2f));
            int res8 = Mathf.RoundToInt(Mathf.Sqrt(length));
            bool is16;
            int res;
            if (length % 2 == 0 && res16 * res16 * 2 == length) { res = res16; is16 = true; }
            else if (res8 * res8 == length) { res = res8; is16 = false; }
            else { error = $"RAW file ({length} bytes) is not a square 8-bit or 16-bit heightmap."; return null; }

            var pixels = new ushort[res * res];
            for (int y = 0; y < res; y++)
            {
                int dstY = flipY ? res - 1 - y : y;
                for (int x = 0; x < res; x++)
                {
                    int src = y * res + x;
                    ushort value = is16
                        ? (bigEndian ? (ushort)((bytes[src * 2] << 8) | bytes[src * 2 + 1]) : (ushort)(bytes[src * 2] | (bytes[src * 2 + 1] << 8)))
                        : (ushort)(bytes[src] * 257);
                    pixels[dstY * res + x] = value;
                }
            }

            normalizedLittleEndian = new byte[pixels.Length * 2];
            Buffer.BlockCopy(pixels, 0, normalizedLittleEndian, 0, normalizedLittleEndian.Length);
            var tex = new Texture2D(res, res, TextureFormat.R16, true, true);
            tex.SetPixelData(pixels, 0);
            tex.Apply(true, false);
            return tex;
        }

#if RWB_ENABLE_TINYEXR
        private static Texture2D LoadExr(string path, out string error)
        {
            error = null;
            try
            {
                TinyEXR.Exr.LoadFromFile(path, out float[] rgba, out int width, out int height);
                if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 3)
                { error = "EXR decoder returned invalid pixel data."; return null; }

                // Convert RGB/RGBA float pixels to a single-channel normalized heightmap.
                // The min/max remap makes HDR values usable as a terrain height range.
                int count = width * height;
                var gray = new float[count];
                float min = float.PositiveInfinity;
                float max = float.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    int p = i * (rgba.Length >= count * 4 ? 4 : 3);
                    float r = rgba[p];
                    float g = rgba[p + 1];
                    float b = rgba[p + 2];
                    float v = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                    gray[i] = v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                float range = max - min;
                if (range <= 0.000001f) { error = "EXR heightmap has no height variation."; return null; }
                for (int i = 0; i < count; i++) gray[i] = Mathf.Clamp01((gray[i] - min) / range);

                var tex = new Texture2D(width, height, TextureFormat.RFloat, true, true);
                tex.SetPixelData(gray, 0);
                tex.Apply(true, false);
                return tex;
            }
            catch (Exception ex) { error = "EXR decode failed: " + ex.Message; return null; }
        }
#endif

        private static void Persist(string fileName, byte[] bytes)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, fileName), bytes);
        }

        public static int LoadSaved(SO.HeightMap.HeightmapLibrary library)
        {
            if (library == null || !Directory.Exists(Folder)) return 0;
            int count = 0;
            foreach (string file in Directory.GetFiles(Folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".r16" && ext != ".exr") continue;
                string id = Path.GetFileNameWithoutExtension(file);
                if (library.Find(id) != null) continue;
                Texture2D tex = Load(file, false, false, false, out _);
                if (tex == null) continue;
                library.AddRuntime(tex);
                RuntimeWorldBuilder.Runtime.Stamp.HeightmapStamp.Warm(tex);
                count++;
            }
            return count;
        }
    }
}
