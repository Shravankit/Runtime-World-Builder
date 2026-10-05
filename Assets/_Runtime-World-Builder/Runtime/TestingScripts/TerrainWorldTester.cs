using RuntimeWorldBuilder.Core.Service;
using RuntimeWorldBuilder.Data;
using RuntimeWorldBuilder.Runtime.World;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.HeightMap;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.Testing
{
    public class TerrainWorldTester : MonoBehaviour
    {
        [SerializeField] TerrainSettings settings;
        [SerializeField] TerrainBiomeSettings biomeSettings;
        [SerializeField] HeightmapLibrary library;
        [SerializeField] string slot = "slot1";

        TerrainWorld world;
        Vector2Int editChunk = new(0, 0);
        int editX, editZ;
        float editValue = -1f;
        double checksumBefore = -1;
        string status = "Press 'Edit chunk (0,0)' first.";

        void Start()
        {
            world = new TerrainWorld(settings, biomeSettings);
            ServiceRegistry.Register(settings);
            ServiceRegistry.Register(world);
            FrameCamera();
        }

        void Update()
        {
            world.Tick(4f);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                     || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (!ctrl) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.Z)) { if (shift) DoRedo(); else DoUndo(); }
            else if (Input.GetKeyDown(KeyCode.Y)) DoRedo();
        }


        void FrameCamera()
        {
            var cam = Camera.main;
            float cx = (world.Min.x + world.Max.x + 1) * 0.5f * settings.chunkSize;
            float cz = (world.Min.y + world.Max.y + 1) * 0.5f * settings.chunkSize;
            float span = (world.Max.x - world.Min.x + 1) * settings.chunkSize;
            cam.farClipPlane = 10000f;
            cam.transform.position = new Vector3(cx, span * 0.9f, cz - span * 0.6f);
            cam.transform.LookAt(new Vector3(cx, 0, cz));
        }

        // 1) make a visible edit (a bump) in chunk (0,0)
        void EditChunk()
        {
            if (!world.TryGetChunk(editChunk, out var c)) { status = "Chunk (0,0) missing"; return; }
            int res = settings.heightmapResolution;
            editX = res / 2; editZ = res / 2;
            int r = 25;

            world.BeginEdit("Bump");
            world.Touch(c);                                     // snapshot BEFORE modifying
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    float d = Mathf.Sqrt(x * x + z * z) / r;
                    if (d > 1f) continue;
                    c.Heights[editZ + z, editX + x] += (1f - d) * 0.4f;
                }
            c.Heights[editZ, editX] = Mathf.Clamp01(c.Heights[editZ, editX]);
            world.CommitChunk(c);
            world.EndEdit();

            editValue = c.Heights[editZ, editX];
            checksumBefore = Checksum(c);
            status = $"Edited chunk (0,0). Center height = {editValue:F4}";
        }

        // 2) extend, then 3) verify
        void Extend(int l, int r, int d, int u)
        {
            world.Extend(l, r, d, u);
            FrameCamera();
            Verify();
        }

        void Verify()
        {
            string msg = $"Extent {world.Min} -> {world.Max}\n";

            if (editValue >= 0 && world.TryGetChunk(editChunk, out var c))
            {
                // read back from the REAL Terrain, not from our array
                float live = c.Terrain.terrainData.GetHeights(editX, editZ, 1, 1)[0, 0];
                double sumNow = Checksum(c);
                bool same = Mathf.Abs(live - editValue) < 1e-5f && System.Math.Abs(sumNow - checksumBefore) < 1e-4;
                msg += $"Old edit preserved: {(same ? "PASS" : "FAIL")} (live {live:F4}, expected {editValue:F4})\n";
            }
            msg += $"Max seam gap: {MaxSeamGap():F6} " + (MaxSeamGap() < 1e-5f ? "(PASS)" : "(FAIL)");
            status = msg;
            Debug.Log(msg);
        }

        double Checksum(TerrainChunkData c)
        {
            var h = c.Terrain.terrainData.GetHeights(0, 0, settings.heightmapResolution, settings.heightmapResolution);
            double s = 0; foreach (var v in h) s += v; return s;
        }

        float MaxSeamGap()
        {
            int res = settings.heightmapResolution, last = res - 1;
            float max = 0f;
            for (int z = world.Min.y; z <= world.Max.y; z++)
                for (int x = world.Min.x; x <= world.Max.x; x++)
                {
                    if (!world.TryGetChunk(new(x, z), out var a)) continue;
                    var ha = a.Terrain.terrainData.GetHeights(0, 0, res, res);
                    if (world.TryGetChunk(new(x + 1, z), out var r))
                    {
                        var hr = r.Terrain.terrainData.GetHeights(0, 0, res, res);
                        for (int i = 0; i < res; i++) max = Mathf.Max(max, Mathf.Abs(ha[i, last] - hr[i, 0]));
                    }
                    if (world.TryGetChunk(new(x, z + 1), out var u))
                    {
                        var hu = u.Terrain.terrainData.GetHeights(0, 0, res, res);
                        for (int i = 0; i < res; i++) max = Mathf.Max(max, Mathf.Abs(ha[last, i] - hu[0, i]));
                    }
                }
            return max;
        }

        #region Save Load
        void SaveWorld() => TerrainSerializer.Save(world, settings, slot);
        void LoadWorld()
        {
            var loaded = TerrainSerializer.Load(slot, settings, biomeSettings, library);
            if (loaded == null) { status = "Load failed, see Console"; return; }

            world.DestroyAll();
            world = loaded;
            ServiceRegistry.Register(world);
            FrameCamera();
            editValue = -1f;
            status = $"Loaded '{slot}'  extent {world.Min} -> {world.Max}";
        }
        #endregion

        #region Undo Redo
        void DoUndo()
        {
            string label = world.UndoLabel;
            if (world.Undo()) { editValue = -1f; status = $"Undo: {label}\nExtent {world.Min} -> {world.Max}"; }
        }

        void DoRedo()
        {
            string label = world.RedoLabel;
            if (world.Redo()) { editValue = -1f; status = $"Redo: {label}\nExtent {world.Min} -> {world.Max}"; }
        }
        #endregion

        void OnDestroy()
        {
            ServiceRegistry.UnRegister<TerrainWorld>();
            ServiceRegistry.UnRegister<TerrainSettings>();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 400));
            if (GUILayout.Button("Edit chunk (0,0)")) EditChunk();
            if (GUILayout.Button("Extend +X")) Extend(0, 1, 0, 0);
            if (GUILayout.Button("Extend -X")) Extend(1, 0, 0, 0);
            if (GUILayout.Button("Extend +Z")) Extend(0, 0, 0, 1);
            if (GUILayout.Button("Extend -Z")) Extend(0, 0, 1, 0);
            if (GUILayout.Button("Verify now")) Verify();

            if (GUILayout.Button("Save")) SaveWorld();
            if (GUILayout.Button("Load")) LoadWorld();

            GUI.enabled = world.CanUndo;
            if (GUILayout.Button($"Undo {world.UndoLabel}  (Ctrl+Z)")) DoUndo();
            GUI.enabled = world.CanRedo;
            if (GUILayout.Button($"Redo {world.RedoLabel}  (Ctrl+Y)")) DoRedo();
            GUI.enabled = true;
            GUILayout.Label(status);
            GUILayout.EndArea();
        }
    }
}