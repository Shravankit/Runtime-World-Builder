using System;
using System.IO;
using System.Reflection;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.Testing;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using SFB;

namespace RuntimeWorldBuilder.Runtime.Import
{
    public class HeightmapImportTool : MonoBehaviour
    {
        // Other terrain/camera input scripts must early-return while this is true.
        public static bool OverPanel { get; private set; }

        [Header("References")]
        [SerializeField] private HeightmapLibrary library;
        [SerializeField] private TerrainWorldTester tester;
        [SerializeField] private UIDocument uiDocument;

        private const int MaxChunksPerSide = 16;
        [Header("Heightmap")]
        [SerializeField] private string path = "";
        [SerializeField] private bool bigEndian;
        [SerializeField] private bool flipY;
        [SerializeField] private bool clearModels = true;

        [Header("Terrain Settings")]
        [SerializeField] private float sizeX = 1024f;
        [SerializeField] private float sizeZ = 1024f;
        [SerializeField, Range(10f, 1000f)] private float heightM = 1000f;

        private int pick;
        private VisualElement root;
        private TextField pathField;
        private Toggle bigEndianToggle, flipYToggle, clearModelsToggle;
        private Slider widthSlider, lengthSlider, heightSlider;
        private Label selectedHeightmapLabel, statusLabel, fileLabel;
        private Button previousButton, nextButton, importButton, buildButton, browseButton;
        private bool uiInitialized;

        private static T FindOne<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }

        private void Start()
        {
            if (tester == null) tester = FindOne<TerrainWorldTester>();
            if (uiDocument == null) uiDocument = FindOne<UIDocument>();
            int restored = HeightmapImporter.LoadSaved(library);
            InitializeUI();
            RefreshHeightmapSelection();
            SetStatus(restored > 0 ? $"Restored {restored} imported heightmap(s)." : "Choose a PNG, EXR, RAW or R16 heightmap.");
        }

        private void OnDisable() => OverPanel = false;

        private void InitializeUI()
        {
            if (uiDocument == null) { Debug.LogError("[HeightmapImportTool] UIDocument is missing."); return; }
            root = uiDocument.rootVisualElement;
            pathField = root.Q<TextField>("fld-heightmap-path");
            fileLabel = root.Q<Label>("lbl-heightmap-file");
            bigEndianToggle = root.Q<Toggle>("tgl-heightmap-big-endian");
            flipYToggle = root.Q<Toggle>("tgl-heightmap-flip-y");
            clearModelsToggle = root.Q<Toggle>("tgl-heightmap-clear-models");
            widthSlider = root.Q<Slider>("sld-heightmap-width");
            lengthSlider = root.Q<Slider>("sld-heightmap-length");
            heightSlider = root.Q<Slider>("sld-heightmap-height");
            selectedHeightmapLabel = root.Q<Label>("lbl-selected-heightmap");
            statusLabel = root.Q<Label>("lbl-heightmap-status");
            previousButton = root.Q<Button>("btn-heightmap-previous");
            nextButton = root.Q<Button>("btn-heightmap-next");
            browseButton = root.Q<Button>("btn-heightmap-browse");
            importButton = root.Q<Button>("btn-heightmap-import");
            buildButton = root.Q<Button>("btn-heightmap-build");

            if (pathField == null || fileLabel == null || bigEndianToggle == null || flipYToggle == null || clearModelsToggle == null ||
                widthSlider == null || lengthSlider == null || heightSlider == null || selectedHeightmapLabel == null || statusLabel == null ||
                previousButton == null || nextButton == null || browseButton == null || importButton == null || buildButton == null)
            { Debug.LogError("[HeightmapImportTool] Missing heightmap UXML element. Check RuntimeTerrain.uxml names."); return; }

            pathField.value = path;
            fileLabel.text = string.IsNullOrEmpty(path) ? "No heightmap selected" : Path.GetFileName(path);
            bigEndianToggle.value = bigEndian;
            flipYToggle.value = flipY;
            clearModelsToggle.value = clearModels;
            widthSlider.value = sizeX;
            lengthSlider.value = sizeZ;
            heightSlider.value = Mathf.Clamp(heightM, 10f, 1000f);

            bigEndianToggle.RegisterValueChangedCallback(e => bigEndian = e.newValue);
            flipYToggle.RegisterValueChangedCallback(e => flipY = e.newValue);
            clearModelsToggle.RegisterValueChangedCallback(e => clearModels = e.newValue);
            widthSlider.RegisterValueChangedCallback(e => sizeX = e.newValue);
            lengthSlider.RegisterValueChangedCallback(e => sizeZ = e.newValue);
            heightSlider.RegisterValueChangedCallback(e => heightM = Mathf.Clamp(e.newValue, 10f, 1000f));

            browseButton.clicked += BrowseForHeightmap;
            importButton.clicked += Import;
            buildButton.clicked += Build;
            previousButton.clicked += SelectPreviousHeightmap;
            nextButton.clicked += SelectNextHeightmap;
            uiInitialized = true;
        }

