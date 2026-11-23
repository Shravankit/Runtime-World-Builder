using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.UI
{
    /// <summary>
    /// TERRAFORM runtime UI controller: header, 12-tool rail, contextual tool panel,
    /// overview/active-tool cards and status bar. All pre-existing events and element
    /// names are kept; new controls are reachable through OnValueChanged / OnButtonClicked.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RuntimeWorldBuilderUI : MonoBehaviour
    {
        // Original values keep their order (serialized ints); new tools are appended.
        public enum ToolMode
        {
            Sculpt, Stamps, Roads, Paint, Materials, Nature, Library, Settings,
            Heightmap, Water, Objects, Chunks, World
        }

        public enum LibraryCategory { Hills, Roads, Rivers, Trees, Grass, Rocks, Textures, Buildings }

        [Serializable]
        public class LibraryAssetItem
        {
            public LibraryCategory Category;
            public string Name;
            public string AssetPath;
            public bool SnapToGround = true;
            public Texture2D Thumbnail;
        }

        [Serializable]
        public class PaintLayerInfo
        {
            public string Name;
            public Color Color = Color.gray;
            [Range(0f, 1f)] public float Coverage;
            public bool IsBase;
        }

        [Serializable]
        public class SavedWorldInfo
        {
            public string Name;
            public string Subtitle;
            public bool IsCurrent;
        }

        [Serializable]
        public struct NoiseSettings
        {
            public string Type;
            public float Scale;
            public int Octaves;
            public float Persistence;
            public float Lacunarity;
            public float HeightMultiplier;
            public string Preset;
            public bool SeamlessEdges;
        }

        private sealed class ToolDef
        {
            public ToolMode Mode;
            public string Rail, Page, Title, Subtitle;
        }

        // Order = numeric shortcut order (1..9) from the design.
        private static readonly ToolDef[] Tools =
        {
            new ToolDef { Mode = ToolMode.Settings,  Rail = "rail-settings",  Page = "pop-generate",  Title = "Generate",         Subtitle = "Shape the foundation of your world" },
            new ToolDef { Mode = ToolMode.Sculpt,    Rail = "rail-sculpt",    Page = "pop-sculpt",    Title = "Sculpt",           Subtitle = "Shape terrain directly in the viewport" },
            new ToolDef { Mode = ToolMode.Heightmap, Rail = "rail-heightmap", Page = "pop-heightmap", Title = "Heightmap",        Subtitle = "Build terrain from elevation data" },
            new ToolDef { Mode = ToolMode.Stamps,    Rail = "rail-stamps",    Page = "pop-stamps",    Title = "Stamp",            Subtitle = "Layer natural forms onto your terrain" },
            new ToolDef { Mode = ToolMode.Roads,     Rail = "rail-roads",     Page = "pop-roads",     Title = "Roads",            Subtitle = "Draw paths that follow the landscape" },
            new ToolDef { Mode = ToolMode.Water,     Rail = "rail-water",     Page = "pop-water",     Title = "Water",            Subtitle = "Bring your landscape to life" },
            new ToolDef { Mode = ToolMode.Paint,     Rail = "rail-paint",     Page = "pop-paint",     Title = "Paint",            Subtitle = "Blend texture layers across the terrain" },
            new ToolDef { Mode = ToolMode.Materials, Rail = "rail-materials", Page = "pop-materials", Title = "Materials",        Subtitle = "Fine tune the terrain surface" },
            new ToolDef { Mode = ToolMode.Nature,    Rail = "rail-nature",    Page = "pop-nature",    Title = "Vegetation",       Subtitle = "Populate a living landscape" },
            new ToolDef { Mode = ToolMode.Objects,   Rail = "rail-objects",   Page = "pop-objects",   Title = "Objects",          Subtitle = "Place and transform world objects" },
            new ToolDef { Mode = ToolMode.Chunks,    Rail = "rail-chunks",    Page = "pop-chunks",    Title = "Chunks",           Subtitle = "Build your world one tile at a time" },
            new ToolDef { Mode = ToolMode.World,     Rail = "rail-world",     Page = "pop-world",     Title = "World Management", Subtitle = "Manage worlds and portable world data" },
        };

        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;

        [Header("Initial Asset Library")]
        [SerializeField]
        private List<LibraryAssetItem> libraryItems = new()
        {
            new LibraryAssetItem { Category = LibraryCategory.Hills, Name = "Alpine Peak", AssetPath = "Heightmaps/AlpinePeak" },
            new LibraryAssetItem { Category = LibraryCategory.Hills, Name = "Canyon Ridge", AssetPath = "Heightmaps/CanyonRidge" },
            new LibraryAssetItem { Category = LibraryCategory.Roads, Name = "Dirt Road", AssetPath = "Roads/DirtRoad" },
            new LibraryAssetItem { Category = LibraryCategory.Roads, Name = "Cobble Road", AssetPath = "Roads/CobbleRoad" },
            new LibraryAssetItem { Category = LibraryCategory.Rivers, Name = "River Path", AssetPath = "Water/RiverPath" },
            new LibraryAssetItem { Category = LibraryCategory.Trees, Name = "Oak Tree", AssetPath = "Trees/OakTree" },
            new LibraryAssetItem { Category = LibraryCategory.Trees, Name = "Pine Tree", AssetPath = "Trees/PineTree" },
            new LibraryAssetItem { Category = LibraryCategory.Grass, Name = "Meadow Grass", AssetPath = "Grass/MeadowGrass" },
            new LibraryAssetItem { Category = LibraryCategory.Rocks, Name = "Mossy Rock", AssetPath = "Rocks/MossyRock" },
            new LibraryAssetItem { Category = LibraryCategory.Textures, Name = "Granite Tex", AssetPath = "Textures/Granite" },
            new LibraryAssetItem { Category = LibraryCategory.Buildings, Name = "Timber Cabin", AssetPath = "Buildings/TimberCabin" }
        };

        [Header("Terrain texture layers (Paint tab)")]
        [SerializeField]
        private List<PaintLayerInfo> paintLayers = new()
        {
            new PaintLayerInfo { Name = "Alpine grass", Color = new Color(0.36f, 0.56f, 0.26f), Coverage = 0.64f, IsBase = true },
            new PaintLayerInfo { Name = "Granite rock", Color = new Color(0.55f, 0.55f, 0.58f), Coverage = 0.28f },
            new PaintLayerInfo { Name = "Riverbed gravel", Color = new Color(0.62f, 0.56f, 0.45f), Coverage = 0.08f }
        };

        // ---------- cached elements ----------
        private VisualElement _root, _sidebar, _rightCluster, _toolRail, _toolPopover;
        private VisualElement _addModalBackdrop, _layersPopover, _chunkGrid, _saveDot;
        private Label _lblActiveTool, _lblActiveDetail, _lblPopoverTitle, _lblPopoverSubtitle;
        private Label _lblChunkCount, _lblStatusMsg, _lblSaveState;
        private Button _btnHideUI;

        // ---------- state ----------
        private bool _uiHidden;
        private ToolMode _currentMode = ToolMode.Settings;
        private string _sculptTool = "Raise";
        private string _waterType = "River";
        private bool _paintErase;
        private string _selectedPaintLayer;
        private LibraryCategory _vegCategory = LibraryCategory.Trees;
        private LibraryCategory _objCategory = LibraryCategory.Buildings;
        private LibraryAssetItem _selectedLibraryItem;
        private Vector2Int? _selectedChunk;
        private List<SavedWorldInfo> _savedWorlds = new();

        private readonly Dictionary<ToolMode, Button> _railButtons = new();
        private readonly Dictionary<ToolMode, VisualElement> _pages = new();
        private readonly Dictionary<ToolMode, ToolDef> _defs = new();
        private readonly List<Button> _chunkCells = new();

        // ---------- Core events (unchanged) ----------
        public event Action<int, int, float, int> OnGenerateTerrainRequested;
        public event Action<Vector2Int> OnExpandWorldRequested;
        public event Action OnVerifyTerrainRequested;
        public event Action<string> OnActiveToolSelected;
        public event Action<float, float, float, float> OnBrushSettingsChanged;
        public event Action<string, string, float, float, bool> OnApplyStampRequested;
        public event Action<float, float> OnPathSettingsChanged;
        public event Action OnSplineUndoPointRequested;
        public event Action OnSplineFinishRequested;
        public event Action OnSplineCancelRequested;
        public event Action<float, float> OnWaterSettingsChanged;
        public event Action<string, bool, bool, bool, float> OnAutoPaintTerrainRequested;
        public event Action<string, bool, bool, bool, float, float, float, bool> OnPopulateVegetationRequested;
        public event Action OnClearVegetationRequested;
        public event Action OnUndoRequested;
        public event Action OnRedoRequested;
        public event Action<string, bool, bool> OnSaveWorldRequested;
        public event Action<string> OnLoadWorldRequested;
        public event Action<bool> OnPlayModeToggled;
        public event Action<string, bool> OnLayerVisibilityChanged;
        public event Action<LibraryAssetItem> OnLibraryAssetSelected;
        public event Action<LibraryAssetItem> OnLibraryAssetAdded;
        public event Action OnBrowseAssetRequested;

        // ---------- New events ----------
        /// <summary>Fires when a rail tab is chosen (Generate, Sculpt, Heightmap, ...).</summary>
        public event Action<ToolMode> OnToolModeChanged;
        /// <summary>Fires for every named field change in the UI: (elementName, newValue).</summary>
        public event Action<string, object> OnValueChanged;
        /// <summary>Fires for every named button click in the UI: (elementName).</summary>
        public event Action<string> OnButtonClicked;
        public event Action<NoiseSettings> OnNoiseSettingsChanged;
        public event Action OnResetRequested;
        public event Action OnFocusTerrainRequested;
        public event Action<string> OnViewModeChanged;
        public event Action<string> OnShadingModeChanged;
        public event Action<string> OnSplineModeChanged;
        public event Action<Vector2Int> OnChunkSelected;
        public event Action<bool, bool> OnRegenerateChunksRequested;
        public event Action<LibraryAssetItem> OnPlacePrefabRequested;
        public event Action<Vector3, float, float, bool> OnObjectTransformChanged;
        public event Action OnObjectDuplicateRequested;
        public event Action OnObjectDeleteRequested;
        public event Action<string> OnPaintLayerSelected;

        private void Reset() => uiDocument = GetComponent<UIDocument>();

        private void OnEnable()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            _root = uiDocument.rootVisualElement;
            if (_root == null) return;

            CacheElements();
            BindHeader();
            BindRail();
            BindGenerate();
            BindSculpt();
            BindHeightmapAndStamp();
            BindRoadsAndWater();
            BindPaintAndMaterials();
            BindVegetationAndObjects();
            BindChunksAndWorld();
            BindOverview();
            BindAddLibraryModal();
            HookGenericEvents();

            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            RefreshPaintLayers();
            RebuildAssetLists();
            RebuildChunkGrid();
            RefreshSavedWorlds();
            RefreshStats();

            SelectRailTool(ToolMode.Settings);
            SetSaveState(true);
            SetStatusMessage("Ready");
        }

        private void CacheElements()
        {
            _sidebar = _root.Q("sidebar");
            _rightCluster = _root.Q("right-cluster");
            _toolRail = _root.Q("tool-rail");
            _toolPopover = _root.Q("tool-popover");
            _addModalBackdrop = _root.Q("add-library-modal-backdrop");
            _layersPopover = _root.Q("layers-popover");
            _chunkGrid = _root.Q("chunk-grid");
            _saveDot = _root.Q("save-dot");

            _lblActiveTool = _root.Q<Label>("lbl-active-tool");
            _lblActiveDetail = _root.Q<Label>("lbl-active-detail");
            _lblPopoverTitle = _root.Q<Label>("lbl-popover-title");
            _lblPopoverSubtitle = _root.Q<Label>("lbl-popover-subtitle");
            _lblChunkCount = _root.Q<Label>("lbl-chunk-count");
            _lblStatusMsg = _root.Q<Label>("lbl-status-msg");
            _lblSaveState = _root.Q<Label>("lbl-save-state");
            _btnHideUI = _root.Q<Button>("btn-hide-ui");
        }

        // =====================================================================
        // Small helpers
        // =====================================================================
        private float F(string name, float def = 0f) => _root.Q(name) switch
        {
            Slider s => s.value,
            SliderInt si => si.value,
            FloatField f => f.value,
            IntegerField i => i.value,
            _ => def
        };

        private bool B(string name, bool def = false) => _root.Q<Toggle>(name)?.value ?? def;

        private string S(string name, string def = "") => _root.Q(name) switch
        {
            DropdownField d => d.value ?? def,
            TextField t => t.value ?? def,
            _ => def
        };

        private void Click(string name, Action action)
        {
            var b = _root.Q<Button>(name);
            if (b != null) b.clicked += action;
        }

        private void Watch(Action onChange, params string[] names)
        {
            foreach (var n in names)
            {
                switch (_root.Q(n))
                {
                    case Slider e: e.RegisterValueChangedCallback(_ => onChange()); break;
                    case SliderInt e: e.RegisterValueChangedCallback(_ => onChange()); break;
                    case Toggle e: e.RegisterValueChangedCallback(_ => onChange()); break;
                    case FloatField e: e.RegisterValueChangedCallback(_ => onChange()); break;
                    case IntegerField e: e.RegisterValueChangedCallback(_ => onChange()); break;
                    case DropdownField e: e.RegisterValueChangedCallback(_ => onChange()); break;
                    case TextField e: e.RegisterValueChangedCallback(_ => onChange()); break;
                }
            }
        }

        /// <summary>Independent segmented control: one active button at a time.</summary>
        private void Segment(Action<string> onPick, params (string button, string id)[] items)
        {
            var group = new List<(Button b, string id)>();
            foreach (var (name, id) in items)
            {
                var b = _root.Q<Button>(name);
                if (b != null) group.Add((b, id));
            }

            foreach (var (b, id) in group)
            {
                b.clicked += () =>
                {
                    foreach (var o in group)
                        o.b.EnableInClassList("seg-btn--active", o.id == id);
                    onPick?.Invoke(id);
                };
            }
        }

        private static string Num(float v) => v.ToString("N0", CultureInfo.InvariantCulture);

        // =====================================================================
        // Header
        // =====================================================================
        private void BindHeader()
        {
            Click("btn-undo", () => OnUndoRequested?.Invoke());
            Click("btn-redo", () => OnRedoRequested?.Invoke());
            Click("btn-reset", () => { OnResetRequested?.Invoke(); SetStatusMessage("Reset requested"); });

            Click("btn-quick-save", () =>
            {
                var name = S("txt-world-name", "MyRuntimeWorld");
                OnSaveWorldRequested?.Invoke(name, B("tgl-compress-chunks", true), B("tgl-restore-stamps", true));
                SetSaveState(true);
                SetStatusMessage($"Saved '{name}'");
            });
            Click("btn-quick-load", () => OnLoadWorldRequested?.Invoke(S("txt-world-name", "MyRuntimeWorld")));

            bool isPlaying = false;
            var btnPlay = _root.Q<Button>("btn-play-mode");
            var playLabel = btnPlay?.Q<Label>(className: "btn-lbl");
            if (btnPlay != null)
            {
                btnPlay.clicked += () =>
                {
                    isPlaying = !isPlaying;
                    if (playLabel != null) playLabel.text = isPlaying ? "Stop" : "Play";
                    OnPlayModeToggled?.Invoke(isPlaying);
                };
            }

            _btnHideUI.clicked += ToggleUiHidden;

            _root.Q<DropdownField>("dd-view-mode")?.RegisterValueChangedCallback(e => OnViewModeChanged?.Invoke(e.newValue));
            _root.Q<DropdownField>("dd-shading-mode")?.RegisterValueChangedCallback(e => OnShadingModeChanged?.Invoke(e.newValue));
        }

        private void ToggleUiHidden()
        {
            _uiHidden = !_uiHidden;
            _sidebar.EnableInClassList("hidden", _uiHidden);
            _rightCluster.EnableInClassList("hidden", _uiHidden);
            _btnHideUI.text = _uiHidden ? "Show UI" : "Hide UI";
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // Ignore shortcuts while typing in a text input.
            if (evt.target is TextElement te && te.ClassListContains("unity-base-text-field__input")) return;

            int idx = evt.keyCode - KeyCode.Alpha1;
            if (idx >= 0 && idx < 9)
            {
                if (_uiHidden) ToggleUiHidden();
                SelectRailTool(Tools[idx].Mode);
            }
            else if (evt.keyCode == KeyCode.H) ToggleUiHidden();
            else if (evt.keyCode == KeyCode.F) OnFocusTerrainRequested?.Invoke();
        }

        // =====================================================================
        // Rail + tool panel
        // =====================================================================
        private void BindRail()
        {
            foreach (var def in Tools)
            {
                _defs[def.Mode] = def;
                var page = _root.Q(def.Page);
                var btn = _root.Q<Button>(def.Rail);
                if (page != null) _pages[def.Mode] = page;
                if (btn == null) continue;

                _railButtons[def.Mode] = btn;
                btn.userData = def.Mode;
                var mode = def.Mode;
                btn.clicked += () => SelectRailTool(mode);
            }

            Click("btn-close-popover", () => _toolPopover.AddToClassList("hidden"));

            var collapse = _root.Q<Button>("btn-collapse-rail");
            if (collapse != null)
            {
                collapse.clicked += () =>
                {
                    bool collapsed = !_toolRail.ClassListContains("tool-rail--collapsed");
                    _toolRail.EnableInClassList("tool-rail--collapsed", collapsed);
                    collapse.text = collapsed ? "»" : "«";
                };
            }
        }

        public void SelectRailTool(ToolMode mode, string title = null)
        {
            if (mode == ToolMode.Library) mode = ToolMode.Nature; // legacy "Features" tab
            if (!_defs.TryGetValue(mode, out var def)) return;

            _currentMode = mode;
            _toolPopover.RemoveFromClassList("hidden");

            foreach (var kvp in _pages)
                kvp.Value.EnableInClassList("hidden", kvp.Key != mode);

            foreach (var kvp in _railButtons)
                kvp.Value.EnableInClassList("rail-btn--active", kvp.Key == mode);

            string shown = string.IsNullOrEmpty(title) ? def.Title : title;
            if (_lblPopoverTitle != null) _lblPopoverTitle.text = shown;
            if (_lblPopoverSubtitle != null) _lblPopoverSubtitle.text = def.Subtitle;
            if (_lblActiveTool != null) _lblActiveTool.text = shown;

            RefreshActiveDetail();
            OnToolModeChanged?.Invoke(mode);

            // Tabs that imply a viewport tool activate it (old chips lived inside these tabs).
            switch (mode)
            {
                case ToolMode.Sculpt: OnActiveToolSelected?.Invoke(_sculptTool); break;
                case ToolMode.Roads: OnActiveToolSelected?.Invoke("Road"); break;
                case ToolMode.Water: OnActiveToolSelected?.Invoke(_waterType); break;
            }
        }

        // =====================================================================
        // Generate
        // =====================================================================
        private void BindGenerate()
        {
            Click("btn-seed-random", () =>
            {
                var seed = _root.Q<IntegerField>("fld-seed");
                if (seed != null) seed.value = UnityEngine.Random.Range(1, 999999);
            });

            Watch(() => OnNoiseSettingsChanged?.Invoke(GetNoiseSettings()),
                "dd-noise-type", "sld-noise-scale", "sld-noise-octaves", "sld-noise-persistence",
                "sld-noise-lacunarity", "sld-height-mult", "dd-gen-preset", "tgl-seamless-edges");

            Watch(RefreshStats, "dd-heightmap-res");

            Click("btn-generate-terrain", () =>
            {
                int res = int.TryParse(S("dd-heightmap-res"), out var r) ? r : 513;
                int size = (int)F("fld-terrain-size", 256);
                float h = F("fld-max-height", 120);
                int seed = (int)F("fld-seed", 12345);
                OnNoiseSettingsChanged?.Invoke(GetNoiseSettings());
                OnGenerateTerrainRequested?.Invoke(res, size, h, seed);
                SetStatusMessage("Generating terrain...");
            });
        }

        public NoiseSettings GetNoiseSettings() => new NoiseSettings
        {
            Type = S("dd-noise-type", "Perlin noise"),
            Scale = F("sld-noise-scale", 420),
            Octaves = (int)F("sld-noise-octaves", 6),
            Persistence = F("sld-noise-persistence", 0.5f),
            Lacunarity = F("sld-noise-lacunarity", 2f),
            HeightMultiplier = F("sld-height-mult", 1f),
            Preset = S("dd-gen-preset", "Alpine Islands"),
            SeamlessEdges = B("tgl-seamless-edges", true)
        };

        // =====================================================================
        // Sculpt
        // =====================================================================
        private void BindSculpt()
        {
            Segment(id =>
                {
                    _sculptTool = id;
                    OnActiveToolSelected?.Invoke(id);
                    SetStatusMessage($"Tool: {id}");
                    RefreshActiveDetail();
                },
                ("btn-tool-hill", "Raise"), ("btn-tool-valley", "Lower"), ("btn-tool-smooth", "Smooth"),
                ("btn-tool-flatten", "Flatten"), ("btn-tool-erosion", "Erosion"));

            Watch(() =>
                {
                    OnBrushSettingsChanged?.Invoke(F("sld-brush-radius"), F("sld-brush-strength"), F("sld-brush-falloff"), F("fld-flatten-height"));
                    RefreshActiveDetail();
                },
                "sld-brush-radius", "sld-brush-strength", "sld-brush-falloff", "fld-flatten-height");

            Click("btn-apply-sculpt", () =>
            {
                OnActiveToolSelected?.Invoke(_sculptTool);
                SetStatusMessage($"Sculpting: {_sculptTool}");
            });
        }

        // =====================================================================
        // Heightmap + Stamp
        // =====================================================================
        private void BindHeightmapAndStamp()
        {
            // Heightmap controls keep their original names so existing importer scripts still bind.
            Click("btn-apply-stamp", () =>
            {
                var lib = S("dd-heightmap-library");
                var blend = S("dd-stamp-blend", "Add");
                OnApplyStampRequested?.Invoke(lib, blend, F("sld-stamp-scale"), F("sld-stamp-rotation"), B("tgl-stamp-preview", true));
                SetStatusMessage($"Applied stamp: {lib}");
            });

            var uniform = _root.Q<Toggle>("tgl-stamp-uniform");
            var rowZ = _root.Q("row-stamp-scale-z");
            if (uniform != null && rowZ != null)
                uniform.RegisterValueChangedCallback(e => rowZ.EnableInClassList("hidden", e.newValue));
        }

        // =====================================================================
        // Roads + Water
        // =====================================================================
        private void BindRoadsAndWater()
        {
            Segment(id => OnSplineModeChanged?.Invoke(id), ("btn-road-draw", "Draw"), ("btn-road-edit", "Edit"));

            Watch(() => { OnPathSettingsChanged?.Invoke(F("sld-path-width"), F("sld-path-depth")); },
                "sld-path-width", "sld-path-depth");
            Watch(RefreshActiveDetail, "txt-spline-name", "txt-water-name");

            foreach (var undo in new[] { "btn-spline-undo-pt", "btn-water-undo-pt" })
                Click(undo, () => OnSplineUndoPointRequested?.Invoke());
            foreach (var fin in new[] { "btn-spline-finish", "btn-water-finish" })
                Click(fin, () => OnSplineFinishRequested?.Invoke());
            foreach (var cancel in new[] { "btn-spline-cancel", "btn-water-cancel" })
                Click(cancel, () => OnSplineCancelRequested?.Invoke());

            Segment(id =>
                {
                    _waterType = id;
                    OnActiveToolSelected?.Invoke(id);
                    SetStatusMessage($"Tool: {id}");
                },
                ("btn-builder-river", "River"), ("btn-builder-lake", "Lake"), ("btn-builder-pond", "Pond"));

            Watch(() => OnWaterSettingsChanged?.Invoke(F("sld-water-radius"), F("fld-water-level")),
                "sld-water-radius", "fld-water-level");
        }

        // =====================================================================
        // Paint + Materials
        // =====================================================================
        private void BindPaintAndMaterials()
        {
            Segment(id => { _paintErase = id == "Erase"; RefreshActiveDetail(); },
                ("btn-paint-mode-paint", "Paint"), ("btn-paint-mode-erase", "Erase"));

            Watch(RefreshActiveDetail, "sld-paint-size");
            Click("btn-manage-layers", () => SelectRailTool(ToolMode.Materials));

            Click("btn-paint-terrain", () =>
            {
                var biome = S("dd-biome-preset");
                OnAutoPaintTerrainRequested?.Invoke(biome, B("tgl-rule-height", true), B("tgl-rule-slope", true),
                    B("tgl-rule-worldpos", true), F("sld-layer-blend", 0.85f));
                SetStatusMessage($"Painted biome: {biome}");
            });
        }

        /// <summary>Replace the Paint-tab texture layer list (also feeds the Materials dropdown).</summary>
        public void SetPaintLayers(IEnumerable<PaintLayerInfo> layers)
        {
            paintLayers = new List<PaintLayerInfo>(layers ?? new List<PaintLayerInfo>());
            RefreshPaintLayers();
        }

        private void RefreshPaintLayers()
        {
            var list = _root.Q("paint-layer-list");
            if (list == null) return;
            list.Clear();

            if (_selectedPaintLayer == null && paintLayers.Count > 0)
                _selectedPaintLayer = paintLayers[0].Name;

            foreach (var layer in paintLayers)
            {
                var row = new VisualElement();
                row.AddToClassList("layer-row");
                row.EnableInClassList("layer-row--selected", layer.Name == _selectedPaintLayer);

                var swatch = new VisualElement();
                swatch.AddToClassList("layer-swatch");
                swatch.style.backgroundColor = layer.Color; // runtime data-driven color

                var text = new VisualElement();
                text.AddToClassList("asset-text");
                var name = new Label(layer.Name);
                name.AddToClassList("asset-name");
                var sub = new Label(layer.IsBase ? "Base layer" : "Texture layer");
                sub.AddToClassList("asset-sub");
                text.Add(name);
                text.Add(sub);

                var pct = new Label($"{Mathf.RoundToInt(layer.Coverage * 100f)}%");
                pct.AddToClassList("layer-pct");

                row.Add(swatch);
                row.Add(text);
                row.Add(pct);

                var captured = layer.Name;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedPaintLayer = captured;
                    RefreshPaintLayers();
                    RefreshActiveDetail();
                    OnPaintLayerSelected?.Invoke(captured);
                });
                list.Add(row);
            }

            var dd = _root.Q<DropdownField>("dd-material-active");
            if (dd != null)
            {
                var names = new List<string>();
                foreach (var l in paintLayers) names.Add(l.Name);
                if (names.Count == 0) names.Add("(none)");
                dd.choices = names;
                dd.SetValueWithoutNotify(_selectedPaintLayer != null && names.Contains(_selectedPaintLayer) ? _selectedPaintLayer : names[0]);
            }
        }

        // =====================================================================
        // Vegetation + Objects (asset lists share the library data)
        // =====================================================================
        private void BindVegetationAndObjects()
        {
            Segment(id =>
                {
                    _vegCategory = (LibraryCategory)Enum.Parse(typeof(LibraryCategory), id);
                    RebuildAssetLists();
                },
                ("veg-cat-trees", "Trees"), ("veg-cat-grass", "Grass"), ("veg-cat-rocks", "Rocks"));

            Click("btn-add-asset", () => OpenAddModal(_vegCategory));
            Click("btn-browse-assets", () => OnBrowseAssetRequested?.Invoke());
            Watch(RefreshActiveDetail, "sld-veg-density");

            Click("btn-populate-veg", () =>
            {
                OnPopulateVegetationRequested?.Invoke(S("dd-veg-preset"), B("tgl-place-trees", true), B("tgl-place-grass", true),
                    B("tgl-biome-dist", true), F("sld-veg-density", 0.7f), F("sld-tree-dist", 500), F("sld-grass-dist", 80), B("tgl-veg-shadows", true));
                SetStatusMessage("Growing vegetation...");
            });
            Click("btn-clear-veg", () => OnClearVegetationRequested?.Invoke());

            // Objects
            _root.Q<DropdownField>("dd-object-category")?.RegisterValueChangedCallback(e =>
            {
                _objCategory = e.newValue switch
                {
                    "Rocks" => LibraryCategory.Rocks,
                    "Trees" => LibraryCategory.Trees,
                    _ => LibraryCategory.Buildings
                };
                RebuildAssetLists();
            });

            Click("btn-add-object", () => OpenAddModal(_objCategory));
            Click("btn-browse-objects", () => OnBrowseAssetRequested?.Invoke());
            Click("btn-place-prefab", () =>
            {
                if (_selectedLibraryItem == null) { SetStatusMessage("Select a prefab first"); return; }
                OnPlacePrefabRequested?.Invoke(_selectedLibraryItem);
                SetStatusMessage($"Placing {_selectedLibraryItem.Name}");
            });

            Segment(id => OnActiveToolSelected?.Invoke(id),
                ("btn-obj-move", "Move"), ("btn-obj-rotate", "Rotate"), ("btn-obj-scale", "Scale"));

            Watch(() => OnObjectTransformChanged?.Invoke(
                    new Vector3(F("fld-obj-x"), F("fld-obj-y"), F("fld-obj-z")), F("sld-obj-rotation"), F("sld-obj-scale", 1f), B("tgl-obj-snap", true)),
                "fld-obj-x", "fld-obj-y", "fld-obj-z", "sld-obj-rotation", "sld-obj-scale", "tgl-obj-snap");

            Click("btn-obj-duplicate", () => OnObjectDuplicateRequested?.Invoke());
            Click("btn-obj-delete", () => OnObjectDeleteRequested?.Invoke());
        }

        /// <summary>Show a selected scene object in the Objects tab without echoing change events.</summary>
        public void SetSelectedObject(string objectName, Vector3 position, float rotationY, float scale)
        {
            _root.Q<FloatField>("fld-obj-x")?.SetValueWithoutNotify(position.x);
            _root.Q<FloatField>("fld-obj-y")?.SetValueWithoutNotify(position.y);
            _root.Q<FloatField>("fld-obj-z")?.SetValueWithoutNotify(position.z);
            _root.Q<Slider>("sld-obj-rotation")?.SetValueWithoutNotify(rotationY);
            _root.Q<Slider>("sld-obj-scale")?.SetValueWithoutNotify(scale);
            var lbl = _root.Q<Label>("lbl-selected-object");
            if (lbl != null) lbl.text = string.IsNullOrEmpty(objectName) ? "Selected: (none)" : $"Selected: {objectName}";
            RefreshActiveDetail();
        }

        public void FilterLibrary(LibraryCategory category)
        {
            if (category == LibraryCategory.Trees || category == LibraryCategory.Grass || category == LibraryCategory.Rocks)
                _vegCategory = category;
            else
                _objCategory = category;
            RebuildAssetLists();
        }

        private void RebuildAssetLists()
        {
            FillAssetList(_root.Q("veg-asset-list"), _vegCategory, "Vegetation asset");
            FillAssetList(_root.Q("obj-asset-list"), _objCategory, "Prefab");
        }

        private void FillAssetList(VisualElement list, LibraryCategory category, string kind)
        {
            if (list == null) return;
            list.Clear();

            foreach (var item in libraryItems)
            {
                if (item.Category != category) continue;

                var row = new VisualElement();
                row.AddToClassList("asset-row");
                row.EnableInClassList("asset-row--selected", item == _selectedLibraryItem);

                var thumb = new VisualElement();
                thumb.AddToClassList("asset-thumb");
                if (item.Thumbnail != null)
                    thumb.style.backgroundImage = new StyleBackground(item.Thumbnail);

                var text = new VisualElement();
                text.AddToClassList("asset-text");
                var name = new Label(item.Name);
                name.AddToClassList("asset-name");
                var sub = new Label(string.IsNullOrEmpty(item.AssetPath) ? kind : $"{kind} · {item.AssetPath}");
                sub.AddToClassList("asset-sub");
                text.Add(name);
                text.Add(sub);

                var radio = new VisualElement();
                radio.AddToClassList("radio");
                radio.EnableInClassList("radio--on", item == _selectedLibraryItem);

                row.Add(thumb);
                row.Add(text);
                row.Add(radio);

                var captured = item;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedLibraryItem = captured;
                    RebuildAssetLists();
                    RefreshActiveDetail();
                    OnLibraryAssetSelected?.Invoke(captured);
                    SetStatusMessage($"Selected {captured.Category}: {captured.Name}");
                });
                list.Add(row);
            }
        }

        // =====================================================================
        // Chunks + World management
        // =====================================================================
        private void BindChunksAndWorld()
        {
            Watch(() => { RebuildChunkGrid(); }, "fld-chunk-cols", "fld-chunk-rows");
            Watch(RefreshStats, "fld-chunk-width", "fld-chunk-depth");

            Click("btn-regenerate-chunks", () =>
            {
                OnRegenerateChunksRequested?.Invoke(B("tgl-chunk-selected-only"), B("tgl-chunk-edges", true));
                SetStatusMessage("Regenerating chunks...");
            });

            Click("btn-expand-pos-z", () => OnExpandWorldRequested?.Invoke(Vector2Int.up));
            Click("btn-expand-neg-z", () => OnExpandWorldRequested?.Invoke(Vector2Int.down));
            Click("btn-expand-pos-x", () => OnExpandWorldRequested?.Invoke(Vector2Int.right));
            Click("btn-expand-neg-x", () => OnExpandWorldRequested?.Invoke(Vector2Int.left));
            Click("btn-verify-continuity", () => OnVerifyTerrainRequested?.Invoke());

            Click("btn-save-world", SaveWorld);
            Click("btn-save-as", SaveWorld);
            Click("btn-load-world", () => OnLoadWorldRequested?.Invoke(S("txt-world-name")));
        }

        private void SaveWorld()
        {
            var name = S("txt-world-name", "MyRuntimeWorld");
            OnSaveWorldRequested?.Invoke(name, B("tgl-compress-chunks", true), B("tgl-restore-stamps", true));
            SetSaveState(true);
            SetStatusMessage($"Saved '{name}'");
        }

        private void RebuildChunkGrid()
        {
            if (_chunkGrid == null) return;
            _chunkGrid.Clear();
            _chunkCells.Clear();

            int cols = Mathf.Clamp((int)F("fld-chunk-cols", 4), 1, 16);
            int rows = Mathf.Clamp((int)F("fld-chunk-rows", 4), 1, 16);

            if (_selectedChunk.HasValue && (_selectedChunk.Value.x >= cols || _selectedChunk.Value.y >= rows))
                _selectedChunk = null;

            for (int y = 0; y < rows; y++)
            {
                var row = new VisualElement();
                row.AddToClassList("chunk-row");
                for (int x = 0; x < cols; x++)
                {
                    var cell = new Button { text = $"{x},{y}" };
                    cell.AddToClassList("chunk-cell");
                    var coord = new Vector2Int(x, y);
                    cell.userData = coord;
                    cell.clicked += () => SelectChunk(coord);
                    _chunkCells.Add(cell);
                    row.Add(cell);
                }
                _chunkGrid.Add(row);
            }

            ApplyChunkSelection();
            RefreshStats();
        }

        private void SelectChunk(Vector2Int coord)
        {
            _selectedChunk = coord;
            ApplyChunkSelection();
            OnChunkSelected?.Invoke(coord);
        }

        private void ApplyChunkSelection()
        {
            foreach (var cell in _chunkCells)
                cell.EnableInClassList("chunk-cell--selected", _selectedChunk.HasValue && (Vector2Int)cell.userData == _selectedChunk.Value);

            var lbl = _root.Q<Label>("lbl-selected-chunk");
            if (lbl != null)
                lbl.text = _selectedChunk.HasValue ? $"Chunk {_selectedChunk.Value.x},{_selectedChunk.Value.y}" : "Chunk —";
            RefreshStats();
        }

        /// <summary>Replace the Saved Worlds list in World Management.</summary>
        public void SetSavedWorlds(IEnumerable<SavedWorldInfo> worlds)
        {
            _savedWorlds = new List<SavedWorldInfo>(worlds ?? new List<SavedWorldInfo>());
            RefreshSavedWorlds();
        }

        private void RefreshSavedWorlds()
        {
            var list = _root.Q("world-list");
            if (list == null) return;
            list.Clear();
            _root.Q<Label>("lbl-no-worlds")?.EnableInClassList("hidden", _savedWorlds.Count > 0);

            foreach (var w in _savedWorlds)
            {
                var row = new VisualElement();
                row.AddToClassList("world-row");

                var text = new VisualElement();
                text.AddToClassList("asset-text");
                var name = new Label(w.Name);
                name.AddToClassList("asset-name");
                var meta = new Label(w.Subtitle ?? string.Empty);
                meta.AddToClassList("world-meta");
                text.Add(name);
                text.Add(meta);
                row.Add(text);

                if (w.IsCurrent)
                {
                    var tag = new Label("CURRENT");
                    tag.AddToClassList("world-tag");
                    row.Add(tag);
                }
                else
                {
                    var load = new Button { text = "LOAD →" };
                    load.AddToClassList("btn");
                    load.AddToClassList("btn--soft");
                    load.AddToClassList("world-load");
                    var captured = w.Name;
                    load.clicked += () => OnLoadWorldRequested?.Invoke(captured);
                    row.Add(load);
                }
                list.Add(row);
            }
        }

        // =====================================================================
        // Overview + layers
        // =====================================================================
        private void BindOverview()
        {
            Click("btn-toggle-layers", () => _layersPopover.ToggleInClassList("hidden"));

            BindLayerToggle("tgl-layer-terrain", "Terrain");
            BindLayerToggle("tgl-layer-water", "Water");
            BindLayerToggle("tgl-layer-roads", "Roads");
            BindLayerToggle("tgl-layer-veg", "Vegetation");
            BindLayerToggle("tgl-layer-buildings", "Buildings");
        }

        private void BindLayerToggle(string toggleName, string layerKey)
        {
            _root.Q<Toggle>(toggleName)?.RegisterValueChangedCallback(evt => OnLayerVisibilityChanged?.Invoke(layerKey, evt.newValue));
        }

        // =====================================================================
        // Add-to-library modal (opened from Vegetation / Objects)
        // =====================================================================
        private void BindAddLibraryModal()
        {
            void CloseModal() => _addModalBackdrop.AddToClassList("hidden");
            Click("btn-close-modal", CloseModal);
            Click("btn-cancel-modal", CloseModal);
            Click("btn-browse-asset", () => OnBrowseAssetRequested?.Invoke());

            Click("btn-confirm-add-item", () =>
            {
                var ddCat = _root.Q<DropdownField>("dd-new-item-category");
                var txtName = _root.Q<TextField>("txt-new-item-name");
                var txtAsset = _root.Q<TextField>("txt-new-item-asset");
                var tglSnap = _root.Q<Toggle>("tgl-new-item-snap");

                var category = (LibraryCategory)Mathf.Clamp(ddCat.index, 0, 7);
                var newItem = new LibraryAssetItem
                {
                    Category = category,
                    Name = string.IsNullOrWhiteSpace(txtName.value) ? "New Asset" : txtName.value,
                    AssetPath = txtAsset.value,
                    SnapToGround = tglSnap.value
                };

                libraryItems.Add(newItem);
                _selectedLibraryItem = newItem;
                FilterLibrary(category);
                OnLibraryAssetAdded?.Invoke(newItem);
                SetStatusMessage($"Added '{newItem.Name}' to {category}");
                CloseModal();
            });
        }

        private void OpenAddModal(LibraryCategory category)
        {
            var dd = _root.Q<DropdownField>("dd-new-item-category");
            if (dd != null) dd.index = (int)category;
            _addModalBackdrop.RemoveFromClassList("hidden");
        }

        // =====================================================================
        // Generic hooks + readouts
        // =====================================================================
        private void HookGenericEvents()
        {
            _root.Query<Slider>().ForEach(e => Hook(e));
            _root.Query<SliderInt>().ForEach(e => Hook(e));
            _root.Query<Toggle>().ForEach(e => Hook(e));
            _root.Query<FloatField>().ForEach(e => Hook(e));
            _root.Query<IntegerField>().ForEach(e => Hook(e));
            _root.Query<DropdownField>().ForEach(e => Hook(e));
            _root.Query<TextField>().ForEach(e => Hook(e));

            _root.Query<Button>().ForEach(b =>
            {
                if (string.IsNullOrEmpty(b.name)) return;
                var n = b.name;
                b.clicked += () => OnButtonClicked?.Invoke(n);
            });
        }

        private void Hook<T>(BaseField<T> field)
        {
            if (string.IsNullOrEmpty(field.name)) return;
            var n = field.name;
            bool viewOnly = n.StartsWith("dd-view") || n.StartsWith("dd-shading") || n.StartsWith("tgl-layer");
            field.RegisterValueChangedCallback(evt =>
            {
                OnValueChanged?.Invoke(n, evt.newValue);
                if (!viewOnly) SetSaveState(false);
            });
        }

        private void RefreshActiveDetail()
        {
            if (_lblActiveDetail == null) return;
            _lblActiveDetail.text = _currentMode switch
            {
                ToolMode.Sculpt => $"{_sculptTool} {F("sld-brush-radius"):0} m  Strength {F("sld-brush-strength"):0.00}",
                ToolMode.Paint => $"{_selectedPaintLayer ?? "—"} {(_paintErase ? "(erase) " : "")}{F("sld-paint-size"):0} m",
                ToolMode.Nature => $"{_selectedLibraryItem?.Name ?? "No asset"}  Density {F("sld-veg-density"):0.00}",
                ToolMode.Roads => S("txt-spline-name"),
                ToolMode.Water => $"{_waterType}: {S("txt-water-name")}",
                ToolMode.Objects => _selectedLibraryItem?.Name ?? string.Empty,
                _ => string.Empty
            };
        }

        private void RefreshStats()
        {
            int res = int.TryParse(S("dd-heightmap-res"), out var r) ? r : 513;
            string resText = $"{Num(res)} × {Num(res)}";
            SetText("lbl-stat-resolution", resText);
            SetText("lbl-chunk-res", resText);

            int cols = Mathf.Clamp((int)F("fld-chunk-cols", 4), 1, 16);
            int rows = Mathf.Clamp((int)F("fld-chunk-rows", 4), 1, 16);
            string dims = $"{Num(cols * F("fld-chunk-width", 512))} × {Num(rows * F("fld-chunk-depth", 512))} m";
            SetText("lbl-stat-chunks", $"{cols} × {rows}");
            SetText("lbl-world-dims", dims);
            SetText("lbl-world-size", dims);
            SetText("lbl-chunk-active", $"{cols * rows} active chunks · {(_selectedChunk.HasValue ? "1 selected" : "none selected")}");
        }

        private void SetText(string labelName, string text)
        {
            var l = _root.Q<Label>(labelName);
            if (l != null) l.text = text;
        }

        // =====================================================================
        // Public API for the rest of the project
        // =====================================================================
        public void SetChunkCount(int count)
        {
            if (_lblChunkCount != null)
                _lblChunkCount.text = $"{count} chunks";
        }

        public void SetVertexCount(long vertices) => SetText("lbl-vertex-count", $"{vertices / 1_000_000f:0.0}M vertices");
        public void SetFps(float fps) => SetText("lbl-fps", $"{Mathf.RoundToInt(fps)} FPS");
        public void SetCursorWorldPosition(Vector3 p) =>
            SetText("lbl-coords", $"X {p.x:0.0}  Y {p.y:0.0}  Z {p.z:0.0}");
        public void SetProjectName(string projectName) => SetText("lbl-project-name", projectName);
        public void SetSplineInfo(int controlPoints, float lengthMeters) =>
            SetText("lbl-spline-info", $"{controlPoints} control points · {Num(lengthMeters)} m total length");
        public void SetStorageInfo(string text) => SetText("lbl-storage", text);
        public void SetLastSaved(string text) => SetText("lbl-last-saved", text);

        public void SetSaveState(bool saved)
        {
            if (_lblSaveState != null) _lblSaveState.text = saved ? "All changes saved" : "Unsaved changes";
            _saveDot?.EnableInClassList("save-dot--dirty", !saved);
        }

        public void SetStatusMessage(string msg)
        {
            if (_lblStatusMsg != null)
                _lblStatusMsg.text = msg;
        }
    }
}