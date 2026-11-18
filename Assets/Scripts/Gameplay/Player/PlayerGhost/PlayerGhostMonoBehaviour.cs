// 需要随角色一起显示的 Ghost 组件可以继承这个基类。Awake 会找到同一角色的 PlayerGhost，子类可从它读取角色关联的对象。
namespace Unity.MP_FPS {
    public abstract class PlayerGhostMonoBehaviour : GhostMonoBehaviour {
        public PlayerGhost PlayerGhost => m_PlayerGhost;
        private PlayerGhost m_PlayerGhost;
        public virtual void Awake() { GetRequiredComponent(out m_PlayerGhost); }
    }
}