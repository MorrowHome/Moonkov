using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Crossed presentation markers play once. Snapshot refreshes and late joins never replay old markers.</summary>
    public sealed class RaidActionFoley
    {
        private uint sequence;
        private HandAction action;
        private float previous;
        private bool observed;
        private int played;
        private SoundSystem.SoundInfo openSound, contactSound, closeSound, flowSound;
        private float flowRetryAt;

        public void Observe(HandAction kind, uint currentSequence, float phase, float contact, string code,
            Vector3 position, bool owner, bool active,float openMarker=.08f, float contactOut=.64f, Transform follow=null)
        {
            if (!active || kind == HandAction.None) { Stop(); return; }
            if (!observed || sequence != currentSequence || action != kind)
            {
                Stop(); observed = true; sequence = currentSequence; action = kind;
                // A snapshot arriving midway through an action must stay midway through its sound track.
                previous = phase < .12f ? -.001f : phase;
            }
            var library = MoonkovAudio.Library;
            if (!library) return;
            code = code ?? string.Empty;
            SoundSystem.SoundInfo Play(SoundDef sound, float gain=1) =>
                MoonkovAudio.PlayWeaponLayer(sound, position, gain, owner ? 0 : 1, follow: follow);
            bool Crossed(int marker, float at)
            {
                if ((played & marker) != 0 || phase < at || previous >= at) return false;
                played |= marker; return true;
            }
            if (Crossed(1, openMarker>0?openMarker:.08f))
            {
                var sound = kind == HandAction.RefillOxygen ? library.Equipment : kind == HandAction.Inject ? library.InjectorCap :
                    kind == HandAction.Drink && code == "water" ? library.BottleOpen :
                    kind == HandAction.Eat && !code.StartsWith("alien_", System.StringComparison.Ordinal) ||
                    kind == HandAction.Drink ? library.PacketOpen : kind == HandAction.Treat ? library.MedicalHandling : null;
                openSound = Play(sound);
            }
            if(kind==HandAction.Drink&&code=="water"&&Crossed(4,.74f))closeSound=Play(library.BottleClose);
            if(kind==HandAction.RefillOxygen&&Crossed(4,.82f))closeSound=Play(library.Equipment,.8f);
            float contactAt = Mathf.Clamp(contact, .28f, .72f);
            bool firstContact = Crossed(2, contactAt);
            SoundDef mouth = kind == HandAction.Eat && !code.Contains("gel") && !code.StartsWith("alien_", System.StringComparison.Ordinal)
                ? library.EatBite : library.DrinkSip;
            if (firstContact)
            {
                var sound = kind == HandAction.Eat || kind == HandAction.Drink ? mouth :
                    kind == HandAction.Inject ? library.InjectorPress : kind == HandAction.Treat ? library.MedicalHandling : null;
                contactSound = Play(sound);
            }
            // A second mouth movement belongs inside the authored contact hold, never the put-away segment.
            if ((kind == HandAction.Eat || kind == HandAction.Drink) &&
                Crossed(8, Mathf.Lerp(contactAt, Mathf.Max(contactAt, contactOut), .65f)) && !firstContact && phase < contactOut)
            {
                GameManager.Instance?.SoundSystem?.Stop(contactSound, .04f);
                contactSound = Play(mouth, .85f);
            }
            bool flowing = kind == HandAction.RefillOxygen && phase >= contactAt && phase < .82f;
            if (!flowing) { GameManager.Instance?.SoundSystem?.Stop(flowSound, .06f); flowSound = null; }
            else if (flowSound?.Handle?.IsValid() != true && Time.unscaledTime >= flowRetryAt)
            {
                flowSound = Play(library.OxygenCanisterFlow);
                flowRetryAt = Time.unscaledTime + .25f;
            }
            previous = Mathf.Max(previous, phase);
        }
        public void Stop()
        {
            var system = GameManager.Instance ? GameManager.Instance.SoundSystem : null;
            system?.Stop(openSound, .06f); system?.Stop(contactSound, .06f);system?.Stop(closeSound,.06f);
            system?.Stop(flowSound, .06f);
            openSound = contactSound = closeSound = flowSound = null; observed = false; played = 0; flowRetryAt = 0;
        }
    }
}
