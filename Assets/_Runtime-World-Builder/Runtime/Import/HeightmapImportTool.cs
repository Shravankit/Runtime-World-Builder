
using System.IO;
using System.Reflection;
using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.Runtime.Testing;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Import
{
    public class HeightmapImportTool : MonoBehaviour
    {
        public static bool OverPanel { get; private set; }

        [Header("References")]
        [SerializeField] private HeightmapLibrary library;
        [SerializeField] private TerrainWorldTester tester;

        [Header("Panel")]
        [SerializeField] private Rect panelRect = new Rect(340, 10, 360, 440);

        private const int MaxChunksPerSide = 16;

        [Header("Heightmap")]
        private string path = "";

        private bool bigEndian;
        private bool flipY;
        private bool clearModels = true;

        [Header("Terrain Settings")]
        private float sizeX = 1024f;
        private float sizeZ = 1024f;
        private float heightM = 1000f;

        private int pick;

        private string status =
            "Enter the full PNG file path and press Import.";

        // --------------------------------------------------
        // FIND OBJECT
        // --------------------------------------------------

        private static T FindOne<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }

        // --------------------------------------------------
        // INITIALIZATION
        // --------------------------------------------------

        private void Start()
        {
            if (tester == null)
                tester = FindOne<TerrainWorldTester>();

            int restored = HeightmapImporter.LoadSaved(library);

            if (restored > 0)
            {
                status = $"Restored {restored} imported heightmap(s).";
            }
        }

        private void OnDisable()
        {
            OverPanel = false;
        }

        private void Update()
        {
            Vector2 mousePosition = new Vector2(
                Input.mousePosition.x,
                Screen.height - Input.mousePosition.y
            );

            OverPanel = panelRect.Contains(mousePosition);
        }

        // --------------------------------------------------
        // IMPORT HEIGHTMAP
        // --------------------------------------------------

        private void Import()
        {
            if (library == null)
            {
                status = "Assign a HeightmapLibrary.";
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                status = "Enter a PNG file path.";
                return;
            }

            string filePath = path.Trim().Trim('"');

            if (Path.GetExtension(filePath).ToLowerInvariant() != ".png")
            {
                status = "Please provide a .png file.";
                return;
            }

            Texture2D texture = HeightmapImporter.Load(
                filePath,
                bigEndian,
                flipY,
                true,
                out string error
            );

            if (texture == null)
            {
                status = "Import failed: " + error;
                return;
            }

            // Add imported heightmap to library.
            pick = library.AddRuntime(texture);

            // Prepare heightmap data.
            HeightmapStamp.Warm(texture);

            status = "Heightmap imported. Rebuilding terrain...";

            // Automatically rebuild terrain.
            Build();
        }

        // --------------------------------------------------
        // CLEAR PLACED MODELS
        // --------------------------------------------------

        private void ClearPlacedModels()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsSortMode.None
            );

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;

                if (behaviour.GetType().Name != "ModelPlacerTool")
                    continue;

                MethodInfo clearMethod = behaviour.GetType().GetMethod(
                    "ClearAll",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic
                );

                if (clearMethod != null)
                {
                    clearMethod.Invoke(behaviour, null);
                }
            }
        }

        // --------------------------------------------------
        // BUILD TERRAIN
        // --------------------------------------------------

        private void Build()
        {
            if (library == null || library.Count == 0)
            {
                status = "No heightmap available.";
                return;
            }

            if (tester == null)
            {
                status = "Assign the TerrainWorldTester.";
                return;
            }

            if (!ServiceRegistry.TryResolve<TerrainSettings>(
                out TerrainSettings settings))
            {
                status = "Terrain not initialised yet.";
                return;
            }

            pick = Mathf.Clamp(pick, 0, library.Count - 1);

            Texture2D texture = library.Get(pick);

            if (texture == null)
            {
                status = "Selected heightmap is missing.";
                return;
            }

            float chunkSize = settings.chunkSize;

            int chunksX = Mathf.Clamp(
                Mathf.CeilToInt(sizeX / chunkSize),
                1,
                MaxChunksPerSide
            );

            int chunksZ = Mathf.Clamp(
                Mathf.CeilToInt(sizeZ / chunkSize),
                1,
                MaxChunksPerSide
            );

            float finalHeight = Mathf.Clamp(heightM, 10f, 1000f);

            if (clearModels)
            {
                ClearPlacedModels();
            }

            // Rebuild the terrain world.
            var world = tester.RebuildWorld(
                Vector2Int.zero,
                new Vector2Int(chunksX - 1, chunksZ - 1)
            );

            if (world == null)
            {
                status = "Failed to rebuild terrain world.";
                return;
            }

            // Full replacement heightmap stamp.
            float padding = settings.CellSpacing * 2f;

            HeightmapStamp stamp = new HeightmapStamp
            {
                heightmap = texture,

                position = new Vector2(
                    chunksX * chunkSize * 0.5f,
                    chunksZ * chunkSize * 0.5f
                ),

                size = new Vector2(
                    chunksX * chunkSize + padding,
                    chunksZ * chunkSize + padding
                ),

                yawDegrees = 0f,
                heightMeters = finalHeight,
                baseY = 0f,

                blend = StampBlend.Replace,

                edgeFalloff = 0f,
                strength = 1f,
                invert = false
            };

            world.PlaceStamp(stamp);

            status =
                $"Built {chunksX * chunkSize:F0} x " +
                $"{chunksZ * chunkSize:F0} m terrain from " +
                $"'{texture.name}', max height {finalHeight:F0} m.";
        }

        // --------------------------------------------------
        // SLIDER
        // --------------------------------------------------

        private float Slider(
            string label,
            float value,
            float min,
            float max)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(label, GUILayout.Width(80));

            value = GUILayout.HorizontalSlider(
                value,
                min,
                max
            );

            GUILayout.Label(
                value.ToString("F0"),
                GUILayout.Width(44)
            );

            GUILayout.EndHorizontal();

            return value;
        }

        // --------------------------------------------------
        // UI
        // --------------------------------------------------

        private void OnGUI()
        {
            GUILayout.BeginArea(panelRect, GUI.skin.box);

            GUILayout.Label("HEIGHTMAP IMPORT");

            GUILayout.Space(5);

            GUILayout.Label("PNG File Path");

            path = GUILayout.TextField(
                path,
                GUILayout.Height(25)
            );

            GUILayout.Label(
                "Example: C:/Heightmaps/terrain.png",
                GUI.skin.label
            );

            GUILayout.Space(5);

            if (GUILayout.Button("IMPORT & BUILD TERRAIN", GUILayout.Height(35)))
            {
                Import();
            }

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();

            bigEndian = GUILayout.Toggle(
                bigEndian,
                "RAW big-endian"
            );

            flipY = GUILayout.Toggle(
                flipY,
                "RAW flip vertical"
            );

            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Heightmap selection.
            GUILayout.Label("IMPORTED HEIGHTMAPS");

            GUILayout.BeginHorizontal();

            int count = library != null ? library.Count : 0;

            if (GUILayout.Button("<", GUILayout.Width(28)) && count > 0)
            {
                pick = (pick - 1 + count) % count;
            }

            string selectedLabel = "(none)";

            if (count > 0)
            {
                selectedLabel =
                    library.Get(Mathf.Clamp(pick, 0, count - 1))?.name
                    ?? "(missing)";
            }

            GUILayout.Label("Selected: " + selectedLabel);

            if (GUILayout.Button(">", GUILayout.Width(28)) && count > 0)
            {
                pick = (pick + 1) % count;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Terrain dimensions.
            GUILayout.Label("TERRAIN DIMENSIONS");

            sizeX = Slider(
                "Width (m)",
                sizeX,
                256f,
                4096f
            );

            sizeZ = Slider(
                "Length (m)",
                sizeZ,
                256f,
                4096f
            );

            heightM = Slider(
                "Height (m)",
                heightM,
                10f,
                1000f
            );

            GUILayout.Space(5);

            clearModels = GUILayout.Toggle(
                clearModels,
                "Clear placed trees/models"
            );

            GUILayout.Space(8);

            if (GUILayout.Button(
                "BUILD TERRAIN FROM HEIGHTMAP",
                GUILayout.Height(35)))
            {
                Build();
            }

            GUILayout.Space(5);

            GUILayout.Label(
                "Rebuilds the world. Ctrl+Z undoes height changes."
            );

            GUILayout.Space(5);

            GUILayout.Label("STATUS");

            GUILayout.Label(status);

            GUILayout.EndArea();
        }
    }
}
