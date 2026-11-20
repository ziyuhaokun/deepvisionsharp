# yu-ui 迁移指南（第二批页面用）

> 适用对象：`src/DeepVisionSharp.Shell` 里仍在用 WPF-UI 4.3.0 的 85 个 xaml / 12,042 行。
> 翻译规则以 [`src/yu-ui/PORTING-CONTRACT.md`](../src/yu-ui/PORTING-CONTRACT.md) 为准；本文件只解决「页面怎么换控件、按什么顺序换、换完怎么验」。
> 验收基准：`src/DeepVisionSharp.Shell/Views/YuUiStyleProbeWindow.xaml`（1240×1600，7 区 + AppStyles 区，离屏渲染截图）。
>
> 用量统计口径：`grep -ro "<ui:NAME"` 的开标签数（已剔除 `<ui:NAME.Xxx>` 属性元素，不统计闭标签与 `{ui:Xxx}` 引用）。任务书里给的 189/164/86… 是「全文提及数」，含闭标签与属性值引用，不能当元素数用；本文件用下面的元素数做工作量估算。

---

## 0. 两条不许违反的规矩

1. **颜色 / 圆角 / 尺寸 / 字号一律 `{DynamicResource Iv*}`**。只有源里本来就是硬编码常量的才照抄常量（例：`IvBadgeStyle` 的 `11`、`IvModelCardStyle` 的 `420×280 / 6`）。
2. **页面里禁止写 implicit `<Style TargetType="TextBlock">`**。原因见 §1，这不是风格问题，是可见 bug。

---

## 1. 全局文字基线（所有迁移页面统一写法）

WPF 没有 Avalonia 的应用级文字主题，`TextBlock` 默认是 `Segoe UI / 12px / 黑`；源 Fluent 默认是 14px。**基线靠继承传递，不靠 implicit 样式**：在页面/窗口根元素上声明一次。

```xml
<Window ...
        Background="{DynamicResource IvBackgroundBrush}"
        FontFamily="{DynamicResource IvFontFamily}"
        FontSize="{DynamicResource IvFontSizeBody}"
        Foreground="{DynamicResource IvText1Brush}">
```

`UserControl` 页面同理（`Foreground` 是 `Control.Foreground`，它就是 `TextElement.ForegroundProperty` 的 add-owner，可继承到子级 TextBlock）。

**为什么不能用 implicit `TextBlock` 样式**：WPF 里样式 setter 的优先级高于属性继承，而 `ContentPresenter` 渲染字符串内容时自动生成的 `TextBlock` 也吃 implicit 样式。写一条 `<Style TargetType="TextBlock"><Setter Property="FontSize" Value="14"/><Setter Property="Foreground" .../></Style>` 的后果是：

- `IvIconButtonStyle` 的 `FontSize=16` 被压成 14（图标按钮整体缩小）；
- `IvPrimaryButtonStyle` 的 `TextOnPrimaryBrush` 白字被压成 `IvText1Brush` 深字（主按钮文字变成不可读）。

需要局部改字色/字号时用：① `TextElement.Foreground` / `TextElement.FontSize` 附加属性（可继承，源 AppStyles 的 badge 就是这么写的）；② `{StaticResource IvCaptionTextStyle}` 这类 keyed 样式。

---

## 2. WPF-UI → yu-ui 控件映射表

