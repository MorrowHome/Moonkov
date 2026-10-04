using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName = "Moonkov/Audio Library")]
    public sealed class MoonkovAudioLibrary : ScriptableObject
    {
        public SoundDef Click, Hover, Confirm, Error, Notification;
        public SoundDef Equipment, Container, Jump, Land, Hit, Death;
        public SoundDef RegolithFootsteps, MetalFootsteps, Cloth;
        public SoundDef ShipInterior;
        // Layered deployment confirmation (success gong + android voice).
        public SoundDef DeployConfirm;
    }
}
