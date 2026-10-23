---
name: "Moonkov inventory surfaces"
description: "原生 UI Toolkit 的深色装备、随身容器、搜刮与仓库视觉系统"
colors:
  surface: "#101416"
  panel: "#191f21"
  equipment-pane: "#171c1e"
  loot-pane: "#1c2223"
  grid: "#101718"
  line: "#414b4e"
  ink: "#eeeee6"
  muted: "#b3bcb9"
  accent: "#c8cfaa"
  item: "#222b2b"
  item-line: "#52605c"
  hover: "#303b38"
  selected: "#354139"
  valid: "#9dcc9e"
  invalid: "#e49e92"
typography:
  pane-title:
    fontFamily: "Noto Sans CJK SC"
    fontSize: "15px"
    fontWeight: 700
  section-title:
    fontFamily: "Noto Sans CJK SC"
    fontSize: "13px"
    fontWeight: 700
  item-label:
    fontFamily: "Noto Sans CJK SC"
    fontSize: "12px"
    fontWeight: 400
  quantity:
    fontFamily: "Noto Sans CJK SC"
    fontSize: "13px"
    fontWeight: 400
rounded:
  square: "0px"
spacing:
  region-gap: "4px"
  pane-padding: "8px"
  pane-gap: "10px"
components:
  inventory-item:
    backgroundColor: "{colors.item}"
    textColor: "{colors.ink}"
    typography: "{typography.item-label}"
    rounded: "{rounded.square}"
  inventory-item-hover:
    backgroundColor: "{colors.hover}"
  inventory-item-selected:
    backgroundColor: "{colors.selected}"
  container-equipment:
    textColor: "{colors.muted}"
    typography: "{typography.item-label}"
    rounded: "{rounded.square}"
    padding: "2px"
    width: "52px"
    height: "52px"
  quickbar-dock:
    height: "62px"
  header-button:
    backgroundColor: "#28312e"
    textColor: "{colors.ink}"
    height: "26px"
---

# Design System: Moonkov inventory surfaces

## Overview

**Creative North Star: "战地装备台"**

本系统记录已实现的局内背包、搜刮和共用仓库容器界面，采用用户指定的塔科夫式深色装备组织方式。主菜单、局域网房间及其他游戏界面继续遵循各自的设计。物品图、空槽、容量和所有权构成主要视觉信息；角色画像使用项目正式角色资产。

界面保持紧凑、方正和低饱和，以明亮正文及浅橄榄描边表达选择。库存事务沿用现有 C# presenter 与权威数据回包；视觉状态不能代替交易结果。

**Key Characteristics:**

- 原生 UXML／USS 固定结构，C# 驱动物品、事件与几何尺寸。
- 正式角色静态画像、可见空槽和现有物品图共同表达装备关系。
- 单行页头、紧凑容器入口与清楚的网格占位。
- 用描边、文字和色彩共同反馈选择、可放置与失败。

## Colors

以炭黑和灰绿构成低反光工作面，保留物品本身的颜色辨识度。精确值以 frontmatter 为准，来源为 `Assets/Resources/Moonkov/UI/RaidInventory.uss`。

### Primary

- **浅橄榄**（accent）：物品 hover、选中和活动页签的强调色。
- **可放置绿**（valid）：有效放置目标的描边，配合区域预览。
- **失败珊瑚**（invalid）：无效放置描边和短暂错误提示。

### Neutral

- **炭黑底**（surface）、**灰绿面板**（panel）：全屏容器和页签的底层。
- **装备工作面**（equipment-pane）、**搜刮工作面**（loot-pane）：保持相邻区域边界清楚。
- **暗格底**（grid）、**物品面**（item）、**物品边线**（item-line）：区分空容量与已占用矩形。
- **分隔灰**（line）、**正文白**（ink）、**辅助灰绿**（muted）：结构、物品身份和次要元数据。
- **悬停面**（hover）、**选中面**（selected）：在浅橄榄描边之外保留可辨的状态变化。

**The State Pairing Rule.** 有效与无效放置同时保留描边和上下文反馈，不能只改变颜色。

## Typography

使用项目已有 `Assets/Localization/Fonts/NotoSansCJKsc-Regular.otf`。标题通过 UI Toolkit bold 样式区分层级，文字间距为零；不引入额外展示字体。

- **Pane title**：区域名称，保持紧凑，不加入重复大标题。
- **Section title**：胸挂、口袋、特殊装备、背包、保险箱的单行标签。
- **Item label**：装备标签、物品短名和底部快捷栏名称。长名称允许省略，完整身份保留在 tooltip／物品详情中。
- **Quantity**：物品数量与电池能量；电池用实际能量加 E 表示，容器用“打开 ↗”区分。

## Layout