| WPF-UI 控件 | 实测元素数 | yu-ui 等价物 | 迁移写法 | 降级 / 视觉差异 |
| --- | ---: | --- | --- | --- |
| `ui:Button` | 100 | `Button` + 样式键 | 见 §2.1 Appearance 映射；`Icon="{ui:SymbolIcon X}"` 改成内容为 `Grid[yu:Icon + TextBlock]` | `Icon` 属性无等价物（71 处 + `ui:Button.Icon` 属性元素 2 处）；`ui:Button.ContextMenu`（4 处）原样保留 |
| `ui:SymbolIcon` | 47 | `yu:Icon` + `yu:IvIcons` | `Data="{x:Static yu:IvIcons.Home}" ViewSize="16"` | 另有 90 处 `Icon="{ui:SymbolIcon X}"` 属性引用（Button 71 / TextBox 11 / NavigationViewItem 7 / PasswordBox 1），合计 137 处符号引用；64 个符号里 13 个、23 处无等价几何（见 §2.2） |
| `ui:Card` | 32 | `ContentControl` + `{StaticResource IvCardStyle}` | 原来写在 `ui:Card` 上的 `Padding="14"/"24"/"28,22"` 必须搬到 `ContentControl.Padding`（实测 8 处 14、3 处 24、1 处 28,22），漏搬会让内容顶到边框 | 圆角变小：`IvCardStyle` 用 `IvRadiusSmall`(2)，外观只有底色 + 1px 细描边；`Card.RenderTransform`（1 处）原样保留 |
| `ui:TextBox` | 16 | `TextBox`（隐式样式 `{x:Type TextBox}`） | 直接换标签名 | `PlaceholderText` 无等价：改成默认 `Text`、或叠一个 `IvText3Brush` 的 TextBlock 占位（15 处 `ui:TextBox` + 1 处 `ui:PasswordBox` 都要过一遍）；`Icon="{ui:SymbolIcon X}"`（11 处）无等价，需要在输入框左侧叠 `yu:Icon` |
| `ui:NumberBox` | 17 | `yu:NumericBox` | `Value/Minimum/Maximum/Step/DecimalPlaces/FixedDecimalPlaces/Unit/IsReadOnly` | `MaxDecimalPlaces` → `DecimalPlaces`；`LargeChange/SmallChange` → `Step`；无 `AcceptExpression`、无 `SpinButtonPlacement` |
| `ui:ProgressRing` | 11 | `ProgressBar` | `IsIndeterminate="True"` | **环形变条形**，占位尺寸要重排（源 20/24px 环 → 需要给 ProgressBar 定宽高） |
| `ui:TitleBar` | 1 | `yu:ThemedWindow` | 由窗口模板提供 `PART_TitleBar`（`CaptionHeight=52`） | `ExtendsContentIntoTitleBar` 概念消失（`ThemedWindow` 本身就是自绘标题栏）；`Header/CenterContent/TrailingContent` 要挪到窗口内容里 |
| `ui:FluentWindow` | 1 | `yu:ThemedWindow` | `MainWindow.xaml` 根标签换掉；`MainWindow.xaml.cs` 的基类改 `ThemedWindow` | 见 §4 组 0 |
| `ui:NavigationViewItem` | 7 | **无等价物** | 建议 `ListBox` + `IvSidebarItemStyle`（32 高、左 2px 强调指示条） | 见下方判断 |
| `ui:NavigationView` | 1 | **无等价物** | 同上：`ListBox` + 图标/文字项，自带标题栏 | 见下方判断 |
| `ui:ToggleSwitch` | 1 | `ToggleButton` + `{StaticResource IvSwitchStyle}` | 内容即左侧标签（实测只出现在 `Pages/Sections/SettingsAppearanceSection.xaml`） | 源的 `OnContent/OffContent` 无等价：只保留 `Content`，状态由 `IsChecked` 驱动 |
| `ui:PasswordBox` | 1 | `PasswordBox`（WPF 原生） | 直接换；无 yu-ui 主题 | 外观退回 WPF 默认（与 `IvInputBrush` 口径不一致），且它的 `PlaceholderText` 要靠叠层 TextBlock 兜；要么单独排一轮自绘、要么接受差异 |
| `ui:ContentDialogHost` | 1 | `yu:Dialog` | `IsOpen/Title/Footer` + `Content`；弹窗本体改 `Dialog` | `ContentDialog` 的按钮栏/异步结果没有等价物：按钮自己放进 `Footer` |
| `ui:SnackbarPresenter` | 1 | **无等价物** | 第 3 批前**保留 WPF-UI**，或自建 `Border` 浮层（`IvPopupBrush` + `IvRadiusOverlay` + `IvPopupShadow`） | 这是移除 WPF-UI 的两个硬门槛之一 |

### NavigationView 的保留 / 自建判断

实测只有 `MainWindow.xaml` 一个文件用 NavigationView，7 个固定顶层项、无分组、无嵌套、无搜索、无折叠需求。结论：**自建，不新增 NavigationView 控件**。

- 做法：`ListBox`（横向图标 + 文字）配 `IvSidebarItemStyle`，选中项驱动 `INavigationService`（替换成 Shell 自己的导航服务或直接 `Frame`）。
- 只要不出现「分组标题 / 折叠成图标条 / 页脚项 / 面包屑」这四类需求，就不需要专门写 `NavigationView`；真出现时再单独立项（那是一个 300 行级的控件加模板）。

### 2.1 `ui:Button` 的 Appearance 映射

| Appearance | 实测数 | 替换为 |
| --- | ---: | --- |
| （未写 Appearance） | 17 | `<Button>`（隐式样式，`IvControlFillBrush` 底 + `IvControlBorderBrush` 描边） |
| `Secondary` | 46 | 同上 |
| `Transparent` | 22 | `Style="{StaticResource IvSubtleButtonStyle}"`（透明底、透明描边，hover 只加 `IvStateHoverBrush` 层） |
| `Primary` | 15 | `Style="{StaticResource IvPrimaryButtonStyle}"`（SemiBold + `IvPrimaryBrush` 底 + `TextOnPrimaryBrush` 字） |
| `Danger`（本仓库 0 处） | 0 | `Style="{StaticResource IvDangerButtonStyle}"` |

