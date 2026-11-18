// 按战局剩余时间提醒玩家回撤离点。错过早期提醒时，只显示当前更紧急的一条。能否撤离仍由服务器进度决定。
namespace Unity.MP_FPS {
    // 只提示玩家。到期和撤离是否成功仍以服务器快照为准。
    public sealed class RaidReturnGuidance {
        private int m_Raid, m_Announced;
        private float m_RevealUntil;
        public int Stage { get; private set; }
        public bool ShowBeacon { get; private set; }

        public void Reset() {
            m_Raid = m_Announced = Stage = 0;
            m_RevealUntil = 0;
            ShowBeacon = false;
        }

        public bool Update(int raid, float seconds, bool active, bool extracting, bool canPresent, float now) {
            // 每一局有自己的提醒记录。局号变化就允许新局从头提醒，不沿用上一局响过的最后一分钟。
            if (raid != m_Raid) {
                Reset();
                m_Raid = raid;
            }
            Stage = active && raid > 0 && seconds > 0 && !float.IsInfinity(seconds)
                ? seconds <= 60 ? 3 : seconds <= 120 ? 2 : seconds <= 300 ? 1 : 0
                : 0;
            // HUD 隐藏时只保留最紧急的一次提醒。过期的早期提醒无需排队补发。
            // 只有提醒变得更紧急且当前有地方显示时才触发。正在撤离不需要再叫玩家返回。
            bool announce = Stage > m_Announced && canPresent && !extracting;
            if (announce) {
                m_Announced = Stage;
                m_RevealUntil = now + 8;
            }
            // 早期提醒只展开几秒。时间更紧时持续显示方向，也不会不断重播声音。
            ShowBeacon = Stage >= 2 || (Stage > 0 && now < m_RevealUntil);
            return announce;
        }

        public string BeaconText =>
            Stage == 3 ? "FINAL MINUTE\nGREEN BEACON / {0:F0}m {1}" :
            Stage == 2 ? "RETURN TO EXTRACTION\nGREEN BEACON / {0:F0}m {1}" :
            "PREPARE TO RETURN\nGREEN BEACON / {0:F0}m {1}";
    }
}