using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public enum StashArtKind { None, Dust, Alloy, Cell, Halo, Outfit, Helmet, Rifle, Pistol, ChestRig, Backpack }

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
            float height = m_Kind == StashArtKind.Rifle ? 45 : m_Kind == StashArtKind.Alloy || m_Kind == StashArtKind.Pistol ? 60 : 100;
            m_Scale = Mathf.Min(contentRect.width / width, contentRect.height / height);
            m_Origin = new Vector2((contentRect.width - width * m_Scale) * 0.5f, (contentRect.height - height * m_Scale) * 0.5f);
            switch (m_Kind)
            {
                case StashArtKind.Dust: Dust(); break;
                case StashArtKind.Alloy: Alloy(); break;
                case StashArtKind.Cell: Cell(); break;
                case StashArtKind.Halo: Halo(); break;
                case StashArtKind.Outfit: Outfit(); break;
                case StashArtKind.Helmet: Helmet(); break;
                case StashArtKind.Rifle: Rifle(); break;
                case StashArtKind.Pistol: Pistol(); break;
                case StashArtKind.ChestRig: ChestRig(); break;
                case StashArtKind.Backpack: Backpack(); break;
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

        private void Helmet()
        {
            Shape(0x535d60, new Vector2(18, 63), new Vector2(20, 35), new Vector2(31, 19), new Vector2(53, 12), new Vector2(75, 22), new Vector2(84, 42), new Vector2(83, 67), new Vector2(67, 83), new Vector2(35, 81));
            Shape(0xc5cdcb, new Vector2(24, 39), new Vector2(33, 25), new Vector2(53, 19), new Vector2(72, 27), new Vector2(78, 41));
            Shape(0x283739, new Vector2(23, 45), new Vector2(78, 45), new Vector2(73, 65), new Vector2(33, 69), new Vector2(24, 60));
            Line(0x8fa8a8, 2, new Vector2(29, 49), new Vector2(73, 49));
            Box(0x7e8887, 14, 44, 8, 20); Box(0x7e8887, 82, 44, 6, 20);
            Line(0xd7ddda, 2, new Vector2(38, 74), new Vector2(64, 74));
        }
        private void Rifle()
        {
            Shape(0x434d50, new Vector2(5, 13), new Vector2(23, 13), new Vector2(31, 18), new Vector2(73, 18), new Vector2(73, 27), new Vector2(29, 27), new Vector2(16, 33), new Vector2(5, 33));
            Box(0x68777b, 34, 15, 37, 4); Box(0x2b3538, 73, 20, 21, 4); Box(0x434d50, 92, 18, 5, 8);
            Shape(0x58666a, new Vector2(37, 26), new Vector2(49, 26), new Vector2(54, 40), new Vector2(43, 42));
            Shape(0x313d41, new Vector2(27, 26), new Vector2(34, 26), new Vector2(32, 39), new Vector2(25, 39));
            Box(0x859193, 42, 8, 13, 6); Box(0x394548, 45, 6, 8, 4);
            Line(0xaeb8b7, 1, new Vector2(36, 21), new Vector2(66, 21));
            for (int x = 55; x < 70; x += 4) Box(0x293639, x, 23, 2, 2);
        }
        private void Pistol()
        {
            Shape(0x526267, new Vector2(17, 14), new Vector2(82, 14), new Vector2(82, 27), new Vector2(47, 27), new Vector2(39, 53), new Vector2(23, 50), new Vector2(30, 28), new Vector2(17, 26));
            Box(0x879496, 20, 14, 60, 4); Box(0x2a373b, 28, 33, 11, 14);
            Line(0x455358, 3, new Vector2(47, 28), new Vector2(58, 28), new Vector2(57, 36), new Vector2(44, 36));
            Box(0x253237, 71, 11, 6, 3);
        }
        private void ChestRig()
        {
            Box(0x7b8987, 24, 13, 12, 26); Box(0x7b8987, 64, 13, 12, 26);
            Shape(0x566763, new Vector2(23, 33), new Vector2(77, 33), new Vector2(82, 82), new Vector2(19, 82));
            Box(0x87958e, 27, 37, 45, 16); Box(0x3c4c49, 24, 58, 15, 21); Box(0x3c4c49, 42, 58, 15, 21); Box(0x3c4c49, 60, 58, 16, 21);
            Line(0xb5c0b5, 1, new Vector2(30, 44), new Vector2(69, 44));
            Box(0xb0bbb2, 45, 49, 10, 5);
        }
        private void Backpack()
        {
            Line(0x5a6968, 6, new Vector2(34, 22), new Vector2(37, 12), new Vector2(62, 12), new Vector2(67, 23));
            Shape(0x647571, new Vector2(28, 20), new Vector2(72, 20), new Vector2(79, 37), new Vector2(78, 87), new Vector2(22, 87), new Vector2(22, 37));
            Box(0x344744, 17, 44, 10, 29); Box(0x344744, 74, 44, 10, 29);
            Box(0x8b9991, 29, 26, 42, 22); Box(0x4e625b, 30, 55, 40, 27);
            Box(0xb8c0b1, 34, 36, 5, 16); Box(0xb8c0b1, 61, 36, 5, 16);
            Line(0xaeb9ad, 1, new Vector2(32, 63), new Vector2(68, 63));
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
            painter.strokeColor = new Color(0.24f, 0.26f, 0.25f, 0.18f); painter.lineWidth = 1;
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