> 口径提醒：`Appearance="Secondary"` 与「不写 Appearance」在 WPF-UI 里是同一个外观，两者都落到 yu-ui 的**隐式** Button 样式，不要给它们套 `IvSubtleButtonStyle`（那会少一圈描边）。

### 2.2 `ui:SymbolIcon` → `yu:IvIcons`（64 个符号 / 137 处）

`IvIcons` 现有 77 个几何，命名不带 `24` 后缀。`yu:Icon` 的尺寸由 `ViewSize` 决定（默认 24），源里靠 `FontSize` 控制大小的写法要改成 `ViewSize`。

**A. 直接等价（40 个符号 / 96 处）**

`FolderOpen24→FolderOpen`(15)、`Add24→Add`(6)、`Image24→Image`(5)、`Delete24→Delete`(5)、`Copy24→Copy`(5)、`ArrowClockwise24→Refresh`(5)、`Play24→Play`(4)、`Search24→Search`(3)、`Folder24→Folder`(3)、`DocumentText24→Document`(3)、`ArrowRight24→ArrowRight`(3)、`Album24→Images`(3)、`Warning24→Warning`(2)、`Alert24→Warning`(1)、`MoreHorizontal24→More`(2)、`Flash24→Bolt`(2)、`Edit24→Edit`(2)、`Dismiss24→Close`(2)、`ChevronRight24→ChevronRight`(2)、`ChevronLeft24→ChevronLeft`(2)、`Checkmark24→Check`(2)、`ChevronUp24→ChevronUp`(1)、`ChevronDown24→ChevronDown`(1)、`CheckmarkCircle24→Success`(1)、`CheckboxChecked24→Checkbox`(1)、`ErrorCircle24→Error`(1)、`QuestionCircle24→Help`(1)、`ImageMultiple24→Images`(1)、`ImageAltText24→Image`(1)、`Home24→Home`(1)、`HandRight24→Pointer`(1)、`CursorHover24→Pointer`(1)、`Cube24→Cube`(1)、`Clock24→Clock`(1)、`Settings24→Settings`(1)、`Save24→Save`(1)、`Sparkle24→Sparkles`(1)、`TextT24→Typography`(1)、`TaskListSquareLtr24→List`(1)、`ClipboardTaskListLtr24→Recipe`(1)

**B. 近义降级（11 个符号 / 18 处，可接受但要进截图核对清单）**

| WPF-UI | 处数 | 降级为 | 视觉差异 |
| --- | ---: | --- | --- |
| `Database24` | 3 | `Tray` | 圆柱（数据库）→ 托盘轮廓 |
| `TagMultiple24` / `Tag24` | 3 / 1 | `Bookmark` | 标签→书签 |
| `NumberSymbol24` | 2 | `Calculator` | # 号 → 计算器 |
| `FolderArrowRight24` | 2 | `FolderOpen` | 少了出向箭头 |
| `Square24` | 2 | `Shape` | 实心方块 → 四角点框 |
| `PaintBrush24` | 1 | `Palette` | 笔刷 → 调色盘 |
| `Pin24` | 1 | `Flag` | 图钉 → 旗 |
| `Wrench24` | 1 | `Settings` | 扳手 → 齿轮 |
| `DeveloperBoard24` | 1 | `Grid` | 开发板 → 4 宫格 |
| `ArrowLeft24` | 1 | `ChevronLeft` | 长箭头 → 尖角 |

**C. 无等价物（13 个符号 / 23 处）—— 只在页面确实需要时才向 `Icons/IvIcons.cs` 追加几何**

`ArrowUpload24`(4)、`ChartMultiple24`(3)、`ArrowUndo24`(2)、`ArrowRedo24`(2)、`Eraser24`(2)、`LockClosed24`(2)、`Stop24`(2)、`Bug24`(1)、`Code24`(1)、`FullScreenMaximize24`(1)、`Subtract24`(1)、`WeatherMoon24`(1)、`ArrowDownload24`(1)

> 追加规则：24×24 画布、`StrokeThickness=1.75`、圆头圆角（`Icon.xaml` 统一处理），命名对齐 `IvIcons` 现有词表（不带 `24` 后缀、用语义名而非控件名）；由拥有 `Icons/IvIcons.cs` 的那一路统一追加，页面迁移不要在页面里内联 geometry。

---

## 3. `Themes/AppStyles.xaml` 的 8 个键（页面可直接引用）

源 `Themes/AppStyles.axaml` 用 Avalonia 的 `Classes` 表达可选装饰，WPF 没有类叠加语义，因此一个类一个 keyed Style，组合态用 `BasedOn` 派生。

