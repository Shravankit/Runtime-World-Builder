using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.SO.HeightMap
{
    [CreateAssetMenu(fileName = "HeightmapLibrary", menuName = "Runtime Terrain Builder/HeightmapLibrary")]
    public class HeightmapLibrary : ScriptableObject
    {
        public List<Texture2D> textures = new();

        // Heightmaps imported while the game runs. Kept out of the serialized list so play mode
        // never writes them into the asset.
        [NonSerialized] List<Texture2D> runtime;
        List<Texture2D> Runtime => runtime ??= new List<Texture2D>();

        public int Count => textures.Count + Runtime.Count;
        public Texture2D Get(int i) => i < textures.Count ? textures[i] : Runtime[i - textures.Count];

        public IEnumerable<Texture2D> All()
        {
            foreach (var t in textures) if (t != null) yield return t;
            foreach (var t in Runtime) if (t != null) yield return t;
        }

        public Texture2D Find(string id)
        {
            var t = textures.Find(x => x != null && x.name == id);
            return t != null ? t : Runtime.Find(x => x != null && x.name == id);
        }

        /// Adds (or replaces, by name) a runtime-imported heightmap. Returns its index for Get().
        public int AddRuntime(Texture2D tex)
        {
            int i = Runtime.FindIndex(x => x != null && x.name == tex.name);
            if (i >= 0) { Runtime[i] = tex; return textures.Count + i; }
            Runtime.Add(tex);
            return textures.Count + Runtime.Count - 1;
        }
    }
}