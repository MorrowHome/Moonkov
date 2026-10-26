using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // The anatomy is raster artwork from the approved monitor, never a procedural mannequin.
    public sealed partial class RaidBodyDiagram : VisualElement
    {
        public const float DesignWidth = 770, DesignHeight = 1370;
        private static readonly Vector2 ImageOrigin = new Vector2(153,60);
        public static readonly Color Ivory = new Color32(196,225,255,255);
        public static readonly Color Injury = new Color32(255,126,83,255);
        private readonly Texture2D m_Texture;
        private readonly Rect[] m_Bounds = new Rect[RaidHealth.PartCount];
        private readonly VisualElement[] m_Targets = new VisualElement[RaidHealth.PartCount];
        private readonly float[] m_Health = new float[RaidHealth.PartCount], m_Maximum = new float[RaidHealth.PartCount];
        private readonly Vector2[] m_Clip = new Vector2[96];
        private readonly float[] m_FillCuts = new float[RaidHealth.PartCount];
        private readonly float[] m_PieceScales = new float[Pieces.Length];
        private readonly Vector2[] m_PieceCenters = new Vector2[Pieces.Length];
        private readonly Action<BodyPart> m_Select;
        private BodyPart m_Selected = BodyPart.Auto;
        private bool m_Ready;
        private float m_Scale;
        private Vector2 m_Origin;

        public RaidBodyDiagram(Action<BodyPart> select = null)
        {
            name = "health-body"; m_Select = select;
            m_Texture = Resources.Load<Texture2D>("Moonkov/UI/MedicalScan/BodyScan");
            if (!m_Texture) throw new InvalidOperationException("Missing approved medical scan body texture");
            pickingMode = select == null ? PickingMode.Ignore : PickingMode.Position;
            style.flexGrow = 1; style.minWidth = 0; style.minHeight = 0;
            for (int i = 0; i < Pieces.Length; i++)
            {
                var low = new Vector2(float.MaxValue,float.MaxValue); var high = new Vector2(float.MinValue,float.MinValue);
                foreach (var point in Pieces[i].Points) { low = Vector2.Min(low,point); high = Vector2.Max(high,point); }
                m_PieceCenters[i] = (low+high)*.5f;
            }
            for (int i = 0; i < RaidHealth.PartCount; i++)
            {
                var part = (BodyPart)i;
                var min = new Vector2(float.MaxValue,float.MaxValue); var max = new Vector2(float.MinValue,float.MinValue);
                foreach (var piece in Pieces) if (piece.Part == part) foreach (var point in piece.Points) { min = Vector2.Min(min,point); max = Vector2.Max(max,point); }
                m_Bounds[i] = Rect.MinMaxRect(min.x,min.y,max.x,max.y);
                VisualElement target;
                if (select == null) target = new VisualElement { pickingMode = PickingMode.Ignore };
                else
                {
                    var button = new Button(() => m_Select(part)); button.RegisterCallback<FocusInEvent>(_ => m_Select(part)); target = button;
                }
                target.name = "healthPart" + part; target.style.position = Position.Absolute; target.style.backgroundColor = Color.clear;
                target.style.borderLeftWidth = target.style.borderRightWidth = target.style.borderTopWidth = target.style.borderBottomWidth = 0;
                target.style.marginLeft = target.style.marginRight = target.style.marginTop = target.style.marginBottom = 0;
                target.style.paddingLeft = target.style.paddingRight = target.style.paddingTop = target.style.paddingBottom = 0;
                target.style.minWidth = target.style.minHeight = 0; Add(target); m_Targets[i] = target;
            }
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Resize());
        }
        public void Present(in PredictedPlayerGhost state, BodyPart selected = BodyPart.Auto)
        {
            bool changed = m_Ready != state.BodyHealthInitialized || m_Selected != selected;
            m_Ready = state.BodyHealthInitialized; m_Selected = selected;
            for (int i = 0; i < RaidHealth.PartCount; i++)
            {
                float health = RaidHealth.Get(state,(BodyPart)i), maximum = RaidHealth.Maximum(state,(BodyPart)i);
                if (m_Health[i] != health || m_Maximum[i] != maximum) changed = true;
                m_Health[i] = health; m_Maximum[i] = maximum;
                m_Targets[i].EnableInClassList("health-body-injured",m_Ready && health <= maximum*.5f);
            }
            if (changed)
            {
                for (int i = 0; i < RaidHealth.PartCount; i++)
                {
                    var part = (BodyPart)i;
                    float fraction = RaidVitalSignal.BodyFill(m_Health[i],m_Maximum[i],m_Ready);
                    m_FillCuts[i] = Shrinks(part) ? float.PositiveInfinity : FindFillCut(part,fraction);
                    if (Shrinks(part)) SetShrinkScales(part,fraction);
                }
                MarkDirtyRepaint();
            }
        }
        private Vector2 Screen(Vector2 point) => m_Origin+(point-ImageOrigin)*m_Scale;
        private void Resize()
        {
            m_Scale = Mathf.Min(contentRect.width/DesignWidth,contentRect.height/DesignHeight);
            if (!float.IsFinite(m_Scale) || m_Scale <= 0) return;
            m_Origin = new Vector2((contentRect.width-DesignWidth*m_Scale)*.5f,(contentRect.height-DesignHeight*m_Scale)*.5f);
            for (int i = 0; i < RaidHealth.PartCount; i++)
            {
                var bounds = m_Bounds[i]; var target = m_Targets[i]; var origin = Screen(bounds.min);
                target.style.left = origin.x; target.style.top = origin.y;
                target.style.width = bounds.width*m_Scale; target.style.height = bounds.height*m_Scale;
            }
            MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (m_Scale <= 0) return;
            for (int index = 0; index < Pieces.Length; index++)
            {
                var piece = Pieces[index]; int part = (int)piece.Part;
                // Keep the entire source silhouette visible, then light only the true remaining area.
                DrawPiece(context,piece.Points,float.PositiveInfinity,new Color(.55f,.55f,.55f,m_Ready ? .6f : .18f));
                DrawPiece(context,piece.Points,m_FillCuts[part],RaidVitalSignal.BodyTint(m_Health[part],m_Maximum[part],m_Ready),
                    Shrinks(piece.Part) ? m_PieceScales[index] : 1,m_PieceCenters[index]);
                if (m_Select == null || m_Selected != piece.Part || !m_Ready) continue;
                var painter = context.painter2D; painter.BeginPath(); painter.MoveTo(Screen(piece.Points[0]));
                for (int i = 1; i < piece.Points.Length; i++) painter.LineTo(Screen(piece.Points[i]));
                painter.ClosePath(); painter.strokeColor = new Color(.85f,.95f,1,.7f); painter.lineWidth = .8f; painter.Stroke();
            }
        }
        private int ClipHull(Vector2[] hull, float bottom)
        {
            int count = 0; var previous = hull[hull.Length-1]; bool previousInside = previous.y <= bottom;
            foreach (var current in hull)
            {
                bool inside = current.y <= bottom;
                if (inside != previousInside)
                    m_Clip[count++] = Vector2.LerpUnclamped(previous,current,(bottom-previous.y)/(current.y-previous.y));
                if (inside) m_Clip[count++] = current;
                previous = current; previousInside = inside;
            }
            return count;
        }
        private static float Area(Vector2[] polygon, int count)
        {
            float sum = 0;
            for (int i = 0,j = count-1; i < count; j = i++) sum += polygon[j].x*polygon[i].y-polygon[i].x*polygon[j].y;
            return Mathf.Abs(sum)*.5f;
        }
        private float FindFillCut(BodyPart part, float fraction)
        {
            if (fraction <= 0) return float.NegativeInfinity;
            if (fraction >= 1) return float.PositiveInfinity;
            float low = float.MaxValue, high = float.MinValue;
            float totalArea = 0;
            foreach (var piece in Pieces)
            {
                if (piece.Part != part) continue;
                totalArea += Area(piece.Points,piece.Points.Length);
                foreach (var point in piece.Points) { low = Mathf.Min(low,point.y); high = Mathf.Max(high,point.y); }
            }
            float target = totalArea*fraction;
            // One shared cut across the entire anatomical part, including every visual plate.
            for (int step = 0; step < 12; step++)
            {
                float middle = (low+high)*.5f;
                float filledArea = 0;
                foreach (var piece in Pieces)
                    if (piece.Part == part) filledArea += Area(m_Clip,ClipHull(piece.Points,middle));
                if (filledArea < target) low = middle; else high = middle;
            }
            return (low+high)*.5f;
        }
        private static bool Shrinks(BodyPart part) => part == BodyPart.Head || part == BodyPart.Chest || part == BodyPart.Abdomen;
        private void SetShrinkScales(BodyPart part, float fraction)
        {
            for (int i = 0; i < Pieces.Length; i++)
            {
                if (Pieces[i].Part != part) continue;
                // Scale area in proportion to health while keeping the original silhouette underneath.
                m_PieceScales[i] = Mathf.Sqrt(fraction);
            }
        }
        private void DrawPiece(MeshGenerationContext context, Vector2[] hull, float bottom, Color color, float scale = 1, Vector2 center = default)
        {
            int count = ClipHull(hull,bottom); if (count < 3 || color.a <= 0 || scale <= 0) return;
            var mesh = context.Allocate(count,(count-2)*3,m_Texture);
            for (int i = 0; i < count; i++)
            {
                var point = m_Clip[i]; var position = Screen(center+(point-center)*scale);
                mesh.SetNextVertex(new Vertex { position = new Vector3(position.x,position.y,Vertex.nearZ), tint = (Color32)color,
                    uv = new Vector2(point.x/ImageWidth,1-point.y/ImageHeight) });
            }
            for (ushort i = 1; i < count-1; i++) { mesh.SetNextIndex(0); mesh.SetNextIndex(i); mesh.SetNextIndex((ushort)(i+1)); }
        }
        public static Color ConditionColor(float fraction) => fraction <= 0 ? new Color(.25f,.24f,.23f) :
            fraction < .3f ? new Color(.72f,.28f,.22f) : fraction < .7f ? new Color(.72f,.54f,.24f) : new Color(.42f,.47f,.55f);
    }
}
