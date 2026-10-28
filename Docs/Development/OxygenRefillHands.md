# 持罐补氧动作

2026-10-29。独立源为 `ArtSource/DollSinger/OxygenRefill.blend`。每秒 60 帧，1–121 帧，共 2 秒。原 `SurvivalActions.blend` 与已有搜刮、吃喝和注射数据未改。

右手握罐身，左手操作瓶头，再回到初始持罐位置。模型来自现有 `oxygen_canister.fbx`。普通罐和大型罐保持各自实际尺寸。指节按罐身圆周求解，避免从罐体内部抄近路。三套角色共 6 段适配，运行时只采样保存的数据，不使用手腕 IK。

## 权威与反馈

服务端用现有 `MedicalItemId` 和倒计时发布独立 `HandAction.RefillOxygen`。动作类型追加到枚举末尾，保留原编号，不新增 Ghost 字段。客户端和服务器仍须使用匹配版本。

动画不补氧、不扣罐。实际结果由两秒服务端操作决定。取消、死亡、受攻击或库存变化沿用原规则。手部展示跟随状态结束并交还瞄准控制。瓶头操作复用装备声，不播放拆包、吞咽或医疗声。供气成功声继续等待服务端成功回执。

## 编辑与导出

`Tools/Blender/oxygen_refill_authoring.py` 提供制作与只读导出入口。`setup()` 仅创建新源，已有文件时拒绝覆盖。修改已有源时直接编辑并保存 Action。`export_runtime()` 从当前独立源读回骨架，再适配其他角色和大型罐。它只替换各角色动作库中的 `RefillOxygen`，保留原动作。

`SourceData/Animations/OxygenRefill/Authoring.json` 记录源哈希与帧检查。`RuntimeValidation.json` 记录保存后往返误差、六段适配的指节轴间距与腕部角度。指节轴间距不是皮肤表面间距，不能据此宣称全部角色无穿插。

原生存动作的 `build_survival_runtime.py` 会保留独立补氧动作。重做补氧后应重新运行 `OxygenHandActionChecks.Run()`，不要用旧源种子覆盖正式动作库。

## 验证范围

独立 Blender 5.2.2 实例制作，不接管地图渲染实例。只使用低成本视口预览，没有启动 Cycles 地图重算。源动作已检查多帧与多角度。保存后骨架往返最大误差约 0.00042 毫米。

Unity 单 Editor 编辑模式编译通过。60 项检查覆盖真实服务端发布、重复快照、取消、三套实际角色、两种实际罐尺寸及多个相位的手指位置。原 51 项氧气反馈与补氧事务检查通过。未进入 Play，未做正式战局、联机、听感或 Player 构建验收。其他角色的最终观感仍须实际游戏确认。
