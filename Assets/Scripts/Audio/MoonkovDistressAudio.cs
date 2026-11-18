// MoonkovPlayerAudio 用这段逻辑处理长时间的不适。低血量、缺氧和身体异常可以触发低频呻吟，短促受击声结束后再给呻吟时机。
namespace Unity.MP_FPS {
    // 持续负伤时低频提示痛苦状态。每个伤害 tick 无需单独发声。
    public sealed class MoonkovDistressAudio {
        public const float FirstDelay = 3.5f, RepeatDelay = 9f;
        public bool Active { get; private set; }
        private float m_Next;

        public static bool PainfulStatus(in PredictedPlayerGhost state) {
            // 缺氧、饥渴、部位失去功能等持续状态都能让角色发出不适声。这段逻辑只读取玩家状态作为声音的输入。
            const SurvivalEffects painful = SurvivalEffects.Hypoxia | SurvivalEffects.Starving |
                                            SurvivalEffects.Dehydrated | SurvivalEffects.ArmDisabled |
                                            SurvivalEffects.LegDisabled | SurvivalEffects.AbdomenDisabled;
            return (RaidNutrition.Effects(state) & painful) != 0 || state.TacticalBleeding ||
                   state.TacticalPoisonSeconds > 0 || state.TacticalWireSeconds > 0;
        }

        public bool Observe(float healthFraction, bool alive, bool painfulStatus, bool hurtCry, float now) {
            // 已经在呻吟的角色需要恢复到更高的血量才退出。两个进入和退出阈值分开后，轻微血量波动不会频繁启停。
            bool active = alive && (painfulStatus || healthFraction <= (Active ? .35f : .28f));
            if (!active) {
                Reset();
                return false;
            }
            if (!Active) {
                Active = true;
                m_Next = now + FirstDelay;
            }
            // 让短促的中弹喊声先播完。有出血痛声时稍后接上，不会整段都把它压住。
            // 短促受击声优先，持续的不适稍后再响。每次呻吟之后用更长的间隔安排下一声。
            if (hurtCry) m_Next = now + FirstDelay;
            if (now < m_Next) return false;
            m_Next = now + RepeatDelay;
            return true;
        }

        public void Reset() {
            Active = false;
            m_Next = 0;
        }
    }
}