namespace Unity.MP_FPS
{
    // Presentation only. Server snapshots remain the authority for expiry and extraction.
    public sealed class RaidReturnGuidance
    {
        private int m_Raid, m_Announced;
        private float m_RevealUntil;
        public int Stage { get; private set; }
        public bool ShowBeacon { get; private set; }

        public void Reset()
        {
            m_Raid = m_Announced = Stage = 0;
            m_RevealUntil = 0;
            ShowBeacon = false;
        }

        public bool Update(int raid, float seconds, bool active, bool extracting, bool canPresent, float now)
        {
            if (raid != m_Raid) { Reset(); m_Raid = raid; }
            Stage = active && raid > 0 && seconds > 0 && !float.IsInfinity(seconds)
                ? seconds <= 60 ? 3 : seconds <= 120 ? 2 : seconds <= 300 ? 1 : 0 : 0;
            // A hidden HUD keeps only the most urgent reminder; never queue obsolete warnings.
            bool announce = Stage > m_Announced && canPresent && !extracting;
            if (announce) { m_Announced = Stage; m_RevealUntil = now + 8; }
            ShowBeacon = Stage >= 2 || Stage > 0 && now < m_RevealUntil;
            return announce;
        }

        public string BeaconText => Stage == 3 ? "FINAL MINUTE\nGREEN BEACON / {0:F0}m {1}"
            : Stage == 2 ? "RETURN TO EXTRACTION\nGREEN BEACON / {0:F0}m {1}"
            : "PREPARE TO RETURN\nGREEN BEACON / {0:F0}m {1}";
    }
}
