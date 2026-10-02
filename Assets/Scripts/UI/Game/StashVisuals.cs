using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public enum StashArtKind { None, Dust, Alloy, Cell, Halo, Outfit }

    // Small original vector illustrations, kept sharp at the shared PanelSettings scale.
    public sealed class StashItemArt : VisualElement
    {
        private readonly StashArtKind m_Kind;
        private Painter2D m_Painter;
        private Vector2 m_Origin;
        private float m_Scale;

        public StashItemArt(StashArtKind kind)
        {
            m_Kind = kind;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1 || contentRect.height < 1) return;
            m_Painter = context.painter2D;
            float width = m_Kind == StashArtKind.Cell ? 60 : 100;
            float height = m_Kind == StashArtKind.Alloy ? 60 : 100;
            m_Scale = Mathf.Min(contentRect.width / width, contentRect.height / height);
            m_Origin = new Vector2((contentRect.width - width * m_Scale) * 0.5f, (contentRect.height - height * m_Scale) * 0.5f);
            switch (m_Kind)
            {
                case StashArtKind.Dust: Dust(); break;
                case StashArtKind.Alloy: Alloy(); break;
                case StashArtKind.Cell: Cell(); break;
                case StashArtKind.Halo: Halo(); break;
                case StashArtKind.Outfit: Outfit(); break;
            }
        }

        private Vector2 P(float x, float y) => m_Origin + new Vector2(x, y) * m_Scale;
        private static Color C(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        private void Shape(uint color, params Vector2[] points)
        {
            m_Painter.fillColor = C(color);
            m_Painter.BeginPath();
            m_Painter.MoveTo(P(points[0].x, points[0].y));
            for (int i = 1; i < points.Length; i++) m_Painter.LineTo(P(points[i].x, points[i].y));
            m_Painter.ClosePath(); m_Painter.Fill();
        }
        private void Box(uint color, float x, float y, float w, float h) => Shape(color,
            new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h));
        private void Line(uint color, float width, params Vector2[] points)
        {
            m_Painter.strokeColor = C(color); m_Painter.lineWidth = width * m_Scale;
            m_Painter.BeginPath(); m_Painter.MoveTo(P(points[0].x, points[0].y));
            for (int i = 1; i < points.Length; i++) m_Painter.LineTo(P(points[i].x, points[i].y));
            m_Painter.Stroke();
        }
        private void Ring(uint color, float width, float x, float y, float radius)
        {
            m_Painter.strokeColor = C(color); m_Painter.lineWidth = width * m_Scale;
            m_Painter.BeginPath();
            m_Painter.Arc(P(x, y), radius * m_Scale, Angle.Degrees(0), Angle.Degrees(360));
            m_Painter.Stroke();
        }

        private void Dust()
        {
            Shape(0x151a1b, new Vector2(24, 14), new Vector2(80, 12), new Vector2(87, 88), new Vector2(21, 94), new Vector2(17, 69));
            Shape(0x778184, new Vector2(23, 10), new Vector2(74, 8), new Vector2(80, 86), new Vector2(23, 91), new Vector2(19, 69));
            Shape(0x4e5a5e, new Vector2(31, 17), new Vector2(68, 15), new Vector2(71, 81), new Vector2(27, 85));
            Shape(0xaebabc, new Vector2(23, 10), new Vector2(31, 17), new Vector2(27, 85), new Vector2(23, 91), new Vector2(19, 69));
            Box(0xc1c8bd, 31, 30, 36, 32); Box(0x465152, 35, 35, 28, 5);
            Box(0x8c998c, 35, 44, 22, 2); Box(0x8c998c, 35, 49, 25, 2); Box(0x8c998c, 35, 54, 14, 2);
            Line(0xd0d5c9, 1.2f, new Vector2(25, 13), new Vector2(72, 11));
            Line(0x313e41, 2, new Vector2(25, 21), new Vector2(72, 19));
            Shape(0xafa992, new Vector2(35, 76), new Vector2(45, 68), new Vector2(53, 72), new Vector2(63, 69), new Vector2(66, 80), new Vector2(36, 82));
            Line(0xc6c5b3, 1, new Vector2(31, 88), new Vector2(71, 85));
        }

        private void Alloy()
        {
            Shape(0x141713, new Vector2(10, 38), new Vector2(72, 20), new Vector2(97, 29), new Vector2(97, 42), new Vector2(30, 58));
            Shape(0x8c9186, new Vector2(7, 30), new Vector2(72, 13), new Vector2(94, 23), new Vector2(29, 43));
            Shape(0x555e57, new Vector2(29, 43), new Vector2(94, 23), new Vector2(94, 36), new Vector2(29, 55));
            Shape(0xabb0a0, new Vector2(7, 30), new Vector2(29, 43), new Vector2(29, 55), new Vector2(7, 42));
            Shape(0xb4b8a9, new Vector2(13, 29), new Vector2(72, 15), new Vector2(84, 21), new Vector2(28, 38));
            Line(0xd9dac4, 1.5f, new Vector2(7, 30), new Vector2(29, 43), new Vector2(94, 23));
            Line(0x414a43, 1, new Vector2(45, 34), new Vector2(59, 30), new Vector2(66, 32));
            Line(0x758076, 1, new Vector2(35, 46), new Vector2(84, 32));
        }

        private void Cell()
        {
            Box(0x131822, 16, 18, 35, 73); Box(0x6d7990, 11, 13, 35, 74);
            Box(0xa2b1b5, 11, 13, 7, 74); Box(0x414d67, 39, 13, 7, 74);
            Box(0xb1b6a5, 11, 12, 35, 7); Box(0x242c3b, 11, 24, 35, 8);
            Box(0x222b3b, 11, 70, 35, 9); Box(0x808f9c, 14, 84, 29, 5);
            Box(0xc3c9b3, 21, 7, 14, 5); Box(0x26344a, 20, 36, 17, 29);
            Shape(0xc5d5dd, new Vector2(31, 39), new Vector2(24, 50), new Vector2(29, 50), new Vector2(25, 62), new Vector2(34, 47), new Vector2(29, 47));
            Line(0xdce4d3, 1.3f, new Vector2(14, 20), new Vector2(14, 66));
        }

        private void Halo()
        {
            Ring(0x141b1b, 9, 50, 54, 33); Ring(0x807b58, 7, 50, 49, 33);
            Ring(0xc6caa0, 2, 50, 48, 34); Ring(0x6c9fa5, 1.2f, 50, 48, 27);
            Shape(0xd2d8bc, new Vector2(50, 6), new Vector2(57, 14), new Vector2(50, 23), new Vector2(43, 14));
            Shape(0x93c2c3, new Vector2(50, 9), new Vector2(54, 14), new Vector2(50, 19), new Vector2(46, 14));
            Line(0xe0dec0, 2, new Vector2(19, 35), new Vector2(16, 42));
            Line(0xe0dec0, 2, new Vector2(81, 35), new Vector2(84, 42));
        }

        private void Outfit()
        {
            Shape(0x0b141a, new Vector2(38, 10), new Vector2(62, 10), new Vector2(80, 31), new Vector2(70, 41), new Vector2(62, 33), new Vector2(83, 90), new Vector2(17, 90), new Vector2(38, 33), new Vector2(30, 41), new Vector2(20, 31));
            Shape(0xd5d7cb, new Vector2(37, 10), new Vector2(44, 14), new Vector2(50, 10), new Vector2(56, 14), new Vector2(63, 10), new Vector2(73, 25), new Vector2(62, 31), new Vector2(38, 31), new Vector2(27, 25));
            Shape(0x486876, new Vector2(39, 30), new Vector2(61, 30), new Vector2(76, 84), new Vector2(24, 84));
            Shape(0x7e979a, new Vector2(44, 32), new Vector2(49, 32), new Vector2(44, 81), new Vector2(34, 83));
            Shape(0x243e4b, new Vector2(53, 32), new Vector2(61, 31), new Vector2(76, 84), new Vector2(58, 83));
            Line(0xc6d1c5, 3, new Vector2(24, 84), new Vector2(50, 90), new Vector2(76, 84));
            Box(0xb9c6c1, 39, 27, 22, 4);
        }
    }

    public sealed class StashGridVisual : VisualElement
    {
        private readonly int m_Columns, m_Rows;
        public StashGridVisual(int columns, int rows)
        {
            m_Columns = columns; m_Rows = rows;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1 || h < 1) return;
            var painter = context.painter2D;
            painter.strokeColor = new Color(0.24f, 0.26f, 0.22f, 0.8f); painter.lineWidth = 1;
            painter.BeginPath();
            for (int x = 0; x <= m_Columns; x++)
            {
                float px = x * w / m_Columns;
                painter.MoveTo(new Vector2(px, 0)); painter.LineTo(new Vector2(px, h));
            }
            for (int y = 0; y <= m_Rows; y++)
            {
                float py = y * h / m_Rows;
                painter.MoveTo(new Vector2(0, py)); painter.LineTo(new Vector2(w, py));
            }
            painter.Stroke();
        }
    }
}
