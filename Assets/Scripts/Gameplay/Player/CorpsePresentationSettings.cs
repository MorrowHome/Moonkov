// 尸体表现的共享配置资产。CorpseVisual 根据角色编号取对应模型，并使用指定的死亡片段来还原姿势。
using UnityEngine;

namespace Unity.MP_FPS {
    [CreateAssetMenu(menuName = "Moonkov/Corpse Presentation")]
    public sealed class CorpsePresentationSettings : ScriptableObject {
        public GameObject[] CharacterVisuals;
        public AnimationClip Death;
    }
}