using System.Collections.Generic;
using RuntimeWorldBuilder.Runtime.Stamp;
using RuntimeWorldBuilder.SO.Biomes;
using RuntimeWorldBuilder.SO.Settings;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public class TerrainChunkData
    {
        public Vector2Int Coord;
        public float[,] Heights;
        public Terrain Terrain;
        public bool Dirty;                      // true = edited, must be saved
    }

    public partial class TerrainWorld
    {
        readonly TerrainSettings s;
        readonly TerrainBiomeSettings biomeSettings;
        readonly Dictionary<Vector2Int, TerrainChunkData> chunks = new();
        readonly List<HeightmapStamp> stamps = new();

        public Vector2Int Min { get; private set; }
        public Vector2Int Max { get; private set; }
        public int Version { get; private set; }    // increases whenever any terrain height changes
        public IReadOnlyList<HeightmapStamp> Stamps => stamps;
        public IEnumerable<TerrainChunkData> Chunks => chunks.Values;

        #region Construction
        public TerrainWorld(TerrainSettings terrainSettings, TerrainBiomeSettings biome)
        {
            s = terrainSettings;
            biomeSettings = biome;
            Min = s.initialMin;
            Max = s.initialMax;

            for (int z = Min.y; z <= Max.y; z++)
                for (int x = Min.x; x <= Max.x; x++)
                    CreateChunk(new Vector2Int(x, z));
        }

        // world restored from a save
        public TerrainWorld(TerrainSettings terrainSettings, TerrainBiomeSettings biome, Vector2Int min, Vector2Int max,
            Dictionary<Vector2Int, float[,]> savedHeights, List<HeightmapStamp> savedStamps)
        {
            s = terrainSettings;
            biomeSettings = biome;
            stamps.AddRange(savedStamps);
            Min = min; Max = max;

            foreach (var kv in savedHeights)             // 1) edited chunks exactly as saved
            {
                var data = new TerrainChunkData { Coord = kv.Key, Heights = kv.Value, Dirty = true };
                data.Terrain = BuildTerrainObject(data, s.ChunkOrigin(kv.Key));
                TerrainPainter.PaintChunk(data, s, biomeSettings);
                chunks[kv.Key] = data;
            }
            for (int z = Min.y; z <= Max.y; z++)         // 2) everything else from the seed
                for (int x = Min.x; x <= Max.x; x++)
                    if (!chunks.ContainsKey(new Vector2Int(x, z)))
                        CreateChunk(new Vector2Int(x, z));
        }

        public void DestroyAll()
        {
            foreach (var d in chunks.Values) DestroyChunkObjects(d);
            chunks.Clear();
            stamps.Clear();
            ClearHistory();
        }

        static void DestroyChunkObjects(TerrainChunkData d)
        {
            if (d.Terrain == null) return;
            var td = d.Terrain.terrainData;
            UnityEngine.Object.Destroy(d.Terrain.gameObject);
            UnityEngine.Object.Destroy(td);              // TerrainData is a runtime asset, would leak otherwise
        }
        #endregion

        #region Chunk Logic
        public bool TryGetChunk(Vector2Int c, out TerrainChunkData d) => chunks.TryGetValue(c, out d);

        /// Grow extent by N chunks per side. Existing chunks are never modified. Undoable.
        public void Extend(int left, int right, int down, int up)
        {
            var oldMin = Min; var oldMax = Max;
            var created = ExtendInternal(left, right, down, up);
            if (created.Count == 0) return;

            PushOp(new ExtendOp
            {
                Label = "Extend",
                OldMin = oldMin,
                OldMax = oldMax,
                L = left,
                R = right,
                D = down,
                U = up,
                Created = created,
            });
        }

        List<Vector2Int> ExtendInternal(int left, int right, int down, int up)
        {
            var newMin = new Vector2Int(Min.x - left, Min.y - down);
            var newMax = new Vector2Int(Max.x + right, Max.y + up);
            var created = new List<Vector2Int>();

            for (int z = newMin.y; z <= newMax.y; z++)
                for (int x = newMin.x; x <= newMax.x; x++)
                {
                    var c = new Vector2Int(x, z);
                    if (chunks.ContainsKey(c)) continue;
                    CreateChunk(c);
                    created.Add(c);
                }

            Min = newMin; Max = newMax;
            Version++;
            return created;
        }

        void RemoveChunk(Vector2Int c)
        {
            if (!chunks.TryGetValue(c, out var d)) return;
            DestroyChunkObjects(d);
            chunks.Remove(c);
            Version++;
        }

        void CreateChunk(Vector2Int c)
        {
            int res = s.heightmapResolution, last = res - 1;
            var h = new float[res, res];
            float sp = s.CellSpacing;
            int baseX = c.x * last, baseZ = c.y * last;

            // 1) fill from deterministic base (global sample index => identical values on shared edges)
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = s.SampleBase01((baseX + x) * sp, (baseZ + z) * sp);

            var data = new TerrainChunkData { Coord = c, Heights = h };

            // 2) remainder of stamps that were placed before this chunk existed
            bool touched = false;
            foreach (var st in stamps) touched |= ApplyStamp(data, st, out _);
            data.Dirty = touched;

            // 3) stitch shared edges from existing (possibly edited) neighbours
            if (chunks.TryGetValue(c + Vector2Int.left, out var L))
                for (int z = 0; z < res; z++) h[z, 0] = L.Heights[z, last];
            if (chunks.TryGetValue(c + Vector2Int.right, out var R))
                for (int z = 0; z < res; z++) h[z, last] = R.Heights[z, 0];
            if (chunks.TryGetValue(c + Vector2Int.down, out var D))
                for (int x = 0; x < res; x++) h[0, x] = D.Heights[last, x];
            if (chunks.TryGetValue(c + Vector2Int.up, out var U))
                for (int x = 0; x < res; x++) h[last, x] = U.Heights[0, x];

            data.Terrain = BuildTerrainObject(data, s.ChunkOrigin(c));
            TerrainPainter.PaintChunk(data, s, biomeSettings);
            chunks[c] = data;
        }

        Terrain BuildTerrainObject(TerrainChunkData d, Vector3 origin)
        {
            var td = new TerrainData { heightmapResolution = s.heightmapResolution };
            td.size = new Vector3(s.chunkSize, s.maxHeight, s.chunkSize);
            td.SetHeights(0, 0, d.Heights);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = $"Chunk_{d.Coord.x}_{d.Coord.y}";
            go.transform.position = origin;
            return go.GetComponent<Terrain>();
        }

        /// Uploads a chunk's Heights and marks it for saving.
        /// For undo: call Touch(chunk) BEFORE modifying Heights, inside BeginEdit/EndEdit.
        public void CommitChunk(TerrainChunkData d)
        {
            int last = s.heightmapResolution - 1;
            CommitRegion(d, new Region(0, 0, last, last));
        }
        #endregion

        #region Stamp Logic
        /// Undoable.
        public void PlaceStamp(HeightmapStamp st)
        {
            if (st.heightmap == null) return;

            st.Prepare();

            BeginEdit("Place stamp");
            stamps.Add(st);
            NoteStampAdded(st);

            st.GetWorldBounds(out var min, out var max);
            float sp = s.CellSpacing;                       // 1 sample margin so shared edges are included
            var cMin = s.WorldToChunk(new Vector3(min.x - sp, 0, min.y - sp));
            var cMax = s.WorldToChunk(new Vector3(max.x + sp, 0, max.y + sp));

            for (int z = cMin.y; z <= cMax.y; z++)
                for (int x = cMin.x; x <= cMax.x; x++)
                    if (chunks.TryGetValue(new Vector2Int(x, z), out var d))
                    {
                        Touch(d);
                        if (ApplyStamp(d, st, out var changed)) CommitRegion(d, changed);
                    }

            EndEdit();                                      // pushes one undo step
        }

        bool ApplyStamp(TerrainChunkData d, HeightmapStamp st, out Region changed)
        {
            int res = s.heightmapResolution;
            float sp = s.CellSpacing, maxH = s.maxHeight;
            int baseX = d.Coord.x * (res - 1), baseZ = d.Coord.y * (res - 1);

            st.GetWorldBounds(out var bmin, out var bmax);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(bmin.x / sp) - baseX - 1, 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(bmax.x / sp) - baseX + 1, 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(bmin.y / sp) - baseZ - 1, 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt(bmax.y / sp) - baseZ + 1, 0, res - 1);

            int cx0 = int.MaxValue, cz0 = int.MaxValue, cx1 = -1, cz1 = -1;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float wx = (baseX + x) * sp, wz = (baseZ + z) * sp;
                    if (st.TryEvaluate(wx, wz, d.Heights[z, x], maxH, out var nv, out _))
                    {
                        d.Heights[z, x] = nv;
                        if (x < cx0) cx0 = x; if (x > cx1) cx1 = x;
                        if (z < cz0) cz0 = z; if (z > cz1) cz1 = z;
                    }
                }
            changed = new Region(cx0, cz0, cx1, cz1);
            return cx1 >= 0;
        }

        public bool TrySampleHeight01(float wx, float wz, out float h01)
        {
            h01 = 0f;
            var c = s.WorldToChunk(new Vector3(wx, 0, wz));
            if (!chunks.TryGetValue(c, out var d)) return false;

            int res = s.heightmapResolution;
            Vector3 o = s.ChunkOrigin(c);
            float fx = (wx - o.x) / s.CellSpacing, fz = (wz - o.z) / s.CellSpacing;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, res - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, res - 2);
            float tx = Mathf.Clamp01(fx - x0), tz = Mathf.Clamp01(fz - z0);

            float a = Mathf.Lerp(d.Heights[z0, x0], d.Heights[z0, x0 + 1], tx);
            float b = Mathf.Lerp(d.Heights[z0 + 1, x0], d.Heights[z0 + 1, x0 + 1], tx);
            h01 = Mathf.Lerp(a, b, tz);
            return true;
        }
        #endregion

        #region Biome Logic
        // ---------- deferred painting ----------
        struct Region
        {
            public int X0, Z0, X1, Z1;
            public Region(int x0, int z0, int x1, int z1) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
            public static Region Union(Region a, Region b) => new(
                Mathf.Min(a.X0, b.X0), Mathf.Min(a.Z0, b.Z0), Mathf.Max(a.X1, b.X1), Mathf.Max(a.Z1, b.Z1));
        }

        readonly Dictionary<Vector2Int, Region> pendingPaint = new();
        readonly List<Vector2Int> tmpKeys = new();

        void QueuePaint(TerrainChunkData d, Region r)
        {
            if (pendingPaint.TryGetValue(d.Coord, out var old)) r = Region.Union(old, r);
            pendingPaint[d.Coord] = r;
        }

        public bool HasPendingPaint => pendingPaint.Count > 0;

        /// Call every frame. Paints queued regions until the time budget is spent (always at least one).
        public void Tick(float budgetMs = 4f)
        {
            if (pendingPaint.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            tmpKeys.Clear(); tmpKeys.AddRange(pendingPaint.Keys);
            foreach (var k in tmpKeys)
            {
                var r = pendingPaint[k];
                pendingPaint.Remove(k);
                if (chunks.TryGetValue(k, out var d))
                    TerrainPainter.PaintRegion(d, s, biomeSettings, r.X0, r.Z0, r.X1, r.Z1);
                if (sw.Elapsed.TotalMilliseconds >= budgetMs) break;
            }
        }

        // uploads only the changed rectangle of heights
        void CommitRegion(TerrainChunkData d, Region r)
        {
            int res = s.heightmapResolution;
            int w = r.X1 - r.X0 + 1, h = r.Z1 - r.Z0 + 1;
            var sub = new float[h, w];
            for (int z = 0; z < h; z++)
                System.Buffer.BlockCopy(d.Heights, ((r.Z0 + z) * res + r.X0) * sizeof(float),
                                        sub, z * w * sizeof(float), w * sizeof(float));
            d.Terrain.terrainData.SetHeights(r.X0, r.Z0, sub);
            QueuePaint(d, r);
            d.Dirty = true;
            Version++;
        }
        #endregion
    }
}