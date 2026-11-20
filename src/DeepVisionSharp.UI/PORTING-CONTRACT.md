# DeepVisionSharp.UI 移植契约（Avalonia → WPF）

> 本文件是并行作业的唯一契约。**动手前先读完**。任何与本文件冲突的做法，以本文件为准。

## 0. 源与目标

| 项 | 值 |
| --- | --- |
| 源（只读） | `C:\Users\yu\codes\products\vision\deepvision\src\InCube.UI.Avalonia`（Avalonia 12.1.5，net10.0） |
| 目标（写） | `C:\Users\yu\codes\github\deepvisionsharp\src\DeepVisionSharp.UI`（WPF，net10.0-windows） |
| 要求 | 颜色 / 尺寸 / 圆角 / 字号 / 间距 / 各交互态视觉**逐值照抄**；XAML 语法从 Avalonia 翻译成 WPF |

这不是复制：Avalonia 与 WPF 的样式机制不同，必须**翻译**，但视觉结果必须一致。

## 1. 命名规范（硬性）

- 目录 `src/DeepVisionSharp.UI/`，项目文件 `DeepVisionSharp.UI.csproj`，`AssemblyName = DeepVisionSharp.UI`，`RootNamespace = DeepVisionSharp.UI`
- C# 命名空间：控件 → `DeepVisionSharp.UI.Controls`；主题服务 → `DeepVisionSharp.UI`
- XAML 使用方引入：`xmlns:dvsui="http://schemas.deepvisionsharp.ui/xaml"`
- **资源键名一律原样保留**（`Iv*` 前缀，逐字符不变），便于与源逐键回归比对
- 显式样式键（`IvPrimaryButtonStyle`、`IvCardStyle`…）原样保留

## 2. 文件所有权（严格，禁止越界写别人的文件）

| 负责人 | 拥有的文件 |
| --- | --- |
| 父代理 | `DeepVisionSharp.UI.csproj`、`AssemblyInfo.cs`、`Themes/Generic.xaml`、`DeepVisionSharpUITokens.xaml`、`DeepVisionSharpUIStyles.xaml`、`DeepVisionSharpUITheme.xaml`、`PORTING-CONTRACT.md`、slnx、Shell 接线 |
| A 令牌 | `Tokens/Palette.xaml`、`Tokens/Metrics.xaml`、`Tokens/Typography.xaml`、`Tokens/Motion.xaml`、`IvTheme.cs` |
| B 图标+输入框控件 | `Icons/IvIcons.cs`、`Controls/Icon.cs`、`Controls/Icon.xaml`、`Controls/NumericBox.cs`、`Controls/NumericBox.xaml` |
| C 输入类样式 | `Styles/Button.xaml`、`Styles/ButtonImplicit.xaml`、`Styles/TextInput.xaml`、`Styles/Selection.xaml`、`Styles/ComboBox.xaml`、`Styles/Switch.xaml`、`Controls/IvPlaceholder.cs` |
| D 容器列表滚动 | `Styles/Common.xaml`、`Styles/Container.xaml`、`Styles/List.xaml`、`Styles/TreeView.xaml`、`Styles/Menu.xaml`、`Styles/ScrollBar.xaml`、`Styles/ProgressBar.xaml`、`Styles/Slider.xaml` |
| E 布局控件 | `Controls/FormRow.cs`、`Controls/FormRow.xaml`、`Controls/FormSection.cs`、`Controls/FormSection.xaml`、`Controls/Dialog.cs`、`Controls/Dialog.xaml`、`Controls/Window.xaml` |
| F 全局文字样式+验收页 | `Themes/AppStyles.xaml`、`Views/DeepVisionSharpUIStyleProbeWindow.xaml(.cs)`（在 Shell 内）、`docs/DeepVisionSharp.UI-migration.md` |

## 3. 源文件 → 目标文件映射

