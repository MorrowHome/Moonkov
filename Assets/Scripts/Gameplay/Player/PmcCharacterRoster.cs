using Unity.Entities;
using Unity.Mathematics;

namespace Unity.MP_FPS
{
    // A shuffled bag gives each available character one turn before repeating.
    // It is server-only and does not tie a character to a fixed AI personality.
    public sealed class PmcCharacterRoster
    {
        private readonly int[] m_Order = new int[3];
        private int m_Count, m_Next;

        public void Reset() { m_Count = m_Next = 0; }

        public static Entity Prefab(in PlayerEntityPrefabs prefabs, int characterIndex) => characterIndex switch
        {
            GameSettings.HatsuneMikuCharacterIndex => prefabs.HatsuneMikuEntityPrefab,
            GameSettings.ShianCharacterIndex => prefabs.ShianEntityPrefab,
            _ => prefabs.DollSingerEntityPrefab
        };

        public bool TryChoose(in PlayerEntityPrefabs prefabs, ref Random random, out Entity prefab, out int characterIndex)
        {
            prefab = Entity.Null;
            characterIndex = GameSettings.DollSingerCharacterIndex;
            if (m_Next >= m_Count)
            {
                m_Count = m_Next = 0;
                for (int index = GameSettings.DollSingerCharacterIndex; index <= GameSettings.ShianCharacterIndex; index++)
                    if (Prefab(prefabs, index) != Entity.Null) m_Order[m_Count++] = index;
                if (m_Count == 0) return false;
                for (int i = m_Count - 1; i > 0; i--)
                {
                    int other = random.NextInt(i + 1);
                    (m_Order[i], m_Order[other]) = (m_Order[other], m_Order[i]);
                }
            }
            characterIndex = m_Order[m_Next++];
            prefab = Prefab(prefabs, characterIndex);
            return prefab != Entity.Null;
        }
    }
}
