// RaidActionPresentation 提供手部动作种类、这一轮的序号和播放进度。这里在进度越过标记时播开盖、吞咽、按针或接氧等声音。
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>渲染动作跨过音效时间点时只播一次。刷新快照或中途加入不会补播旧声音。</summary>
    public sealed class RaidActionFoley {
        private uint sequence;
        private HandAction action;
        private float previous;
        private bool observed;
        private int played;
        private SoundSystem.SoundInfo openSound, contactSound, closeSound, flowSound;
        private float flowRetryAt;

        public void Observe(
            HandAction kind,
            uint currentSequence,
            float phase,
            float contact,
            string code,
            Vector3 position,
            bool owner,
            bool active,
            float openMarker = .08f,
            float contactOut = .64f,
            Transform follow = null) {
            if (!active || kind == HandAction.None) {
                Stop();
                return;
            }
            // 动作种类或序号变了就从头跟踪。首次看到时已过半的动作以当时进度作起点，避免补响早就过去的开盖声。
            if (!observed || sequence != currentSequence || action != kind) {
                Stop();
                observed = true;
                sequence = currentSequence;
                action = kind;
                // 刚收到已经进行到一半的动作时，声音也从对应时刻开始。
                previous = phase < .12f ? -.001f : phase;
            }
            var library = MoonkovAudio.Library;
            if (!library) return;
            code = code ?? string.Empty;
            // 医疗品也分操作方式。止血包需要一段包扎摩擦声，其他治疗品可以只播开封和接触时的音。
            bool wrapping = kind == HandAction.Treat &&
                            (code == "trauma_kit" || code == "hemostatic_bandage" || code == "rescue_kit");

            SoundSystem.SoundInfo Play(SoundDef sound, float gain = 1) {
                return MoonkovAudio.PlayWeaponLayer(sound, position, gain, owner ? 0 : 1, follow: follow);
            }

            // 记住这一轮已经响过的标记。网络重复给同一段进度时可以接着播动作，声音仍只响一次。
            bool Crossed(int marker, float at) {
                if ((played & marker) != 0 || phase < at || previous >= at) return false;
                played |= marker;
                return true;
            }

            if (Crossed(1, openMarker > 0 ? openMarker : .08f)) {
                var sound = kind == HandAction.RefillOxygen ? library.OxygenConnect :
                    kind == HandAction.Inject ? library.InjectorCap :
                    kind == HandAction.Drink && code == "water" ? library.BottleOpen :
                    (kind == HandAction.Eat && !code.StartsWith("alien_", System.StringComparison.Ordinal)) ||
                    kind == HandAction.Drink ? library.PacketOpen :
                    wrapping ? library.MedicalOpen :
                    kind == HandAction.Treat ? library.PacketOpen : null;
                openSound = Play(sound);
            }
            if (kind == HandAction.Drink && code == "water" && Crossed(4, .74f)) closeSound = Play(library.BottleClose);
            if (kind == HandAction.RefillOxygen && Crossed(4, .82f)) closeSound = Play(library.OxygenDisconnect, .8f);
            // 接触时机跟角色动作配置走，再限制在动作中间。针剂、吃喝和药膏就能在手碰到正确的位置时响。
            float contactAt = Mathf.Clamp(contact, .28f, .72f);
            bool firstContact = Crossed(2, contactAt);
            var mouth = kind == HandAction.Eat && !code.Contains("gel") &&
                        !code.StartsWith("alien_", System.StringComparison.Ordinal)
                ? library.EatBite
                : library.DrinkSip;
            if (firstContact) {
                var sound = kind == HandAction.Eat || kind == HandAction.Drink ? mouth :
                    kind == HandAction.Inject ? library.InjectorPress :
                    kind == HandAction.Treat && !wrapping ? library.MedicalGel : null;
                contactSound = Play(sound);
            }
            // 第二次嘴部动作应处在接触保持段内，收起物品的阶段不用再出声。
            if ((kind == HandAction.Eat || kind == HandAction.Drink) &&
                Crossed(8, Mathf.Lerp(contactAt, Mathf.Max(contactAt, contactOut), .65f)) && !firstContact &&
                phase < contactOut) {
                GameManager.Instance?.SoundSystem?.Stop(contactSound, .04f);
                contactSound = Play(mouth, .85f);
            }
            // 供氧和包扎持续一小段时间，所以维护循环声来表现连续的声音。离开该段或中止动作时一起收回。
            bool flowing = (kind == HandAction.RefillOxygen && phase >= contactAt && phase < .82f) ||
                           (wrapping && phase >= .28f && phase < .88f);
            if (!flowing) {
                GameManager.Instance?.SoundSystem?.Stop(flowSound, .06f);
                flowSound = null;
            } else if (flowSound?.Handle?.IsValid() != true && Time.unscaledTime >= flowRetryAt) {
                flowSound = Play(wrapping ? library.MedicalHandling : library.OxygenCanisterFlow);
                flowRetryAt = Time.unscaledTime + .25f;
            }
            previous = Mathf.Max(previous, phase);
        }

        public void Stop() {
            var system = GameManager.Instance ? GameManager.Instance.SoundSystem : null;
            system?.Stop(openSound, .06f);
            system?.Stop(contactSound, .06f);
            system?.Stop(closeSound, .06f);
            system?.Stop(flowSound, .06f);
            openSound = contactSound = closeSound = flowSound = null;
            observed = false;
            played = 0;
            flowRetryAt = 0;
        }
    }
}