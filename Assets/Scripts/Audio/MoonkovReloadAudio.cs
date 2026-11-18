// 从服务器给的装填开始 tick、当前弹量和进度推断何时开播、停播或提示装满。同一次装填的进度只承认新到达的阶段。
namespace Unity.MP_FPS {
    [System.Flags]
    public enum ReloadAudioEvent {
        None = 0,
        Start = 1,
        Stop = 2,
        Ready = 4
    }

    public enum ReloadSoundPhase {
        None,
        Open,
        Feed,
        Lock
    }

    // 极短的装填可能在两个渲染帧之间整段经过。装填 tick 与弹药增长仍能确认完成。
    public sealed class MoonkovReloadAudio {
        private bool m_PhaseInitialized;
        private uint m_PhaseTick, m_PhaseWeapon;
        private int m_PhaseIndex;

        // 在预测之外读取服务端的动作时间。如果跳过一帧，
        // 只播最近一个机构声，避免把中间错过的声音一次播完。
        public ReloadSoundPhase ObservePhase(in PredictedPlayerGhost state, WeaponData weapon) {
            // 较旧快照直接跳过，保持已记录的时间顺序。
            if (m_PhaseInitialized && state.LastReloadTick != m_PhaseTick && m_PhaseTick != 0 &&
                unchecked((int)(state.LastReloadTick - m_PhaseTick)) <= 0)
                return ReloadSoundPhase.None;
            // 用正在补的弹量决定中间要划几段，再用总进度划分开盖、进弹和锁定。能量武器才能用这些同步进度播放对应瞬间。
            int pulses = UnityEngine.Mathf.Clamp(state.ReloadTargetAmmo - state.CurrentAmmo, 1, 6);
            float progress = HaloReload.Progress(state, weapon);
            int index = !state.ControllerState.IsReloadingState || state.CurrentHealth <= 0 ? -1 :
                progress < .18f ? 0 :
                progress >= .84f ? pulses + 1 :
                1 + UnityEngine.Mathf.Min(pulses - 1, UnityEngine.Mathf.FloorToInt((progress - .18f) / .66f * pulses));
            // 同一次装填要越过已播过的阶段才会响。快照回退后仍记最高进度，漏掉几帧时直接跟到新阶段。
            bool sameReload = state.LastReloadTick == m_PhaseTick && state.EquippedWeaponID == m_PhaseWeapon;
            bool changed = m_PhaseInitialized && index >= 0 && (sameReload
                ? index > m_PhaseIndex
                : state.LastReloadTick != 0 && state.LastReloadTick != m_PhaseTick);
            // 即使收到重复或修正过的状态，也保留已经观测到的最大序号。
            m_PhaseIndex = sameReload ? UnityEngine.Mathf.Max(m_PhaseIndex, index) : index;
            m_PhaseInitialized = true;
            m_PhaseTick = state.LastReloadTick;
            m_PhaseWeapon = state.EquippedWeaponID;
            return !changed ? ReloadSoundPhase.None :
                index == 0 ? ReloadSoundPhase.Open :
                index == pulses + 1 ? ReloadSoundPhase.Lock : ReloadSoundPhase.Feed;
        }

        public static UnityEngine.Vector2 Window(SoundDef sound, ReloadSoundPhase phase) {
            float length = sound && sound.Clips != null && sound.Clips.Count > 0 && sound.Clips[0]
                ? sound.Clips[0].length
                : 1;
            // 这组录音的动作点在 3%、28%、57% 和 84%。
            // 受伤或模块改变动作时长时，播放仍保留原录音音高。
            // 装填录音包含好几个动作。这里只取对应片段的开始比例和持续时间，让伤势与模块改快慢时仍听得到原本的机械声。
            return phase switch {
                ReloadSoundPhase.Open => new UnityEngine.Vector2(0, UnityEngine.Mathf.Min(.28f, length * .24f)),
                ReloadSoundPhase.Feed => new UnityEngine.Vector2(.27f, UnityEngine.Mathf.Min(.2f, length * .22f)),
                ReloadSoundPhase.Lock => new UnityEngine.Vector2(.83f, length * .17f),
                _ => UnityEngine.Vector2.zero
            };
        }

        // 同一组的各个版本都等长。播放速度跟随服务端确认的剩余时间。
        public static float PitchForDuration(SoundDef sound, float remaining) {
            if (!sound || sound.Clips == null || sound.Clips.Count == 0 || !sound.Clips[0] ||
                !float.IsFinite(remaining) || remaining <= .05f)
                return 0;
            // 连续的换弹音可以调播放速率来贴合剩余动作时间。之后把倍率换成 SoundDef 使用的音分。
            float rate = UnityEngine.Mathf.Clamp(sound.Clips[0].length / remaining, .25f, 3f);
            return 1200 * UnityEngine.Mathf.Log(rate, 2);
        }

        private bool m_Initialized, m_Reloading;
        private uint m_Tick, m_Weapon;
        private int m_Ammo;

        public ReloadAudioEvent Observe(uint tick, uint weapon, int ammo, bool reloading, bool alive) {
            var result = ReloadAudioEvent.None;
            if (m_Initialized) {
                // 装填和结束有时在两帧消息之间全经过。只要看到新 tick 且同一把枪弹量升了，就仍能响装填就绪。
                bool started = MoonkovPlayerAudio.NewTick(tick, ref m_Tick);
                bool same = weapon == m_Weapon;
                if (started || !same || !alive || (m_Reloading && !reloading)) result |= ReloadAudioEvent.Stop;
                if (started && reloading && alive) result |= ReloadAudioEvent.Start;
                if (same && alive && (m_Reloading || started) && !reloading && ammo > m_Ammo)
                    result |= ReloadAudioEvent.Ready;
            } else {
                m_Initialized = true;
                m_Tick = tick;
            }
            m_Weapon = weapon;
            m_Ammo = ammo;
            m_Reloading = reloading;
            return result;
        }
    }
}