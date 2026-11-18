
using System;
using System.Collections.Generic;

using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Data;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using RuntimeWorldBuilder.SO.Vegetation;

using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class TerrainWorldTester : MonoBehaviour
    {
        [SerializeField] TerrainSettings settings;
        [SerializeField] TerrainBiomeSettings biomeSettings;
        [SerializeField] VegetationSettings vegetationSettings;
        [SerializeField] HeightmapLibrary library;
        [SerializeField] string slot = "slot1";

        TerrainWorld world;

        Vector2Int editChunk = new(0, 0);

        int editX;
        int editZ;

        float editValue = -1f;

        double checksumBefore = -1;

        string status = "Press 'Edit chunk (0,0)' first.";

        // --------------------------------------------------
        // INITIALIZATION
        // --------------------------------------------------

        void Start()
        {
            if (settings == null)
            {
                Debug.LogError("TerrainSettings is not assigned.");
                return;
            }

            if (biomeSettings == null)
            {
                Debug.LogError("TerrainBiomeSettings is not assigned.");
                return;
            }

            try
            {
                world = new TerrainWorld(
                    settings,
                    biomeSettings,
                    vegetationSettings
                );

                ServiceRegistry.Register(settings);
                ServiceRegistry.Register(world);

                FrameCamera();
            }
            catch (Exception e)
            {
                Debug.LogError("Terrain initialization failed: " + e);
                status = "Terrain initialization failed.";
            }
        }

        void Update()
        {
            if (world == null)
                return;

            world.Tick(4f);

            bool ctrl =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftCommand) ||
                Input.GetKey(KeyCode.RightCommand);

            if (!ctrl)
                return;

            bool shift =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (shift)
                    DoRedo();
                else
                    DoUndo();
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                DoRedo();
            }
        }

        // --------------------------------------------------
        // REBUILD WORLD
        // --------------------------------------------------

        public TerrainWorld RebuildWorld(
            Vector2Int min,
            Vector2Int max)
        {
            if (settings == null)
            {
                Debug.LogError("Cannot rebuild: TerrainSettings is missing.");
                return null;
            }

            if (biomeSettings == null)
            {
                Debug.LogError("Cannot rebuild: TerrainBiomeSettings is missing.");
                return null;
            }

            if (min.x > max.x || min.y > max.y)
            {
                Debug.LogError("Invalid terrain world bounds.");
                return null;
            }

            TerrainWorld newWorld = null;

            try
            {
                // Construct the new world with the requested bounds.
                // Empty saved data means chunks are generated normally.
                newWorld = new TerrainWorld(
                    settings,
                    biomeSettings,
                    min,
                    max,
                    new Dictionary<Vector2Int, float[,]>(),
                    new List<RuntimeWorldBuilder.Runtime.Stamp.HeightmapStamp>(),
                    vegetationSettings
                );

                if (newWorld == null)
                {
                    Debug.LogError("New terrain world could not be created.");
                    return null;
                }

                // Only destroy the old world after the new one is created.
                TerrainWorld oldWorld = world;

                world = newWorld;

                ServiceRegistry.Register(settings);
                ServiceRegistry.Register(world);

                if (oldWorld != null)
                    oldWorld.DestroyAll();

                editValue = -1f;
                checksumBefore = -1;

                FrameCamera();

                status =
                    $"World rebuilt: {world.Min} -> {world.Max}";

                Debug.Log(status);

                return world;
            }
            catch (Exception e)
            {
                Debug.LogError("World rebuild failed: " + e);

                if (newWorld != null && newWorld != world)
                    newWorld.DestroyAll();

                status = "World rebuild failed. Check Console.";

                return null;
            }
        }

        // --------------------------------------------------
        // CAMERA
        // --------------------------------------------------

        void FrameCamera()
        {
            if (world == null || settings == null)
                return;

            Camera cam = Camera.main;

            if (cam == null)
            {
                Debug.LogWarning("No Main Camera found.");
                return;
            }

            float cx =
                (world.Min.x + world.Max.x + 1) *
                0.5f * settings.chunkSize;

            float cz =
                (world.Min.y + world.Max.y + 1) *
                0.5f * settings.chunkSize;

            float span =
                (world.Max.x - world.Min.x + 1) *
                settings.chunkSize;

            cam.farClipPlane = 10000f;

            cam.transform.position = new Vector3(
                cx,
                span * 0.9f,
                cz - span * 0.6f
            );

            cam.transform.LookAt(
                new Vector3(cx, 0, cz)
            );
        }

        // --------------------------------------------------
        // EDIT CHUNK
        // --------------------------------------------------

        void EditChunk()
        {
            if (world == null)
                return;

            if (!world.TryGetChunk(editChunk, out var c))
            {
                status = "Chunk (0,0) missing.";
                return;
            }

            int res = settings.heightmapResolution;

            editX = res / 2;
            editZ = res / 2;

            int radius = 25;

            world.BeginEdit("Bump");

            world.Touch(c);

            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    float d =
                        Mathf.Sqrt(x * x + z * z) / radius;

                    if (d > 1f)
                        continue;

                    c.Heights[editZ + z, editX + x] +=
                        (1f - d) * 0.4f;
                }
            }

            c.Heights[editZ, editX] =
                Mathf.Clamp01(c.Heights[editZ, editX]);

            world.CommitChunk(c);
            world.EndEdit();

            editValue = c.Heights[editZ, editX];

            checksumBefore = Checksum(c);

            status =
                $"Edited chunk (0,0). Center height = {editValue:F4}";
        }

        // --------------------------------------------------
        // EXTEND WORLD
        // --------------------------------------------------

        void Extend(int l, int r, int d, int u)
        {
            if (world == null)
                return;

            world.Extend(l, r, d, u);

            FrameCamera();

            Verify();
        }

        // --------------------------------------------------
        // VERIFY WORLD
        // --------------------------------------------------

        void Verify()
        {
            if (world == null)
                return;

            string msg =
                $"Extent {world.Min} -> {world.Max}\n";

            if (editValue >= 0 &&
                world.TryGetChunk(editChunk, out var c))
            {
                float live =
                    c.Terrain.terrainData.GetHeights(
                        editX,
                        editZ,
                        1,
                        1
                    )[0, 0];

                double sumNow = Checksum(c);

                bool same =
                    Mathf.Abs(live - editValue) < 1e-5f &&
                    Math.Abs(sumNow - checksumBefore) < 1e-4;

                msg +=
                    $"Old edit preserved: " +
                    $"{(same ? "PASS" : "FAIL")} " +
                    $"(live {live:F4}, expected {editValue:F4})\n";
            }

            float seamGap = MaxSeamGap();

            msg +=
                $"Max seam gap: {seamGap:F6} " +
                (seamGap < 1e-5f ? "(PASS)" : "(FAIL)");

            status = msg;

            Debug.Log(msg);
        }

        double Checksum(TerrainChunkData c)
        {
            var heights =
                c.Terrain.terrainData.GetHeights(
                    0,
                    0,
                    settings.heightmapResolution,
                    settings.heightmapResolution
                );

            double sum = 0;

            foreach (var value in heights)
                sum += value;

            return sum;
        }

        float MaxSeamGap()
        {
            if (world == null)
                return 0f;

            int res = settings.heightmapResolution;
            int last = res - 1;

            float max = 0f;

            for (int z = world.Min.y; z <= world.Max.y; z++)
            {
                for (int x = world.Min.x; x <= world.Max.x; x++)
                {
                    if (!world.TryGetChunk(
                            new Vector2Int(x, z),
                            out var a))
                        continue;

                    var ha =
                        a.Terrain.terrainData.GetHeights(
                            0, 0, res, res
                        );

                    if (world.TryGetChunk(
                            new Vector2Int(x + 1, z),
                            out var right))
                    {
                        var hr =
                            right.Terrain.terrainData.GetHeights(
                                0, 0, res, res
                            );

                        for (int i = 0; i < res; i++)
                        {
                            max = Mathf.Max(
                                max,
                                Mathf.Abs(
                                    ha[i, last] - hr[i, 0]
                                )
                            );
                        }
                    }

                    if (world.TryGetChunk(
                            new Vector2Int(x, z + 1),
                            out var up))
                    {
                        var hu =
                            up.Terrain.terrainData.GetHeights(
                                0, 0, res, res
                            );

                        for (int i = 0; i < res; i++)
                        {
                            max = Mathf.Max(
                                max,
                                Mathf.Abs(
                                    ha[last, i] - hu[0, i]
                                )
                            );
                        }
                    }
                }
            }

            return max;
        }

        // --------------------------------------------------
        // SAVE / LOAD
        // --------------------------------------------------

        void SaveWorld()
        {
            if (world == null)
                return;

            TerrainSerializer.Save(
                world,
                settings,
                slot
            );
        }

        void LoadWorld()
        {
            if (world == null)
                return;

            TerrainWorld loaded = TerrainSerializer.Load(
                slot,
                settings,
                biomeSettings,
                library,
                vegetationSettings
            );

            if (loaded == null)
            {
                status = "Load failed, see Console.";
                return;
            }

            world.DestroyAll();

            world = loaded;

            ServiceRegistry.Register(world);

            FrameCamera();

            editValue = -1f;

            status =
                $"Loaded '{slot}' extent {world.Min} -> {world.Max}";
        }

        // --------------------------------------------------
        // UNDO / REDO
        // --------------------------------------------------

        void DoUndo()
        {
            if (world == null)
                return;

            string label = world.UndoLabel;

            if (world.Undo())
            {
                editValue = -1f;

                status =
                    $"Undo: {label}\n" +
                    $"Extent {world.Min} -> {world.Max}";
            }
        }

        void DoRedo()
        {
            if (world == null)
                return;

            string label = world.RedoLabel;

            if (world.Redo())
            {
                editValue = -1f;

                status =
                    $"Redo: {label}\n" +
                    $"Extent {world.Min} -> {world.Max}";
            }
        }

        // --------------------------------------------------
        // CLEANUP
        // --------------------------------------------------

        void OnDestroy()
        {
            ServiceRegistry.UnRegister<TerrainWorld>();
            ServiceRegistry.UnRegister<TerrainSettings>();
        }

        // --------------------------------------------------
        // GUI
        // --------------------------------------------------

        void OnGUI()
        {
            if (world == null)
                return;

            GUILayout.BeginArea(
                new Rect(10, 10, 320, 400)
            );

            if (GUILayout.Button("Edit chunk (0,0)"))
                EditChunk();

            if (GUILayout.Button("Extend +X"))
                Extend(0, 1, 0, 0);

            if (GUILayout.Button("Extend -X"))
                Extend(1, 0, 0, 0);

            if (GUILayout.Button("Extend +Z"))
                Extend(0, 0, 0, 1);

            if (GUILayout.Button("Extend -Z"))
                Extend(0, 0, 1, 0);

            if (GUILayout.Button("Verify now"))
                Verify();

            if (GUILayout.Button("Save"))
                SaveWorld();

            if (GUILayout.Button("Load"))
                LoadWorld();

            GUI.enabled = world.CanUndo;

            if (GUILayout.Button(
                    $"Undo {world.UndoLabel} (Ctrl+Z)"))
                DoUndo();

            GUI.enabled = world.CanRedo;

            if (GUILayout.Button(
                    $"Redo {world.RedoLabel} (Ctrl+Y)"))
                DoRedo();

            GUI.enabled = true;

            GUILayout.Label(status);

            GUILayout.EndArea();
        }
    }
}
