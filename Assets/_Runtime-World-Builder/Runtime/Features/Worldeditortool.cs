using System.Collections.Generic;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Import;
using RuntimeWorldBuilder.Runtime.Testing;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using RuntimeWorldBuilder.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Drives Road / River / Lake / Pond / Raise / Lower / Smooth / Flatten / Erosion / Stamp from the rail tabs.
    /// Sculpt tools are a continuous brush: hold LMB and drag. Shift = invert Raise/Lower.
    /// [ ] and + - change brush size. One drag = one undo step.
    public class WorldEditorTool : MonoBehaviour
    {
        private enum Mode { None, Stamp, Road, River, Lake, Pond, Hill, Valley, Flatten, Smooth, Erosion }

        [Header("Feature Materials")]
        [SerializeField] private FeatureMaterials materials = new();
        [SerializeField] private RoadStyle road = new();

        [Header("Water")]
        [SerializeField] private float riverWidth = 24f, riverDepth = 4f;
        [SerializeField] private float lakeRadius = 45f, lakeDepth = 8f;
        [SerializeField] private float pondRadius = 12f, pondDepth = 2f;

        [Header("Sculpt Brush")]
        [SerializeField] private float shapeRadius = 60f;
        [Tooltip("Metres per second at strength 1 for Raise / Lower.")]
        [SerializeField] private float raiseMetersPerSecond = 25f;
        [Tooltip("Slopes steeper than this are eroded down.")]
        [SerializeField] private float erosionTalusDegrees = 30f;

        [Header("References")]
        [SerializeField] private HeightmapPlacer stamper;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private RuntimeWorldBuilderUI builderUI;

        private Mode mode = Mode.None;
        private readonly List<Vector2> points = new();
        private LineRenderer lineRenderer, brushRing;
        private Transform featureRoot;
        private VisualElement uiRoot;
        private bool stamperOn = true;
        private readonly List<GameObject> markers = new();
        private static Material markerMat;

        // brush state (bound to the Sculpt tab)
        private float brushStrength = 0.8f, brushFalloff = 0.5f, flattenHeightM = 25f;
        private int brushShape;                 // 0 soft round, 1 hard round, 2 square
        private bool showOutline = true, lockTarget;
        private bool strokeActive;
        private TerrainWorld strokeWorld;
        private float flattenTarget01;

        // ---------------- lifecycle ----------------
        private void OnEnable() => FeatureEvents.ClearRequested += ClearFeatures;

        private void OnDisable()
        {
            FeatureEvents.ClearRequested -= ClearFeatures;
            EndStroke();
            HideRing();
            SetStamper(true);
            ClearPreviewLine();
        }

        private void Awake() => CreateLineRenderers();

        private void Start()
        {
            if (stamper == null) stamper = FindFirstObjectByType<HeightmapPlacer>();
            if (uiDocument == null) uiDocument = FindFirstObjectByType<UIDocument>();
            if (builderUI == null) builderUI = FindFirstObjectByType<RuntimeWorldBuilderUI>();

            uiRoot = uiDocument != null ? uiDocument.rootVisualElement : null;

            if (builderUI != null)
            {
                builderUI.OnToolModeChanged += OnToolModeChanged;
                builderUI.OnActiveToolSelected += OnActiveToolSelected;
            }
            else Debug.LogError("[WorldEditorTool] RuntimeWorldBuilderUI not found - tools cannot be selected.");

            if (uiRoot != null)
            {
                Click("btn-spline-finish", FinishCurrentFeature);
                Click("btn-water-finish", FinishCurrentFeature);
                Click("btn-spline-cancel", Cancel);
                Click("btn-water-cancel", Cancel);
                Click("btn-spline-undo-pt", RemoveLastPoint);
                Click("btn-water-undo-pt", RemoveLastPoint);
                Click("btn-clear-features", ClearFeatures);

                BindSlider("sld-path-width", v => road.width = v);
                BindSlider("sld-road-shoulder", v => road.shoulder = v);
                BindSlider("sld-water-width", v => riverWidth = v);
                BindSlider("sld-water-depth", v => { riverDepth = v; lakeDepth = Mathf.Max(2f, v); pondDepth = Mathf.Clamp(v * 0.5f, 0.5f, 5f); });
                BindSlider("sld-water-radius", v => { lakeRadius = v; pondRadius = Mathf.Min(v, 30f); });

                // sculpt brush
                BindSlider("sld-brush-radius", v => shapeRadius = Mathf.Max(5f, v * 2f));
                BindSlider("sld-brush-strength", v => brushStrength = v);
                BindSlider("sld-brush-falloff", v => brushFalloff = v);
                BindToggle("tgl-brush-outline", v => showOutline = v);
                BindToggle("tgl-lock-target", v => lockTarget = v);
                BindFloat("fld-flatten-height", v => flattenHeightM = v);
                var shape = uiRoot.Q<DropdownField>("dd-brush-shape");
                if (shape != null)
                {
                    brushShape = Mathf.Max(0, shape.choices.IndexOf(shape.value));
                    shape.RegisterValueChangedCallback(e => brushShape = Mathf.Max(0, shape.choices.IndexOf(e.newValue)));
                }
            }
            SetStamper(false);
        }

        private void OnDestroy()
        {
            if (builderUI != null)
            {
                builderUI.OnToolModeChanged -= OnToolModeChanged;
                builderUI.OnActiveToolSelected -= OnActiveToolSelected;
            }
            if (lineRenderer != null) Destroy(lineRenderer.gameObject);
            if (brushRing != null) Destroy(brushRing.gameObject);
        }

        private void Click(string name, System.Action a)
        {
            var b = uiRoot.Q<Button>(name);
            if (b != null) b.clicked += a;
        }

        private void BindSlider(string name, System.Action<float> set)
        {
            var s = uiRoot.Q<Slider>(name);
            if (s == null) return;
            set(s.value);
            s.RegisterValueChangedCallback(e => set(e.newValue));
        }

        private void BindToggle(string name, System.Action<bool> set)
        {
            var t = uiRoot.Q<Toggle>(name);
            if (t == null) return;
            set(t.value);
            t.RegisterValueChangedCallback(e => set(e.newValue));
        }

        private void BindFloat(string name, System.Action<float> set)
        {
            var f = uiRoot.Q<FloatField>(name);
            if (f == null) return;
            set(f.value);
            f.RegisterValueChangedCallback(e => set(e.newValue));
        }

        private void CreateLineRenderers()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));

            var go = new GameObject("WorldEditorLine");
            go.transform.SetParent(transform, false);
            lineRenderer = go.AddComponent<LineRenderer>();
            lineRenderer.material = mat;
            lineRenderer.startColor = lineRenderer.endColor = new Color(1f, 0.9f, 0.1f);
            lineRenderer.widthMultiplier = 1.5f;
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 0;

            var rg = new GameObject("BrushRing");
            rg.transform.SetParent(transform, false);
            rg.layer = 2;
            brushRing = rg.AddComponent<LineRenderer>();
            brushRing.material = mat;
            brushRing.useWorldSpace = true;
            brushRing.positionCount = RingPoints + 1;
            brushRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            brushRing.enabled = false;
        }

        // ---------------- tool selection from UI ----------------
        private void OnToolModeChanged(RuntimeWorldBuilderUI.ToolMode tab)
        {
            SetMode(tab == RuntimeWorldBuilderUI.ToolMode.Stamps ? Mode.Stamp : Mode.None);
        }

        private void OnActiveToolSelected(string tool)
        {
            switch (tool)
            {
                case "Raise": SetMode(Mode.Hill); break;
                case "Lower": SetMode(Mode.Valley); break;
                case "Flatten": SetMode(Mode.Flatten); break;
                case "Smooth": SetMode(Mode.Smooth); break;
                case "Erosion": SetMode(Mode.Erosion); break;
                case "Road": SetMode(Mode.Road); break;
                case "River": SetMode(Mode.River); break;
                case "Lake": SetMode(Mode.Lake); break;
                case "Pond": SetMode(Mode.Pond); break;
                    // Move / Rotate / Scale belong to the Objects tab: ignored here
            }
        }

        private void SetMode(Mode m)
        {
            EndStroke();
            mode = m;
            Cancel();
            if (!IsBrush(m)) HideRing();
            Debug.Log("[WorldEditorTool] Active tool = " + m);
            builderUI?.SetStatusMessage(m == Mode.None ? "No terrain tool active" : $"Tool: {m}");
        }

        private static bool IsBrush(Mode m) =>
            m == Mode.Hill || m == Mode.Valley || m == Mode.Flatten || m == Mode.Smooth || m == Mode.Erosion;

        private void SetStamper(bool on)
        {
            HeightmapPlacer.ToolAllowed = on;
            if (stamper == null || stamperOn == on) return;
            stamperOn = on;
            foreach (var lr in stamper.GetComponents<LineRenderer>()) lr.enabled = on;
            var ghost = stamper.transform.Find("StampGhost");
            var arrow = stamper.transform.Find("StampArrow");
            if (ghost != null) ghost.gameObject.SetActive(on);
            if (arrow != null) arrow.gameObject.SetActive(on);
        }

        // ---------------- UI pointer ----------------
        private bool IsPointerOverUI()
        {
            if (HeightmapImportTool.OverPanel) return true;
            if (uiRoot == null || uiRoot.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(uiRoot.panel, Input.mousePosition);
            var picked = uiRoot.panel.Pick(p);
            return picked != null && picked != uiRoot && picked.pickingMode != PickingMode.Ignore;
        }

        private bool Typing()
        {
            var f = uiRoot?.panel?.focusController?.focusedElement as VisualElement;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }

        private Transform Root
        {
            get
            {
                if (featureRoot == null) featureRoot = new GameObject("RoadsAndWater").transform;
                return featureRoot;
            }
        }

        // ---------------- update ----------------
        private void Update()
        {
            if (points.Count > 0) UpdateMarkers();

            bool overUI = IsPointerOverUI();
            bool preview = PlayerPreviewTool.Active;

            SetStamper(mode == Mode.Stamp && !overUI && !preview);

            bool brush = IsBrush(mode);
            if (!brush || preview) { HideRing(); EndStroke(); }

            if (mode == Mode.None || mode == Mode.Stamp || preview) return;
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world)) return;
            if (!ServiceRegistry.TryResolve<TerrainSettings>(out var settings)) return;

            if (brush) { UpdateBrush(world, settings, overUI); return; }

            if (!Typing())
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Finish(world, settings);
                if (Input.GetKeyDown(KeyCode.Backspace)) RemoveLastPoint(world, settings);
            }

            if (overUI || !Input.GetMouseButtonDown(0) || Camera.main == null) return;
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hit, 20000f)) return;

            HandleTerrainClick(world, settings, new Vector2(hit.point.x, hit.point.z));
        }

        private void HandleTerrainClick(TerrainWorld world, TerrainSettings s, Vector2 p)
        {
            switch (mode)
            {
                case Mode.Road:
                case Mode.River:
                    points.Add(p);
                    RefreshLine(world, s);
                    Status($"{mode}: {points.Count} points. Enter / Build to finish.");
                    break;
                case Mode.Lake:
                    Status(WaterBuilder.CreateLake(world, s, p, lakeRadius, lakeDepth, materials, Root) != null ? "Lake created." : "Lake failed. Check Console.");
                    break;
                case Mode.Pond:
                    Status(WaterBuilder.CreateLake(world, s, p, pondRadius, pondDepth, materials, Root) != null ? "Pond created." : "Pond failed. Check Console.");
                    break;
            }
        }

        // ---------------- sculpt brush ----------------
        const int RingPoints = 64;

        private void UpdateBrush(TerrainWorld world, TerrainSettings s, bool overUI)
        {
            if (!Typing())
            {
                // size: + - keys and [ ]
                float f = SizeKeys.Factor(Time.deltaTime);
                if (Input.GetKey(KeyCode.RightBracket)) f *= Mathf.Pow(2f, Time.deltaTime);
                if (Input.GetKey(KeyCode.LeftBracket)) f *= Mathf.Pow(2f, -Time.deltaTime);
                if (f != 1f)
                {
                    shapeRadius = Mathf.Clamp(shapeRadius * f, 5f, 200f);
                    uiRoot?.Q<Slider>("sld-brush-radius")?.SetValueWithoutNotify(shapeRadius * 0.5f);
                }
            }

            RaycastHit hit = default;
            bool hasHit = !overUI && Camera.main != null &&
                          Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 20000f);

            if (showOutline && hasHit) DrawRing(world, s, hit.point); else HideRing();

            if (!strokeActive && hasHit && Input.GetMouseButtonDown(0))
                BeginStroke(world, s, new Vector2(hit.point.x, hit.point.z));

            if (!strokeActive) return;
            if (!Input.GetMouseButton(0)) { EndStroke(); return; }
            if (hasHit) ApplyBrush(world, s, new Vector2(hit.point.x, hit.point.z));
        }

        private void BeginStroke(TerrainWorld world, TerrainSettings s, Vector2 c)
        {
            strokeActive = true;
            strokeWorld = world;
            world.BeginEdit(mode switch
            {
                Mode.Hill => "Raise",
                Mode.Valley => "Lower",
                Mode.Flatten => "Flatten",
                Mode.Smooth => "Smooth",
                _ => "Erosion"
            });

            if (mode == Mode.Flatten)
            {
                if (lockTarget) flattenTarget01 = Mathf.Clamp01(flattenHeightM / s.maxHeight);
                else if (!world.TrySampleHeight01(c.x, c.y, out flattenTarget01)) flattenTarget01 = 0f;
            }
        }

        private void EndStroke()
        {
            if (!strokeActive) return;
            strokeActive = false;
            strokeWorld?.EndEdit();     // one undo step for the whole drag
            strokeWorld = null;
        }

        /// 0..1 brush weight at an offset from the brush centre.
        private float BrushWeight(float dx, float dz)
        {
            float r = shapeRadius;
            float d = brushShape == 2 ? Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) : Mathf.Sqrt(dx * dx + dz * dz);
            float t = d / r;
            if (t >= 1f) return 0f;
            float fall = brushShape == 1 ? 0.05f : Mathf.Max(0.001f, brushFalloff);
            float inner = 1f - fall;
            if (t <= inner) return 1f;
            float u = (t - inner) / fall;
            return 1f - u * u * (3f - 2f * u);
        }

        private void ApplyBrush(TerrainWorld world, TerrainSettings s, Vector2 c)
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float r = shapeRadius, maxH = s.maxHeight, sp = s.CellSpacing;
            Vector2 min = c - Vector2.one * r, max = c + Vector2.one * r;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float rate = Mathf.Clamp01(brushStrength * dt * 10f);

            switch (mode)
            {
                case Mode.Hill:
                case Mode.Valley:
                    {
                        bool up = (mode == Mode.Hill) != shift;
                        float amt = (up ? 1f : -1f) * raiseMetersPerSecond * brushStrength * dt / maxH;
                        world.ModifyHeights(up ? "Raise" : "Lower", min, max, (wx, wz, cur) =>
                        {
                            float w = BrushWeight(wx - c.x, wz - c.y);
                            return w <= 0f ? cur : cur + amt * w;
                        });
                        break;
                    }
                case Mode.Flatten:
                    {
                        float target = flattenTarget01;
                        world.ModifyHeights("Flatten", min, max, (wx, wz, cur) =>
                        {
                            float w = BrushWeight(wx - c.x, wz - c.y);
                            return w <= 0f ? cur : Mathf.Lerp(cur, target, w * rate);
                        });
                        break;
                    }
                case Mode.Smooth:
                case Mode.Erosion:
                    {
                        // neighbours need an untouched copy of the heights, so snapshot the brush area first
                        int gx0 = Mathf.FloorToInt(min.x / sp) - 3, gz0 = Mathf.FloorToInt(min.y / sp) - 3;
                        int gw = Mathf.CeilToInt(max.x / sp) + 3 - gx0 + 1, gh = Mathf.CeilToInt(max.y / sp) + 3 - gz0 + 1;
                        var grid = new float[gw * gh];
                        for (int j = 0; j < gh; j++)
                            for (int i = 0; i < gw; i++)
                                grid[j * gw + i] = world.TrySampleHeight01((gx0 + i) * sp, (gz0 + j) * sp, out float h) ? h : float.NaN;

                        float Get(int i, int j, float fallback)
                        {
                            if (i < 0 || j < 0 || i >= gw || j >= gh) return fallback;
                            float v = grid[j * gw + i];
                            return float.IsNaN(v) ? fallback : v;
                        }

                        if (mode == Mode.Smooth)
                        {
                            world.ModifyHeights("Smooth", min, max, (wx, wz, cur) =>
                            {
                                float w = BrushWeight(wx - c.x, wz - c.y);
                                if (w <= 0f) return cur;
                                int ci = Mathf.RoundToInt(wx / sp) - gx0, cj = Mathf.RoundToInt(wz / sp) - gz0;
                                float sum = 0f; int n = 0;
                                for (int dj = -2; dj <= 2; dj++)
                                    for (int di = -2; di <= 2; di++) { sum += Get(ci + di, cj + dj, cur); n++; }
                                return Mathf.Lerp(cur, sum / n, w * rate);
                            });
                        }
                        else
                        {
                            float talus = Mathf.Tan(erosionTalusDegrees * Mathf.Deg2Rad) * sp / maxH;
                            float k = rate * 0.4f;      // <= 0.4 * 0.5 = stable for 4 neighbours
                            world.ModifyHeights("Erosion", min, max, (wx, wz, cur) =>
                            {
                                float w = BrushWeight(wx - c.x, wz - c.y);
                                if (w <= 0f) return cur;
                                int ci = Mathf.RoundToInt(wx / sp) - gx0, cj = Mathf.RoundToInt(wz / sp) - gz0;
                                float d = 0f;
                                for (int n = 0; n < 4; n++)
                                {
                                    int ni = ci + (n == 0 ? 1 : n == 1 ? -1 : 0), nj = cj + (n == 2 ? 1 : n == 3 ? -1 : 0);
                                    float diff = cur - Get(ni, nj, cur);
                                    if (diff > talus) d -= diff - talus;            // too steep: shed material downhill
                                    else if (diff < -talus) d += -diff - talus;     // pit next to a cliff: receive it
                                }
                                return cur + d * k * w;
                            });
                        }
                        break;
                    }
            }
        }

        private void DrawRing(TerrainWorld world, TerrainSettings s, Vector3 center)
        {
            if (brushRing == null) return;
            brushRing.enabled = true;

            Color col = mode switch
            {
                Mode.Hill => new Color(0.3f, 1f, 0.45f),
                Mode.Valley => new Color(1f, 0.4f, 0.35f),
                Mode.Flatten => new Color(0.4f, 0.7f, 1f),
                Mode.Smooth => new Color(0.8f, 0.6f, 1f),
                _ => new Color(1f, 0.8f, 0.3f)
            };
            brushRing.startColor = brushRing.endColor = col;
            brushRing.widthMultiplier = Mathf.Max(0.3f, shapeRadius * 0.02f);

            for (int i = 0; i <= RingPoints; i++)
            {
                float a = i / (float)RingPoints * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                if (brushShape == 2) { float m = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(sz)); cx /= m; sz /= m; }
                float x = center.x + cx * shapeRadius, z = center.z + sz * shapeRadius;
                float y = world.TrySampleHeight01(x, z, out float h01) ? h01 * s.maxHeight : center.y;
                brushRing.SetPosition(i, new Vector3(x, y + 1.5f, z));
            }
        }

        private void HideRing()
        {
            if (brushRing != null && brushRing.enabled) brushRing.enabled = false;
        }

        // ---------------- finish / cancel ----------------
        private void FinishCurrentFeature()
        {
            if (ServiceRegistry.TryResolve<TerrainWorld>(out var w) && ServiceRegistry.TryResolve<TerrainSettings>(out var s))
                Finish(w, s);
        }

        private void Finish(TerrainWorld world, TerrainSettings s)
        {
            if (mode != Mode.Road && mode != Mode.River) return;
            if (points.Count < 2) { Status("You need at least 2 points."); return; }

            if (mode == Mode.Road)
                Status(RoadBuilder.Build(world, s, points, road, materials, Root) ? "Road built." : "Road failed.");
            else
                Status(WaterBuilder.CreateRiver(world, s, points, riverWidth, riverDepth, materials, Root) != null ? "River built." : "River failed.");

            points.Clear();
            ClearPreviewLine();
        }

        private void RemoveLastPoint()
        {
            if (ServiceRegistry.TryResolve<TerrainWorld>(out var w) && ServiceRegistry.TryResolve<TerrainSettings>(out var s))
                RemoveLastPoint(w, s);
        }

        private void RemoveLastPoint(TerrainWorld w, TerrainSettings s)
        {
            if (points.Count == 0) return;
            points.RemoveAt(points.Count - 1);
            RefreshLine(w, s);
            Status($"{points.Count} points remaining.");
        }

        private void Cancel()
        {
            points.Clear();
            ClearPreviewLine();
        }

        // ---------------- preview ----------------
        private void RefreshLine(TerrainWorld world, TerrainSettings s)
        {
            if (lineRenderer == null) return;
            List<Vector2> sampled = points.Count >= 2 ? PathSpline.Sample(points, 4f) : points;
            lineRenderer.positionCount = sampled.Count;
            for (int i = 0; i < sampled.Count; i++)
            {
                float y = world.TrySampleHeight01(sampled[i].x, sampled[i].y, out float h01) ? h01 * s.maxHeight : 0f;
                lineRenderer.SetPosition(i, new Vector3(sampled[i].x, y + 1.5f, sampled[i].y));
            }
            UpdateMarkers();
        }

        private void ClearPreviewLine()
        {
            if (lineRenderer != null) lineRenderer.positionCount = 0;
            UpdateMarkers();
        }

        public void ClearFeatures()
        {
            if (featureRoot != null) { Destroy(featureRoot.gameObject); featureRoot = null; }
            WaterRegistry.Clear();
            points.Clear();
            ClearPreviewLine();
            Status("Road and water objects cleared. Terrain carving remains.");
        }

        private void Status(string msg)
        {
            Debug.Log("[WorldEditorTool] " + msg);
            builderUI?.SetStatusMessage(msg);
        }

        private void UpdateMarkers()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var w) || !ServiceRegistry.TryResolve<TerrainSettings>(out var s)) return;
            if (markerMat == null) { markerMat = new Material(Shader.Find("Sprites/Default")); markerMat.color = new Color(1f, 0.3f, 0.1f); }
            while (markers.Count < points.Count)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(m.GetComponent<Collider>());
                m.layer = 2;
                m.GetComponent<MeshRenderer>().sharedMaterial = markerMat;
                m.transform.SetParent(transform, false);
                markers.Add(m);
            }
            for (int i = 0; i < markers.Count; i++)
            {
                bool on = i < points.Count;
                markers[i].SetActive(on);
                if (!on) continue;
                w.TrySampleHeight01(points[i].x, points[i].y, out float h01);
                var pos = new Vector3(points[i].x, h01 * s.maxHeight + 2f, points[i].y);
                float d = Camera.main != null ? Vector3.Distance(Camera.main.transform.position, pos) : 100f;
                markers[i].transform.position = pos;
                markers[i].transform.localScale = Vector3.one * Mathf.Max(1.5f, d * 0.012f);
            }
        }
    }
}