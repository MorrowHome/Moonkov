# 六种光环武器接入

> 2026-10-23 外观修复：九把新增武器重制为各自的轮廓和信号色，透明组件、库存图标、SVG 源稿与视觉 Prefab 同步更新。保留资源 GUID、装备 ID 与射击规则；手势和第一人称透视修复另见独立分支 `fix/halo-weapon-gestures`。原接入数值与历史画面见下文。

> 当前伤害以 [武器伤害基线](WeaponBalance.md) 为准；下文数值与验证记录保留为初次接入时的历史证据。

首批验收快照（提交 `582f8262`）；后续逐把新增见 [追加武器记录](HaloArsenalFollowups.md)。

2026-10-21，分支 `feat/aliens`。制作契约见 [策划与新版美术要求](Planning/HaloArsenalExpansion.md)。当前批次增加蝉鸣、三律、天枢、望舒、星棘、坠日，保留旧四种武器及原作者动作。

## 获取和使用

在仓库商店使用月尘购买，放入主武器或副武器装备槽，再进入战斗。正式对局数字键仍选择装备槽，不按武器种类增加快捷键。新增物品不通过登录赠送；交换槽位、存储和重装保留该物品的实际弹量。装填按照新增的弹数扣除随身电池能量，电量不足时只能部分装填。

演示入口是 `Assets/DollSinger/Scenes/WeaponGestureLab.unity`。Play 后可用按钮或 1–9、0 选择十种武器，Space 开火，R 装填，调节俯仰／第一人称及四机位。这里使用正式 `DollSingerNetworkPlayer` 实例并注入本地状态，实验室补弹用于预览，不代表正式对局的能量结算。

| 武器 / ID | 实际射击方式 | 容量 / 单伤 | 装填 / 每弹能量 | 独立视觉和作者动作 |
|---|---|---|---|---|
| 蝉鸣 Cicada / 6 | 自动，0.065 s，散布 3.4° | 36 / 8 | 1.7 s / 2 | 错层宽月刃、音叉；交臂双剑指、短刺与合拢再展开 |
| 三律 Trine / 7 | 按下一次三发，串内 0.085 s，串后 0.28 s | 24 / 17 | 2.2 s / 4 | 三块独立厚甲片与三角空孔；三指扇印、左手扣腕、逐指击发 |
| 天枢 Aegis / 8 | 自动，0.11 s，举枪 0.55 s | 72 / 18 | 4.2 s / 4 | 厚分段盾环；前后双掌压印、双臂承重回弹 |
| 望舒 Vesper / 9 | 半自动，0.32 s，2.8 倍光学放大 | 12 / 42 | 2.4 s / 8 | 实心菱刃与宽弦月；剑诀前送、左手后拉弦、旋回装填 |
| 星棘 Thorn / 10 | 半自动，0.55 s，一次五针，散布 1.4° | 8 / 每针 9 | 2.5 s / 每次 10 | 五片分离宽刀瓣；五指爆爪、左手扣前臂、收束再绽开 |
| 坠日 Sundown / 11 | 半自动，1.05 s，22 m/s 抛物线，3.5 m 范围 | 3 / 96 范围伤害 | 3.4 s / 18 | 厚断环和实心日核；上下错层聚球、共同推出 |

## 源文件与运行资源

- `Tools/Art/halo_arsenal.py` 输出 18 张独立 640×640 透明 RGBA 组件、6 张库存图标；SVG 原稿位于 `ArtSource/HaloArsenal/`。主体依靠金／象牙平涂块面，使用透明切口，Shader 使用普通 Alpha 混合。无烘焙 Bloom。
- `Assets/DollSinger/Art/HaloArsenal/` 包含组件、独立材质和 Shader；六份光环 Prefab 位于 `Assets/DollSinger/Prefabs/`，WeaponData 位于 `Assets/DollSinger/`。注册表、商店、库存图标、正式玩家及实验室均有实际引用。
- 新作者源为 `ArtSource/DollSinger/HaloExpansion.blend`，脚本为 `Tools/Blender/halo_expansion_gestures.py`。使用 Blender 5.2.2、原骨架与真实绑定轴，60 fps 烘焙全部 101 根双臂／手指／捩骨。
- 六种动作各有 pose／fire／reload 和 8 个额外俯仰采样，三律另有对应三个实际发射序号的 fire 变体。原 `WeaponGestures.blend` 不覆盖，旧四个 motion 与 `a37f5fdd` 基线逐项相同。
- 原作者源 SHA256：`af4770c614c98f3a0fa64c2e764daf477c8bc821cc54e9ada0327c6cec36a33a`。旧四个 motion 规范 JSON SHA256：`7c7d2fb624c8d59b164edf9097cbf962af1c9826147920514c8ab6ce28675b02`。

重新生成当前图片需 Python 和 ImageMagick，运行 `python Tools/Art/halo_arsenal.py`。动作可用 `blender --background --python Tools/Blender/halo_expansion_gestures.py` 重做；`-- --export-only` 从保存的作者 Action 导出；`-- --only=Thorn_FiveNeedles` 只重做指定新武器，其余动作保持原样。Unity 中执行 **Tools → Moonkov → Install Halo Arsenal Weapons** 更新绑定。

