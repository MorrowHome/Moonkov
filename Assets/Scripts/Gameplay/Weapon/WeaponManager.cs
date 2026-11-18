// 场景级的武器注册表入口。需要把角色身上的武器编号查成 WeaponData 时，从这个实例取得 WeaponRegistry。
using UnityEngine;

namespace Unity.MP_FPS {
    public class WeaponManager : MonoBehaviour {
        public static WeaponManager Instance { get; private set; }
        public WeaponRegistry WeaponRegistry;

        // 场景自己的注册表挂到统一入口。角色系统之后可以直接用编号查武器配置。
        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
    }
}