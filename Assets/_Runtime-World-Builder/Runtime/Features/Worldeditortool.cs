using System.Collections.Generic;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Testing;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Features
{
    public class WorldEditorTool : MonoBehaviour
    {
        private enum Mode
        {
            None,
            Road,
            River,
            Lake,
            Pond,
            Hill,
            Valley,
            Flatten
        }

        [Header("Feature Materials")]
        [SerializeField] private FeatureMaterials materials = new();
        [SerializeField] private RoadStyle road = new();

        [Header("River Settings")]
        [SerializeField] private float riverWidth = 12f;
        [SerializeField] private float riverDepth = 3f;

        [Header("Lake Settings")]
        [SerializeField] private float lakeRadius = 45f;
        [SerializeField] private float lakeDepth = 8f;

        [Header("Pond Settings")]
        [SerializeField] private float pondRadius = 12f;
        [SerializeField] private float pondDepth = 2f;

        [Header("Terrain Shape Settings")]
        [SerializeField] private float shapeRadius = 80f;
        [SerializeField] private float hillHeight = 30f;
        [SerializeField] private float valleyDepth = 20f;
        [SerializeField] private float flattenRadius = 40f;

        [Header("References")]
        [SerializeField] private HeightmapPlacer stamper;
        [SerializeField] private UIDocument uiDocument;

        private Mode mode = Mode.None;

        private readonly List<Vector2> points = new();

        private LineRenderer lineRenderer;
        private Transform featureRoot;

        private VisualElement uiRoot;
        private VisualElement toolPopover;
        private Label activeToolLabel;

        private Slider pathWidthSlider;
        private Slider pathDepthSlider;
        private Slider waterRadiusSlider;
        private FloatField waterLevelField;

        private string status = "Select a feature tool.";

        private bool stamperOn = true;
        private bool uiInitialized;

        // --------------------------------------------------
        // INITIALIZATION
        // --------------------------------------------------

        private void Awake()
        {
            CreateLineRenderer();
        }

        private void Start()
        {
            if (stamper == null)
                stamper = FindFirstObjectByType<HeightmapPlacer>();

            if (uiDocument == null)
                uiDocument = FindFirstObjectByType<UIDocument>();

            InitializeUIToolkit();
        }

        private void OnDisable()
        {
            SetStamper(true);
            ClearPreviewLine();
        }

        private void OnDestroy()
        {
            if (lineRenderer != null)
            {
                if (Application.isPlaying)
                    Destroy(lineRenderer.gameObject);
                else
                    DestroyImmediate(lineRenderer.gameObject);
            }
        }

        private void CreateLineRenderer()
        {
            GameObject go = new GameObject("WorldEditorLine");
            go.transform.SetParent(transform, false);

            lineRenderer = go.AddComponent<LineRenderer>();

            Shader shader = Shader.Find("Sprites/Default");

            if (shader != null)
                lineRenderer.material = new Material(shader);

            lineRenderer.startColor = new Color(1f, 0.9f, 0.1f);
            lineRenderer.endColor = new Color(1f, 0.9f, 0.1f);

            lineRenderer.widthMultiplier = 1.5f;
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 0;
        }

        // --------------------------------------------------
        // UI TOOLKIT INITIALIZATION
        // --------------------------------------------------

        private void InitializeUIToolkit()
        {
            if (uiDocument == null)
            {
                Debug.LogError(
                    "WorldEditorTool: UIDocument not assigned."
                );
                return;
            }

            uiRoot = uiDocument.rootVisualElement;

            if (uiRoot == null)
                return;

            toolPopover = uiRoot.Q<VisualElement>("tool-popover");
            activeToolLabel = uiRoot.Q<Label>("lbl-active-tool");

            pathWidthSlider = uiRoot.Q<Slider>("sld-path-width");
            pathDepthSlider = uiRoot.Q<Slider>("sld-path-depth");
            waterRadiusSlider = uiRoot.Q<Slider>("sld-water-radius");
            waterLevelField = uiRoot.Q<FloatField>("fld-water-level");

            BindFeatureButtons();
            BindFeatureSettings();

            uiInitialized = true;

            UpdateStatus("Select a feature tool.");
        }

        // --------------------------------------------------
        // BUTTON BINDINGS
        // --------------------------------------------------

        private void BindFeatureButtons()
        {
            BindButton("btn-builder-road", () => SetMode(Mode.Road));
            BindButton("btn-builder-river", () => SetMode(Mode.River));
            BindButton("btn-builder-lake", () => SetMode(Mode.Lake));
            BindButton("btn-builder-pond", () => SetMode(Mode.Pond));

            BindButton("btn-tool-hill", () => SetMode(Mode.Hill));
            BindButton("btn-tool-valley", () => SetMode(Mode.Valley));
            BindButton("btn-tool-flatten", () => SetMode(Mode.Flatten));

            BindButton("btn-spline-finish", FinishCurrentFeature);
            BindButton("btn-spline-cancel", Cancel);

            BindButton("btn-spline-undo-pt", RemoveLastPoint);

            BindButton("btn-close-popover", () =>
            {
                SetMode(Mode.None);
            });

            // Optional button: add this to UXML if you want
            // a dedicated clear button.
            BindButton("btn-clear-features", ClearFeatures);
        }

        private void BindButton(string elementName, System.Action callback)
        {
            Button button = uiRoot.Q<Button>(elementName);

            if (button == null)
                return;

            button.clicked += callback;
        }

        // --------------------------------------------------
        // SETTINGS BINDINGS
        // --------------------------------------------------

        private void BindFeatureSettings()
        {
            if (pathWidthSlider != null)
            {
                pathWidthSlider.RegisterValueChangedCallback(evt =>
                {
                    switch (mode)
                    {
                        case Mode.Road:
                            road.width = evt.newValue;
                            break;

                        case Mode.River:
                            riverWidth = evt.newValue;
                            break;
                    }
                });
            }

            if (pathDepthSlider != null)
            {
                pathDepthSlider.RegisterValueChangedCallback(evt =>
                {
                    switch (mode)
                    {
                        case Mode.Road:
                            road.shoulder = evt.newValue;
                            break;

                        case Mode.River:
                            riverDepth = evt.newValue;
                            break;
                    }
                });
            }

            if (waterRadiusSlider != null)
            {
                waterRadiusSlider.RegisterValueChangedCallback(evt =>
                {
                    switch (mode)
                    {
                        case Mode.Lake:
                            lakeRadius = evt.newValue;
                            break;

                        case Mode.Pond:
                            pondRadius = evt.newValue;
                            break;
                    }
                });
            }

            if (waterLevelField != null)
            {
                waterLevelField.RegisterValueChangedCallback(evt =>
                {
                    lakeDepth = Mathf.Max(0f, evt.newValue);
                    pondDepth = Mathf.Max(0f, evt.newValue);
                });
            }
        }

        // --------------------------------------------------
        // MODE MANAGEMENT
        // --------------------------------------------------

        private void SetMode(Mode newMode)
        {
            mode = newMode;

            Cancel();

            UpdateModeUI();

            UpdateStatus(
                newMode == Mode.None
                    ? "Feature tool disabled."
                    : $"Selected: {newMode}. Click on terrain."
            );
        }

        private void UpdateModeUI()
        {
            if (activeToolLabel != null)
                activeToolLabel.text = mode.ToString();

            UpdateButtonState("btn-builder-road", mode == Mode.Road);
            UpdateButtonState("btn-builder-river", mode == Mode.River);
            UpdateButtonState("btn-builder-lake", mode == Mode.Lake);
            UpdateButtonState("btn-builder-pond", mode == Mode.Pond);

            UpdateButtonState("btn-tool-hill", mode == Mode.Hill);
            UpdateButtonState("btn-tool-valley", mode == Mode.Valley);
            UpdateButtonState("btn-tool-flatten", mode == Mode.Flatten);

            UpdateModeSettings();
        }

        private void UpdateButtonState(string elementName, bool active)
        {
            Button button = uiRoot?.Q<Button>(elementName);

            if (button == null)
                return;

            button.EnableInClassList("tool-chip--active", active);
        }

        private void UpdateModeSettings()
        {
            if (pathWidthSlider != null)
            {
                switch (mode)
                {
                    case Mode.Road:
                        pathWidthSlider.lowValue = 3f;
                        pathWidthSlider.highValue = 20f;
                        pathWidthSlider.value = road.width;
                        break;

                    case Mode.River:
                        pathWidthSlider.lowValue = 4f;
                        pathWidthSlider.highValue = 60f;
                        pathWidthSlider.value = riverWidth;
                        break;
                }
            }

            if (pathDepthSlider != null)
            {
                switch (mode)
                {
                    case Mode.Road:
                        pathDepthSlider.lowValue = 1f;
                        pathDepthSlider.highValue = 20f;
                        pathDepthSlider.value = road.shoulder;
                        break;

                    case Mode.River:
                        pathDepthSlider.lowValue = 1f;
                        pathDepthSlider.highValue = 10f;
                        pathDepthSlider.value = riverDepth;
                        break;
                }
            }

            if (waterRadiusSlider != null)
            {
                if (mode == Mode.Lake)
                {
                    waterRadiusSlider.lowValue = 20f;
                    waterRadiusSlider.highValue = 150f;
                    waterRadiusSlider.value = lakeRadius;
                }
                else if (mode == Mode.Pond)
                {
                    waterRadiusSlider.lowValue = 5f;
                    waterRadiusSlider.highValue = 30f;
                    waterRadiusSlider.value = pondRadius;
                }
            }

            if (waterLevelField != null)
            {
                waterLevelField.value =
                    mode == Mode.Pond ? pondDepth : lakeDepth;
            }
        }

        // --------------------------------------------------
        // FEATURE ROOT
        // --------------------------------------------------

        private Transform Root
        {
            get
            {
                if (featureRoot == null)
                {
                    GameObject go = new GameObject("RoadsAndWater");
                    featureRoot = go.transform;
                }

                return featureRoot;
            }
        }

        // --------------------------------------------------
        // STAMPER CONTROL
        // --------------------------------------------------

        private void SetStamper(bool enabledState)
        {
            if (stamper == null || stamperOn == enabledState)
                return;

            stamperOn = enabledState;
            stamper.enabled = enabledState;

            foreach (LineRenderer lr in stamper.GetComponents<LineRenderer>())
                lr.enabled = enabledState;

            Transform ghost = stamper.transform.Find("StampGhost");
            Transform arrow = stamper.transform.Find("StampArrow");

            if (ghost != null)
                ghost.gameObject.SetActive(enabledState);

            if (arrow != null)
                arrow.gameObject.SetActive(enabledState);
        }

        // --------------------------------------------------
        // UI POINTER DETECTION
        // --------------------------------------------------

        private bool IsPointerOverUI()
        {
            if (uiDocument == null || uiRoot == null)
                return false;

            IPanel panel = uiRoot.panel;

            if (panel == null)
                return false;

            Vector2 panelPosition =
                RuntimePanelUtils.ScreenToPanel(
                    panel,
                    Input.mousePosition
                );

            VisualElement picked = panel.Pick(panelPosition);

            return picked != null &&
                   picked.pickingMode != PickingMode.Ignore;
        }

        // --------------------------------------------------
        // UPDATE
        // --------------------------------------------------

        private void Update()
        {
            bool overUI = IsPointerOverUI();

            // Keep the stamper disabled while the feature editor
            // is active or the pointer is interacting with UI.
            SetStamper(mode == Mode.None && !overUI);

            if (Input.GetKeyDown(KeyCode.F3))
                TogglePopover();

            if (mode == Mode.None)
                return;

            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
                return;

            if (!ServiceRegistry.TryResolve<TerrainSettings>(out var settings))
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Finish(world, settings);
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
                RemoveLastPoint(world, settings);

            if (overUI || !Input.GetMouseButtonDown(0))
                return;

            if (Camera.main == null)
                return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, 20000f))
                return;

            Vector2 position = new Vector2(hit.point.x, hit.point.z);

            HandleTerrainClick(world, settings, position);
        }

        // --------------------------------------------------
        // TERRAIN CLICK HANDLING
        // --------------------------------------------------

        private void HandleTerrainClick(
            TerrainWorld world,
            TerrainSettings settings,
            Vector2 position)
        {
            switch (mode)
            {
                case Mode.Road:
                case Mode.River:

                    points.Add(position);

                    RefreshLine(world, settings);

                    UpdateStatus(
                        $"{mode}: {points.Count} points. Press Enter to build."
                    );

                    break;

                case Mode.Lake:

                    UpdateStatus(
                        WaterBuilder.CreateLake(
                            world,
                            settings,
                            position,
                            lakeRadius,
                            lakeDepth,
                            materials,
                            Root
                        ) != null
                            ? "Lake created."
                            : "Lake creation failed. Check Console."
                    );

                    break;

                case Mode.Pond:

                    UpdateStatus(
                        WaterBuilder.CreateLake(
                            world,
                            settings,
                            position,
                            pondRadius,
                            pondDepth,
                            materials,
                            Root
                        ) != null
                            ? "Pond created."
                            : "Pond creation failed. Check Console."
                    );

                    break;

                case Mode.Hill:

                    TerrainShaper.Hill(
                        world,
                        settings,
                        position,
                        shapeRadius,
                        hillHeight,
                        true
                    );

                    UpdateStatus("Hill raised.");
                    break;

                case Mode.Valley:

                    TerrainShaper.Hill(
                        world,
                        settings,
                        position,
                        shapeRadius,
                        valleyDepth,
                        false
                    );

                    UpdateStatus("Valley created.");
                    break;

                case Mode.Flatten:

                    UpdateStatus(
                        TerrainShaper.Flatten(
                            world,
                            settings,
                            position,
                            flattenRadius
                        )
                            ? "Terrain flattened."
                            : "Flatten failed. Position is outside terrain."
                    );

                    break;
            }
        }

        // --------------------------------------------------
        // FINISH ROAD / RIVER
        // --------------------------------------------------

        private void FinishCurrentFeature()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
                return;

            if (!ServiceRegistry.TryResolve<TerrainSettings>(out var settings))
                return;

            Finish(world, settings);
        }

        private void Finish(
            TerrainWorld world,
            TerrainSettings settings)
        {
            if (mode != Mode.Road && mode != Mode.River)
                return;

            if (points.Count < 2)
            {
                UpdateStatus("You need at least 2 points.");
                return;
            }

            if (mode == Mode.Road)
            {
                bool success = RoadBuilder.Build(
                    world,
                    settings,
                    points,
                    road,
                    materials,
                    Root
                );

                UpdateStatus(success ? "Road built." : "Road creation failed.");
            }
            else
            {
                bool success = WaterBuilder.CreateRiver(
                    world,
                    settings,
                    points,
                    riverWidth,
                    riverDepth,
                    materials,
                    Root
                ) != null;

                UpdateStatus(success ? "River built." : "River creation failed.");
            }

            points.Clear();
            ClearPreviewLine();
        }

        // --------------------------------------------------
        // REMOVE LAST POINT
        // --------------------------------------------------

        private void RemoveLastPoint()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
                return;

            if (!ServiceRegistry.TryResolve<TerrainSettings>(out var settings))
                return;

            RemoveLastPoint(world, settings);
        }

        private void RemoveLastPoint(
            TerrainWorld world,
            TerrainSettings settings)
        {
            if (points.Count == 0)
                return;

            points.RemoveAt(points.Count - 1);

            RefreshLine(world, settings);

            UpdateStatus($"{points.Count} points remaining.");
        }

        // --------------------------------------------------
        // CANCEL
        // --------------------------------------------------

        private void Cancel()
        {
            points.Clear();
            ClearPreviewLine();

            UpdateStatus("Operation cancelled.");
        }

        // --------------------------------------------------
        // PREVIEW LINE
        // --------------------------------------------------

        private void RefreshLine(
            TerrainWorld world,
            TerrainSettings settings)
        {
            if (lineRenderer == null)
                return;

            List<Vector2> sampled =
                points.Count >= 2
                    ? PathSpline.Sample(points, 4f)
                    : points;

            lineRenderer.positionCount = sampled.Count;

            for (int i = 0; i < sampled.Count; i++)
            {
                float y = world.TrySampleHeight01(
                    sampled[i].x,
                    sampled[i].y,
                    out float height01
                )
                    ? height01 * settings.maxHeight
                    : 0f;

                lineRenderer.SetPosition(
                    i,
                    new Vector3(
                        sampled[i].x,
                        y + 1.5f,
                        sampled[i].y
                    )
                );
            }
        }

        private void ClearPreviewLine()
        {
            if (lineRenderer != null)
                lineRenderer.positionCount = 0;
        }

        // --------------------------------------------------
        // CLEAR FEATURES
        // --------------------------------------------------

        public void ClearFeatures()
        {
            if (featureRoot != null)
            {
                Destroy(featureRoot.gameObject);
                featureRoot = null;
            }

            WaterRegistry.Clear();

            points.Clear();
            ClearPreviewLine();

            UpdateStatus(
                "Road and water objects cleared. Terrain carving remains."
            );
        }

        // --------------------------------------------------
        // UI VISIBILITY
        // --------------------------------------------------

        private void TogglePopover()
        {
            if (toolPopover == null)
                return;

            toolPopover.ToggleInClassList("hidden");
        }

        // --------------------------------------------------
        // STATUS
        // --------------------------------------------------

        private void UpdateStatus(string message)
        {
            status = message;

            Debug.Log("[WorldEditorTool] " + message);

            if (activeToolLabel != null)
            {
                activeToolLabel.text =
                    mode == Mode.None
                        ? "World Editor"
                        : mode.ToString();
            }
        }
    }
}