| 源（Avalonia） | 行数 | 目标（WPF） | 负责人 |
| --- | --- | --- | --- |
| `Tokens/Palette.axaml` | 187 | `Tokens/Palette.xaml` | A |
| `Tokens/Metrics.axaml` | 30 | `Tokens/Metrics.xaml` | A |
| `Tokens/Typography.axaml` | 63 | `Tokens/Typography.xaml` | A |
| `Tokens/Motion.axaml` | 11 | `Tokens/Motion.xaml` | A |
| `Icons/IvIcons.g.cs` | 249（77 个图标） | `Icons/IvIcons.cs` | B |
| `Controls/Icon.cs` | 87 | `Controls/Icon.cs` + `Icon.xaml` | B |
| `Controls/NumericBox.cs` + `Themes/Forms.axaml` 中 NumericBox 部分 | 119 | `Controls/NumericBox.cs` + `NumericBox.xaml` | B |
| `Styles/Button.axaml` | 134 | `Styles/Button.xaml` | C |
| `Styles/ButtonImplicit.axaml` | 65 | `Styles/ButtonImplicit.xaml` | C |
| `Styles/TextInput.axaml` | 109 | `Styles/TextInput.xaml` | C |
| `Styles/Selection.axaml` | 204 | `Styles/Selection.xaml` | C |
| `Styles/ComboBox.axaml` | 112 | `Styles/ComboBox.xaml` | C |
| `Styles/Switch.axaml` | 76 | `Styles/Switch.xaml` | C |
| `Styles/Common.axaml` | 14 | `Styles/Common.xaml` | D |
| `Styles/Container.axaml` | 46 | `Styles/Container.xaml` | D |
| `Styles/List.axaml` | 110 | `Styles/List.xaml` | D |
| `Styles/TreeView.axaml` | 27 | `Styles/TreeView.xaml` | D |
| `Styles/Menu.axaml` | 47 | `Styles/Menu.xaml` | D |
| `Styles/ScrollBar.axaml` | 67 | `Styles/ScrollBar.xaml` | D |
| `Styles/ProgressBar.axaml` | 57 | `Styles/ProgressBar.xaml` | D |
| `Styles/Slider.axaml` | 121 | `Styles/Slider.xaml` | D |
| `Themes/AppStyles.axaml` | 84 | `Themes/AppStyles.xaml`（**只含**键型样式：IvBadgeStyle / IvStatusDotStyle / IvStatusSquareStyle / IvModelCardStyle / IvSelectableRowStyle / IvSelectableRowSelectedStyle / IvStatusTextStyle / IvStatusTextWarningStyle） | F |
| `Themes/Forms.axaml` | 176 | 拆进 `Controls/{FormRow,FormSection,Dialog,NumericBox}.xaml` | E（NumericBox 归 B） |
| `Themes/Generic.axaml` | 96 | `Controls/Window.xaml`（ThemedWindow + 三个标题栏按钮） | E |

## 4. 令牌键清单（A 必须全部覆盖，逐值照抄）

**Palette.xaml —— Light 与 Dark 各一套，键名完全相同（95 个）**

```
IvBackgroundBrush IvSurface1Brush IvSurface2Brush IvSurface3Brush IvSurface4Brush
IvSurfaceDisabledBrush IvSurfaceSubtleHoverBrush IvSurfaceSubtlePressedBrush IvSurfaceSelectedBrush
IvBandBrush IvControlFillBrush IvInputBrush IvPopupBrush IvOverlayBrush
IvStrokeSubtleBrush IvStrokeBrush IvStrokeStrongBrush IvControlBorderBrush IvControlBorderHoverBrush
IvAccentBorderBrush IvDangerBorderBrush IvWarningBorderBrush
IvText1Brush IvText2Brush IvText3Brush IvTextDisabledBrush IvTextOnAccentBrush
IvPrimaryBrush IvPrimaryHoverBrush IvPrimaryPressedBrush IvTextOnPrimaryBrush
IvAccentBrush IvAccentHoverBrush IvAccentPressedBrush IvAccentSubtleBrush IvAccentTextBrush
IvFocusBrush IvFocusRingBrush
IvSuccessBrush IvSuccessSubtleBrush IvSuccessTextBrush
IvWarningBrush IvWarningTextBrush IvWarningSubtleBrush
IvDangerBrush IvDangerHoverBrush IvDangerPressedBrush IvDangerSubtleBrush IvDangerTextBrush IvDangerRingBrush
IvInfoBrush IvInfoSubtleBrush IvRowAltBrush IvSwitchTrackBrush IvThumbBrush
IvStateHoverBrush IvStatePressedBrush
IvColorSuccess IvColorWarning IvColorDanger IvColorAccent
IvLogErrorFgBrush IvLogErrorBgBrush IvLogErrorAccentBrush
IvLogWarnFgBrush IvLogWarnBgBrush IvLogWarnAccentBrush
IvLogInfoFgBrush IvLogDebugFgBrush IvLogTraceFgBrush IvLogDefaultFgBrush
IvLogHighlightBgBrush IvLogHighlightFgBrush
IvHudBackgroundBrush IvHudHoverBackgroundBrush IvHudPressedBackgroundBrush
IvHudBorderBrush IvHudHoverBorderBrush IvHudTextBrush IvHudTextStrongBrush IvHudScrimBrush
IvHudInfoTextBrush IvHudDangerTextBrush
IvImageCanvasBrush IvCaptionCloseHoverBrush IvCaptionClosePressedBrush IvCaptionCloseTextBrush
IvControlShadow
```