| 键 | TargetType | 对应源的类选择器 | 用途 |
| --- | --- | --- | --- |
| `IvBadgeStyle` | `Border` | `Border.badge` | 徽标小标签：淡底 + 细描边 + 等宽 11px `IvText2Brush` |
| `IvStatusDotStyle` | `Ellipse` | `Ellipse.statusDot` | 8×8 状态圆点（默认 `IvSuccessBrush`） |
| `IvStatusSquareStyle` | `Rectangle` | `Rectangle.statusSquare` | 7×7 状态方块 |
| `IvModelCardStyle` | `Border` | `Border.modelCard` | 420×280 模型卡，悬停描边换 `IvAccentBorderBrush` + 上浮 2px |
| `IvSelectableRowStyle` | `Border` | `Border.selectableRow` | 可选行（10,8 内边距 + 1px 描边 + 6 圆角） |
| `IvSelectableRowSelectedStyle` | `Border` | `Border.selectableRow.selected` | 选中行（BasedOn 上一行） |
| `IvStatusTextStyle` | `TextBlock` | `TextBlock.statusText` | `IvText2Brush` 次要文字 |
| `IvStatusTextWarningStyle` | `TextBlock` | `TextBlock.statusText.warning` | `IvWarningBrush` 警告文字 |

**条件态怎么写（WPF 没有 `Classes.selected="{Binding IsSelected}"`）**：

```xml
<Border>
    <Border.Style>
        <Style TargetType="Border" BasedOn="{StaticResource IvSelectableRowStyle}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                    <Setter Property="BorderBrush" Value="{DynamicResource IvAccentBrush}" />
                    <Setter Property="Background" Value="{DynamicResource IvAccentSubtleBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
</Border>
```

（源的 `Border.selectableRow.selected` 现在只有 `GallerySamplePickerDialogContent.xaml` 一处用，迁移时按上面写法就地展开即可；`IvSelectableRowSelectedStyle` 留给静态选中态的场景。）

**`IvModelCardStyle` 的 150ms 上浮**：WPF 的样式 setter 上的 `Freezable`（`TranslateTransform`）会被冻结，冻结对象不能做动画，所以库里只给悬停终态（瞬时上浮 2px）。要平滑过渡的页面在自己标记上声明 `<TranslateTransform/>`，再按 `Storyboard.TargetProperty="RenderTransform.(TranslateTransform.Y)"` 补 0→-2 / -2→0 的 150ms `CubicEase EaseOut` 动画（本仓库 `DashboardPageResources.xaml` 的 `ProjectCardStyle` 就是可用范例）。

---

## 4. 分批迁移建议

四组的实测规模（文件数 = 该目录递归的 xaml 数；行数 = 这些文件合计）：

| 组 | 文件数 | 行数 | 主要 WPF-UI 用量 |
| --- | ---: | ---: | --- |
| `Views/Pages`（含 `Sections/`） | 39 | 5,626 | Button 57、SymbolIcon 31、Card 27、ProgressRing 9、TextBox 3、NumberBox 3、ToggleSwitch 1 |
| `Views/Dialogs`（含 `Contents/`） | 21 | 2,396 | TextBox 12、Button 12、SymbolIcon 6、Card 5、NumberBox 3、ProgressRing 2、PasswordBox 1 |
| `Views/Labeling`（含 `Cards/`） | 6 | 866 | Button 20、SymbolIcon 1 |
| `Views/Training`（含 `Sections/`） | 12 | 2,295 | NumberBox 11、Button 7、SymbolIcon 5、TextBox 1 |
| `MainWindow.xaml` + `Views/Controls/` | 2 | 528 | FluentWindow 1、NavigationView 1、NavigationViewItem 7、TitleBar 1、ContentDialogHost 1、SnackbarPresenter 1、Button 4、SymbolIcon 4 |

> 表中 `SymbolIcon` 只算元素。各组另有 `Icon="{ui:SymbolIcon X}"` 属性引用（Button/TextBox/NavigationViewItem），实测：Pages 52、Labeling 18、Dialogs 11、Training 0、Views/Controls 1、MainWindow 7，合计 90 处 —— 这些也会变成 `yu:Icon`，做工作量估算时要和上面的 SymbolIcon 元素数相加（全库 137 处符号引用）。

**建议顺序：Labeling → Training → Dialogs → Pages → MainWindow。** 从「按钮密集但结构简单」到「模板密集」到最后动壳。

### 组 3：Views/Labeling（6 文件 / 866 行）—— 灰度批

