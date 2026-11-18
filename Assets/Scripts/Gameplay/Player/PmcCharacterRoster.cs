// 服务端为人形敌人挑外观角色。可用角色先打乱，每个轮到一次才重新洗牌，让同局外观不扎堆。
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.MP_FPS {
    // A shuffled bag gives each available character one turn before repeating.
    // It is server-only and does not tie a character to a fixed AI personality.
    public sealed class PmcCharacterRoster {
        private readonly int[] m_Order = new int[3];
        private int m_Count, m_Next;
        public void Reset() { m_Count = m_Next = 0; }

        // 将角色类型编号映射到该角色的 ECS 预制体。服务器可据此给不同阵营或席位生成外观。
        public static Entity Prefab(in PlayerEntityPrefabs prefabs, int characterIndex) {
            return characterIndex switch {
                GameSettings.HatsuneMikuCharacterIndex => prefabs.HatsuneMikuEntityPrefab,
                GameSettings.ShianCharacterIndex => prefabs.ShianEntityPrefab,
                _ => prefabs.DollSingerEntityPrefab
            };
        }

        public bool TryChoose(
            in PlayerEntityPrefabs prefabs,
            ref Random random,
            out Entity prefab,
            out int characterIndex) {
            prefab = Entity.Null;
            characterIndex = GameSettings.DollSingerCharacterIndex;
            if (m_Next >= m_Count) {
                m_Count = m_Next = 0;
                // 本轮只收场景实际加载过的角色资源。某个角色预制体缺失时，剩下几个仍能参加。
                for (int index = GameSettings.DollSingerCharacterIndex;
                     index <= GameSettings.ShianCharacterIndex;
                     index++)
                    if (Prefab(prefabs, index) != Entity.Null)
                        m_Order[m_Count++] = index;
                if (m_Count == 0) return false;
                for (int i = m_Count - 1; i > 0; i--) {
                    int other = random.NextInt(i + 1);
                    (m_Order[i], m_Order[other]) = (m_Order[other], m_Order[i]);
                }
            }
            // 每取一个轮到下一项，用尽之后再打乱。用同一个角色不强制绑定哪类行为或武器。
            characterIndex = m_Order[m_Next++];
            prefab = Prefab(prefabs, characterIndex);
            return prefab != Entity.Null;
        }
    }
}