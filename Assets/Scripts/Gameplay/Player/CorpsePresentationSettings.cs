// 尸体表现的共享配置资产。CorpseVisual 根据角色编号取对应模型，并使用指定的死亡片段来还原姿势。
using UnityEngine;

namespace Unity.MP_FPS {
    [CreateAssetMenu(menuName = "Moonkov/Corpse Presentation")]
    public sealed class CorpsePresentationSettings : ScriptableObject {
        // 角色编号对应数组里的外观。Death 是用来在场景里保存最后姿势的片段。
        public GameObject[] CharacterVisuals;
        public AnimationClip Death;
    }
}