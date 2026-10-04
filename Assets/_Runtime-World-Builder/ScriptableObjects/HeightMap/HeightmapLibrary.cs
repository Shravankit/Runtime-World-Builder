using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.SO.HeightMap
{
    [CreateAssetMenu(fileName = "HeightmapLibrary", menuName = "Runtime Terrain Builder/HeightmapLibrary")]
    public class HeightmapLibrary : ScriptableObject
    {
        public List<Texture2D> textures = new();
        public Texture2D Find(string id) => textures.Find(t => t != null && t.name == id);
    }
}