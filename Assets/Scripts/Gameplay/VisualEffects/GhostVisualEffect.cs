// 给会在网络上同步的临时特效计时。服务器到期后销毁 Ghost 实体，客户端随网络里的对象移除一起收掉画面。
namespace Unity.MP_FPS {
    public class GhostVisualEffect : GhostMonoBehaviour, IUpdateServer {
        public float Lifetime = 3f;
        private float _time;

        public void UpdateServer(float deltaTime) {
            _time += deltaTime;
            if (_time >= Lifetime) GhostGameObject.DestroyEntity();
        }
    }
}