库存窗口使用父级的 98% 宽、97% 高，最大宽度 1800 个逻辑单位，内边距使用 pane-padding。页头以装备／健康页签及关闭按钮占据单行。具体左右中分区和顺序保存在 inventory surface brief，不能推导为其他页面的通用模板。

装备区在局内为 32% 宽且上限 410，仓库共用界面为 27% 且上限 370。其余空间由随身容器和右侧目标分配；仓库右栏采用更大伸展权重。左侧和中栏相接，以细线分隔；右栏保留 pane-gap。容器、搜刮分别拥有滚动区域，页头和局内快捷栏固定。

随身储物网格根据中栏每一行的实际可用宽度等比例填满，保持格子方形、物品占格比例和真实容器行列。多区域按列数共享同一格宽；当前公式是可用宽度扣除每区域 region-gap 后除以总列数，最小格宽 32。三特殊装备槽例外：各为紧凑的 52×52 方形，不随中栏放大。胸挂、背包、保险箱的装备入口紧邻其内容网格；未装备仍显示紧凑空入口。

仓库网格按右栏宽度缩放，格宽范围 38–76；普通搜刮宽网格为 40–64。这些是现有几何规则，不是浏览器媒体查询断点。1600×900 与 1280×720 的最终原生面板 fixture 已检查；不外推到未检查尺寸或目标设备。

## Elevation & Depth

采用暗色底、略亮物品面、细线与透明叠层表达深度。身体装备槽在画像上使用半透明暗底，物品名条使用半透明深色底。当前库存系统没有阴影或装饰入场动画；拖动物品和放置预览提供即时操作反馈。

## Shapes

方形容器入口与矩形占格物品使用直角轮廓。默认边线细，选中物品使用更明显的双单位描边；装备身体槽与画像锚点对齐，避免互相覆盖。图标保留现有项目美术，不把每个容器包装成有额外说明的大卡片。

## Components

### Body equipment targets

六个身体装备槽覆盖正式角色静态全身画像的两侧，分别表示项圈、能量护盾、主武器、副武器、手枪和近战装备。标签、物品图和空状态各有固定位置；具体布局按烘焙锚点及面板尺寸计算。

画像为 `FieldOperatorPortrait.png`，由 `RaidOperatorPortraitBake` 在 Unity 中烘焙项目正式 DollSinger 角色，来源元数据嵌入 PNG。运行时读取静态贴图和布局数据，不创建画像摄像机。

### Container rows

一行小标题之下，胸挂／背包／保险箱使用紧凑装备图标入口，紧挨实际内容网格。入口支持穿戴目标与已有物品；空装备只显示“空”。口袋直接显示实际网格。三特殊装备槽以图标和 tooltip 表达工具身份。合计为六身体槽、三容器装备入口、三工具槽，共十二个装备目标。

### Inventory items

物品按照真实旋转方向和尺寸占格，顶部短名称、主体图标、角落数量或能量共同表达状态。hover 提亮物品面与边框，选中加强边框。拖放预览以有效绿／失败珊瑚反馈目标；失败保持物品原归属，短暂原因提示靠近底部显示。

### Target inventory

目标内容与随身内容保持独立所有权和滚动。普通物资、尸体、仓库使用相同物品占格语言；尸体先显示遗留装备，再显示其容器内容。没有目标时使用简短空状态，避免持续操作教程和占位说明。

### Tabs and actions

装备／健康页签使用紧凑按钮，活动状态以灰绿底和浅橄榄底线表达。关闭按钮保留“关闭 / Tab”。常驻大标题与重复区域说明保持隐藏。

### Raid quickbar

局内 1–9 快捷栏停靠在独立 dock 内，最大行宽 660，槽高 56，数字、物品图、名称和数量共享库存组件语言。空槽保留数字目标；不可用与等待状态使用已有描边、透明度与反馈状态。没有持续教程字幕。仓库 fixture 不展示局内 dock。

## Do's and Don'ts

### Do:

- **Do** 保留六身体装备槽、三容器装备入口和三工具槽的明确目标关系。
- **Do** 让随身储物网格依据中栏实际宽度缩放，同时保留真实容量和占格比例；三个工具槽保持 52×52。
- **Do** 使用项目中文字体、现有物品图和有来源记录的正式角色静态画像。
- **Do** 以服务器回包确认库存结果，失败后保留原物品并给出短暂原因。

### Don't:

- **Don't** 恢复左侧八张装备卡的两列布局，或把胸挂、背包、保险箱移出中栏。
- **Don't** 加回重复大标题、常驻操作说明、大容器卡片或快捷栏教程字幕。
- **Don't** 为此库存界面新增运行时角色预览摄像机或外部生成画像。
- **Don't** 把原生 Editor 面板截图和定向回调检查写成正式地图 Play、物理输入或联机验收。
