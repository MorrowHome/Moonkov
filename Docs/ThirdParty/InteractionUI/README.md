# 交互界面上游来源

日期：2026-10-29。运行界面仍使用 UI Toolkit。未加入 React、WebView、网页字体或品牌图片。

## Carbon

作者：IBM Corp. 2016, 2026。许可：Apache-2.0。完整许可见 [Carbon-LICENSE.txt](Carbon-LICENSE.txt)。

实际读取的上游文件：

- [StructuredList.tsx](https://github.com/carbon-design-system/carbon/blob/main/packages/react/src/components/StructuredList/StructuredList.tsx)。
- [_structured-list.scss](https://github.com/carbon-design-system/carbon/blob/main/packages/styles/scss/components/structured-list/_structured-list.scss)。
- [_inline-notification.scss](https://github.com/carbon-design-system/carbon/blob/main/packages/styles/scss/components/notification/_inline-notification.scss)。
- [_tabs.scss](https://github.com/carbon-design-system/carbon/blob/main/packages/styles/scss/components/tabs/_tabs.scss)。

修改：将表格行改为原生 Button。保留列表分隔、8 像素纵向间距、16 像素首列间距及 hover、focus、active 状态。行内通知保留侧边状态线、48 像素最小高度和独立说明。页签保留单行排列与底部选中线。颜色改用项目已有色值。没有搬入上游 JavaScript 状态或图标。

落点：`Assets/Resources/Moonkov/UI/MissionHUD.uxml` 和 `MissionHUD.uss`。

## shadcn

作者：shadcn，2023。许可：MIT。完整许可见 [shadcn-LICENSE.txt](shadcn-LICENSE.txt)。

实际读取：[kbd.tsx](https://github.com/shadcn-ui/ui/blob/main/apps/v4/registry/new-york-v4/ui/kbd.tsx)。

修改：将行内键帽转换为 Label。保留内容自适应宽度、最小宽度、居中、短圆角及禁用拾取。高度由 20 改为 26 像素。字号改为 14 像素。键名来自运行中的 InputAction binding。

## 版本边界

当前已读取 main 上的具体源文件并保存许可。来源 commit 尚未锁定。GitHub commit/ref 接口在当前环境不可读，终端联网也被权限拒绝。不能把 main 链接称为固定版本。后续需在获准的联网环境补齐来源 commit，再核对这些文件；不要猜写哈希。

读取日期为 2026-10-29 UTC。读取渠道为网页工具返回的带行号源码文本。没有取得完整原始源码文件字节，也没有原始源码的本地 SHA256。两份许可文本由该网页文本保存为 UTF-8/LF；本地许可 SHA256 只标识保存后的文件，不代表上游原始字节。