- 风险点：`Cards/` 里的 4 个 LabelingBoxListCard / ClassListCard / DetectionToolsCard / EditActionsCard 是按钮最密的地方（20 个），且大量依赖 `Resources/StatusPalette.xaml` 的硬编码状态色（`#15803D` 之类）；这批色不走 Iv 令牌，迁移时保持不动，只换控件。
- 验收：整组 `ui:` 归零；`LabelingPage` 截图与迁移前逐像素 diff（只允许出现按钮圆角/描边差异）；4 张卡片各滚一遍 hover/disabled。

### 组 4：Views/Training（12 文件 / 2,295 行）

- 风险点：`NumberBox` 11 处最集中，语义映射容易漏（`MaxDecimalPlaces` → `DecimalPlaces`、`SmallChange` → `Step`）；训练参数网格是手工对齐的数字列，`IvControlHeight=30` 与 WPF-UI 的行高不同，列宽会整体移位；`TrainingRunPanel.xaml` 有固定高度行内布局，改字号会撑破。
- 验收：迁移前先截一张参数表基线图，迁移后逐行比对行高/列宽；`NumberBox` 逐个确认 `Unit`、`DecimalPlaces`、`Minimum/Maximum` 与源一致。

### 组 2：Views/Dialogs（21 文件 / 2,396 行）

- 风险点：`TextBox` 12 处，`PlaceholderText` 无等价物，必须逐个改成默认 `Text` 或叠占位 TextBlock；`PasswordBox` 1 处（`Contents/SettingsDebugAccessDialogContent.xaml`）无 yu-ui 主题；`ui:ContentDialogHost` 是服务层（`IContentDialogService`）驱动的，换 `yu:Dialog` 会牵到 ViewModel/Service 接口；`StartupMessageWindow.xaml` 是普通 `Window`，不是 `FluentWindow`。
- 验收：每个 dialog 都能实际弹一次（成功/失败/取消三条路径）；文本框聚焦态（`IvFocusRingBrush` 外扩环）与只读态各截一张。

### 组 1：Views/Pages（39 文件 / 5,626 行）—— 最大批，拆两拨

- 第一拨：`Pages/Sections/`（26 文件）——这些是纯局部区块，DataTemplate 集中，先做能快速暴露 implicit 样式冲突。
- 第二拨：13 个页面壳 + 4 个 `*PageResources.xaml`。
- 风险点：① `*PageResources.xaml` 里存着页面级 implicit `TextBlock` / `ui:Button` 隐式样式覆盖（例如 `DashboardPageResources.xaml` 有一行 `<Style TargetType="{x:Type TextBlock}">`）——**这些必须删掉换成根元素继承**，否则会和 yu-ui 的控件级令牌打架；② `Card` 27 处，其中 `Card.RenderTransform` 动画 1 处（`Sections/DashboardProjectCardWallSection.xaml`），换 `ContentControl` 后动画目标要对齐到 `ContentControl`；③ `ProgressRing` 9 处全部要重排占位尺寸；④ `ui:Button.Icon` / `Icon="{ui:SymbolIcon X}"` 要拆成 `Grid[yu:Icon + TextBlock]`（Pages 组最密，全库 73 处），会改变按钮内部水平排布。
- 验收：每页一张 1600×1200 截图 + 一次主题切换（`IvTheme.Apply(Dark)`）截图；`hover/pressed/disabled` 三态在探针页统一核对（页面只核对布局，不重复核对控件视觉）。

### 组 0：MainWindow（最后做）

- 前置条件：`Snackbar` 与 `ContentDialog` 有替代方案（当前没有），否则 WPF-UI 不能移除。
- 风险点：`MainWindow.xaml.cs` 直接依赖 `FluentWindow`、`INavigationService`、`ISnackbarService`、`IContentDialogService`、`ApplicationThemeManager`；`NavigationView` 自建后导航契约（`NavigationViewItem.TargetPageType`）要改成自己的路由。
- 验收：7 个导航项逐项点进点出、标题栏三键（最小化/最大化/关闭）、任务中心浮层、两种主题。

---

## 5. 共存策略（三步，零回归优先）

### 第 0 步：只 merge 令牌（零回归的起点）

`App.xaml` 的 `MergedDictionaries` 里加一行：

```xml
<ResourceDictionary Source="pack://application:,,,/yu-ui;component/YuUiTokens.xaml" />
```

- 只提供 `Iv*` 令牌，**不含隐式样式**，现有页面外观一个像素都不变。
- 判据：build 通过 + 现有页面截图无差异。
- 用途：让已迁移的局部区块能引用令牌，未迁移的区块继续用 WPF-UI。

### 第 1 步：按组启用隐式样式（页面级 merge，不是 App 级）

某个页面迁完后，只在**它自己的** `ResourceDictionary` 里 merge 全量样式：

