
using System;
using System.Collections.Generic;
using System.IO;
using SFB;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Import
{
    public class TerrainLayerImporter : MonoBehaviour
    {
        [Header("Terrain Settings")]
        [SerializeField] private Vector2 tileSize = new Vector2(15, 15);
        [SerializeField] private Vector2 tileOffset = Vector2.zero;

        [Header("UI")]
        [SerializeField] private UIDocument uiDocument;

        private Button browseButton;
        private Label statusLabel;

        private RuntimeWorldBuilder.Runtime.Testing.TerrainWorldTester terrainTester;

        private readonly List<TerrainLayer> layers = new();
        private readonly List<Texture2D> runtimeTextures = new();

        private class TextureSet
        {
            public string name;
            public string baseColor;
            public string normal;
            public string mask;
        }

        private readonly Dictionary<string, TextureSet> textureSets = new();

        private void Start()
        {
            terrainTester =
                FindFirstObjectByType<RuntimeWorldBuilder.Runtime.Testing.TerrainWorldTester>();

            if (uiDocument == null)
                uiDocument = FindFirstObjectByType<UIDocument>();

            if (uiDocument == null)
            {
                Debug.LogError("[TerrainLayerImporter] UIDocument missing.");
                return;
            }

            var root = uiDocument.rootVisualElement;

            browseButton = root.Q<Button>("btn-import-terrain-layer");
            statusLabel = root.Q<Label>("lbl-layer-import-status");

            if (browseButton != null)
                browseButton.clicked += BrowseTextures;
            else
                Debug.LogWarning("Import terrain layer button not found.");

            if (terrainTester == null)
                SetStatus("Waiting for TerrainWorldTester.");
        }

        private void OnDestroy()
        {
            if (browseButton != null)
                browseButton.clicked -= BrowseTextures;
        }

        public void BrowseTextures()
        {
            if (terrainTester == null)
            {
                terrainTester =
                    FindFirstObjectByType<RuntimeWorldBuilder.Runtime.Testing.TerrainWorldTester>();
            }

            if (terrainTester == null)
            {
                SetStatus("TerrainWorldTester not found.");
                return;
            }

            var extensions = new[]
            {
                new ExtensionFilter(
                    "PBR Textures",
                    "png", "jpg", "jpeg"
                )
            };

            string[] paths = StandaloneFileBrowser.OpenFilePanel(
                "Import Terrain PBR Textures",
                "",
                extensions,
                true
            );

            if (paths == null || paths.Length == 0)
                return;

            ImportTextureSet(paths);
        }

        public void ImportTextureSet(string[] paths)
        {
            textureSets.Clear();

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    !File.Exists(path))
                    continue;

                string fileName =
                    Path.GetFileNameWithoutExtension(path);

                if (!TryGetMapType(
                    fileName.ToLowerInvariant(),
                    out string layerName,
                    out string mapType))
                    continue;

                if (!textureSets.TryGetValue(layerName, out var set))
                {
                    set = new TextureSet { name = layerName };
                    textureSets.Add(layerName, set);
                }

                switch (mapType)
                {
                    case "base":
                        set.baseColor = path;
                        break;

                    case "normal":
                        set.normal = path;
                        break;

                    case "mask":
                        set.mask = path;
                        break;
                }
            }

            int created = 0;

            foreach (var set in textureSets.Values)
            {
                if (CreateTerrainLayer(set))
                    created++;
            }

            if (created > 0)
            {
                terrainTester.ApplyTerrainLayers(layers.ToArray());

                SetStatus(
                    $"Imported {created} layer(s). Total: {layers.Count}"
                );
            }
            else
            {
                SetStatus(
                    "No valid layers found. Check filenames and Base Color maps."
                );
            }
        }

        private bool TryGetMapType(
            string fileName,
            out string layerName,
            out string mapType)
        {
            layerName = null;
            mapType = null;

            string[][] suffixGroups =
            {
                new[]
                {
                    "_basecolor", "_base_color",
                    "_albedo", "_diffuse", "_base"
                },
                new[]
                {
                    "_normalgl", "_normaldx", "_normal"
                },
                new[]
                {
                    "_maskmap", "_mask"
                }
            };

            string[] types = { "base", "normal", "mask" };

            for (int i = 0; i < suffixGroups.Length; i++)
            {
                foreach (string suffix in suffixGroups[i])
                {
                    if (!fileName.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    layerName = fileName.Substring(
                        0,
                        fileName.Length - suffix.Length
                    ).Trim('_', ' ');

                    mapType = types[i];

                    return !string.IsNullOrEmpty(layerName);
                }
            }

            return false;
        }

        private bool CreateTerrainLayer(TextureSet set)
        {
            if (string.IsNullOrWhiteSpace(set.baseColor))
            {
                Debug.LogWarning(
                    $"Missing Base Color for {set.name}"
                );
                return false;
            }

            Texture2D baseMap = LoadTexture(set.baseColor, false);

            if (baseMap == null)
                return false;

            Texture2D normalMap = null;
            Texture2D maskMap = null;

            if (!string.IsNullOrEmpty(set.normal))
                normalMap = LoadTexture(set.normal, true);

            if (!string.IsNullOrEmpty(set.mask))
                maskMap = LoadTexture(set.mask, true);

            var layer = new TerrainLayer
            {
                name = set.name,
                diffuseTexture = baseMap,
                normalMapTexture = normalMap,
                maskMapTexture = maskMap,
                tileSize = tileSize,
                tileOffset = tileOffset
            };

            int existingIndex = layers.FindIndex(
                x => x != null && x.name == set.name
            );

            if (existingIndex >= 0)
            {
                layers[existingIndex] = layer;
            }
            else
            {
                layers.Add(layer);
            }

            Debug.Log($"Created terrain layer: {set.name}");

            return true;
        }

        private Texture2D LoadTexture(string path, bool linear)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);

                Texture2D texture = new Texture2D(
                    2, 2,
                    TextureFormat.RGBA32,
                    true,
                    linear
                );

                if (!texture.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                texture.name =
                    Path.GetFileNameWithoutExtension(path);

                texture.wrapMode = TextureWrapMode.Repeat;
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = 8;

                texture.Apply(true, true);

                runtimeTextures.Add(texture);

                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"Texture import failed: {e.Message}"
                );

                return null;
            }
        }

        public void SetTileSize(Vector2 size)
        {
            tileSize = new Vector2(
                Mathf.Max(0.1f, size.x),
                Mathf.Max(0.1f, size.y)
            );
        }

        private void SetStatus(string message)
        {
            Debug.Log("[TerrainLayerImporter] " + message);

            if (statusLabel != null)
                statusLabel.text = message;
        }
    }
}
