using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashItemArt
    {
        private void MedicalGel()
        {
            Shape(0x516466, new Vector2(27, 15), new Vector2(73, 15), new Vector2(65, 77), new Vector2(35, 77));
            Shape(0xdce4d5, new Vector2(31, 19), new Vector2(69, 19), new Vector2(61, 75), new Vector2(39, 75));
            Box(0x97aea6, 27, 11, 46, 8); Box(0x547f73, 40, 77, 20, 12);
            Box(0xb65a50, 45, 31, 10, 30); Box(0xb65a50, 36, 41, 28, 10);
            Line(0xffffff, 2, new Vector2(35, 24), new Vector2(40, 64));
        }

        private void TraumaKit()
        {
            Line(0x534c40, 7, new Vector2(34, 25), new Vector2(34, 15), new Vector2(66, 15), new Vector2(66, 25));
            Shape(0x6c3d2b, new Vector2(18, 25), new Vector2(82, 25), new Vector2(88, 34), new Vector2(84, 86), new Vector2(16, 86), new Vector2(12, 34));
            Box(0xc77d4b, 20, 29, 60, 52); Box(0xebb077, 20, 29, 60, 9);
            Box(0xe8e5cd, 44, 43, 12, 29); Box(0xe8e5cd, 35, 51, 30, 12);
            Box(0x554f44, 22, 34, 9, 17); Box(0x554f44, 69, 34, 9, 17);
            Line(0x754d35, 2, new Vector2(20, 77), new Vector2(80, 77));
        }

        private void EnergyGel()
        {
            Box(0x6c5e32, 40, 8, 20, 10); Box(0xb3aa7f, 43, 18, 14, 7);
            Shape(0xa78431, new Vector2(34, 25), new Vector2(66, 25), new Vector2(77, 82), new Vector2(71, 89), new Vector2(29, 89), new Vector2(23, 82));
            Shape(0xddbb62, new Vector2(38, 28), new Vector2(61, 28), new Vector2(69, 79), new Vector2(31, 79));
            Shape(0x403e32, new Vector2(54, 35), new Vector2(40, 56), new Vector2(50, 56), new Vector2(45, 73), new Vector2(62, 49), new Vector2(51, 49));
            Line(0xf2ddb0, 2, new Vector2(36, 34), new Vector2(30, 74));
        }

        private void ProteinBar()
        {
            Shape(0x634d40, new Vector2(10, 32), new Vector2(89, 27), new Vector2(93, 68), new Vector2(13, 74));
            Shape(0xa17652, new Vector2(19, 33), new Vector2(80, 29), new Vector2(84, 68), new Vector2(22, 72));
            Shape(0xd7c59a, new Vector2(32, 32), new Vector2(65, 30), new Vector2(69, 69), new Vector2(36, 72));
            Line(0x746543, 3, new Vector2(42, 43), new Vector2(60, 42));
            Line(0x746543, 3, new Vector2(43, 52), new Vector2(59, 51));
            Ring(0x746543, 2, 51, 62, 4);
            for (int y = 36; y < 68; y += 7) { Box(0xc5aa80, 14, y, 5, 2); Box(0xc5aa80, 84, y - 4, 5, 2); }
        }

        private void Electrolyte()
        {
            Shape(0x366f88, new Vector2(21, 16), new Vector2(78, 16), new Vector2(73, 85), new Vector2(27, 85));
            Box(0x98c9ce, 23, 18, 53, 8); Box(0xc7dfd6, 29, 32, 42, 41);
            Shape(0x4a91ae, new Vector2(50, 36), new Vector2(63, 53), new Vector2(63, 62), new Vector2(57, 68), new Vector2(43, 68), new Vector2(37, 62), new Vector2(37, 53));
            Line(0xe5f3df, 2, new Vector2(43, 53), new Vector2(43, 60), new Vector2(47, 63));
            Line(0x8cb7bb, 2, new Vector2(29, 80), new Vector2(70, 80));
        }

        private void OxygenReserve()
        {
            Box(0x405b50, 43, 9, 14, 13); Box(0xa2b8a7, 31, 5, 38, 6);
            Shape(0x365d4f, new Vector2(37, 21), new Vector2(63, 21), new Vector2(70, 30), new Vector2(70, 86), new Vector2(64, 94), new Vector2(36, 94), new Vector2(30, 86), new Vector2(30, 30));
            Box(0x75a58d, 35, 30, 28, 56); Box(0xb9d1b4, 37, 33, 5, 48);
            Box(0xd6dac0, 28, 40, 44, 9); Box(0xd6dac0, 28, 72, 44, 9);
            Ring(0xe7e9d1, 2, 51, 60, 8); Ring(0xe7e9d1, 2, 51, 60, 3);
            Ring(0xcbd4ba, 2, 72, 23, 8); Line(0xe8b769, 2, new Vector2(72, 23), new Vector2(75, 19));
        }

        private void ScoutRig()
        {
            Box(0xa18e62, 27, 13, 10, 33); Box(0xa18e62, 63, 13, 10, 33);
            Shape(0x7d7251, new Vector2(18, 44), new Vector2(81, 44), new Vector2(77, 79), new Vector2(22, 79));
            for (int x = 24; x <= 64; x += 20)
            { Box(0xb3a172, x, 47, 16, 26); Box(0xd0bf90, x, 47, 16, 7); Box(0x655e48, x + 6, 55, 4, 6); }
            Line(0xd7cba6, 2, new Vector2(29, 15), new Vector2(29, 38));
        }

        private void FieldRig()
        {
            Box(0x688077, 21, 10, 16, 26); Box(0x688077, 63, 10, 16, 26);
            Shape(0x3c5751, new Vector2(18, 32), new Vector2(82, 32), new Vector2(88, 88), new Vector2(12, 88));
            for (int y = 37; y <= 62; y += 25)
                for (int x = 20; x <= 64; x += 22)
                { Box(0x829589, x, y, 17, 21); Box(0xaabb9e, x, y, 17, 6); Box(0x344d46, x + 7, y + 7, 4, 7); }
            Box(0xc7c9b1, 44, 24, 12, 7);
        }

        private void SlingPack()
        {
            Line(0x3d6269, 9, new Vector2(27, 55), new Vector2(29, 17), new Vector2(70, 71));
            Shape(0x416b74, new Vector2(39, 37), new Vector2(63, 30), new Vector2(78, 71), new Vector2(63, 88), new Vector2(30, 82), new Vector2(27, 55));
            Shape(0x7ca4a4, new Vector2(42, 42), new Vector2(60, 38), new Vector2(67, 57), new Vector2(35, 68), new Vector2(33, 55));
            Line(0xd1d0b5, 2, new Vector2(37, 66), new Vector2(66, 56));
            Box(0xd1d0b5, 61, 59, 4, 9); Box(0x294b54, 43, 72, 16, 5);
        }

        private void CargoPack()
        {
            Line(0x65716b, 5, new Vector2(22, 87), new Vector2(22, 12), new Vector2(78, 12), new Vector2(78, 87));
            Box(0x6f5741, 24, 17, 52, 73); Box(0xb18456, 29, 17, 42, 64);
            Box(0xc7a478, 28, 18, 44, 21); Box(0x8b6949, 30, 47, 40, 28);
            Box(0x947853, 12, 43, 16, 36); Box(0x947853, 72, 43, 16, 36);
            Box(0x3b514c, 35, 23, 6, 63); Box(0x3b514c, 60, 23, 6, 63);
            Box(0xc4c8ac, 33, 39, 10, 9); Box(0xc4c8ac, 58, 39, 10, 9);
            Line(0xd6c199, 2, new Vector2(15, 52), new Vector2(25, 52));
            Line(0xd6c199, 2, new Vector2(75, 52), new Vector2(85, 52));
        }

        private void CircuitBoard()
        {
            Box(0x244b3f, 16, 18, 68, 65); Box(0x3e7660, 20, 22, 60, 57);
            for (int x = 27; x < 78; x += 10) Box(0xd3b369, x, 78, 5, 9);
            Box(0x253c37, 40, 36, 24, 23); Box(0x879a7d, 43, 39, 18, 17);
            Line(0xb4b979, 2, new Vector2(23, 32), new Vector2(33, 32), new Vector2(33, 46), new Vector2(40, 46));
            Line(0xb4b979, 2, new Vector2(64, 48), new Vector2(73, 48), new Vector2(73, 66), new Vector2(53, 66));
            Box(0x273e34, 25, 59, 9, 12); Box(0xc5cba3, 67, 27, 7, 8);
        }

        private void CopperCoil()
        {
            Ring(0x58473a, 13, 50, 51, 28);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30 * Mathf.Deg2Rad;
                var inner = new Vector2(50 + 21 * Mathf.Cos(angle), 51 + 21 * Mathf.Sin(angle));
                var outer = new Vector2(50 + 35 * Mathf.Cos(angle), 51 + 35 * Mathf.Sin(angle));
                Line(i < 6 ? 0xc68b58u : 0xa36c45u, 6, inner, outer);
            }
            Ring(0xe5b879, 1.5f, 50, 51, 22);
            Line(0xc68b58, 4, new Vector2(66, 76), new Vector2(78, 88), new Vector2(91, 88));
            Line(0xc68b58, 4, new Vector2(26, 35), new Vector2(15, 23), new Vector2(9, 23));
        }

        private void CeramicPlate()
        {
            Shape(0x777c72, new Vector2(20, 29), new Vector2(70, 16), new Vector2(89, 34), new Vector2(82, 77), new Vector2(32, 90), new Vector2(12, 72));
            Shape(0xd5d3ba, new Vector2(20, 24), new Vector2(70, 11), new Vector2(89, 29), new Vector2(82, 72), new Vector2(32, 85), new Vector2(12, 67));
            Shape(0xefead4, new Vector2(24, 30), new Vector2(68, 19), new Vector2(80, 33), new Vector2(74, 66), new Vector2(34, 77), new Vector2(21, 64));
            Line(0xb4b5a0, 2, new Vector2(33, 28), new Vector2(45, 74));
            Line(0xb4b5a0, 2, new Vector2(52, 23), new Vector2(64, 69));
            Line(0xb4b5a0, 2, new Vector2(22, 48), new Vector2(78, 34));
            Line(0xb4b5a0, 2, new Vector2(25, 64), new Vector2(76, 50));
        }

        private void OpticalSensor()
        {
            Box(0x596a6e, 13, 30, 41, 42); Box(0x819190, 13, 30, 41, 9);
            Box(0x354b54, 43, 26, 34, 49); Box(0x182e3c, 54, 23, 25, 55);
            Ring(0x748e93, 7, 68, 50, 22); Ring(0x66aab5, 5, 68, 50, 15); Ring(0xb5ded3, 2, 68, 50, 8);
            Line(0xdde6cf, 3, new Vector2(60, 39), new Vector2(65, 36));
            Box(0xcab674, 18, 61, 18, 6); Box(0x324c51, 22, 75, 10, 10);
        }

        private void DataDrive()
        {
            Box(0x373d46, 23, 18, 54, 64); Box(0x8a5b57, 27, 22, 46, 56);
            Box(0xc5c5ab, 32, 29, 36, 23); Box(0x4b5150, 36, 34, 27, 4);
            Box(0x737b6f, 36, 43, 18, 3); Box(0x483b3c, 33, 58, 34, 13);
            for (int x = 32; x < 69; x += 8) Box(0xd7b573, x, 80, 5, 8);
            Ring(0xd5c6a7, 1.5f, 29, 24, 2); Ring(0xd5c6a7, 1.5f, 70, 74, 2);
        }

        private void HeliumSample()
        {
            Box(0x526774, 35, 10, 30, 16); Box(0xb4c5c1, 39, 26, 22, 8);
            Shape(0x557d95, new Vector2(38, 32), new Vector2(62, 32), new Vector2(72, 46), new Vector2(68, 86), new Vector2(32, 86), new Vector2(28, 46));
            Box(0x9bc4cb, 36, 43, 28, 33); Box(0xcfe4d8, 38, 46, 5, 26);
            Ring(0xe9e2b1, 2, 51, 59, 10); Ring(0xe9e2b1, 2, 51, 59, 3);
            Box(0xc9b57c, 33, 79, 34, 7); Box(0x2e536e, 39, 14, 22, 5);
        }

        private void QuantumCore()
        {
            for (int i = 25; i < 80; i += 10)
            { Box(0xc9b37c, i, 14, 5, 10); Box(0xc9b37c, i, 77, 5, 10); Box(0xc9b37c, 14, i, 10, 5); Box(0xc9b37c, 77, i, 10, 5); }
            Box(0x443f62, 23, 23, 54, 54); Box(0x8675a5, 28, 28, 44, 44);
            Shape(0xb7cfd0, new Vector2(50, 33), new Vector2(67, 50), new Vector2(50, 67), new Vector2(33, 50));
            Shape(0x435f77, new Vector2(50, 40), new Vector2(60, 50), new Vector2(50, 60), new Vector2(40, 50));
            Box(0xdbdfb9, 47, 47, 6, 6);
        }

        private void BlackBox()
        {
            Line(0x384343, 4, new Vector2(70, 32), new Vector2(79, 12), new Vector2(85, 14));
            Shape(0x5b4738, new Vector2(17, 30), new Vector2(70, 23), new Vector2(87, 37), new Vector2(87, 74), new Vector2(31, 84), new Vector2(14, 69));
            Shape(0xd08a43, new Vector2(19, 30), new Vector2(69, 25), new Vector2(82, 37), new Vector2(30, 44));
            Shape(0xb66c35, new Vector2(30, 44), new Vector2(82, 37), new Vector2(82, 70), new Vector2(30, 79));
            Box(0x364545, 35, 45, 7, 31); Box(0x364545, 68, 41, 7, 31);
            Box(0xe7dfbf, 46, 49, 17, 15); Box(0x4d5146, 50, 53, 9, 3);
            Line(0xefd5a2, 2, new Vector2(25, 33), new Vector2(65, 29));
        }
    }
}