        private void BrowseForHeightmap()
        {
            string[] paths = StandaloneFileBrowser.OpenFilePanel(
                "Select Terrain Heightmap", string.IsNullOrEmpty(path) ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : Path.GetDirectoryName(path),
                new[] { new ExtensionFilter("Heightmaps", "png", "jpg", "jpeg", "exr", "raw", "r16") }, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0])) return;
            path = paths[0];
            pathField.value = path;
            fileLabel.text = Path.GetFileName(path);
            SetStatus("Selected " + Path.GetFileName(path) + ". Press Import & Build.");
        }

        private void Update()
        {
            OverPanel = false;
            if (!uiInitialized || root == null || root.panel == null) return;

            Vector2 screenPosition = Input.mousePosition;
            if (Input.touchCount > 0) screenPosition = Input.GetTouch(0).position;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, screenPosition);
            VisualElement picked = root.panel.Pick(panelPosition);
            OverPanel = IsInsideRoot(picked);
        }

        private bool IsInsideRoot(VisualElement element)
        {
            while (element != null)
            {
                if (element == root) return true;
                element = element.parent;
            }
            return false;
        }

        private void Import()
        {
            if (library == null) { SetStatus("Assign a HeightmapLibrary in the Inspector."); return; }
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { SetStatus("Choose an existing heightmap file first."); return; }

            Texture2D texture = HeightmapImporter.Load(path, bigEndian, flipY, true, out string error);
            if (texture == null) { SetStatus("Import failed: " + error); return; }

            pick = library.AddRuntime(texture);
            HeightmapStamp.Warm(texture);
            RefreshHeightmapSelection();
            SetStatus("Imported " + texture.name + ". Rebuilding terrain...");
            Build();
        }

        private void SelectPreviousHeightmap()
        {
            if (library == null || library.Count == 0) return;
            pick = (pick - 1 + library.Count) % library.Count;
            RefreshHeightmapSelection();
        }

        private void SelectNextHeightmap()
        {
            if (library == null || library.Count == 0) return;
            pick = (pick + 1) % library.Count;
            RefreshHeightmapSelection();
        }

        private void RefreshHeightmapSelection()
        {
            if (selectedHeightmapLabel == null) return;
            int count = library != null ? library.Count : 0;
            if (count == 0) { selectedHeightmapLabel.text = "Selected: (none)"; return; }
            pick = Mathf.Clamp(pick, 0, count - 1);
            Texture2D selected = library.Get(pick);
            selectedHeightmapLabel.text = selected != null ? $"Selected: {selected.name} ({pick + 1}/{count})" : $"Selected: (missing) ({pick + 1}/{count})";
        }

        private void ClearPlacedModels()
        {
#if UNITY_2023_1_OR_NEWER
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
#else
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
#endif
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || behaviour.GetType().Name != "ModelPlacerTool") continue;
                MethodInfo method = behaviour.GetType().GetMethod("ClearAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null) method.Invoke(behaviour, null);
            }
        }

        private void Build()
        {
            if (library == null || library.Count == 0) { SetStatus("No heightmap available."); return; }
            if (tester == null) { SetStatus("Assign the TerrainWorldTester."); return; }
            if (!ServiceRegistry.TryResolve<TerrainSettings>(out TerrainSettings settings)) { SetStatus("Terrain not initialised yet."); return; }
            pick = Mathf.Clamp(pick, 0, library.Count - 1);
            Texture2D texture = library.Get(pick);
            if (texture == null) { SetStatus("Selected heightmap is missing."); return; }

            float chunkSize = settings.chunkSize;
            int chunksX = Mathf.Clamp(Mathf.CeilToInt(sizeX / chunkSize), 1, MaxChunksPerSide);
            int chunksZ = Mathf.Clamp(Mathf.CeilToInt(sizeZ / chunkSize), 1, MaxChunksPerSide);
            float finalHeight = Mathf.Clamp(heightM, 10f, 1000f);
            if (clearModels) ClearPlacedModels();

            var world = tester.RebuildWorld(Vector2Int.zero, new Vector2Int(chunksX - 1, chunksZ - 1));
            if (world == null) { SetStatus("Failed to rebuild terrain world."); return; }

            float padding = settings.CellSpacing * 2f;
            var stamp = new HeightmapStamp
            {
                heightmap = texture,
                position = new Vector2(chunksX * chunkSize * 0.5f, chunksZ * chunkSize * 0.5f),
                size = new Vector2(chunksX * chunkSize + padding, chunksZ * chunkSize + padding),
                yawDegrees = 0f,
                heightMeters = finalHeight,
                baseY = 0f,
                blend = StampBlend.Replace,
                edgeFalloff = 0f,
                strength = 1f,
                invert = false
            };
            world.PlaceStamp(stamp);
            SetStatus($"Built {chunksX * chunkSize:F0} x {chunksZ * chunkSize:F0} m terrain from '{texture.name}', max height {finalHeight:F0} m.");
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
            Debug.Log("[HeightmapImportTool] " + message);
        }
    }
}
