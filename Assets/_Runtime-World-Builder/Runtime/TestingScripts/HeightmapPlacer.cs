using System.Collections.Generic;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;
using UnityEngine.UIElements;

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

        [Header("UI Toolkit")]
        [SerializeField] private UIDocument uiDocument;

        private VisualElement uiRoot;
        private bool isPointerOverUI;

        public Vector2 StampSize => size;
        public float StampHeight => heightMeters;
        public float StampRotation => yaw;
        public float StampStrength => strength;
        public float StampFalloff => edgeFalloff;
        public StampBlend CurrentBlend => blend;
        public bool IsAspectLocked => lockAspect;

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

            InitializeUIToolkit();
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
            if (uiDocument == null || uiDocument.rootVisualElement == null)
                return false;

            var panel = uiDocument.rootVisualElement.panel;

            if (panel == null)
                return false;

            Vector2 panelPosition =
                RuntimePanelUtils.ScreenToPanel(panel, Input.mousePosition);

            VisualElement pickedElement = panel.Pick(panelPosition);

            return pickedElement != null &&
                   pickedElement.pickingMode != PickingMode.Ignore;
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
        void InitializeUIToolkit()
        {
            if (uiDocument == null)
                uiDocument = FindFirstObjectByType<UIDocument>();

            if (uiDocument == null)
            {
                Debug.LogError("HeightmapPlacer: UIDocument not found.");
                return;
            }

            uiRoot = uiDocument.rootVisualElement;

            BindStampUI();
        }

        void BindStampUI()
        {
            var scaleSlider = uiRoot.Q<Slider>("sld-stamp-scale");
            var rotationSlider = uiRoot.Q<Slider>("sld-stamp-rotation");
            var previewToggle = uiRoot.Q<Toggle>("tgl-stamp-preview");
            var blendDropdown = uiRoot.Q<DropdownField>("dd-stamp-blend");

            if (scaleSlider != null)
            {
                scaleSlider.RegisterValueChangedCallback(evt =>
                {
                    size = new Vector2(evt.newValue, evt.newValue);
                    dirty = true;
                });
            }

            if (rotationSlider != null)
            {
                rotationSlider.RegisterValueChangedCallback(evt =>
                {
                    yaw = evt.newValue;
                    dirty = true;
                });
            }

            if (previewToggle != null)
            {
                previewToggle.RegisterValueChangedCallback(evt =>
                {
                    showPredictedHeight = evt.newValue;
                    dirty = true;
                });
            }

            if (blendDropdown != null)
            {
                blendDropdown.RegisterValueChangedCallback(evt =>
                {
                    if (System.Enum.TryParse(evt.newValue, out StampBlend selected))
                    {
                        blend = selected;
                        dirty = true;
                    }
                });
            }

            var applyButton = uiRoot.Q<Button>("btn-apply-stamp");

            if (applyButton != null)
            {
                applyButton.clicked += ApplyStampAtCurrentPosition;
            }
        }

        public void SetStampSize(float value)
        {
            size = ClampSize(new Vector2(value, value));
            dirty = true;
        }

        public void SetStampHeight(float value)
        {
            heightMeters = Mathf.Clamp(value, 0f, maxHeightMeters);
            dirty = true;
        }

        public void SetStampRotation(float value)
        {
            yaw = Mathf.Repeat(value, 360f);
            dirty = true;
        }

        public void SetStampStrength(float value)
        {
            strength = Mathf.Clamp01(value);
            dirty = true;
        }

        public void SetStampFalloff(float value)
        {
            edgeFalloff = Mathf.Clamp(value, 0f, 0.5f);
            dirty = true;
        }

        public void SetStampBlend(StampBlend value)
        {
            blend = value;
            dirty = true;
        }

        public void SetStampInvert(bool value)
        {
            invert = value;
            dirty = true;
        }

        void ApplyStampAtCurrentPosition()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world))
                return;

            Texture2D tex = Current;

            if (tex == null)
                return;

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, 20000f))
                return;

            world.PlaceStamp(MakeStamp(hit.point, tex));

            dirty = true;
        }
    }
}