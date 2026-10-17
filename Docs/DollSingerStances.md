# DollSinger 下蹲、趴下与踮脚

本地 `DollSingerPlayer`、正式联机 `DollSingerNetworkPlayer`、使用正式移动器的离线走图预览共用这套姿态。

| 输入 | 行为 |
|---|---|
| C | 切换下蹲／站立；从趴下切到下蹲 |
| 左 Ctrl（按住） | 临时下蹲，松开恢复切换前姿态 |
| Z | 切换趴下／站立 |
| X（按住） | 站立时踮脚，松开恢复 |
| Space | 请求起身；有足够空间时恢复站立并跳跃 |
| 手柄 B／十字键下 | 切换下蹲／趴下 |
| 手柄 RB（按住） | 站立时踮脚 |

下蹲、趴下、踮脚分别使用普通走速的 50%、22%、40%，不能奔跑；保持低姿态时不能起跳。空中不进入新姿态，跌落时暂时让出姿态动画层，保留原下落和护裙动画。趴下时关闭 Q/E 侧倾；其他视角、自由观察、瞄准与武器手势继续沿用原有系统。

碰撞胶囊底部保持原高度。下蹲高度为站立高度的 65%，趴下高度为 0.58 米（不低于胶囊直径），踮脚增加 0.06 米。起身和踮脚前查询当前 PhysicsScene 的实际碰撞；掩体低顶会阻止抬高，离开后自动完成仍在请求的姿态。忽略自身、触发器和控制器排除层，保留 Host 两条时间线的碰撞隔离。

第一／第三人称相机沿用已有骨骼眼位跟随。服务器发射点和命中代理的高度随碰撞胶囊变化。这里保留 CharacterController 的竖直胶囊近似，未加入覆盖趴下身体全长的水平移动碰撞，因此腿部与窄墙角仍可能有视觉交叉；不等于完整的人体物理碰撞。

## 动画来源与改编

全部新增源文件位于 `Assets/DollSinger/Animation/Stances/Source`，均为作者提供的免费版本，不依赖登录、付费包或新增 Unity 包。

| 作者／资源 | 项目使用 | 授权 |
|---|---|---|
| [Quaternius / Universal Animation Library Standard](https://quaternius.itch.io/universal-animation-library)，2026-06-16 的 v3 发布 | `Crouch_Idle_Loop`、`Crouch_Fwd_Loop` | CC0 1.0；保留原 `License.txt` |
| [Kay Lousberg / KayKit Character Animations 1.1](https://kaylousberg.itch.io/kaykit-character-animations)，Rig_Medium_MovementAdvanced | `Crawling`、`Sneaking` | CC0 1.0；保留原 `KayKit-License.txt` |

下载日期：2026-10-17。Quaternius [作者页](https://quaternius.com/packs/universalanimationlibrary.html) 和两份下载页均说明 CC0；许可证原文见同目录文本。

- 趴下待机冻结 `Crawling` 首帧，移动播放其循环；修正 KayKit FBX 与项目相反的朝向。
- 踮脚以项目现有 Mixamo 待机与下载的 KayKit 潜行循环为基础，通过 `DollSingerMovement` 在动画后绕当前脚尖抬脚跟、抬高骨盆，复用现有双骨骼腿部解算保持脚尖接地。它是资源加姿态适配，不是独立下载的踮脚 mocap。
- 下蹲和爬行当前共用各自的前向移动循环，侧移／倒退没有独立专用循环。
- 原地 `.anim` 副本去除水平累计位移，由 CharacterController 负责实际运动；源 FBX 保留，方便重定向与重建。
- `Stances` 层位于 `FreeFall Cover` 与 `Halo Aim` 之间，保留原有手指枪和不同武器手势。低姿态关闭原站立转身脚步层。

Unity 菜单 **Tools → Doll Singer → Set Up Stances** 可重建六份动作副本和姿态层，保留既有 GUID；重建会覆盖这六份副本的手动编辑。它不重建角色预制体或地图。

## 联机与验证

姿态意图复用 PlayerInput 的空闲标志位；实际姿态占用 `ControllerState.StateFlags` 的两位，保留原 Ghost 结构布局。服务端与预测端执行同一套限速、接地和顶上空间检查；远端动画使用服务器同步的实际姿态，不读取观察者按键。需要客户端和服务器同时更新到此版本。

针对性检查入口：**Tools → Moonkov → Check Character Stances**。覆盖命令清洗／缓冲、状态位隔离、移动限速、冲刺／跳跃限制、低顶起身、胶囊脚底锚定、Humanoid 循环、趴下朝向和踮脚接触。输出在 `Temp/StanceValidation/checks.txt`。

本次验证：Unity 6000.5.10f1 编译通过；42 项姿态定向检查、已有 `MoonkovNetworkMovementChecks` 回归检查通过。在 `DollSingerDemo` 的 Play 中通过输入姿态状态依次驱动下蹲→趴下→踮脚→站立，胶囊高度依次为 1.105／0.58／1.76／1.70 米，站立退出姿态层权重恢复为 0。踮脚相对头高约 1.29 米，恢复站立约 1.23 米。未验证真实按键手感、双端联机、独立服务器构建或最终裙摆视觉。

KayKit 的 Humanoid 导入保留源警告：四肢局部平移和未映射的中间 chest 旋转不进入标准 Humanoid 重定向；六份使用中的动作副本和 Avatar 已实际采样通过。没有通过关闭警告或修改全局导入设置隐藏它。项目原有 Airlock 序列化／过时 API、Account API 与 URP 非烘焙 Area Light 提示不属于本次姿态改动。
