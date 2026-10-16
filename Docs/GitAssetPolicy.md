# Git 资源管理与本次治理记录

## 提交边界

版本库保留代码、场景、Prefab、材质、源贴图、模型、音频、项目设置、包清单与锁文件，以及这些 Unity 资源对应的 `.meta`。

`Library/`、`Temp/`、`obj/`、`Logs/`、`UserSettings/`、构建目录、GI 中间缓存、`Assets/SceneDependencyCache/` 和本地配置不提交。`.gitignore` 不会自动停止跟踪已经入库的文件；发现这种情况时使用 `git rm --cached`，保留本地副本。

`Backend/` 下手写的 `.csproj` 是构建源文件，应保留；Unity 自动生成的工程文件继续忽略。

## 烘焙资源

当前 MC00 的 `States/*/Atlas*.exr`、`States/*/Probes.asset`、`Reflections/Room*.exr` 和 `MC00Bake/LightingData.asset` 均保留在版本控制中。它们是已发布场景及分电路照明的输入，重新烘焙有时间成本，且运行时没有替代它们的自动生成步骤。相关二进制数据使用 Git LFS；对应 `.meta` 和光照配置正常提交。

烘焙 Shadowmask 如被场景使用，也属于需要提交的资源；实时阴影图集由运行时生成，不是要提交的磁盘资源。不要使用 `*Lightmap*`、`*.exr`、`*Shadow*` 这类宽泛忽略规则。

Unity 将实际光照数据与烘焙中间缓存区分存储，参见 [Lighting Data Assets](https://docs.unity.com/en-us/engine/6000.6/manual/lighting-overview/direct-and-indirect-lighting/lightmap-data/lightmap-snapshot) 和 [GI cache](https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/direct-and-indirect-lighting/lightmap-data/gicache)。

只有在另行建立固定版本、配置、生成命令、资源分发和构建前重烘流程后，才考虑将烘焙成品移出 Git。Git LFS 减少普通 Git 对象体积，实际资源仍需上传并占用 LFS 存储。

## 2026-10-16 操作记录

- 在现有 `develop` 分支整理全部待提交项目改动，目标远端为 `origin`（`MorrowHome/Moonkov`）。
- 发现并停止跟踪 33 个已被忽略规则覆盖的场景依赖缓存文件及其 `.meta`，保留本地文件。
- 补充 `Assets/Library.meta` 忽略规则；现有 Library 缓存目录规则继续有效。
- 补充 `LightingData.asset` 的 LFS 规则，当前改动的光照数据随烘焙修复提交；不重写既有历史。
- 按 Git 治理、EVA 气闸、异形行为、MC00 烘焙和其余项目资产分组提交，并推送 `develop`。
- 本轮验证仅针对 Git 暂存差异、忽略边界、LFS 指针和远端同步。未执行 Unity 编译、Play、性能或联机验收；已有文档中的验证记录属于此前工作。
