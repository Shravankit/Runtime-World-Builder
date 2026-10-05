using System.Collections.Generic;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class HeightmapPlacer : MonoBehaviour
    {
        [Header("Heightmaps")]
        [SerializeField] HeightmapLibrary library;
        [SerializeField] Texture2D heightmap;          // fallback if no library is assigned

        [Header("Stamp")]
        [SerializeField] Vector2 size = new(200, 200);
        [SerializeField] float heightMeters = 50f;
        [SerializeField] float baseY = 0f;
        [SerializeField] StampBlend blend = StampBlend.Add;
        [Range(0f, 0.5f)][SerializeField] float edgeFalloff = 0.15f;
        [Range(0f, 1f)][SerializeField] float strength = 1f;
        [SerializeField] bool invert;

        [Header("Limits")]
        [SerializeField] float minSize = 20f;
        [SerializeField] float maxSize = 2000f;
        [SerializeField] float maxHeightMeters = 300f;

        [Header("Preview")]
        [SerializeField] bool showPredictedHeight = true;
        [SerializeField] float lift = 0.3f;

        [Header("UI")]
        [Tooltip("Screen rect (top-left origin) of the tester's button panel, so clicks on it don't place stamps")]
        [SerializeField] Rect testerPanelRect = new(10, 10, 320, 400);

        const int N = 48;
        static readonly string[] blendNames = System.Enum.GetNames(typeof(StampBlend));

        int index;
        float yaw;
        bool lockAspect = true;
        bool dirty = true;
        Vector3 lastCenter; float lastYaw, lastH; Vector2 lastSize;
        Texture2D lastTex;

        readonly Dictionary<Texture2D, HeightmapStamp> previewStamps = new();

        LineRenderer outline, arrow;
        Mesh mesh;
        MeshRenderer ghost;
        Vector3[] verts; Color[] cols;

        Vector2 scroll;
        Rect paletteRect, panelRect;

        int lastVersion = -1;

        Texture2D Current
        {
            get
            {
                if (library != null && library.textures.Count > 0)
                {
                    index = Mathf.Clamp(index, 0, library.textures.Count - 1);
                    return library.textures[index];
                }
                return heightmap;
            }
        }

        void Awake()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));

            outline = gameObject.AddComponent<LineRenderer>();
            outline.positionCount = 5;
            outline.useWorldSpace = true;
            outline.material = mat;
            outline.startColor = outline.endColor = Color.yellow;

            // arrow = "top of the image" direction
            var ag = new GameObject("StampArrow");
            ag.transform.SetParent(transform, false);
            arrow = ag.AddComponent<LineRenderer>();
            arrow.positionCount = 5;
            arrow.useWorldSpace = true;
            arrow.material = mat;
            arrow.startColor = arrow.endColor = new Color(1f, 0.5f, 0f);

            var go = new GameObject("StampGhost");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh = BuildMesh();
            ghost = go.AddComponent<MeshRenderer>();
            ghost.sharedMaterial = mat;
            ghost.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.receiveShadows = false;
        }

        Mesh BuildMesh()
        {
            var m = new Mesh { name = "StampGhost" };
            verts = new Vector3[(N + 1) * (N + 1)];
            cols = new Color[verts.Length];
            var tris = new int[N * N * 6];
            int t = 0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            m.vertices = verts; m.colors = cols; m.triangles = tris;
            m.MarkDynamic();
            return m;
        }

        void Start()
        {
            if (library != null) foreach (var t in library.textures) HeightmapStamp.Warm(t);
            if (heightmap != null) HeightmapStamp.Warm(heightmap);
        }

        void Select(int i)
        {
            int n = library.textures.Count;
            index = ((i % n) + n) % n;
            dirty = true;
        }

        Vector2 ClampSize(Vector2 v) =>
            new(Mathf.Clamp(v.x, minSize, maxSize), Mathf.Clamp(v.y, minSize, maxSize));

        bool OverUI()
        {
            var p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return paletteRect.Contains(p) || panelRect.Contains(p) || testerPanelRect.Contains(p);
        }

        void Update()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world)) return;

            if (world.Version != lastVersion) { dirty = true; lastVersion = world.Version; }

            if (library != null && library.textures.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.RightBracket)) Select(index + 1);
                if (Input.GetKeyDown(KeyCode.LeftBracket)) Select(index - 1);
                for (int k = 0; k < Mathf.Min(9, library.textures.Count); k++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + k)) Select(k);
            }

            var tex = Current;
            if (tex == null || OverUI())
            {
                outline.enabled = false; arrow.enabled = false; ghost.enabled = false; return;
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // ---- rotate ----
            float rotSpeed = shift ? 15f : 60f;
            if (Input.GetKey(KeyCode.Q)) yaw -= rotSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) yaw += rotSpeed * Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.T)) yaw += 90f;
            if (Input.GetMouseButton(1)) yaw += Input.GetAxis("Mouse X") * 3f;
            yaw = Mathf.Repeat(yaw, 360f);

            // ---- scale ----
            float sc = Input.mouseScrollDelta.y;
            if (sc != 0f)
            {
                float f = 1f + sc * 0.05f;
                if (shift && !ctrl) size.x *= f;          // width only
                else if (ctrl && !shift) size.y *= f;     // length only
                else size *= f;                           // both
                size = ClampSize(size);
            }

            // ---- height ----
            if (Input.GetKey(KeyCode.R)) heightMeters += 30f * Time.deltaTime;
            if (Input.GetKey(KeyCode.F)) heightMeters -= 30f * Time.deltaTime;
            heightMeters = Mathf.Clamp(heightMeters, 0f, maxHeightMeters);

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 20000f))
            {
                outline.enabled = false; arrow.enabled = false; ghost.enabled = false; return;
            }
            outline.enabled = true; arrow.enabled = true; ghost.enabled = true;

            DrawOutline(hit.point);

            if (hit.point != lastCenter || yaw != lastYaw || size != lastSize ||
                heightMeters != lastH || tex != lastTex)
                dirty = true;

            if (dirty)
            {
                UpdateGhost(world, hit.point, tex);
                lastCenter = hit.point; lastYaw = yaw; lastSize = size; lastH = heightMeters; lastTex = tex;
                dirty = false;
            }

            // don't place while right-dragging to rotate
            if (Input.GetMouseButtonDown(0))
            {
                world.PlaceStamp(MakeStamp(hit.point, tex));
                dirty = true;
            }
        }

        HeightmapStamp MakeStamp(Vector3 center, Texture2D tex) => new HeightmapStamp
        {
            heightmap = tex,
            position = new Vector2(center.x, center.z),
            yawDegrees = yaw,
            size = size,
            heightMeters = heightMeters,
            baseY = baseY,
            blend = blend,
            edgeFalloff = edgeFalloff,
            strength = strength,
            invert = invert,
        };

        HeightmapStamp GetPreviewStamp(Texture2D tex)
        {
            if (!previewStamps.TryGetValue(tex, out var st))
                previewStamps[tex] = st = new HeightmapStamp();
            return st;
        }

        void UpdateGhost(TerrainWorld world, Vector3 center, Texture2D tex)
        {
            var ps = GetPreviewStamp(tex);
            ps.heightmap = tex;
            ps.position = new Vector2(center.x, center.z);
            ps.yawDegrees = yaw;
            ps.size = size;
            ps.heightMeters = heightMeters;
            ps.baseY = baseY;
            ps.blend = blend;
            ps.edgeFalloff = edgeFalloff;
            ps.strength = strength;
            ps.invert = invert;
            ps.Prepare();

            float maxH = ServiceRegistry.TryResolve<TerrainSettings>(out var st) ? st.maxHeight : 200f;
            var rot = Quaternion.Euler(0, yaw, 0);

            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    float u = i / (float)N, v = j / (float)N;
                    var local = new Vector3((u - 0.5f) * size.x, 0, (v - 0.5f) * size.y);
                    var wp = center + rot * local;

                    int idx = j * (N + 1) + i;
                    bool onTerrain = world.TrySampleHeight01(wp.x, wp.z, out float cur);
                    float result = cur, w = 0f;
                    if (onTerrain) ps.TryEvaluate(wp.x, wp.z, cur, maxH, out result, out w);

                    float y = (showPredictedHeight ? result : cur) * maxH + lift;
                    verts[idx] = new Vector3(wp.x, y, wp.z);

                    float delta = Mathf.Clamp01(Mathf.Abs(result - cur) * maxH / Mathf.Max(1f, Mathf.Abs(heightMeters)));
                    var col = Color.Lerp(new Color(0.2f, 0.6f, 1f), new Color(1f, 0.85f, 0.2f), delta);
                    col.a = onTerrain ? Mathf.Lerp(0.15f, 0.55f, w) : 0f;
                    cols[idx] = col;
                }

            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.RecalculateBounds();
        }

        void DrawOutline(Vector3 center)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            float lw = Mathf.Max(0.5f, size.magnitude * 0.005f);
            Vector3 up = Vector3.up * 2f;

            Vector3[] c = { new(-hx, 0, -hz), new(hx, 0, -hz), new(hx, 0, hz), new(-hx, 0, hz), new(-hx, 0, -hz) };
            for (int i = 0; i < 5; i++) outline.SetPosition(i, center + rot * c[i] + up);
            outline.widthMultiplier = lw;

            // arrow from center to the top edge of the image, with a head
            Vector3 tip = new(0, 0, hz);
            float hw = Mathf.Min(hx, hz) * 0.15f;
            arrow.SetPosition(0, center + up * 1.5f);
            arrow.SetPosition(1, center + rot * tip + up * 1.5f);
            arrow.SetPosition(2, center + rot * (tip + new Vector3(-hw, 0, -hw)) + up * 1.5f);
            arrow.SetPosition(3, center + rot * tip + up * 1.5f);
            arrow.SetPosition(4, center + rot * (tip + new Vector3(hw, 0, -hw)) + up * 1.5f);
            arrow.widthMultiplier = lw * 1.4f;
        }

        // ---------- UI ----------
        float Slider(string label, float v, float min, float max, string fmt = "F0")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(62));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.Label(v.ToString(fmt), GUILayout.Width(44));
            GUILayout.EndHorizontal();
            return v;
        }

        void OnGUI()
        {
            DrawPanel();
            DrawPalette();
        }

        void DrawPanel()
        {
            panelRect = new Rect(Screen.width - 310, 10, 300, 372);
            GUILayout.BeginArea(panelRect, GUI.skin.box);
            GUILayout.Label("Stamp");
            GUI.changed = false;

            yaw = Slider("Rotation", Mathf.Repeat(yaw, 360f), 0f, 360f);

            float nx = Slider("Width", size.x, minSize, maxSize);
            float nz = Slider("Length", size.y, minSize, maxSize);
            if (nx != size.x)
                size = lockAspect ? ClampSize(new Vector2(nx, size.y * (nx / size.x))) : new Vector2(nx, size.y);
            else if (nz != size.y)
                size = lockAspect ? ClampSize(new Vector2(size.x * (nz / size.y), nz)) : new Vector2(size.x, nz);
            lockAspect = GUILayout.Toggle(lockAspect, "Lock aspect (width + length together)");

            heightMeters = Slider("Height", heightMeters, 0f, maxHeightMeters);
            edgeFalloff = Slider("Falloff", edgeFalloff, 0f, 0.5f, "F2");
            strength = Slider("Strength", strength, 0f, 1f, "F2");

            blend = (StampBlend)GUILayout.Toolbar((int)blend, blendNames);
            invert = GUILayout.Toggle(invert, "Invert heightmap");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-90°")) yaw = Mathf.Repeat(yaw - 90f, 360f);
            if (GUILayout.Button("+90°")) yaw = Mathf.Repeat(yaw + 90f, 360f);
            if (GUILayout.Button("Swap W/L")) size = new Vector2(size.y, size.x);
            if (GUILayout.Button("Reset"))
            {
                yaw = 0f; size = new Vector2(200, 200); heightMeters = 50f;
                edgeFalloff = 0.15f; strength = 1f; invert = false;
            }
            GUILayout.EndHorizontal();

            if (GUI.changed) dirty = true;

            GUILayout.Label("Right-drag: rotate   Wheel: scale\nShift+wheel: width   Ctrl+wheel: length\nQ/E/T: rotate   R/F: height", GUI.skin.label);
            GUILayout.EndArea();
        }

        void DrawPalette()
        {
            if (library == null || library.textures.Count == 0) return;

            const float cell = 72f;
            paletteRect = new Rect(340, Screen.height - cell - 70, Screen.width - 670, cell + 60);

            GUILayout.BeginArea(paletteRect, GUI.skin.box);
            GUILayout.Label($"Heightmap: {Current.name}   ([ ] or 1-9 to switch)");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(cell + 20));
            GUILayout.BeginHorizontal();

            for (int i = 0; i < library.textures.Count; i++)
            {
                var tex = library.textures[i];
                if (tex == null) continue;

                var prev = GUI.backgroundColor;
                GUI.backgroundColor = i == index ? Color.yellow : Color.white;
                bool clicked = GUILayout.Button(GUIContent.none, GUILayout.Width(cell), GUILayout.Height(cell));
                GUI.backgroundColor = prev;

                var r = GUILayoutUtility.GetLastRect();
                GUI.DrawTexture(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), tex, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x + 6, r.y + 2, 30, 18), (i + 1).ToString());

                if (clicked) Select(i);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}