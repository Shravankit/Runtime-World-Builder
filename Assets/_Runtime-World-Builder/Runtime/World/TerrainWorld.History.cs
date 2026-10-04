using System;
using System.Collections.Generic;
using RuntimeWorldBuilder.Runtime.Stamp;
using UnityEngine;

namespace RuntimeWorldBuilder.Runtime.World
{
    public partial class TerrainWorld
    {
        // ---------- settings ----------
        public int MaxHistory = 30;
        public long MaxHistoryBytes = 256L * 1024 * 1024;

        // ---------- state ----------
        readonly List<Op> undoStack = new();
        readonly List<Op> redoStack = new();

        int depth;
        string pendingLabel;
        Dictionary<Vector2Int, Snap> pendingSnaps;
        List<HeightmapStamp> pendingStamps;

        public event Action HistoryChanged;

        public bool CanUndo => depth == 0 && undoStack.Count > 0;
        public bool CanRedo => depth == 0 && redoStack.Count > 0;
        public string UndoLabel => undoStack.Count > 0 ? undoStack[undoStack.Count - 1].Label : "";
        public string RedoLabel => redoStack.Count > 0 ? redoStack[redoStack.Count - 1].Label : "";

        // ---------- operations ----------
        abstract class Op
        {
            public string Label;
            public abstract long Bytes { get; }
            public abstract void Undo(TerrainWorld w);
            public abstract void Redo(TerrainWorld w);
        }

        sealed class Snap
        {
            public Vector2Int Coord;
            public float[,] Before, After;
            public bool DirtyBefore, DirtyAfter;
        }

        sealed class EditOp : Op
        {
            public readonly List<Snap> Snaps = new();
            public readonly List<HeightmapStamp> AddedStamps = new();

            public override long Bytes
            {
                get { long b = 0; foreach (var s in Snaps) b += (s.Before.LongLength + s.After.LongLength) * 4; return b; }
            }

            public override void Undo(TerrainWorld w)
            {
                foreach (var st in AddedStamps) w.stamps.Remove(st);
                foreach (var sn in Snaps) w.Restore(sn.Coord, sn.Before, sn.DirtyBefore);
            }

            public override void Redo(TerrainWorld w)
            {
                foreach (var st in AddedStamps) if (!w.stamps.Contains(st)) w.stamps.Add(st);
                foreach (var sn in Snaps) w.Restore(sn.Coord, sn.After, sn.DirtyAfter);
            }
        }

        sealed class ExtendOp : Op
        {
            public Vector2Int OldMin, OldMax;
            public int L, R, D, U;
            public List<Vector2Int> Created;

            public override long Bytes => 256;

            public override void Undo(TerrainWorld w)
            {
                foreach (var c in Created) w.RemoveChunk(c);
                w.Min = OldMin; w.Max = OldMax;
                w.Version++;
            }

            public override void Redo(TerrainWorld w) => w.ExtendInternal(L, R, D, U);
        }

        // ---------- recording ----------
        /// Start grouping changes into ONE undo step (e.g. mouse-down of a brush stroke). Calls can nest.
        public void BeginEdit(string label)
        {
            if (depth++ > 0) return;
            pendingLabel = label;
            pendingSnaps = new Dictionary<Vector2Int, Snap>();
            pendingStamps = new List<HeightmapStamp>();
        }

        /// Snapshot a chunk BEFORE you change its Heights. Safe to call repeatedly.
        public void Touch(TerrainChunkData d)
        {
            if (depth == 0 || pendingSnaps.ContainsKey(d.Coord)) return;
            pendingSnaps[d.Coord] = new Snap
            {
                Coord = d.Coord,
                Before = (float[,])d.Heights.Clone(),
                DirtyBefore = d.Dirty,
            };
        }

        void NoteStampAdded(HeightmapStamp st) { if (depth > 0) pendingStamps.Add(st); }

        /// Finish the group and push it on the undo stack (nothing is pushed if nothing changed).
        public void EndEdit()
        {
            if (depth == 0) return;
            if (--depth > 0) return;

            var op = new EditOp { Label = pendingLabel };
            foreach (var sn in pendingSnaps.Values)
            {
                if (!chunks.TryGetValue(sn.Coord, out var d)) continue;
                if (SameHeights(sn.Before, d.Heights)) { d.Dirty = sn.DirtyBefore; continue; }   // touched but unchanged

                sn.After = (float[,])d.Heights.Clone();
                sn.DirtyAfter = d.Dirty;
                op.Snaps.Add(sn);
            }
            op.AddedStamps.AddRange(pendingStamps);
            pendingSnaps = null; pendingStamps = null;

            if (op.Snaps.Count == 0 && op.AddedStamps.Count == 0) return;
            PushOp(op);
        }

        static bool SameHeights(float[,] a, float[,] b)
        {
            int n0 = a.GetLength(0), n1 = a.GetLength(1);
            for (int z = 0; z < n0; z++)
                for (int x = 0; x < n1; x++)
                    if (a[z, x] != b[z, x]) return false;
            return true;
        }

        void PushOp(Op op)
        {
            redoStack.Clear();
            undoStack.Add(op);
            TrimHistory();
            HistoryChanged?.Invoke();
        }

        void TrimHistory()
        {
            long total = 0;
            foreach (var o in undoStack) total += o.Bytes;
            while (undoStack.Count > 1 && (undoStack.Count > MaxHistory || total > MaxHistoryBytes))
            {
                total -= undoStack[0].Bytes;
                undoStack.RemoveAt(0);
            }
        }

        // ---------- undo / redo ----------
        public bool Undo()
        {
            if (!CanUndo) return false;
            var op = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            op.Undo(this);
            redoStack.Add(op);
            HistoryChanged?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            var op = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            op.Redo(this);
            undoStack.Add(op);
            HistoryChanged?.Invoke();
            return true;
        }

        public void ClearHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
            depth = 0;
            pendingSnaps = null; pendingStamps = null;
            HistoryChanged?.Invoke();
        }

        void Restore(Vector2Int c, float[,] src, bool dirty)
        {
            if (!chunks.TryGetValue(c, out var d)) return;
            Array.Copy(src, d.Heights, src.Length);
            d.Terrain.terrainData.SetHeights(0, 0, d.Heights);
            d.Dirty = dirty;
            Version++;
        }
    }
}