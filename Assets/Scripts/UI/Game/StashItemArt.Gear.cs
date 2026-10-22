using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashItemArt
    {
        private void Disc(uint color,float x,float y,float radius)
        {
            m_Painter.fillColor=C(color);m_Painter.BeginPath();
            m_Painter.Arc(P(x,y),radius*m_Scale,UnityEngine.UIElements.Angle.Degrees(0),UnityEngine.UIElements.Angle.Degrees(360));
            m_Painter.Fill();
        }
        private void LifeSupportShroud()
        {
            // Matches the fitted navy band, lilac bow and gold cat bell on the real character.
            Shape(0x202740,new Vector2(12,33),new Vector2(29,24),new Vector2(71,24),new Vector2(88,33),
                new Vector2(88,54),new Vector2(73,64),new Vector2(27,64),new Vector2(12,54));
            Line(0xd4af70,2,new Vector2(14,34),new Vector2(30,27),new Vector2(70,27),new Vector2(86,34));
            Line(0xd4af70,2,new Vector2(14,53),new Vector2(29,61),new Vector2(71,61),new Vector2(86,53));
            Shape(0x9380c1,new Vector2(48,46),new Vector2(29,34),new Vector2(27,57),new Vector2(48,51));
            Shape(0xa491d1,new Vector2(52,46),new Vector2(71,34),new Vector2(73,57),new Vector2(52,51));
            Shape(0xb2a3df,new Vector2(44,49),new Vector2(34,69),new Vector2(42,66),new Vector2(47,71),new Vector2(50,50));
            Shape(0xb2a3df,new Vector2(53,49),new Vector2(59,71),new Vector2(64,66),new Vector2(69,69),new Vector2(57,50));
            Box(0xc8b9ec,46,43,8,12);Ring(0xc59b57,2,50,61,4);
            Disc(0xdeb86d,50,76,12);Disc(0xf0d397,47,72,6);
            Shape(0xdeb86d,new Vector2(39,70),new Vector2(38,60),new Vector2(47,66));
            Shape(0xdeb86d,new Vector2(53,66),new Vector2(62,60),new Vector2(61,70));
            Disc(0x30283d,46,75,1.4f);Disc(0x30283d,54,75,1.4f);
            Line(0x554137,1.4f,new Vector2(42,80),new Vector2(58,80));
            Line(0x554137,1.4f,new Vector2(50,80),new Vector2(50,86));
        }
        private void ShieldGenerator()
        {
            // A carried projector with three emitter vanes, rather than plate armor.
            Shape(0x344251, new Vector2(30, 13), new Vector2(70, 13), new Vector2(83, 32),
                new Vector2(77, 82), new Vector2(62, 92), new Vector2(38, 92), new Vector2(23, 82), new Vector2(17, 32));
            Shape(0xb1bbc1, new Vector2(34, 19), new Vector2(66, 19), new Vector2(73, 34),
                new Vector2(67, 77), new Vector2(56, 84), new Vector2(44, 84), new Vector2(33, 77), new Vector2(27, 34));
            Ring(0x273d5e, 9, 50, 49, 16); Ring(0x739ecd, 3, 50, 49, 11);
            Ring(0xe4ece9, 2, 50, 49, 5);
            Line(0x386ba3, 4, new Vector2(50, 21), new Vector2(50, 30));
            Line(0x386ba3, 4, new Vector2(30, 68), new Vector2(38, 62));
            Line(0x386ba3, 4, new Vector2(70, 68), new Vector2(62, 62));
            Box(0x263442, 42, 76, 16, 5); Box(0x7096bc, 45, 77, 10, 2);
        }
        private void EnergySword()
        {
            Shape(0x17487e, new Vector2(84, 6), new Vector2(83, 28), new Vector2(45, 70), new Vector2(29, 55));
            Shape(0x6ca7cf, new Vector2(84, 6), new Vector2(79, 27), new Vector2(43, 65), new Vector2(35, 57));
            Line(0xe2f4ee, 2, new Vector2(78, 17), new Vector2(41, 59));
            Shape(0x8c9ba8, new Vector2(27, 51), new Vector2(48, 70), new Vector2(41, 77), new Vector2(20, 58));
            Line(0xd7dfdc, 2, new Vector2(26, 55), new Vector2(43, 70));
            Shape(0x303b4b, new Vector2(24, 63), new Vector2(36, 74), new Vector2(19, 92), new Vector2(7, 81));
            Line(0x6b8091, 3, new Vector2(17, 75), new Vector2(26, 83));
            Line(0x6b8091, 3, new Vector2(21, 70), new Vector2(30, 78));
        }
        private void CombatKnife()
        {
            Shape(0x7f8d98, new Vector2(79, 10), new Vector2(78, 41), new Vector2(48, 72), new Vector2(31, 56));
            Shape(0xd3dedb, new Vector2(79, 10), new Vector2(72, 40), new Vector2(43, 66), new Vector2(37, 59));
            Line(0xf0f2e9, 1.5f, new Vector2(76, 22), new Vector2(41, 60));
            Shape(0x445363, new Vector2(28, 51), new Vector2(51, 72), new Vector2(45, 78), new Vector2(22, 57));
            Shape(0x263342, new Vector2(26, 62), new Vector2(39, 74), new Vector2(20, 94), new Vector2(8, 82));
            for (int n = 0; n < 3; n++)
                Line(0x89989d, 2, new Vector2(15 + n * 4, 77 - n * 4), new Vector2(24 + n * 4, 85 - n * 4));
            Ring(0xaeb8b8, 2, 17, 85, 3);
        }
    }
}
