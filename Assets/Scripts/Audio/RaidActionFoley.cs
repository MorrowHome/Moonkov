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
        private SoundSystem.SoundInfo openSound, contactSound, closeSound;

        public void Observe(HandAction kind, uint currentSequence, float phase, float contact, string code,
            Vector3 position, bool owner, bool active,float openMarker=.08f)
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
                    kind == HandAction.Drink || kind == HandAction.Treat && code != "oxygen_canister" && code != "oxygen_reserve" ? library.PacketOpen : null;
                openSound = MoonkovAudio.PlayWeaponLayer(sound, position, 1, owner ? 0 : 1);
            }
            if(kind==HandAction.Drink&&code=="water"&&Crossed(4,.74f))closeSound=MoonkovAudio.PlayWeaponLayer(library.BottleOpen,position,1,owner?0:1);
            if(kind==HandAction.RefillOxygen&&Crossed(4,.82f))closeSound=MoonkovAudio.PlayWeaponLayer(library.Equipment,position,.7f,owner?0:1);
            if (Crossed(2, Mathf.Clamp(contact, .28f, .72f)))
            {
                var sound = kind == HandAction.Eat ? library.EatBite : kind == HandAction.Drink ? library.DrinkSip :
                    kind == HandAction.Inject ? library.InjectorPress : kind == HandAction.Treat ? library.Cloth : null;
                contactSound = MoonkovAudio.PlayWeaponLayer(sound, position, 1, owner ? 0 : 1);
            }
            previous = Mathf.Max(previous, phase);
        }
        public void Stop()
        {
            var system = GameManager.Instance ? GameManager.Instance.SoundSystem : null;
            system?.Stop(openSound, .06f); system?.Stop(contactSound, .06f);system?.Stop(closeSound,.06f);
            openSound = contactSound = closeSound = null; observed = false; played = 0;
        }
    }
}
