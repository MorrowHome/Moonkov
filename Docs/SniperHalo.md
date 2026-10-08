# 狙击光环

狙击枪是独立库存物品 `sniper`，武器 ID 5，占 2×2 四格，可放主武器或副武器栏。商店购买 90 月尘、卖出 45 月尘，不自动赠送给旧角色。弹量随物品保存，存仓、切换、死亡掉落和撤离沿用现有库存流程。

| 参数 | 初始值 | 调整位置 |
|---|---|---|
| 弹仓 | 5 发 | `Assets/DollSinger/SniperWeapon.asset` → Magazine Size |
| 伤害 | 110 | 同资源 → Damage |
| 射击间隔 | 1.4 秒，单发 | 同资源 → Cooldown In Ms / Automatic |
| 充能 | 3 秒，每补一发 18 能量 | 同资源 → Reload Time / Energy Per Round |
| 弹道 | 800 m/s、月面重力 1.62 m/s² | 同资源 → Projectile Speed / Gravity |
| 第一人称倍率 | 6 倍 | `Assets/DollSinger/Prefabs/SniperHalo.prefab` → Scope Magnification |
| 光环大小 | 1 倍 | 同预制体 → Overall Scale |
| 瞄准镜在画面中的直径 | 屏幕高度的 70% | 同预制体 → Scope Viewport Fraction |

光环采用冰蓝双层断环、五颗菱形电容及外围距离刻度。头顶时收拢；瞄准时双环沿轴线分开形成光学筒，第一人称相机按投影正切得到真实 6 倍目标放大。光环尺寸同步补偿倍率，中央视野保持中空，小点固定，不受射击退环影响。第三人称保持原来的常规瞄准倍率。

射击反馈先短促展开、闪亮，再执行约 0.9 秒的解锁退环与回位，表现单发拉栓感。充能时双环反转、五颗电容绕行并按服务端批准的实际目标弹量点亮，最后收拢闪亮锁定；支撑手在瞄准点下方扫动、转腕。

```mermaid
stateDiagram-v2
    头顶收拢 --> 双环展开: 瞄准
    双环展开 --> 六倍瞄准: 第一人称
    六倍瞄准 --> 射击退环: 单发射击
    射击退环 --> 六倍瞄准: 退环回位
    六倍瞄准 --> 反转充能: 服务端预留能量
    反转充能 --> 收拢锁定: 达到实际目标弹量
    收拢锁定 --> 六倍瞄准
    六倍瞄准 --> 头顶收拢: 松开瞄准
```

安装入口：`Tools/Doll Singer/Install Sniper Optic Halo`。验证入口 `SniperHaloSetup.Validate` 针对注册、两个角色绑定、买入装备存仓卖出、部分能量守恒、旧快照重放和瞄准倍率恢复。`RenderPreview` 在独立场景渲染光环动作，不修改战局或账号。

本地角色演示可在 `DollSingerHaloAim` 的 Local Primary / Local Secondary 选择 Sniper；真实战局需从商店购买后在库存中装备。演示有独立弹量，不代表账号获得物品或免费充能。

2026-10-08 验证：Unity 编译、针对性检查和原生光环动作渲染通过，Console 无错误；后端编译通过。本地数据服务已加载新的商店目录，保留原来的监听地址，健康状态为 `ready`。检查使用内存库存，没有改动玩家物品。尚未验收完整角色的 Play Mode 手势、实战伤害手感及双客户端表现。