**Metrics.xaml（21 个）**：`IvControlHeightCompact IvControlHeightSmall IvControlHeight IvIconButtonSize IvControlHeightLarge IvRadiusSmall IvRadiusButton IvRadiusControl IvRadiusFocusRing IvRadiusFocusRingPill IvRadiusOverlay IvControlPadding IvInputPadding IvCardPadding IvStrokeThickness IvSpace1 IvSpace2 IvSpace3 IvSpace4 IvSpace5 IvSpace6`

**Typography.xaml（24 个）**：`IvFontFamily IvDisplayFontFamily IvMonoFontFamily IvFontSizeTiny IvFontSizeMini IvFontSizeSmall IvFontSizeCaption IvFontSizeBodySmall IvFontSizeBody IvFontSizeBodyStrong IvFontSizeSubtitle IvFontSizeHeading IvFontSizeTitle IvFontSizeDisplay IvIconSize IvCaptionTextStyle IvBodyTextStyle IvBodyStrongTextStyle IvSubtitleTextStyle IvTitleTextStyle IvDisplayTextStyle IvCodeTextStyle IvNumericTextStyle`

**Motion.xaml（4 个）**：`IvMotionTimeSpanFast(120ms) IvMotionTimeSpanNormal(200ms) IvMotionTimeSpanIndeterminate(1200ms) IvMotionEase(CubicEaseOut)`

> `Iv*TextStyle` 在 Avalonia 是 `Style` 对象，WPF 里同样用 `<Style x:Key>`（TargetType TextBlock）。

## 5. 主题机制（Avalonia ThemeDictionaries → WPF）

Avalonia 用 `ThemeDictionaries` 按 `RequestedThemeVariant` 自动切换；WPF 没有该机制。

- `Tokens/PaletteLight.xaml` / `Tokens/PaletteDark.xaml`：**不要**建这两个文件，改成 **`Tokens/Palette.xaml` 一个文件里不合并主题**，而是：
  - `Tokens/Palette.xaml` = Light 全套（默认，App 直接 merge 就能用）
  - `Tokens/Palette.Dark.xaml` 允许 A 额外产出，但不进总入口，供 `IvTheme` 运行时换入
- `IvTheme.cs` 提供：`public enum IvThemeMode { Light, Dark }`、`public static void Apply(Application app, IvThemeMode mode)`、`public static IvThemeMode Current { get; }`
  - 实现：把 `Tokens/Palette.xaml`（或 `Palette.Dark.xaml`）以 `pack://application:,,,/DeepVisionSharp.UI;component/Tokens/Palette.xaml` 形式换入 `Application.Current.Resources.MergedDictionaries`，位置保持在第 0 位，不破坏其它字典
- 所有样式引用颜色一律用 `{DynamicResource Iv...}`，**禁止 StaticResource**（否则换不了主题）

## 6. 控件 API 契约（WPF 版）

| 控件 | 属性（全部 DependencyProperty） | 模板部件名 |
| --- | --- | --- |
| `dvsui:Icon` | `Data(Geometry)`、`ViewSize(double, 24)`、`StrokeThickness(double, 1.75)` | `PART_Path`（Path，Stroke=Foreground，Data={TemplateBinding Data}） |
| `dvsui:NumericBox` | `Value(double, TwoWay)`、`Minimum(0)`、`Maximum(1000)`、`Step(1)`、`DecimalPlaces(2)`、`FixedDecimalPlaces(false)`、`Unit(string)`、`IsReadOnly(bool)` | `PART_TextBox`、`PART_UpButton`、`PART_DownButton` |
| `dvsui:FormRow` | `Header(object)`、`Description(string)` | 默认 `PART_ContentPresenter` |
| `dvsui:FormSection` | `Header(object)`、`HasHeader(bool)` | `PART_ContentPresenter` |
| `dvsui:Dialog` | `Title(string)`、`Footer(object)`、`IsOpen(bool)` | `PART_ContentPresenter`、`PART_FooterPresenter`、`PART_TitleText` |
| `dvsui:ThemedWindow` | 继承 `Window`；`CaptionHeight(double, 52)` | `PART_TitleBar`、`PART_MinimizeButton`、`PART_MaximizeButton`、`PART_CloseButton` |
| `dvsui:IvPlaceholder`（附加属性，`static class`） | `Text(string?)`：`RegisterAttached` + `GetText/SetText`，null 校验（`d` 为 null 抛 `ArgumentNullException`） | 无样式；供 TextBox/ComboBox 模板以 `{Binding Path=(dvsui:IvPlaceholder.Text), RelativeSource={RelativeSource TemplatedParent}}` 取占位文本 |

> **占位文本**：Avalonia 的 `TextBox.PlaceholderText` 在 WPF 中不存在。统一用附加属性 `dvsui:IvPlaceholder.Text`，**禁止**借用 `Tag`（现有 Shell 已有 `Tag` 的其它用途，会撞车）。