```xml
<ResourceDictionary Source="pack://application:,,,/yu-ui;component/YuUiStyles.xaml" />
```

或直接用入口 `pack://application:,,,/yu-ui;component/YuUiTheme.xaml`（= 令牌 + 样式）。探针页 `YuUiStyleProbeWindow.xaml` 就是后者。

- **不要**在这一步把 `YuUiStyles.xaml` 挂到 `App.xaml`：那会让所有还没迁移的页面跟着换脸（按钮/输入框/滚动条/进度条全变），无法定位回归来源。
- 判据：该目录 `grep -r "schemas.lepo.co/wpfui"` 0 命中，且该组截图 diff 只包含预期差异。

### 第 2 步：全局启用 + 移除 WPF-UI

全部 5 组迁完后：把 `YuUiTheme.xaml` 提到 `App.xaml` 的第 0 位，然后按下面 5 条判据全绿才动包引用。

1. `grep -rn "schemas.lepo.co/wpfui" src --include=*.xaml` → 0 命中；
2. `grep -rn "Wpf.Ui" src --include=*.cs` → 0 命中（`FluentWindow`/`INavigationService`/`ISnackbarService`/`IContentDialogService`/`ApplicationThemeManager`/`SymbolRegular` 全部清掉）；
3. Snackbar / ContentDialog 行为已有 yu-ui 实现（**当前缺，这是硬门槛**）；
4. 两种主题都能跑：`IvTheme.Apply(app, IvThemeMode.Dark)` / `Light` 切换后无残留旧色（`Palette.Dark.xaml` 需先进总入口的换入流程）；
5. 探针页 + 各组截图基线全绿。

满足后再删 `DeepVisionSharp.Shell.csproj` 的 `WPF-UI` / `WPF-UI.DependencyInjection`（同时 `ApplicationThemeManager` 的调用点要换成 `IvTheme`）。App.xaml 的这两行一起删：

```xml
<ui:ThemesDictionary Theme="Dark" />
<ui:ControlsDictionary />
```

移除前的任何时刻都可回退：第 0/1 步都是单行 merge 改动（`App.xaml` 现在还没 merge `YuUiTokens.xaml`，所以第 0 步那一行仍要人工加上），删掉即恢复 WPF-UI 外观；包引用留到最后一步就是保底退路。

---

## 6. 已知无法 1:1 的清单（跨页通用）

| 源的特性 | WPF 现状 | 视觉差异预期 |
| --- | --- | --- |
| `ProgressRing`（环形进度，11 处） | `ProgressBar` 不确定态（条形） | 形状不同，占位尺寸要重排 |
| `Transitions` 集合语法（hover 浓度渐变、`TransformOperationsTransition`） | `Trigger` + `Storyboard` | 能动画的属性（Opacity）可对齐；样式 setter 上的 `Freezable`（`TranslateTransform`）被冻结 → 上浮 2px 为瞬时 |
| `BoxShadow` 内阴影 | `DropShadowEffect`（只有外阴影） | 内阴影效果丢失（对话框卡片最明显） |
| `ClipToBounds` 的圆角裁剪 | `Border.Clip` / `OpacityMask` 手写几何 | 圆角图片/圆角日志框需要额外 Clip 绑定 |
| `ThemeDictionaries` 自动切换 | `IvTheme.Apply` 运行时换字典 | 切换是显式的，不会跟随系统 |
| `PlaceholderText`（15 处 `ui:TextBox` + 1 处 `ui:PasswordBox`） | 无等价物 | 需要默认文本或叠加占位 TextBlock |
| `PointerOver` 在禁用态下的组合语义 | `IsMouseOver` 在 `IsEnabled=False` 时仍可能为真 | 依赖它的样式必须显式补 `IsEnabled=False` 的 `Trigger` |

---

## 7. 收尾自检命令

```bash
# 1) 组内是否还有 WPF-UI 引用
grep -rn "schemas.lepo.co/wpfui" src/DeepVisionSharp.Shell --include=*.xaml | wc -l

# 2) 是否还在页面里写 implicit TextBlock 样式（必须 0）
grep -rn 'Style TargetType="{x:Type TextBlock}"' src/DeepVisionSharp.Shell --include=*.xaml

# 3) 颜色是否还在用 StaticResource 引 Iv  brush（必须 0）
grep -rn 'StaticResource Iv[A-Za-z]*Brush' src/yu-ui src/DeepVisionSharp.Shell --include=*.xaml

# 4) 探针页本身是否还引用 WPF-UI（必须 0）
grep -n "lepo.co" src/DeepVisionSharp.Shell/Views/YuUiStyleProbeWindow.xaml
```

---

## 8. 执行结果（方案 B）

