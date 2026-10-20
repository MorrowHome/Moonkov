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

## 岚轮 Tempest / ID 13

六片宽实心旋风刀翼，瞄准时缓慢转动，开火加速脉冲。左掌在上展开、右拳在下旋压；装填收拢手掌并让拳印旋回。自动 0.26 s／发，14 发容量，每发 26 范围伤害、半径 1.35 m；弹速 34 m/s、重力 3.24 m/s²、装填 2.8 s、每弹补充花费 7 能量。商店 62 月尘，主／副武器槽；实验室新增「岚轮」按钮。

Bake revision：`5b0ef4c950a4`，此前十一个 motion 保持不变。新增完整 pose/fire/reload 与俯仰 Action、独立 PNG／图标／SVG／材质／Prefab／武器配置。仅岚轮的第一人称取景下移 2.5% 屏高，修正开关镜时手臂擦入中央边缘，既有武器取景不变。

- [第一人称](AI/HaloArsenalFollowups/Tempest/tempest-owner.png) · [关闭投射灯](AI/HaloArsenalFollowups/Tempest/tempest-unlit-owner.png) · [四机位动作片段](AI/HaloArsenalFollowups/Tempest/Tempest-Actions.mp4)
- [规则与实际伤害](AI/HaloArsenalFollowups/Tempest/Rules.txt)：持续按住在 60 Hz 下 2 秒实际发射 8 次；余下 6 发用尽后停止，弹量不为负；1 秒飞行 34 m、下降 1.62 m。真实弹体命中后，主目标和弹道旁另一目标均受到 26 点伤害，确认使用范围路径。采购／换槽／持久化／部分装填和旧武器规则一并通过。
- [动作检查](AI/HaloArsenalFollowups/Tempest/Gestures.txt)：正式玩家四机位，待机／射击／装填、俯仰 -80 到 +80、16:9／4:3、中央手臂网格留白与 48 次中断开关镜通过。Unity 编译和本地 Play 无新增运行异常；验证范围同上，不代称正式地图或联机实战。

后端共享清单的 `MoonPersistence` .NET 10 编译通过（0 警告、0 错误）。ShopChecks 已成功构建，但因本地缺少 `appsettings.local.json`，未执行数据库／HTTP 集成检查；Unity 内采购和装备夹具结果不等同于该后端检查。
