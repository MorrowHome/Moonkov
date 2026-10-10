# DollSinger 第三人称相机

相机采用项目已有的 Cinemachine 3.1.7 `CinemachineThirdPersonFollow`。没有安装新包，不需要重新配置场景或预制体；本地演示与联机拥有者都通过 `DollSingerView` 接入。

## 行为

- 围绕角色躯干旋转。旋转中心的高度沿角色的 up 方向计算，不随俯仰旋转到脚下；蹲伏／趴下时随眼睛高度降低。
- 从下向上看时，相机臂接触地面就立即缩短。碰到墙壁也同样处理；离开遮挡后平滑恢复到滚轮设定的距离。
- 碰撞球包住近裁剪面四角，随 FOV、宽高比及 near clip 调整，避免相机中心还在地面上、画面已经穿地。
- 第一／第三人称过渡直接缩短碰撞相机臂，不对一个已经校正的相机位置再做穿过障碍物的插值。拉近时重置 Cinemachine 的旧碰撞收缩量，避免过长的恢复量把短相机臂推过旋转中心。
- 保留现有第一人称眼睛／侧倾、输入和联机瞄准接口，不新增网络状态。

## 手动调整

选择拥有者相机上的 `DollSingerView`。以下参数直接在 Inspector 可调，正式联机相机对应 `Assets/Prefabs/PlayerGhosts/MainCamera.prefab`，本地相机对应 `Assets/DollSinger/Prefabs/DollSingerPlayer.prefab`：

| 参数 | 默认值 | 用途 |
| --- | --- | --- |
| `thirdPersonOrbitHeight` | 1.25 m | 站姿躯干旋转中心；低姿态自动降低 |
| `minThirdPersonDistance` / `maxThirdPersonDistance` | 1.25 / 6 m | 滚轮希望保持的距离；碰撞允许比最小值更近 |
| `cameraCollisionRecoverySeconds` | 0.35 s | 离开遮挡后恢复距离的阻尼 |
| `thirdPersonCameraRadius` | 0.2 m | 最小碰撞半径；近裁剪面更大时自动扩大 |
| `cameraObstacleLayers` | DefaultRaycastLayers | 环境碰撞层；自动排除客户端／服务器角色层和第一人称叠加层 |

## 生命周期与接管点

`DollSingerView` 仍是实际相机的唯一写入者，在角色动画与侧倾之后手动计算 Cinemachine 状态。运行时虚拟相机不交给 Brain 自动驱动，因此不会与网络视角或第一人称抢更新。

Cinemachine 的自身碰撞过滤使用已有 `CinemachineTarget` 标签：仅在同步计算期间临时标记拥有者的碰撞对象，`finally` 立即还原，不修改任何预制体标签。官方碰撞查询使用默认 PhysicsScene；当前正式地图与本地演示都使用这一场景，不支持把拥有者放进独立 LocalPhysicsMode.Physics3D 场景。


## 验证

菜单：**Tools → Moonkov → Check Third Person Camera**。针对性检查实际 Cinemachine 适配器的仰视地形碰撞、近裁剪面、自身过滤、快速拉近、墙壁、距离恢复及运行时对象清理，不保存测试对象或改动地图。

已完成 Unity Editor 编译、上述检查和本地演示 Play Mode 的陡角仰视检查。正式月球地图的玩家操作、host/client、蹲伏／趴下手感与性能仍需实际验收；这份检查不能替代这些结果。

参考：[Cinemachine Third Person Follow](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineThirdPersonFollow.html)。
