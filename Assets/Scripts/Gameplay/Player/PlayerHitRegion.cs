// 标记一个附加碰撞体对应哪个身体部位，例如超出主胶囊范围的胳膊。射线能识别部位，但血量在对应的 ECS 角色上。
using UnityEngine;

namespace Unity.MP_FPS {
    // Query-only extension of the existing body capsule, for arms outside its
    // outline. Health/ownership always live on the linked player ECS entity.
    public sealed class PlayerHitRegion : MonoBehaviour {
        // 伤害规则从命中碰撞体上的部位标记决定如何扣血。碰撞体字段只负责定位。
        public BodyPart Part;
    }
}