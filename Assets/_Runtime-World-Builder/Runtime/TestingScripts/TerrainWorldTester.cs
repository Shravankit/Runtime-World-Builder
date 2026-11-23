using System;
using System.Collections.Generic;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Data;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using RuntimeWorldBuilder.SO.Vegetation;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class TerrainWorldTester : MonoBehaviour
    {
        [Header("Terrain References")]
        [SerializeField] private TerrainSettings settings;
        [SerializeField] private TerrainBiomeSettings biomeSettings;
        [SerializeField] private VegetationSettings vegetationSettings;
        [SerializeField] private HeightmapLibrary library;

        [Header("Save Slot")]
        [SerializeField] private string slot = "slot1";

        [Header("UI Toolkit")]
        [SerializeField] private UIDocument uiDocument;

        private TerrainWorld world;

        private Vector2Int editChunk = new Vector2Int(0, 0);

        private int editX;
        private int editZ;

        private float editValue = -1f;
        private double checksumBefore = -1;

        private string status = "Press Edit Chunk (0,0) first.";

        private VisualElement root;
        private TextField worldNameField;
        private Label statusLabel;
        private Button undoButton;
        private Button redoButton;

        private bool uiInitialized;

        // Runtime material layers shared by every terrain chunk.
        private TerrainLayer[] runtimeTerrainLayers =
            Array.Empty<TerrainLayer>();

        private bool lastCanUndo, lastCanRedo;

        // --------------------------------------------------
        // INITIALIZATION
        // --------------------------------------------------

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogError("TerrainSettings is not assigned.");
                SetStatus("TerrainSettings is missing.");
                return;
            }

            if (biomeSettings == null)
            {
                Debug.LogError("TerrainBiomeSettings is not assigned.");
                SetStatus("TerrainBiomeSettings is missing.");
                return;
            }

            try
            {
                world = new TerrainWorld(
                    settings,
                    biomeSettings,
                    vegetationSettings
                );

                ApplyLayersToGeneratedChunks();

                ServiceRegistry.Register(settings);
                ServiceRegistry.Register(world);

                InitializeUI();
                FrameCamera();
                RefreshUndoRedoButtons();
                SetStatus(status);
            }
            catch (Exception e)
            {
                Debug.LogError("Terrain initialization failed: " + e);
                SetStatus("Terrain initialization failed. Check Console.");
            }
        }

        private void InitializeUI()
        {
            if (uiDocument == null)
                uiDocument = FindFirstObjectByType<UIDocument>();

            if (uiDocument == null)
            {
                Debug.LogError(
                    "[TerrainWorldTester] No UIDocument found."
                );
                return;
            }

            root = uiDocument.rootVisualElement;

            // Settings controls
            RegisterButton("btn-edit-chunk", EditChunk);

            RegisterButton("btn-expand-pos-x",
                () => Extend(0, 1, 0, 0));

            RegisterButton("btn-expand-neg-x",
                () => Extend(1, 0, 0, 0));

            RegisterButton("btn-expand-pos-z",
                () => Extend(0, 0, 0, 1));

            RegisterButton("btn-expand-neg-z",
                () => Extend(0, 0, 1, 0));

            RegisterButton("btn-verify-continuity", Verify);

            RegisterButton("btn-save-world", SaveWorld);
            RegisterButton("btn-load-world", LoadWorld);

            // Top bar controls
            RegisterButton("btn-quick-save", SaveWorld);
            RegisterButton("btn-quick-load", LoadWorld);

            undoButton = root.Q<Button>("btn-undo");
            redoButton = root.Q<Button>("btn-redo");

            if (undoButton != null)
                undoButton.clicked += DoUndo;

            if (redoButton != null)
                redoButton.clicked += DoRedo;

            worldNameField = root.Q<TextField>("txt-world-name");
            statusLabel = root.Q<Label>("lbl-tester-status");

            if (worldNameField != null)
                worldNameField.value = slot;

            uiInitialized = true;
        }

        private void RegisterButton(string elementName, Action callback)
        {
            Button button = root.Q<Button>(elementName);

            if (button == null)
            {
                Debug.LogWarning(
                    $"[TerrainWorldTester] UI button '{elementName}' not found."
                );
                return;
            }

            button.clicked += callback;
        }

        // --------------------------------------------------
        // UPDATE / KEYBOARD SHORTCUTS
        // --------------------------------------------------

        private void Update()
        {
            if (world == null)
                return;

            world.Tick(4f);

            // Keep Undo/Redo buttons in sync with the history, whatever caused the change
            bool canUndo = world.CanUndo, canRedo = world.CanRedo;
            if (canUndo != lastCanUndo || canRedo != lastCanRedo)
            {
                lastCanUndo = canUndo;
                lastCanRedo = canRedo;
                RefreshUndoRedoButtons();
            }

            bool ctrl =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftCommand) ||
                Input.GetKey(KeyCode.RightCommand);

            if (!ctrl)
                return;

            if (uiInitialized &&
                root != null &&
                root.focusController.focusedElement is TextField)
                return;

            bool shift =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (shift) DoRedo();
                else DoUndo();
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                DoRedo();
            }
        }

        // --------------------------------------------------
        // REBUILD WORLD
        // --------------------------------------------------

        public TerrainWorld RebuildWorld(Vector2Int min, Vector2Int max)
        {
            if (settings == null)
            {
                SetStatus("Cannot rebuild: TerrainSettings is missing.");
                return null;
            }

            if (biomeSettings == null)
            {
                SetStatus("Cannot rebuild: TerrainBiomeSettings is missing.");
                return null;
            }

            if (min.x > max.x || min.y > max.y)
            {
                SetStatus("Invalid terrain world bounds.");
                return null;
            }

            TerrainWorld newWorld = null;

            try
            {
                newWorld = new TerrainWorld(
                    settings,
                    biomeSettings,
                    min,
                    max,
                    new Dictionary<Vector2Int, float[,]>(),
                    new List<HeightmapStamp>(),
                    vegetationSettings
                );

                if (newWorld == null)
                {
                    SetStatus("New terrain world could not be created.");
                    return null;
                }

                TerrainWorld oldWorld = world;

                world = newWorld;

                ApplyLayersToGeneratedChunks();

                ServiceRegistry.Register(settings);
                ServiceRegistry.Register(world);

                if (oldWorld != null)
                    oldWorld.DestroyAll();

                // Roads, rivers, lakes and markers belong to the old terrain
                RuntimeWorldBuilder.Runtime.Features.FeatureEvents.RequestClear();

                editValue = -1f;
                checksumBefore = -1;

                FrameCamera();

                SetStatus($"World rebuilt: {world.Min} -> {world.Max}");

                return world;
            }
            catch (Exception e)
            {
                Debug.LogError("World rebuild failed: " + e);

                if (newWorld != null && newWorld != world)
                    newWorld.DestroyAll();

                SetStatus("World rebuild failed. Check Console.");

                return null;
            }
        }

        // --------------------------------------------------
        // CAMERA
        // --------------------------------------------------

        private void FrameCamera()
        {
            if (world == null || settings == null)
                return;

            Camera cam = Camera.main;

            if (cam == null)
            {
                Debug.LogWarning("No Main Camera found.");
                return;
            }

            float cx = (world.Min.x + world.Max.x + 1) * 0.5f * settings.chunkSize;
            float cz = (world.Min.y + world.Max.y + 1) * 0.5f * settings.chunkSize;

            float spanX = (world.Max.x - world.Min.x + 1) * settings.chunkSize;
            float spanZ = (world.Max.y - world.Min.y + 1) * settings.chunkSize;
            float span = Mathf.Max(spanX, spanZ);

            cam.farClipPlane = 10000f;

            var orbit = cam.GetComponent<RuntimeWorldBuilder.Runtime.Features.OrbitCameraController>();
            if (orbit != null)
            {
                // north (+Z) up the screen, tilted, whole terrain in view
                orbit.FocusOn(new Vector3(cx, settings.maxHeight * 0.15f, cz), span);
                return;
            }

            // Fallback without the orbit controller: same north-facing angle
            cam.transform.position = new Vector3(cx, span * 0.6f, cz - span * 0.85f);
            cam.transform.LookAt(new Vector3(cx, 0f, cz));
        }

        // --------------------------------------------------
        // EDIT CHUNK
        // --------------------------------------------------

        private void EditChunk()
        {
            if (world == null)
                return;

            if (!world.TryGetChunk(editChunk, out var chunk))
            {
                SetStatus("Chunk (0,0) missing.");
                return;
            }

            int resolution = settings.heightmapResolution;

            editX = resolution / 2;
            editZ = resolution / 2;

            int radius = 25;

            world.BeginEdit("Bump");
            world.Touch(chunk);

            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    float distance =
                        Mathf.Sqrt(x * x + z * z) / radius;

                    if (distance > 1f)
                        continue;

                    chunk.Heights[editZ + z, editX + x] +=
                        (1f - distance) * 0.4f;
                }
            }

            chunk.Heights[editZ, editX] =
                Mathf.Clamp01(chunk.Heights[editZ, editX]);

            world.CommitChunk(chunk);
            world.EndEdit();

            editValue = chunk.Heights[editZ, editX];
            checksumBefore = Checksum(chunk);

            SetStatus(
                $"Edited chunk (0,0). Center height = {editValue:F4}"
            );

            RefreshUndoRedoButtons();
        }

        // --------------------------------------------------
        // EXTEND WORLD
        // --------------------------------------------------

        private void Extend(int left, int right, int down, int up)
        {
            if (world == null)
                return;

            world.Extend(left, right, down, up);

            ApplyLayersToGeneratedChunks();

            FrameCamera();
            Verify();
        }

        // --------------------------------------------------
        // VERIFY WORLD
        // --------------------------------------------------

        private void Verify()
        {
            if (world == null)
                return;

            string message =
                $"Extent {world.Min} -> {world.Max}\n";

            if (editValue >= 0 &&
                world.TryGetChunk(editChunk, out var chunk))
            {
                float live =
                    chunk.Terrain.terrainData.GetHeights(
                        editX,
                        editZ,
                        1,
                        1
                    )[0, 0];

                double sumNow = Checksum(chunk);

                bool same =
                    Mathf.Abs(live - editValue) < 1e-5f &&
                    Math.Abs(sumNow - checksumBefore) < 1e-4;

                message +=
                    "Old edit preserved: " +
                    $"{(same ? "PASS" : "FAIL")} " +
                    $"(live {live:F4}, expected {editValue:F4})\n";
            }

            float seamGap = MaxSeamGap();

            message +=
                $"Max seam gap: {seamGap:F6} " +
                (seamGap < 1e-5f ? "(PASS)" : "(FAIL)");

            SetStatus(message);
        }

        private double Checksum(TerrainChunkData chunk)
        {
            var heights =
                chunk.Terrain.terrainData.GetHeights(
                    0,
                    0,
                    settings.heightmapResolution,
                    settings.heightmapResolution
                );

            double sum = 0;

            foreach (float value in heights)
                sum += value;

            return sum;
        }

        private float MaxSeamGap()
        {
            if (world == null)
                return 0f;

            int resolution = settings.heightmapResolution;
            int last = resolution - 1;

            float max = 0f;

            for (int z = world.Min.y; z <= world.Max.y; z++)
            {
                for (int x = world.Min.x; x <= world.Max.x; x++)
                {
                    if (!world.TryGetChunk(
                        new Vector2Int(x, z),
                        out var a))
                        continue;

                    var heightsA =
                        a.Terrain.terrainData.GetHeights(
                            0, 0, resolution, resolution
                        );

                    if (world.TryGetChunk(
                        new Vector2Int(x + 1, z),
                        out var right))
                    {
                        var heightsRight =
                            right.Terrain.terrainData.GetHeights(
                                0, 0, resolution, resolution
                            );

                        for (int i = 0; i < resolution; i++)
                        {
                            max = Mathf.Max(
                                max,
                                Mathf.Abs(
                                    heightsA[i, last] -
                                    heightsRight[i, 0]
                                )
                            );
                        }
                    }

                    if (world.TryGetChunk(
                        new Vector2Int(x, z + 1),
                        out var up))
                    {
                        var heightsUp =
                            up.Terrain.terrainData.GetHeights(
                                0, 0, resolution, resolution
                            );

                        for (int i = 0; i < resolution; i++)
                        {
                            max = Mathf.Max(
                                max,
                                Mathf.Abs(
                                    heightsA[last, i] -
                                    heightsUp[0, i]
                                )
                            );
                        }
                    }
                }
            }

            return max;
        }

        // --------------------------------------------------
        // SAVE / LOAD
        // --------------------------------------------------

        private void UpdateSlotFromUI()
        {
            if (worldNameField != null &&
                !string.IsNullOrWhiteSpace(worldNameField.value))
            {
                slot = worldNameField.value.Trim();
            }
        }

        private void SaveWorld()
        {
            if (world == null)
            {
                SetStatus("No terrain world to save.");
                return;
            }

            UpdateSlotFromUI();

            TerrainSerializer.Save(
                world,
                settings,
                slot
            );

            SetStatus($"Saved world '{slot}'.");
        }

        private void LoadWorld()
        {
            if (world == null)
            {
                SetStatus("No terrain world available.");
                return;
            }

            UpdateSlotFromUI();

            TerrainWorld loaded = TerrainSerializer.Load(
                slot,
                settings,
                biomeSettings,
                library,
                vegetationSettings
            );

            if (loaded == null)
            {
                SetStatus("Load failed. Check Console.");
                return;
            }

            world.DestroyAll();

            // Roads, rivers, lakes and markers are not saved with the world
            RuntimeWorldBuilder.Runtime.Features.FeatureEvents.RequestClear();

            world = loaded;

            ApplyLayersToGeneratedChunks();

            ServiceRegistry.Register(world);

            FrameCamera();

            editValue = -1f;
            checksumBefore = -1;

            SetStatus($"Loaded '{slot}' extent {world.Min} -> {world.Max}");

            RefreshUndoRedoButtons();
        }

        // --------------------------------------------------
        // UNDO / REDO
        // --------------------------------------------------

        private void DoUndo()
        {
            if (world == null)
                return;

            string label = world.UndoLabel;

            if (world.Undo())
            {
                editValue = -1f;

                SetStatus(
                    $"Undo: {label}\n" +
                    $"Extent {world.Min} -> {world.Max}"
                );
            }

            RefreshUndoRedoButtons();
        }

        private void DoRedo()
        {
            if (world == null)
                return;

            string label = world.RedoLabel;

            if (world.Redo())
            {
                editValue = -1f;

                SetStatus(
                    $"Redo: {label}\n" +
                    $"Extent {world.Min} -> {world.Max}"
                );
            }

            RefreshUndoRedoButtons();
        }

        private void RefreshUndoRedoButtons()
        {
            if (undoButton != null)
                undoButton.SetEnabled(world != null && world.CanUndo);

            if (redoButton != null)
                redoButton.SetEnabled(world != null && world.CanRedo);
        }

        // --------------------------------------------------
        // STATUS
        // --------------------------------------------------

        private void SetStatus(string message)
        {
            status = message;

            if (statusLabel != null)
                statusLabel.text = message;

            Debug.Log("[TerrainWorldTester] " + message);
        }

        // --------------------------------------------------
        // CLEANUP
        // --------------------------------------------------

        private void OnDestroy()
        {
            ServiceRegistry.UnRegister<TerrainWorld>();
            ServiceRegistry.UnRegister<TerrainSettings>();
        }


        /// <summary>
        /// Stores and applies material layers to every generated terrain chunk.
        /// </summary>
        public void ApplyTerrainLayers(TerrainLayer[] layers)
        {
            if (layers == null || layers.Length == 0)
                return;

            runtimeTerrainLayers = layers;

            ApplyLayersToGeneratedChunks();
        }

        /// <summary>
        /// Applies the current material layers to all available chunks.
        /// </summary>
        public void ApplyLayersToGeneratedChunks()
        {
            if (world == null || runtimeTerrainLayers == null ||
                runtimeTerrainLayers.Length == 0)
                return;

            int applied = 0;

            for (int z = world.Min.y; z <= world.Max.y; z++)
            {
                for (int x = world.Min.x; x <= world.Max.x; x++)
                {
                    Vector2Int coordinate = new Vector2Int(x, z);

                    if (!world.TryGetChunk(coordinate, out var chunk))
                        continue;

                    if (chunk == null || chunk.Terrain == null)
                        continue;

                    TerrainData data = chunk.Terrain.terrainData;

                    if (data == null)
                        continue;

                    data.terrainLayers = runtimeTerrainLayers;

                    applied++;
                }
            }

            Debug.Log(
                $"[TerrainWorldTester] Applied {runtimeTerrainLayers.Length} " +
                $"material layer(s) to {applied} terrain chunks."
            );
        }

    }
}
