# 逐把追加光环武器

用户要求「完成后再给我做更多的。一次 commit 一个」。首批六把在 `582f8262` 完成后，后续每把独立制作、验证和提交。沿用大块平涂结构和 Blender 原骨架动作；不覆盖已验收动作。

## 回环 Rondo / ID 12

两枚反向勾玉组成的宽实心回环，左右双指枪错层举起。按下触发两发，第一发右手回弹、第二发左手回弹；节点按照真实 BurstSequence 脉冲。16 发容量、每发 26 伤害、串内 0.07 s、串后 0.38 s、装填 2.1 s、每枚补回的弹消耗 6 能量。商店 50 月尘，主／副武器槽装备；实验室增加「回环」按钮，正式对局仍按装备槽选武器。

新增独立 SVG、640×640 透明组件／图标、材质、Prefab、WeaponData 和 pose/fire/reload Actions。双发各有对应 Fire Action，在 8 个额外俯仰采样中一并烘焙。Bake revision：`fed23d246d69`；前十个 motion 与 `582f8262` 逐项相同。

- [第一人称](AI/HaloArsenalFollowups/Rondo/rondo-owner.png) · [关闭投射灯](AI/HaloArsenalFollowups/Rondo/rondo-unlit-owner.png) · [四机位射击／装填片段](AI/HaloArsenalFollowups/Rondo/Rondo-Actions.mp4)
- [规则与真实弹体伤害](AI/HaloArsenalFollowups/Rondo/Rules.txt)：按下双发、长按不重复、1／2 剩余弹量、松开重按、死亡／切换／装填／瞄准取消、预测同状态重放；采购、装备、弹量持久化、有限电池装填、真实 Projectile→Impact→ECS 26 点单弹伤害通过。
- [动作检查](AI/HaloArsenalFollowups/Rondo/Gestures.txt)：正式网络玩家实例，待机／双发／装填、俯仰 -80 到 +80、16:9／4:3、中央手臂网格留白、指向准星与 48 次快速开关镜中断通过。

为逐把接入，安装器支持 `HaloArsenalSetup.InstallOne(id)`，本地驱动与实验室列表按已注册武器长度分配；新增双发参数不会改动三律的三发配置。验证器支持 `BlenderGestureValidation.RunWeapon(id-2)` 和 `HaloArsenalCapture.RunWeapon(id)`，每把保留单独证据。

验证层级为编辑器编译、本地 Play、正式角色实例和 ECS/PhysicsScene 夹具。未验证实时 Host/Client、正式地图实战或独立构建。远程分支已有其他任务的 15 个提交，普通推送被拒绝时保留本地提交，不强推或混入外部任务。
