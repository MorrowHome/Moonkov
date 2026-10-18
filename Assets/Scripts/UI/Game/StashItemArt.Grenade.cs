using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashItemArt
    {
        private void EnergyGrenade()
        {
            Ring(0x16435c, 10, 50, 50, 28);
            Ring(0x28799c, 5, 50, 50, 25);
            Ring(0x71e5ff, 3, 50, 50, 21);
            Shape(0x2b9ecc, new Vector2(50,24), new Vector2(69,35), new Vector2(76,53),
                new Vector2(63,73), new Vector2(42,77), new Vector2(25,59), new Vector2(29,39));
            Shape(0x95f4ff, new Vector2(47,30), new Vector2(63,42), new Vector2(66,58),
                new Vector2(53,69), new Vector2(37,62), new Vector2(34,43));
            Shape(0xf0ffff, new Vector2(49,36), new Vector2(60,48), new Vector2(54,62),
                new Vector2(42,57), new Vector2(41,44));
            Line(0xb8f8ff, 2, new Vector2(13,62), new Vector2(24,67), new Vector2(72,29), new Vector2(84,37));
            Line(0x62d8ff, 2, new Vector2(25,16), new Vector2(35,24), new Vector2(68,77), new Vector2(79,83));
            Line(0xe2ffff, 2, new Vector2(17,30), new Vector2(11,23));
            Line(0x70dfff, 2, new Vector2(80,60), new Vector2(90,66), new Vector2(85,72));
        }
    }
}
