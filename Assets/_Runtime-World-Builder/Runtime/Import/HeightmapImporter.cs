using System;
using System.IO;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.SO.HeightMap;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Import
{
    /// Loads heightmap files from disk while the game is running.
    ///   .png / .jpg   8-bit (Unity decodes 16-bit PNGs down to 8 bit, so expect terracing on smooth terrain)
    ///   .raw / .r16   square 16-bit (or 8-bit) heightmap, size inferred from the file length. Best quality.
    ///   .exr          cannot be decoded at runtime by Unity; export as .raw/.r16 or .png instead.
    /// Every import is copied into persistentDataPath/ImportedHeightmaps so saved worlds can find the
    /// stamp's heightmap again (stamps are stored by name). Call LoadSaved() once at startup.
    public static class HeightmapImporter
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "ImportedHeightmaps");

        public static Texture2D Load(string path, bool bigEndian, bool flipY, bool keepCopy, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "File not found."; return null; }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string name = Path.GetFileNameWithoutExtension(path);
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex;

                switch (ext)
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);   // linear, readable
                        if (!tex.LoadImage(bytes, false))
                        {
                            UnityEngine.Object.Destroy(tex);
                            error = "Could not decode the image.";
                            return null;
                        }
                        tex.Apply(true, false);
                        if (keepCopy) Persist(name + ext, bytes);
                        break;

                    case ".raw":
                    case ".r16":
                        tex = FromRaw(bytes, bigEndian, flipY, out var data, out error);
                        if (tex == null) return null;
                        if (keepCopy)
                        {
                            var le = new byte[data.Length * 2];
                            Buffer.BlockCopy(data, 0, le, 0, le.Length);
                            Persist(name + ".r16", le);                                   // normalised: little-endian, already flipped
                        }
                        break;

                    case ".exr":
                        error = "EXR can't be decoded at runtime. Export the heightmap as .raw/.r16 (best) or .png.";
                        return null;

                    default:
                        error = $"Unsupported file type '{ext}'. Use .png, .jpg, .raw or .r16.";
                        return null;
                }

                tex.name = name;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        static Texture2D FromRaw(byte[] b, bool big, bool flipY, out ushort[] data, out string error)
        {
            data = null; error = null;
            int n = b.Length;
            int res16 = Mathf.RoundToInt(Mathf.Sqrt(n / 2f));
            int res8 = Mathf.RoundToInt(Mathf.Sqrt(n));
            int res; bool is16;
            if (n % 2 == 0 && res16 * res16 * 2 == n) { res = res16; is16 = true; }
            else if (res8 * res8 == n) { res = res8; is16 = false; }
            else { error = $"RAW file is {n} bytes, which is not a square 8-bit or 16-bit heightmap."; return null; }

            data = new ushort[res * res];
            for (int z = 0; z < res; z++)
            {
                int dstRow = flipY ? res - 1 - z : z;
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;
                    ushort v;
                    if (is16)
                        v = big ? (ushort)((b[2 * i] << 8) | b[2 * i + 1]) : (ushort)(b[2 * i] | (b[2 * i + 1] << 8));
                    else
                        v = (ushort)(b[i] * 257);
                    data[dstRow * res + x] = v;
                }
            }

            var tex = new Texture2D(res, res, TextureFormat.R16, true, true);
            tex.SetPixelData(data, 0);
            tex.Apply(true, false);   // mip chain lets HeightmapStamp cache a 1024 version of very large maps
            return tex;
        }

        static void Persist(string fileName, byte[] bytes)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, fileName), bytes);
        }

        /// Re-registers everything imported in earlier sessions. Call once at startup, before loading a save.
        public static int LoadSaved(HeightmapLibrary lib)
        {
            if (lib == null || !Directory.Exists(Folder)) return 0;
            int count = 0;
            foreach (var f in Directory.GetFiles(Folder))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".r16") continue;
                if (lib.Find(Path.GetFileNameWithoutExtension(f)) != null) continue;

                var t = Load(f, false, false, false, out _);
                if (t == null) continue;
                lib.AddRuntime(t);
                HeightmapStamp.Warm(t);
                count++;
            }
            return count;
        }
    }
}