> 方案 B 的顺序：先按 §4 的建议顺序分波迁页面，页面归零后再动壳（`FluentWindow` → `yu:ThemedWindow`、自建导航、yu-ui 的提示条/对话框宿主），最后删 `App.xaml` 的 WPF-UI 字典与包引用。
> 本节数据全部取自本仓库提交与 `rg` 实测，命令附在每条之后。

### 8.1 页面迁移（三波，共 85 个文件）

| 批次 | 提交 | 文件数 | 范围 |
| --- | --- | ---: | --- |
| 第一波 | `82a5b8c` | 52（全 xaml） | `Views/Pages`（含 `Sections/`）、4 个 `*PageResources.xaml`、`Views/Dialogs`（含 `Contents/` 与 `Sections/`） |
| 第二波 | `6224986` | 28（全 xaml） | `Views/Training`（含 `Sections/`）、`Views/Labeling`（含 `Cards/`）、`Views/Controls/TaskCenterPanel.xaml`、`Views/Controls/ImageLightboxOverlay.xaml` |
| 第三波 | `99cb63b` | 5（3 xaml + 2 cs） | 页面级最后 3 处残留：`Controls/PasswordBoxAssist.cs`、`Converters/ProjectTypeVisualConverter.cs`、`Contents/SettingsDebugAccessDialogContent.xaml`，外加 `Dialogs/StartupMessageWindow.xaml` 与 `.xaml.cs` |

<!-- 口径：git show --numstat --format= <commit> | wc -l -->

`src/yu-ui` 本身 42 个文件（随 `f0df86f` 入库）：令牌四件套（`Palette` / `Palette.Dark` / `Metrics` / `Typography` / `Motion`）、14 个 `Styles/*.xaml`、7 个自绘控件、77 个图标几何、`Themes/Generic.xaml` 与 `Themes/AppStyles.xaml`。三级入口不变：`YuUiTokens.xaml`（仅令牌）/ `YuUiStyles.xaml`（含隐式样式）/ `YuUiTheme.xaml`（全量）。

### 8.2 壳层替换（第 3 步）

旧 → 新调用链，接口定义与命名元素见 [`docs/shell-chrome-contract.md`](shell-chrome-contract.md)：

| 旧（WPF-UI 4.3.0） | 新（yu-ui / 自建） | 行为等价说明 |
| --- | --- | --- |
| `ui:FluentWindow` + `ui:TitleBar`（`ExtendsContentIntoTitleBar`） | `yu:ThemedWindow`（52px 自绘标题栏） | 标题栏三键由模板 `PART_MinimizeButton` / `PART_MaximizeButton` / `PART_CloseButton` 提供；原标题栏里的「项目名」与「主题切换」移到标题栏下方的应用工具条行 |
| `ui:NavigationView` + 7×`ui:NavigationViewItem` | 固定展开的 `ListBox`（`NavList`）+ `IvSidebarItemStyle` | 7 项的顺序、文案、图标与 `AutomationProperties.AutomationId` 逐个不变；`Tag` 承载 `AppRoute` |
| `ui:ContentDialogHost` + `IContentDialogService` | `yu:Dialog`（`ShellDialog`）+ `IDialogHost.ShowAsync` 返回 `AppDialogResult` | 按钮文案、`AppDialogResult` 取值、取消令牌语义保持 |
| `ui:SnackbarPresenter` + `ISnackbarService` | `ISnackbarHost`（`SnackbarKind`：Info / Success / Warning / Error + `TimeSpan`） | 未挂载时静默丢弃（与旧行为一致，启动早期不崩） |
| 导航（`INavigationService.Navigate`） | `IPageHost`（`CurrentPage` / `Show`） | 未挂载时抛 `InvalidOperationException("导航宿主未挂载")`；页面在窗口建立后才导航 |
| `ApplicationThemeManager.Apply/Changed` | `AppThemeService` 实现 `IAppThemeService`（`Current` / `Changed` / `Apply`） | 主题切换落到 `IvTheme.Apply(app, IvThemeMode)`，切换后提示条/对话框/页面同步换色 |
| 服务层 `SetPresenter(...)` 注入 | `Attach(host)` | `MainWindow` 构造函数在 `InitializeComponent()` 之后依次 `Attach(this)`：导航 → 提示条 → 对话框 |

### 8.3 全局资源接线（App.xaml）

`MergedDictionaries` 只剩两条，顺序固定：