控件基类：`Icon`/`NumericBox` → `Control` + `DefaultStyleKey`；`FormRow`/`FormSection`/`Dialog` → `ContentControl`；`ThemedWindow` → `Window`（WPF 自定义 Window 走 `WindowStyle=None` + `AllowsTransparency` 按源实现）。

## 7. Avalonia → WPF 翻译速查（必须遵守）

| Avalonia | WPF |
| --- | --- |
| `<Style Selector="Button">` | `<Style TargetType="Button">` |
| `<Style Selector="Button.iv-primary">` / `x:Key` | 同 key 的 `<Style x:Key="IvPrimaryButtonStyle" TargetType="Button">` |
| 伪类 `:pointerover` `:pressed` `:focus` `:disabled` | `<Trigger Property="IsMouseOver" Value="True">`、`IsPressed`、`IsKeyboardFocused`、`IsEnabled=False` |
| `Selector="^ /template/ Border#PART_X"` | 模板内部直接命名 `x:Name` + `<Trigger ... TargetName="PART_X">` |
| `CornerRadius` 资源 | `<CornerRadius x:Key="IvRadiusButton">2</CornerRadius>`（WPF 支持） |
| `x:Double` | `<sys:Double x:Key>: `（`xmlns:sys="clr-namespace:System;assembly=System.Runtime"`） |
| `Transitions` / `Animation` | Trigger + Storyboard，时长取 `IvMotionTimeSpan*`，缓动取 `IvMotionEase` |
| `BoxShadow` 资源 | `<DropShadowEffect>` |
| `{StaticResource}`（源里有用到） | 本项目统一 `{DynamicResource}`（颜色类）；几何/尺寸可 Static |
| `FontWeight="SemiBold"` | `FontWeight="SemiBold"`（同值） |
| `TextBlock` 隐式样式 | `<Style TargetType="TextBlock">` 只在 `Themes/AppStyles.xaml` 里出现，其它文件禁止隐式改 TextBlock |
| `ContentControl` 的 `ContentPresenter` | `<ContentPresenter Content="{TemplateBinding Content}" .../>` |
| `ScrollViewer` 内 Template 名 | WPF 必须叫 `PART_ContentHost`（TextBox/ScrollViewer 强制） |
| ComboBox 模板 | 必须含 `PART_Popup`、`PART_EditableTextBox`（可编辑时）、`PART_ItemsPresenter` 由 `ItemsPresenter` 提供 |

**Avalonia 有、WPF 没有的**（遇到就按此降级，并在返回里列出）：`ThemeDictionaries` 自动切换、`Transitions` 集合语法、`Classes` 伪类系统、`ClipToBounds` 的圆角裁剪（WPF 用 `Border.Clip` 或 `OpacityMask`）、`BoxShadow` 内阴影、`PointerOver` 在禁用态下的组合语义。

## 8. 硬性禁止

0. **全库禁止任何 implicit `<Style TargetType="TextBlock">`**（包括 `Themes/AppStyles.xaml`）。原因：WPF 的隐式样式优先级高于属性继承，会压掉控件级字号（`IvIconButtonStyle` 的 16px 变 14px），还会连带 ContentPresenter 里由字符串自动生成的 TextBlock，使主色按钮的白字变深色。全局文字基线改为「页面/窗口根元素声明 `FontFamily`、`FontSize`，前景色用 `TextElement.Foreground` 附加属性（它才继承，Panel 没有 Foreground 属性）」。

1. 库内**禁止**引用 WPF-UI（`http://schemas.lepo.co/wpfui/2022/xaml`）任何类型
2. **禁止**修改令牌数值（颜色/尺寸/字号/时长一律照抄源）
3. **禁止**跑 `dotnet build` / `dotnet restore`（父代理串行编译，避免 obj 争用）
4. **禁止**写不属于自己的文件（见第 2 节）
5. **禁止**新增 NuGet 包
6. **禁止**引入编译警告（仓库 `TreatWarningsAsErrors=true`，LangVersion 14，Nullable enable）
7. **禁止**用 `StaticResource` 引用 `Iv*Brush` 颜色

## 9. 交付要求（每个 lane 的返回内容）

1. 创建/修改的文件列表（绝对路径）
2. 逐文件「源 → 目标」对应说明：哪些控件/状态已 1:1 覆盖
3. **无法 1:1 的点**：源里的什么视觉特性、WPF 怎么降级的、视觉差异预期
4. 自检结论：键名是否与源完全一致（列出你比对的方法）、是否有遗漏状态（hover/pressed/focus/disabled/selected）
5. 每个 .xaml 顶部必须有一行注释：`<!-- 源: Styles/Button.axaml (134 行) | 转换: selector→Style/Trigger, 伪类→Trigger -->`
