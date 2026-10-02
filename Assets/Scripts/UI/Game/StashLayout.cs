using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Client
{
    // Client presentation coordinates only. This model never changes account quantities.
    public sealed class StashLayout
    {
        public sealed class Placement
        {
            public int X, Y, BaseWidth, BaseHeight;
            public bool Rotated, Locked, Favorite;
            public bool InStorage = true, Available = true;
            public int Width => Rotated ? BaseHeight : BaseWidth;
            public int Height => Rotated ? BaseWidth : BaseHeight;
        }

        public readonly Dictionary<string, Placement> Items = new Dictionary<string, Placement>();
        private readonly int m_Columns, m_Rows;
        public StashLayout(int columns, int rows) { m_Columns = columns; m_Rows = rows; }
        public void Add(string id, int width, int height, int x, int y) =>
            Items.Add(id, new Placement { BaseWidth = width, BaseHeight = height, X = x, Y = y });

        public bool CanPlace(string id, int x, int y, bool rotated)
        {
            var item = Items[id];
            int w = rotated ? item.BaseHeight : item.BaseWidth;
            int h = rotated ? item.BaseWidth : item.BaseHeight;
            if (x < 0 || y < 0 || x + w > m_Columns || y + h > m_Rows) return false;
            var rect = new RectInt(x, y, w, h);
            foreach (var pair in Items)
            {
                if (pair.Key == id) continue;
                var other = pair.Value;
                if (!other.InStorage || !other.Available) continue;
                if (rect.Overlaps(new RectInt(other.X, other.Y, other.Width, other.Height))) return false;
            }
            return true;
        }

        public bool TryMove(string id, int x, int y, bool rotated)
        {
            var item = Items[id];
            if (item.Locked || !CanPlace(id, x, y, rotated)) return false;
            item.X = x; item.Y = y; item.Rotated = rotated;
            return true;
        }

        public bool FindSpace(string id, out Vector2Int position)
        {
            for (int y = 0; y < m_Rows; y++)
                for (int x = 0; x < m_Columns; x++)
                    if (CanPlace(id, x, y, Items[id].Rotated)) { position = new Vector2Int(x, y); return true; }
            position = default; return false;
        }
    }
}
