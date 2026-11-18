using System.Collections.Generic;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Features
{
    /// Click-to-place roads, rivers, lakes and ponds on the runtime terrain.
    /// Road/River: click points, Enter (or Finish) to build, Backspace removes last point, Esc cancels.
    /// Lake/Pond: one click.
    public class FeaturePlacer : MonoBehaviour
    {
        /// True while this tool wants the mouse. HeightmapPlacer.OverUI() should return true when this is set.
        public static bool Active { get; private set; }

        enum Mode { None, Road, River, Lake, Pond }
        static readonly string[] modeNames = { "Off", "Road", "River", "Lake", "Pond" };

        [SerializeField] FeatureMaterials materials = new();
        [SerializeField] RoadStyle road = new();
        [SerializeField] float riverWidth = 12f, riverDepth = 3f;
        [SerializeField] float lakeRadius = 45f, lakeDepth = 8f;
        [SerializeField] float pondRadius = 12f, pondDepth = 2f;
        [Tooltip("Screen rect (top-left origin). Keep clear of the tester panel at (10,10,320,400).")]
        [SerializeField] Rect panelRect = new(10, 420, 330, 340);

        Mode mode;
        bool open = true;                       // F3 toggles the panel; IMGUI costs nothing while it is hidden
        readonly List<Vector2> pts = new();
        LineRenderer line;
        Transform root;
        string status = "Pick a mode, then click the terrain.";

        void Awake()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(1f, 0.9f, 0.1f);
            line.widthMultiplier = 1.5f;
            line.useWorldSpace = true;
            line.positionCount = 0;
        }

        Transform Root
        {
            get
            {
                if (root == null) root = new GameObject("RoadsAndWater").transform;
                return root;
            }
        }

        void Update()
        {
            var mp = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetKeyDown(KeyCode.F3)) open = !open;
            bool overPanel = (open ? panelRect : new Rect(panelRect.x, panelRect.y, 120, 24)).Contains(mp);
            Active = mode != Mode.None || overPanel;

            if (mode == Mode.None) return;
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world)) return;
            if (!ServiceRegistry.TryResolve<TerrainSettings>(out var s)) return;

            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Finish(world, s);
            if (Input.GetKeyDown(KeyCode.Backspace) && pts.Count > 0) { pts.RemoveAt(pts.Count - 1); RefreshLine(world, s); }

            if (overPanel || !Input.GetMouseButtonDown(0)) return;
            if (Camera.main == null) return;
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hit, 20000f)) return;

            var p = new Vector2(hit.point.x, hit.point.z);
            switch (mode)
            {
                case Mode.Road:
                case Mode.River:
                    pts.Add(p);
                    RefreshLine(world, s);
                    status = $"{mode}: {pts.Count} points. Enter to build.";
                    break;
                case Mode.Lake:
                    status = WaterBuilder.CreateLake(world, s, p, lakeRadius, lakeDepth, materials, Root) != null
                        ? "Lake created." : "Lake failed (see Console).";
                    break;
                case Mode.Pond:
                    status = WaterBuilder.CreateLake(world, s, p, pondRadius, pondDepth, materials, Root) != null
                        ? "Pond created." : "Pond failed (see Console).";
                    break;
            }
        }

        void Finish(TerrainWorld world, TerrainSettings s)
        {
            if (pts.Count < 2) { status = "Need at least 2 points."; return; }

            if (mode == Mode.Road)
                status = RoadBuilder.Build(world, s, pts, road, materials, Root) ? "Road built." : "Road failed.";
            else if (mode == Mode.River)
                status = WaterBuilder.CreateRiver(world, s, pts, riverWidth, riverDepth, materials, Root) != null
                    ? "River built." : "River failed.";

            pts.Clear();
            line.positionCount = 0;
        }

        void Cancel()
        {
            pts.Clear();
            line.positionCount = 0;
            status = "Cancelled.";
        }

        void RefreshLine(TerrainWorld world, TerrainSettings s)
        {
            var show = pts.Count >= 2 ? PathSpline.Sample(pts, 4f) : pts;
            line.positionCount = show.Count;
            for (int i = 0; i < show.Count; i++)
            {
                float yy = world.TrySampleHeight01(show[i].x, show[i].y, out float h01) ? h01 * s.maxHeight : 0f;
                line.SetPosition(i, new Vector3(show[i].x, yy + 1.5f, show[i].y));
            }
        }

        // ---------- UI ----------
        float Slider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.Label(v.ToString("F1"), GUILayout.Width(44));
            GUILayout.EndHorizontal();
            return v;
        }

        void OnGUI()
        {
            if (!open)
            {
                if (GUI.Button(new Rect(panelRect.x, panelRect.y, 120, 24), "Roads & Water")) open = true;
                return;
            }
            GUILayout.BeginArea(panelRect, GUI.skin.box);
            GUILayout.Label("Roads & Water");
            if (GUILayout.Button("Hide panel (F3)")) open = false;

            int m = GUILayout.Toolbar((int)mode, modeNames);
            if ((Mode)m != mode) { mode = (Mode)m; Cancel(); Active = true; status = $"Mode: {mode}"; }

            switch (mode)
            {
                case Mode.Road:
                    road.width = Slider("Width", road.width, 3f, 20f);
                    road.shoulder = Slider("Shoulder", road.shoulder, 1f, 20f);
                    road.maxGrade = Slider("Max grade", road.maxGrade, 0.02f, 0.4f);
                    road.bridgeClearance = Slider("Bridge height", road.bridgeClearance, 1f, 10f);
                    break;
                case Mode.River:
                    riverWidth = Slider("Width", riverWidth, 4f, 60f);
                    riverDepth = Slider("Depth", riverDepth, 1f, 10f);
                    GUILayout.Label("Click from upstream (high) to downstream (low).");
                    break;
                case Mode.Lake:
                    lakeRadius = Slider("Radius", lakeRadius, 20f, 150f);
                    lakeDepth = Slider("Depth", lakeDepth, 2f, 20f);
                    break;
                case Mode.Pond:
                    pondRadius = Slider("Radius", pondRadius, 5f, 30f);
                    pondDepth = Slider("Depth", pondDepth, 0.5f, 5f);
                    break;
            }

            if (mode == Mode.Road || mode == Mode.River)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Finish (Enter)") &&
                    ServiceRegistry.TryResolve<TerrainWorld>(out var w) && ServiceRegistry.TryResolve<TerrainSettings>(out var st))
                    Finish(w, st);
                if (GUILayout.Button("Cancel (Esc)")) Cancel();
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Clear roads/water objects"))
            {
                if (root != null) Destroy(root.gameObject);
                root = null;
                WaterRegistry.Clear();
                status = "Objects cleared (terrain carving stays; use Ctrl+Z to undo it).";
            }

            GUILayout.Label(status);
            GUILayout.EndArea();
        }
    }
}