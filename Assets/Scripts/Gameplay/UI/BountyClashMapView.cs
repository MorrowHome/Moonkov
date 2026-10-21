using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    // A schematic from approved server signals; no scene camera or enemy ghost scan.
    public sealed class BountyClashMapView : VisualElement
    {
        private ClashSnapshotRpc m_Snapshot;
        private Vector3 m_Local;
        private bool m_HasLocal;
        private readonly List<ClashParticipantRpc> m_Friends = new List<ClashParticipantRpc>(3);
        private readonly Label m_ExitA, m_ExitB;
        public BountyClashMapView()
        {
            pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
            var north = MapLabel("N"); north.style.left = Length.Percent(48); north.style.top = 2; Add(north);
            m_ExitA = MapLabel("A"); m_ExitB = MapLabel("B"); Add(m_ExitA); Add(m_ExitB);
        }
        private static Label MapLabel(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute; label.style.fontSize = 11;
            label.style.color = new Color(.76f, .92f, .82f); return label;
        }
        public void Present(ClashSnapshotRpc value, IEnumerable<ClashParticipantRpc> rows, Vector3 local, bool hasLocal)
        {
            m_Snapshot = value; m_Local = local; m_HasLocal = hasLocal; m_Friends.Clear();
            foreach (var row in rows) if (row.OwnTeam && row.TeamId == value.LocalTeamId && !row.IsLocal && (row.Life == ClashLife.Alive || row.Life == ClashLife.Downed))
                m_Friends.Add(row);
            var a = Project(value.ExitPositionA); var b = Project(value.ExitPositionB);
            m_ExitA.style.left = Mathf.Clamp(a.x + 7, 2, Mathf.Max(2, contentRect.width - 14)); m_ExitA.style.top = a.y - 8;
            m_ExitB.style.left = Mathf.Clamp(b.x + 7, 2, Mathf.Max(2, contentRect.width - 14)); m_ExitB.style.top = b.y - 8;
            MarkDirtyRepaint();
        }
        private Vector2 Project(Vector3 point)
        {
            float radius = Mathf.Max(1, m_Snapshot.ArenaRadius), scale = (Mathf.Min(contentRect.width, contentRect.height) - 16) / (2 * radius);
            var delta = point - (Vector3)m_Snapshot.ArenaCenter;
            return contentRect.center + new Vector2(delta.x, -delta.z) * scale;
        }
        private static void Dot(Painter2D p, Vector2 at, float radius, Color color)
        { p.fillColor = color; p.BeginPath(); p.Arc(at, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 2 || m_Snapshot.ArenaRadius <= 0) return;
            var p = context.painter2D; float radius = Mathf.Min(contentRect.width, contentRect.height) * .5f - 8;
            p.lineWidth = 1; p.strokeColor = new Color(.58f, .66f, .76f, .65f);
            p.BeginPath(); p.Arc(contentRect.center, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.strokeColor = new Color(.5f, .6f, .7f, .2f);
            p.BeginPath(); p.MoveTo(contentRect.center + Vector2.left * radius); p.LineTo(contentRect.center + Vector2.right * radius);
            p.MoveTo(contentRect.center + Vector2.up * radius); p.LineTo(contentRect.center + Vector2.down * radius); p.Stroke();
            var target = Project(m_Snapshot.TargetPosition);
            p.strokeColor = new Color(.9f, .77f, .48f); p.BeginPath();
            p.MoveTo(target + Vector2.up * 6); p.LineTo(target + Vector2.right * 6); p.LineTo(target + Vector2.down * 6);
            p.LineTo(target + Vector2.left * 6); p.ClosePath(); p.Stroke();
            Dot(p, Project(m_Snapshot.ExitPositionA), 5, new Color(.56f, .83f, .66f));
            Dot(p, Project(m_Snapshot.ExitPositionB), 5, new Color(.56f, .83f, .66f));
            foreach (var friend in m_Friends)
            {
                var at = Project(friend.Position);
                if (friend.Life == ClashLife.Downed)
                {
                    p.strokeColor = new Color(1, .72f, .35f); p.lineWidth = 2;
                    p.BeginPath(); p.MoveTo(at + Vector2.left * 4); p.LineTo(at + Vector2.right * 4);
                    p.MoveTo(at + Vector2.up * 4); p.LineTo(at + Vector2.down * 4); p.Stroke(); p.lineWidth = 1;
                }
                else Dot(p, at, 3, new Color(.55f, .78f, .95f));
            }
            if (m_HasLocal) Dot(p, Project(m_Local), 4, Color.white);
            if (m_Snapshot.Bounty == ClashBountyState.Ground) Dot(p, Project(m_Snapshot.BountyPosition), 4, new Color(1, .75f, .25f));
            else if (m_Snapshot.Bounty == ClashBountyState.Carried)
            {
                var coarse = (Vector3)m_Snapshot.BountyPosition;
                coarse.x = Mathf.Round(coarse.x / 25) * 25; coarse.z = Mathf.Round(coarse.z / 25) * 25;
                p.strokeColor = new Color(.95f, .64f, .27f, .8f); p.lineWidth = 2;
                p.BeginPath(); p.Arc(Project(coarse), Mathf.Max(8, radius * 25 / m_Snapshot.ArenaRadius), Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            }
        }
    }
}
