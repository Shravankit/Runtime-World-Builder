using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.UI
{
    /// <summary>
    /// Low-coverage studio UI Toolkit controller with a dynamic Asset Library
    /// and "Add to Asset Library" modal.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RuntimeWorldBuilderUI : MonoBehaviour
    {
        public enum ToolMode
        {
            Sculpt,
            Stamps,
            Roads,
            Paint,
            Nature,
            Library,
            Settings
        }

        public enum LibraryCategory
        {
            Hills,
            Roads,
            Rivers,
            Trees,
            Grass,
            Rocks,
            Textures,
            Buildings
        }

        [Serializable]
        public class LibraryAssetItem
        {
            public LibraryCategory Category;
            public string Name;
            public string AssetPath;
            public bool SnapToGround = true;
            public Texture2D Thumbnail;
        }

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

        private VisualElement _root;
        private VisualElement _toolPopover;
        private VisualElement _libraryContent;
        private VisualElement _libraryCardsContainer;
        private VisualElement _addModalBackdrop;
        private VisualElement _layersPopover;
        private VisualElement _workspaceArea;
        private VisualElement _bottomDockWrap;

        private Label _lblActiveTool;
        private Label _lblPopoverTitle;
        private Label _lblChunkCount;
        private Label _lblStatusMsg;
        private Button _btnHideUI;

        private bool _uiHidden;
        private bool _libraryMinimized;
        private LibraryCategory _activeLibraryCategory = LibraryCategory.Hills;
        private LibraryAssetItem _selectedLibraryItem;

        private readonly Dictionary<ToolMode, Button> _railButtons = new();
        private readonly Dictionary<ToolMode, VisualElement> _popoverBodies = new();
        private readonly Dictionary<LibraryCategory, Button> _libTabs = new();
        private readonly List<Button> _toolChips = new();

        // Core Events
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

        // Asset Library Events
        public event Action<LibraryAssetItem> OnLibraryAssetSelected;
        public event Action<LibraryAssetItem> OnLibraryAssetAdded;
        public event Action OnBrowseAssetRequested;

        private void Reset() => uiDocument = GetComponent<UIDocument>();

        private void OnEnable()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            _root = uiDocument.rootVisualElement;
            if (_root == null) return;

            CacheElements();
            BindTopBar();
            BindLeftRail();
            BindPopovers();
            BindMiniMapAndLayers();
            BindAssetLibrary();
            BindAddLibraryModal();

            SelectRailTool(ToolMode.Sculpt);
            FilterLibrary(LibraryCategory.Hills);
            SetStatusMessage("Ready");
        }

        private void CacheElements()
        {
            _workspaceArea = _root.Q<VisualElement>("workspace-area");
            _bottomDockWrap = _root.Q<VisualElement>("bottom-dock-wrap");
            _toolPopover = _root.Q<VisualElement>("tool-popover");
            _libraryContent = _root.Q<VisualElement>("library-content");
            _libraryCardsContainer = _root.Q<VisualElement>("library-cards-container");
            _addModalBackdrop = _root.Q<VisualElement>("add-library-modal-backdrop");
            _layersPopover = _root.Q<VisualElement>("layers-popover");

            _lblActiveTool = _root.Q<Label>("lbl-active-tool");
            _lblPopoverTitle = _root.Q<Label>("lbl-popover-title");
            _lblChunkCount = _root.Q<Label>("lbl-chunk-count");
            _lblStatusMsg = _root.Q<Label>("lbl-status-msg");
            _btnHideUI = _root.Q<Button>("btn-hide-ui");
        }

        private void BindTopBar()
        {
            _root.Q<Button>("btn-undo").clicked += () => OnUndoRequested?.Invoke();
            _root.Q<Button>("btn-redo").clicked += () => OnRedoRequested?.Invoke();

            _root.Q<Button>("btn-quick-save").clicked += () =>
            {
                var name = _root.Q<TextField>("txt-world-name")?.value ?? "MyRuntimeWorld";
                var compress = _root.Q<Toggle>("tgl-compress-chunks")?.value ?? true;
                var stamps = _root.Q<Toggle>("tgl-restore-stamps")?.value ?? true;
                OnSaveWorldRequested?.Invoke(name, compress, stamps);
                SetStatusMessage($"Saved '{name}'");
            };

            _root.Q<Button>("btn-quick-load").clicked += () =>
            {
                var name = _root.Q<TextField>("txt-world-name")?.value ?? "MyRuntimeWorld";
                OnLoadWorldRequested?.Invoke(name);
            };

            bool isPlaying = false;
            var btnPlay = _root.Q<Button>("btn-play-mode");
            btnPlay.clicked += () =>
            {
                isPlaying = !isPlaying;
                btnPlay.text = isPlaying ? "■ Stop" : "▶ Play";
                OnPlayModeToggled?.Invoke(isPlaying);
            };

            _btnHideUI.clicked += () =>
            {
                _uiHidden = !_uiHidden;
                _workspaceArea.EnableInClassList("hidden", _uiHidden);
                _bottomDockWrap.EnableInClassList("hidden", _uiHidden);
                _btnHideUI.text = _uiHidden ? "Show UI" : "✕ Hide UI";
            };
        }

        private void BindLeftRail()
        {
            RegisterRail(ToolMode.Sculpt, "rail-sculpt", "pop-sculpt", "Brush");
            RegisterRail(ToolMode.Stamps, "rail-stamps", "pop-stamps", "Heightmap Stamp");
            RegisterRail(ToolMode.Roads, "rail-roads", "pop-roads", "Roads & Water");
            RegisterRail(ToolMode.Paint, "rail-paint", "pop-paint", "Paint Biomes");
            RegisterRail(ToolMode.Nature, "rail-nature", "pop-nature", "Paint & Nature");
            RegisterRail(ToolMode.Settings, "rail-settings", "pop-settings", "World Settings");

            var libBtn = _root.Q<Button>("rail-library");
            if (libBtn != null)
            {
                _railButtons[ToolMode.Library] = libBtn;
                libBtn.clicked += () =>
                {
                    _libraryMinimized = false;
                    _libraryContent.RemoveFromClassList("hidden");
                    _toolPopover.AddToClassList("hidden");
                    SetActiveRailHighlight(ToolMode.Library);
                };
            }

            _root.Q<Button>("btn-close-popover").clicked += () => _toolPopover.AddToClassList("hidden");
        }

        private void RegisterRail(ToolMode mode, string btnName, string popName, string title)
        {
            var btn = _root.Q<Button>(btnName);
            var pop = _root.Q<VisualElement>(popName);
            if (btn == null || pop == null) return;

            _railButtons[mode] = btn;
            _popoverBodies[mode] = pop;
            btn.clicked += () => SelectRailTool(mode, title);
        }

        public void SelectRailTool(ToolMode mode, string title = null)
        {
            SetActiveRailHighlight(mode);
            _toolPopover.RemoveFromClassList("hidden");

            foreach (var kvp in _popoverBodies)
                kvp.Value.EnableInClassList("hidden", kvp.Key != mode);

            if (_lblPopoverTitle != null && !string.IsNullOrEmpty(title))
                _lblPopoverTitle.text = title;

            if (_lblActiveTool != null)
                _lblActiveTool.text = mode.ToString();
        }

        private void SetActiveRailHighlight(ToolMode active)
        {
            foreach (var kvp in _railButtons)
                kvp.Value.EnableInClassList("rail-btn--active", kvp.Key == active);
        }

        private void BindPopovers()
        {
            // Sculpt
            RegisterToolChip("btn-tool-hill", "Raise");
            RegisterToolChip("btn-tool-valley", "Lower");
            RegisterToolChip("btn-tool-smooth", "Smooth");
            RegisterToolChip("btn-tool-flatten", "Flatten");

            var sldRad = _root.Q<Slider>("sld-brush-radius");
            var sldStr = _root.Q<Slider>("sld-brush-strength");
            var sldFall = _root.Q<Slider>("sld-brush-falloff");
            var fldFlat = _root.Q<FloatField>("fld-flatten-height");
            void NotifyBrush() => OnBrushSettingsChanged?.Invoke(sldRad.value, sldStr.value, sldFall.value, fldFlat.value);
            sldRad.RegisterValueChangedCallback(_ => NotifyBrush());
            sldStr.RegisterValueChangedCallback(_ => NotifyBrush());
            sldFall.RegisterValueChangedCallback(_ => NotifyBrush());
            fldFlat.RegisterValueChangedCallback(_ => NotifyBrush());

            // Stamps
            _root.Q<Button>("btn-apply-stamp").clicked += () =>
            {
                var lib = _root.Q<DropdownField>("dd-heightmap-library").value;
                var blend = _root.Q<DropdownField>("dd-stamp-blend").value;
                var scale = _root.Q<Slider>("sld-stamp-scale").value;
                var rot = _root.Q<Slider>("sld-stamp-rotation").value;
                var prev = _root.Q<Toggle>("tgl-stamp-preview").value;
                OnApplyStampRequested?.Invoke(lib, blend, scale, rot, prev);
                SetStatusMessage($"Applied stamp: {lib}");
            };

            // Roads & Water
            RegisterToolChip("btn-builder-road", "Road");
            RegisterToolChip("btn-builder-river", "River");
            RegisterToolChip("btn-builder-lake", "Lake");
            RegisterToolChip("btn-builder-pond", "Pond");

            var sldW = _root.Q<Slider>("sld-path-width");
            var sldD = _root.Q<Slider>("sld-path-depth");
            void NotifyPath() => OnPathSettingsChanged?.Invoke(sldW.value, sldD.value);
            sldW.RegisterValueChangedCallback(_ => NotifyPath());
            sldD.RegisterValueChangedCallback(_ => NotifyPath());

            _root.Q<Button>("btn-spline-undo-pt").clicked += () => OnSplineUndoPointRequested?.Invoke();
            _root.Q<Button>("btn-spline-finish").clicked += () => OnSplineFinishRequested?.Invoke();
            _root.Q<Button>("btn-spline-cancel").clicked += () => OnSplineCancelRequested?.Invoke();

            var sldWR = _root.Q<Slider>("sld-water-radius");
            var fldWL = _root.Q<FloatField>("fld-water-level");
            void NotifyWater() => OnWaterSettingsChanged?.Invoke(sldWR.value, fldWL.value);
            sldWR.RegisterValueChangedCallback(_ => NotifyWater());
            fldWL.RegisterValueChangedCallback(_ => NotifyWater());

            // Paint
            _root.Q<Button>("btn-paint-terrain").clicked += () =>
            {
                var biome = _root.Q<DropdownField>("dd-biome-preset").value;
                var h = _root.Q<Toggle>("tgl-rule-height").value;
                var s = _root.Q<Toggle>("tgl-rule-slope").value;
                var w = _root.Q<Toggle>("tgl-rule-worldpos").value;
                var b = _root.Q<Slider>("sld-layer-blend").value;
                OnAutoPaintTerrainRequested?.Invoke(biome, h, s, w, b);
                SetStatusMessage($"Painted biome: {biome}");
            };

            // Nature
            _root.Q<Button>("btn-populate-veg").clicked += () =>
            {
                var veg = _root.Q<DropdownField>("dd-veg-preset").value;
                var trees = _root.Q<Toggle>("tgl-place-trees").value;
                var grass = _root.Q<Toggle>("tgl-place-grass").value;
                var bio = _root.Q<Toggle>("tgl-biome-dist").value;
                var dens = _root.Q<Slider>("sld-veg-density").value;
                var tDist = _root.Q<Slider>("sld-tree-dist").value;
                var gDist = _root.Q<Slider>("sld-grass-dist").value;
                var sh = _root.Q<Toggle>("tgl-veg-shadows").value;
                OnPopulateVegetationRequested?.Invoke(veg, trees, grass, bio, dens, tDist, gDist, sh);
                SetStatusMessage("Growing vegetation...");
            };
            _root.Q<Button>("btn-clear-veg").clicked += () => OnClearVegetationRequested?.Invoke();

            // Settings (Gen, Expand, Save/Load)
            _root.Q<Button>("btn-generate-terrain").clicked += () =>
            {
                int res = int.TryParse(_root.Q<DropdownField>("dd-heightmap-res").value, out var r) ? r : 513;
                int size = _root.Q<IntegerField>("fld-terrain-size").value;
                float h = _root.Q<FloatField>("fld-max-height").value;
                int seed = _root.Q<IntegerField>("fld-seed").value;
                OnGenerateTerrainRequested?.Invoke(res, size, h, seed);
            };
            _root.Q<Button>("btn-expand-pos-z").clicked += () => OnExpandWorldRequested?.Invoke(Vector2Int.up);
            _root.Q<Button>("btn-expand-neg-z").clicked += () => OnExpandWorldRequested?.Invoke(Vector2Int.down);
            _root.Q<Button>("btn-expand-pos-x").clicked += () => OnExpandWorldRequested?.Invoke(Vector2Int.right);
            _root.Q<Button>("btn-expand-neg-x").clicked += () => OnExpandWorldRequested?.Invoke(Vector2Int.left);
            _root.Q<Button>("btn-verify-continuity").clicked += () => OnVerifyTerrainRequested?.Invoke();

            _root.Q<Button>("btn-save-world").clicked += () =>
            {
                var name = _root.Q<TextField>("txt-world-name").value;
                var comp = _root.Q<Toggle>("tgl-compress-chunks").value;
                var st = _root.Q<Toggle>("tgl-restore-stamps").value;
                OnSaveWorldRequested?.Invoke(name, comp, st);
            };
            _root.Q<Button>("btn-load-world").clicked += () =>
                OnLoadWorldRequested?.Invoke(_root.Q<TextField>("txt-world-name").value);
        }

        private void RegisterToolChip(string btnName, string toolId)
        {
            var btn = _root.Q<Button>(btnName);
            if (btn == null) return;
            _toolChips.Add(btn);
            btn.userData = toolId;
            btn.clicked += () =>
            {
                foreach (var chip in _toolChips)
                    chip.EnableInClassList("tool-chip--active", (chip.userData as string) == toolId);
                OnActiveToolSelected?.Invoke(toolId);
                SetStatusMessage($"Tool: {toolId}");
            };
        }

        private void BindMiniMapAndLayers()
        {
            _root.Q<Button>("btn-toggle-layers").clicked += () =>
                _layersPopover.ToggleInClassList("hidden");

            BindLayerToggle("tgl-layer-terrain", "Terrain");
            BindLayerToggle("tgl-layer-water", "Water");
            BindLayerToggle("tgl-layer-roads", "Roads");
            BindLayerToggle("tgl-layer-veg", "Vegetation");
            BindLayerToggle("tgl-layer-buildings", "Buildings");
        }

        private void BindLayerToggle(string toggleName, string layerKey)
        {
            var tgl = _root.Q<Toggle>(toggleName);
            tgl?.RegisterValueChangedCallback(evt => OnLayerVisibilityChanged?.Invoke(layerKey, evt.newValue));
        }

        private void BindAssetLibrary()
        {
            RegisterLibTab(LibraryCategory.Hills, "lib-tab-hills");
            RegisterLibTab(LibraryCategory.Roads, "lib-tab-roads");
            RegisterLibTab(LibraryCategory.Rivers, "lib-tab-rivers");
            RegisterLibTab(LibraryCategory.Trees, "lib-tab-trees");
            RegisterLibTab(LibraryCategory.Grass, "lib-tab-grass");
            RegisterLibTab(LibraryCategory.Rocks, "lib-tab-rocks");
            RegisterLibTab(LibraryCategory.Textures, "lib-tab-textures");
            RegisterLibTab(LibraryCategory.Buildings, "lib-tab-buildings");

            var btnMin = _root.Q<Button>("btn-minimize-library");
            btnMin.clicked += () =>
            {
                _libraryMinimized = !_libraryMinimized;
                _libraryContent.EnableInClassList("hidden", _libraryMinimized);
                btnMin.text = _libraryMinimized ? "⌃" : "⌄";
            };
        }

        private void RegisterLibTab(LibraryCategory category, string tabName)
        {
            var btn = _root.Q<Button>(tabName);
            if (btn == null) return;
            _libTabs[category] = btn;
            btn.clicked += () => FilterLibrary(category);
        }

        public void FilterLibrary(LibraryCategory category)
        {
            _activeLibraryCategory = category;
            foreach (var kvp in _libTabs)
                kvp.Value.EnableInClassList("lib-tab--active", kvp.Key == category);

            RebuildLibraryCards();
        }

        private void RebuildLibraryCards()
        {
            if (_libraryCardsContainer == null) return;
            _libraryCardsContainer.Clear();

            foreach (var item in libraryItems)
            {
                if (item.Category != _activeLibraryCategory) continue;

                var card = new VisualElement();
                card.AddToClassList("asset-card");
                if (item == _selectedLibraryItem)
                    card.AddToClassList("asset-card--selected");

                var thumb = new VisualElement();
                thumb.AddToClassList("asset-card__thumb");
                if (item.Thumbnail != null)
                    thumb.style.backgroundImage = new StyleBackground(item.Thumbnail);

                var lbl = new Label(item.Name);
                lbl.AddToClassList("asset-card__label");

                card.Add(thumb);
                card.Add(lbl);

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedLibraryItem = item;
                    RebuildLibraryCards();
                    OnLibraryAssetSelected?.Invoke(item);
                    SetStatusMessage($"Selected {item.Category}: {item.Name}");
                });

                _libraryCardsContainer.Add(card);
            }
        }

        private void BindAddLibraryModal()
        {
            _root.Q<Button>("btn-open-add-modal").clicked += () =>
            {
                _root.Q<DropdownField>("dd-new-item-category").index = (int)_activeLibraryCategory;
                _addModalBackdrop.RemoveFromClassList("hidden");
            };

            void CloseModal() => _addModalBackdrop.AddToClassList("hidden");
            _root.Q<Button>("btn-close-modal").clicked += CloseModal;
            _root.Q<Button>("btn-cancel-modal").clicked += CloseModal;

            _root.Q<Button>("btn-browse-asset").clicked += () => OnBrowseAssetRequested?.Invoke();

            _root.Q<Button>("btn-confirm-add-item").clicked += () =>
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
            };
        }

        public void SetChunkCount(int count)
        {
            if (_lblChunkCount != null)
                _lblChunkCount.text = $"Chunks: {count}";
        }

        public void SetStatusMessage(string msg)
        {
            if (_lblStatusMsg != null)
                _lblStatusMsg.text = msg;
        }
    }
}