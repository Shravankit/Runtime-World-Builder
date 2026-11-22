
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class VegetationTuner : MonoBehaviour
    {
        [Header("UI Toolkit")]
        [SerializeField] private UIDocument uiDocument;

        private Toggle treesToggle;
        private Toggle grassToggle;
        private Toggle shadowsToggle;

        private Slider treeDistanceSlider;
        private Slider billboardDistanceSlider;
        private Slider grassDistanceSlider;
        private Slider densitySlider;

        private TerrainWorld lastAppliedWorld;
        private bool uiBound;

        private bool trees = true;
        private bool grass = true;
        private bool shadows = true;

        private float treeDist = 600f;
        private float billboardDist = 200f;
        private float detailDist = 80f;
        private float density = 0.7f;

        private void Reset()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BindUI();
        }

        private void Start()
        {
            // Retry in case the UIDocument was not ready during OnEnable.
            BindUI();
        }

        private void Update()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
            {
                lastAppliedWorld = null;
                return;
            }

            // Apply settings when a new terrain world is created.
            if (world != lastAppliedWorld)
            {
                Apply();
            }
        }

        private void BindUI()
        {
            if (uiBound)
                return;

            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            if (uiDocument == null)
                uiDocument = FindFirstObjectByType<UIDocument>();

            if (uiDocument == null)
                return;

            VisualElement root = uiDocument.rootVisualElement;

            if (root == null)
                return;

            treesToggle = root.Q<Toggle>("tgl-place-trees");
            grassToggle = root.Q<Toggle>("tgl-place-grass");
            shadowsToggle = root.Q<Toggle>("tgl-veg-shadows");

            treeDistanceSlider = root.Q<Slider>("sld-tree-dist");
            billboardDistanceSlider = root.Q<Slider>("sld-tree-billboard-dist");
            grassDistanceSlider = root.Q<Slider>("sld-grass-dist");
            densitySlider = root.Q<Slider>("sld-veg-density");

            if (treesToggle == null ||
                grassToggle == null ||
                shadowsToggle == null ||
                treeDistanceSlider == null ||
                grassDistanceSlider == null ||
                densitySlider == null)
            {
                Debug.LogWarning(
                    "[VegetationTuner] One or more Nature UI controls were not found."
                );
                return;
            }

            // Read initial values from the UI.
            trees = treesToggle.value;
            grass = grassToggle.value;
            shadows = shadowsToggle.value;

            treeDist = treeDistanceSlider.value;
            detailDist = grassDistanceSlider.value;
            density = densitySlider.value;

            if (billboardDistanceSlider != null)
                billboardDist = billboardDistanceSlider.value;

            // Register callbacks.
            treesToggle.RegisterValueChangedCallback(OnTreesChanged);
            grassToggle.RegisterValueChangedCallback(OnGrassChanged);
            shadowsToggle.RegisterValueChangedCallback(OnShadowsChanged);

            treeDistanceSlider.RegisterValueChangedCallback(OnTreeDistanceChanged);
            grassDistanceSlider.RegisterValueChangedCallback(OnGrassDistanceChanged);
            densitySlider.RegisterValueChangedCallback(OnDensityChanged);

            if (billboardDistanceSlider != null)
                billboardDistanceSlider.RegisterValueChangedCallback(OnBillboardDistanceChanged);

            uiBound = true;

            Apply();
        }

        private void OnTreesChanged(ChangeEvent<bool> evt)
        {
            trees = evt.newValue;
            Apply();
        }

        private void OnGrassChanged(ChangeEvent<bool> evt)
        {
            grass = evt.newValue;
            Apply();
        }

        private void OnShadowsChanged(ChangeEvent<bool> evt)
        {
            shadows = evt.newValue;
            Apply();
        }

        private void OnTreeDistanceChanged(ChangeEvent<float> evt)
        {
            treeDist = evt.newValue;
            Apply();
        }

        private void OnBillboardDistanceChanged(ChangeEvent<float> evt)
        {
            billboardDist = evt.newValue;
            Apply();
        }

        private void OnGrassDistanceChanged(ChangeEvent<float> evt)
        {
            detailDist = evt.newValue;
            Apply();
        }

        private void OnDensityChanged(ChangeEvent<float> evt)
        {
            density = evt.newValue;
            Apply();
        }

        private void Apply()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
                return;

            foreach (var chunk in world.Chunks)
            {
                Terrain terrain = chunk.Terrain;

                if (terrain == null)
                    continue;

                terrain.drawTreesAndFoliage = trees || grass;

                // Zero disables tree rendering.
                terrain.treeDistance = trees ? treeDist : 0f;

                terrain.treeBillboardDistance = billboardDist;

                // Zero disables grass/detail rendering.
                terrain.detailObjectDistance = grass ? detailDist : 0f;

                terrain.detailObjectDensity = density;
            }

            // This setting affects the entire application.
            QualitySettings.shadows = shadows
                ? ShadowQuality.All
                : ShadowQuality.Disable;

            lastAppliedWorld = world;
        }

        private void OnDisable()
        {
            UnbindUI();
        }

        private void UnbindUI()
        {
            if (!uiBound)
                return;

            treesToggle?.UnregisterValueChangedCallback(OnTreesChanged);
            grassToggle?.UnregisterValueChangedCallback(OnGrassChanged);
            shadowsToggle?.UnregisterValueChangedCallback(OnShadowsChanged);

            treeDistanceSlider?.UnregisterValueChangedCallback(OnTreeDistanceChanged);
            grassDistanceSlider?.UnregisterValueChangedCallback(OnGrassDistanceChanged);
            densitySlider?.UnregisterValueChangedCallback(OnDensityChanged);

            if (billboardDistanceSlider != null)
                billboardDistanceSlider.UnregisterValueChangedCallback(
                    OnBillboardDistanceChanged
                );

            uiBound = false;
        }
    }
}
