# 二次元角色渲染方案选型

核实日期：2026-10-16。工程环境：Unity 6000.5.10f1、URP 17.5、Apple Silicon / Metal。

优先沿用 lilToon，采用经过实际展示验证的材质资源与原生功能；更换 Shader 时优先评估 NiloToonURP 完整版与 RealToon。此前自行生成虹膜、局部皮肤效果和整套材质的试验已停止，不能作为质量达标的交付。

## 参考作品说明

komado 的 [Chiffon 官方商品页](https://komado.booth.pm/items/5354471)明确列出使用 lilToon。因此参考效果证明 lilToon 可以支撑这类审美，不能由本次失败推断 Shader 已到上限。参考角色的五官、虹膜贴图、妆容、发束与材质共同形成最终观感；这是对参考的美术判断，替换 Shader 或照搬参数不能保证复现。

Chiffon 是 VRChat 模型，商品说明不保证其他程序中的工作情况；模型许可与 Shader 许可需分别处理。这里仅用作视觉参考，未购买、导入或复制其模型、贴图、材质。

## 候选比较

| 方案 | 已核实事实 | 对当前项目的判断 |
| --- | --- | --- |
| lilToon 原生功能与成熟材质 | 本项目已经安装；Chiffon 使用它；支持 MatCap 光照调制与阴影区禁用 | 首选。最接近用户提供的参考，保留现有动画和遮光逻辑的迁移成本最低。原生预设只作基础，不能等同精品头像成品材质 |
| NiloToonURP 完整版 | 作者列出 Unity 6 / URP 17.x 支持，提供完整源码和商业授权渠道 | 重点替代候选。先核实 6000.5 / URP 17.5 / Apple Silicon Metal，以及现有角色、额外灯与战术遮光的实际表现；未在本项目测试 |
| RealToon | 商店列出 Unity 6 URP 兼容；5.0.16 于 2026-09-17 发布；作者列出眼睛视差折射、各向异性相关选项 | 可直接通过 Asset Store 获取的商业候选。具体 6000.5 / Metal 兼容性、材质迁移和眼睛效果仍需验证，不能凭通用兼容表称已通过 |
| Unity Toon Shader | Unity 官方项目，支持 URP，侧重三色分层与动画色彩控制 | 免费替代候选。更偏赛璐璐色彩设计；对于本次柔润目标，适配程度是美术判断，不能只看功能清单 |
| Poiyomi 10 URP | 现已提供 URP 版，支持 Unity 6.0–6.6；官方严格支持列表是 Windows / Linux、D3D11 / Vulkan experimental | 当前 Mac / Metal 环境不作为首选，不能使用“VRChat 常用”代替平台验证 |

来源：[lilToon MatCap](https://lilxyzw.github.io/lilToon/ja_JP/reflections/matcap.html)、[NiloToon 作者说明与效果展示](https://github.com/ColinLeung-NiloCat/UnityURPToonLitShaderExample)、[RealToon 商品页](https://assetstore.unity.com/packages/vfx/shaders/realtoon-pro-anime-toon-shader-65518)、[RealToon 作者发布说明](https://discussions.unity.com/t/realtoon-an-aaa-toon-anime-shader-and-stylized/632467)、[Unity Toon Shader](https://github.com/Unity-Technologies/com.unity.toonshader)、[Poiyomi 官方平台要求](https://www.poiyomi.com/10.0/intro)。

NiloToon 的公开 GitHub 仓库只包含简化教学 Shader，README 的精美展示属于完整产品；不得把免费示例导入后当作完整版。完整版需通过作者的授权渠道获取，公开固定价格未核实。未购买、联系作者或安装新 Shader。

## 现成 lilToon 材质资源

以下是资源筛选结果，尚未用本角色验证，不直接推荐购买后无条件套用：

- [SKIN MATERIAL NATURAL COLOR](https://wakamaru.booth.pm/items/6419713)：提供脸部、身体材质及 MatCap / Normal，方向比继续自制大面积白色高光更值得比较。页面限制可自由使用状态的再分发，游戏内嵌使用需进一步确认。
- [艶やか肌マテリアル](https://kamoooome.booth.pm/items/8279467)：提供脸、身体、头发与逆光切换，说明使用 lilToon 原生能力。个人与法人条件不同，再分发有限制，不能将“个人商用可”直接解释成游戏分发可。
- [ShapeshifterClinic MatCap Vol.1](https://booth.pm/ja/items/7100790)：商品页标明 CC0，提供免费样包及付费集合。可作为通用反光素材候选，但不等于完整角色 LookDev。
- [しっとり肌](https://booth.pm/ja/items/8412661)明确禁止商用及购买者以外可用形式的公开，本游戏路线排除。

商品页可见的文字不代表已取得资源或完整授权。BOOTH 的头像展示可能依赖专用 UV、妆容和拍摄灯光；每份资源要在角色副本上比较。不要引入 VRCSDK / Modular Avatar 来套用单个材质资源。

## lilToon 接入原则

现有包包含 Skin-Illust / Skin-Anime、Hair-Standard / Hair-Illust、Cloth 系列原生预设，可以用于独立副本的对照基础。预设应用可能同时改变 Shader 与渲染模式，不能覆盖共享材质后再修引用。

脸部先保留原始五官、嘴部与虹膜设计，恢复色彩层次，再比较材质质感。任何新反光必须按部位有遮罩；MatCap 使用光照调制，必要时在阴影中抑制。官方文档说明这些选项用于避免暗环境中的 MatCap 浮亮，但具体游戏遮光仍要实测。

现有 MoonToon 遮光扩展承担战术隐藏需求。切换 Shader 时必须验证角色在顶棚遮挡、室内局部灯和无光环境中的明暗，不能仅验证美术摄影棚。禁用最低亮度托底、自发光眼睛或恒亮轮廓光等破坏隐藏的设置。

只有同镜头、同姿态、同曝光的原版对照与游戏阴影测试通过后，才替换正式材质。比较需包含静态脸部、嘴部表情、转头眼睛与头发高光；性能使用一致分辨率与灯光，分别记录 CPU、GPU 和 Draw / SetPass。GPU 数据不可用时明确缺测。

## 当前工程状态

工作分支为 `character-lookdev`，基线为 `491d07e3`。本轮未将试验材质写回正式角色、Prefab 或游戏地图；此前已提交的渲染版本保留，本次没有倒退其提交历史。

被否定的 `Assets/DollSinger/LookDev`、两份新建 LookDev 编辑器脚本及 `Docs/LookDev` 已完整移到工程外可回退归档：

`/Users/morrow/.codex/visualizations/2026/10/16/01a14511-e71c-7cb2-ad4d-f10205fac7c8/rejected-lookdev`

归档内的截图和性能记录属于未验收试验，不能用于宣称本次正式画质或性能改善。编辑器已退出试验场景、回到 MainMenu，Quality Pipeline 确认为原有 PC_RPAsset；移除试验后的编译状态为 up_to_date，0 errors / 0 warnings。此前 Console 的 SubScene 断言不在本次清理的修复范围。

本次交付是方案筛选与试验清理；尚无新的通过审美验收的渲染版本，也没有新增 Shader 的运行性能结论。
