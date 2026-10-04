using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class HeightmapPlacer : MonoBehaviour
    {
        [SerializeField] Texture2D heightmap;
        [SerializeField] Vector2 size = new(200, 200);
        [SerializeField] float heightMeters = 50f;
        [SerializeField] float baseY = 0f;
        [SerializeField] StampBlend blend = StampBlend.Add;
        [Range(0f, 0.5f)][SerializeField] float edgeFalloff = 0.15f;
        [SerializeField] bool invert;

        [Header("Preview")]
        [Tooltip("ON: ghost shows the terrain height AFTER stamping. OFF: ghost drapes on the current surface.")]
        [SerializeField] bool showPredictedHeight = true;
        [SerializeField] float lift = 0.3f;
        const int N = 48;

        float yaw;
        bool dirty = true;
        Vector3 lastCenter; float lastYaw, lastH; Vector2 lastSize;

        HeightmapStamp previewStamp = new();
        LineRenderer outline;
        Mesh mesh;
        MeshRenderer ghost;
        Vector3[] verts; Color[] cols;

        void Awake()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));   // unlit, vertex-colored, works in Built-in & URP

            outline = gameObject.AddComponent<LineRenderer>();
            outline.positionCount = 5;
            outline.useWorldSpace = true;
            outline.material = mat;
            outline.startColor = outline.endColor = Color.yellow;

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

        void Update()
        {
            if (!ServiceRegistry.TryResolve<TerrainWorld>(out var world)) return;

            if (Input.GetKey(KeyCode.Q)) yaw -= 60f * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) yaw += 60f * Time.deltaTime;
            size *= 1f + Input.mouseScrollDelta.y * 0.05f;
            if (Input.GetKey(KeyCode.R)) heightMeters += 30f * Time.deltaTime;
            if (Input.GetKey(KeyCode.F)) heightMeters -= 30f * Time.deltaTime;

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 20000f))
            {
                outline.enabled = false; ghost.enabled = false; return;
            }
            outline.enabled = true; ghost.enabled = heightmap != null;

            DrawOutline(hit.point);

            if (hit.point != lastCenter || yaw != lastYaw || size != lastSize || heightMeters != lastH)
                dirty = true;
            if (dirty && heightmap != null)
            {
                UpdateGhost(world, hit.point);
                lastCenter = hit.point; lastYaw = yaw; lastSize = size; lastH = heightMeters;
                dirty = false;
            }

            if (Input.GetMouseButtonDown(0) && heightmap != null)
            {
                world.PlaceStamp(MakeStamp(hit.point));
                dirty = true;                       // terrain changed, rebuild the ghost
            }
        }

        HeightmapStamp MakeStamp(Vector3 center) => new HeightmapStamp
        {
            heightmap = heightmap,
            position = new Vector2(center.x, center.z),
            yawDegrees = yaw,
            size = size,
            heightMeters = heightMeters,
            baseY = baseY,
            blend = blend,
            edgeFalloff = edgeFalloff,
            invert = invert,
        };

        void UpdateGhost(TerrainWorld world, Vector3 center)
        {
            // reuse one stamp object so the pixel cache is built only once
            previewStamp.heightmap = heightmap;
            previewStamp.position = new Vector2(center.x, center.z);
            previewStamp.yawDegrees = yaw;
            previewStamp.size = size;
            previewStamp.heightMeters = heightMeters;
            previewStamp.baseY = baseY;
            previewStamp.blend = blend;
            previewStamp.edgeFalloff = edgeFalloff;
            previewStamp.invert = invert;
            previewStamp.Prepare();

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
                    if (onTerrain) previewStamp.TryEvaluate(wp.x, wp.z, cur, maxH, out result, out w);

                    float y = (showPredictedHeight ? result : cur) * maxH + lift;
                    verts[idx] = new Vector3(wp.x, y, wp.z);

                    float delta = Mathf.Clamp01(Mathf.Abs(result - cur) * maxH / Mathf.Max(1f, Mathf.Abs(heightMeters)));
                    var col = Color.Lerp(new Color(0.2f, 0.6f, 1f), new Color(1f, 0.85f, 0.2f), delta);
                    col.a = onTerrain ? Mathf.Lerp(0.15f, 0.55f, w) : 0f;   // hidden outside the extent
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
            Vector3[] c = { new(-hx, 0, -hz), new(hx, 0, -hz), new(hx, 0, hz), new(-hx, 0, hz), new(-hx, 0, -hz) };
            for (int i = 0; i < 5; i++) outline.SetPosition(i, center + rot * c[i] + Vector3.up * 2f);
            outline.widthMultiplier = Mathf.Max(0.5f, size.magnitude * 0.005f);
        }
    }
}