1. `pack://application:,,,/yu-ui;component/YuUiTheme.xaml` —— **全量**入口（令牌 + 隐式样式），不是只含令牌的 `YuUiTokens.xaml`。`ui:ThemesDictionary` / `ui:ControlsDictionary` 删掉后，`Button` / `TextBox` / `ListBox` / `ScrollBar` / `ComboBox` / `ProgressBar` 的全局基线只能由这里提供；只 merge 令牌会让这些原生控件退回 WPF 默认外观，与已迁移页面的 yu-ui 视觉对不上（§5 第 2 步「全局启用」）。
2. `pack://application:,,,/DeepVisionSharp;component/Resources/StatusPalette.xaml` —— Shell 自有状态色（训练任务、任务中心、模型库、数据集角色）。放第二位：键名与 `Iv*` 不重叠，页面级覆盖时仍以本地字典优先。

主题切换不依赖 merge 位置：`IvTheme.Apply` 会深度找到这里嵌套的 `Tokens/Palette.xaml` 并原地换入（位置不变，合并字典按倒序解析）。

`Resources/StatusPalette.xaml` 本次只做审计与注释订正：21 个键仍全部有页面引用，没有任何 `Iv*` 同名冲突，原注释里提到的 WPF-UI 语义刷子（`SystemFillColor*`）实测已无使用者；**颜色值一个未动**（训练/标注/任务状态色必须与主题无关，白字对比度才稳）。

### 8.4 WPF-UI 移除状态

- `src` 下 XAML 命名空间 `http://schemas.lepo.co/wpfui/2022/xaml`、C# 的 `Wpf.Ui*` 与 `SymbolRegular`：0 命中，App.xaml 的 `xmlns:ui` 与两个字典已删除；
- 判据命令（结果须为 0 行）：

```bash
rg -n 'lepo\.co/wpfui' src --glob '*.xaml'
rg -n 'Wpf\.Ui|SymbolRegular' src --glob '*.cs'
rg -n 'ui:|Wpf\.Ui|lepo\.co|SymbolRegular' src
```

- `packaging/installer/src/RuntimePackage.SetupHost.Wpf` 是独立的安装器进程，自带 `WPF-UI` 包与自己的 `App.xaml` 字典，**不在本次范围**：它不引用 `src/yu-ui`，改它只会扩大安装回归面（判据命令要排除该目录）。

### 8.5 剩余风险

| 风险 | 影响 | 处置 / 验收点 |
| --- | --- | --- |
| `ThemedWindow` 标题栏与原 `ui:TitleBar` 内容合并 | 标题栏只有「左图标 + 标题 + 右三键」；原 `TitleBar` 里的项目名与主题切换必须挪到标题栏下方工具条行，漏挪即功能丢失 | 已按 §5 挪到工具条行；验收：项目名显示、主题切换可点且图标随主题变化、三键可用、双击标题栏最大化 |
| Mica 毛玻璃丢失 | `WindowBackdropType="Mica"` 无等价物，窗口底色改为 `{DynamicResource IvBackgroundBrush}`，标题栏与内容同色，没有亚克力透景 | 接受视觉变化，不再引用 `WindowBackdropType`；深浅切换后确认标题栏与内容无断层 |
| `NavigationView` 折叠能力丢失 | 自建侧栏固定展开（宽 168），没有汉堡按钮、折叠成图标条与条目上翻 | 当前产品无折叠/分组/搜索需求（§2 判断）；真出现需单独立项重写侧栏 |
| 残留 WPF-UI 语义色键（实测，本轮未清） | 删掉 `ui:ThemesDictionary` 后 `TextFillColor*Brush` / `ControlStrokeColor*Brush` / `ControlFillColor*Brush` / `SolidBackgroundFillColor*Brush` / `ApplicationBackgroundBrush` / `CardBackgroundFillColor*Brush` / `SystemAccentColor*` 不再存在。仍按名查找的 7 个文件：`Controls/CanvasCornerFloat.xaml`、`Controls/CanvasCornerFloat.xaml.cs`、`Controls/ParamHelpButton.xaml`、`Views/Dialogs/StartupMessageWindow.xaml.cs`、`Views/Dialogs/Contents/InferenceBatchReportDialogContent.xaml.cs`、`Views/Dialogs/Contents/ProjectLabelStatisticsDialogContent.xaml.cs`、`Views/Training/Controls/MetricLineChart.cs` | `DynamicResource` 引用静默落回 WPF 默认值（不崩、不报错，只是颜色错）；`.cs` 侧 `TryFindResource` / `FindBrush` / `SnapshotBrush` 带硬编码 fallback（`CanvasCornerFloat` 落 `#202020`，`MetricLineChart` 落 `Brushes.DimGray`/`LightGray`），退化不致命。建议下一轮按 `TextFillColor*→IvText*`、`ControlStrokeColor*→IvStroke*`、`ControlFillColor*→IvSurface*`、`ApplicationBackgroundBrush→IvBackgroundBrush`、`SystemAccentColorPrimaryBrush→IvAccentBrush` 映射清掉 |