## 权威规则与限制

三律复用预测和服务端共同的 `HaloModuleFire`，由按下沿触发，松开后完成本串，长按不会额外生成下一串。死亡、失去瞄准、切换和装填会取消残余串。弹体仍按每枚独立生成、碰撞和扣血。星棘在一次射击扣弹后生成五枚弹体，每次补回该弹只计一份能量。

单弹体散布补齐为双方共用的 tick 哈希，避免配置了散布却仍完全直射。坠日范围伤害遍历当前 ECS 世界的生命实体，并限制到弹体所在 PhysicsScene；每实体只结算一次，沿用能量手雷的墙体遮挡查询。射手的相同 GhostOwner 免伤，其他玩家仍可受伤；没有阵营友军免伤规则。无 GhostOwner 的异形能够受到直击和范围伤害。初始化的人体伤害按现有身体部位生命比例分摊；范围内为固定伤害，没有距离衰减。

独立 `DollSingerPlayer` 的新增武器通过 `LocalHaloArsenalDriver` 使用同一串发／扣弹／装填规则和有限本地电池，避免本地预览无限弹或三律长按全自动。这个独立角色仍是外观演示，不替代正式服务端战斗。

## 验证与画面

- [规则与实际弹体扣血记录](AI/HaloArsenalExpansion/Rules.txt)：六把购买、装备、物品弹量持久化、部分装填和能量不足；三律完整／残余弹量、长按、松开重按、缓冲、取消和同状态重放；星棘单次扣弹和五弹体；坠日重力与薄墙扫掠。
- 该记录还通过真实 `Projectile.Advance(server=true) → Impact → ECS` 验证六把伤害，范围检查包含多碰撞体去重、无 owner 异形、初始化人体、射手免伤、其他玩家受伤、遮挡、死亡过滤和跨物理场景隔离。测试直接供应生成状态，不模拟网络传输。
- 最终 bake revision：`2cadb68e7cc9`。
- [最终动作与第一人称回归记录](AI/HaloArsenalExpansion/Gestures.txt)。以该文件中的最终 bake revision 和测试结果为准；旧中间截图及失败样本不作为交付验收。
- 四机位截图和六段动作短片保存在 `Docs/AI/HaloArsenalExpansion/`。每段左上为第一人称，右上正面，左下侧面，右下背面。另有 `*-unlit-owner.png` 关闭光环投射照明的平涂对照。

本轮验证层级为 Unity 编译、本地 Play、正式网络玩家实例、真实弹体及 ECS/PhysicsScene 测试。正式地图引用同一套玩家与武器注册资源；本轮没有进行正式地图实战、实时 Host/Client 对局、独立构建或 GPU 性能验收。

### 动作片段与平涂对照

- 蝉鸣：[四机位动作片段](AI/HaloArsenalExpansion/Cicada-Actions.mp4) · [第一人称](AI/HaloArsenalExpansion/cicada-owner.png) · [关闭投射灯](AI/HaloArsenalExpansion/cicada-unlit-owner.png)
- 三律：[四机位动作片段](AI/HaloArsenalExpansion/Trine-Actions.mp4) · [第一人称](AI/HaloArsenalExpansion/trine-owner.png) · [关闭投射灯](AI/HaloArsenalExpansion/trine-unlit-owner.png)
- 天枢：[四机位动作片段](AI/HaloArsenalExpansion/Aegis-Actions.mp4) · [第一人称](AI/HaloArsenalExpansion/aegis-owner.png) · [关闭投射灯](AI/HaloArsenalExpansion/aegis-unlit-owner.png)
- 望舒：[四机位动作片段](AI/HaloArsenalExpansion/Vesper-Actions.mp4) · [第一人称](AI/HaloArsenalExpansion/vesper-owner.png) · [关闭投射灯](AI/HaloArsenalExpansion/vesper-unlit-owner.png)
- 星棘：[四机位动作片段](AI/HaloArsenalExpansion/Thorn-Actions.mp4) · [第一人称](AI/HaloArsenalExpansion/thorn-owner.png) · [关闭投射灯](AI/HaloArsenalExpansion/thorn-unlit-owner.png)
- 坠日：[四机位动作片段](AI/HaloArsenalExpansion/Sundown-Actions.mp4) · [第一人称](AI/HaloArsenalExpansion/sundown-owner.png) · [关闭投射灯](AI/HaloArsenalExpansion/sundown-unlit-owner.png)

短片由 24 fps 实际四路 RenderTexture 录制，1280×720 H.264，无补帧；时长与帧数见 `Clips.json`。关闭照明对照仍保留场景基础光；实验室未启用 Bloom Volume。当前 Unity groundTruth：compilationFailed=false、consoleErrors=0；另有 Unity AI Toolkit 账户 API 连通警告。生成的 Prefab 保留 Unity 空字符串尾空格格式，源码空白检查通过。
