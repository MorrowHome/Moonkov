using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName="Moonkov/Corpse Presentation")]
    public sealed class CorpsePresentationSettings : ScriptableObject
    {
        public GameObject[] CharacterVisuals;
        public AnimationClip Death;
    }
}
