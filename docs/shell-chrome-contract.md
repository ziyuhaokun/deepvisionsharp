# 壳层去 WPF-UI 契约（方案 B）

> 三方并行作业的唯一契约：**服务层 / MainWindow 壳层 / App 资源与收尾**。
> 文件所有权见第 6 节，越界即视为失败。

## 1. 目标

把 WPF-UI 从 Shell 里彻底摘掉，外壳改为 yu-ui 语言：
自绘标题栏（`yu:ThemedWindow`）+ 自建侧边导航 + yu-ui 的提示条 / 对话框宿主。
**对用户可见的行为必须保持**：导航项与顺序、主题切换、退出确认、提示条语义、对话框按钮与返回值、自动化 ID。

## 2. 宿主契约（父代理已落地，勿改签名）

`src/DeepVisionSharp.Shell/Services/Hosting/ISnackbarHost.cs`

```csharp
public enum SnackbarKind { Info, Success, Warning, Error }

public interface ISnackbarHost
{
    bool IsAttached { get; }
    void Show(string title, string message, SnackbarKind kind, TimeSpan duration);
}
```

`Services/Hosting/IPageHost.cs`

```csharp
public interface IPageHost
{
    bool IsAttached { get; }
    object? CurrentPage { get; }
    void Show(object page);
}
```

`Services/Hosting/IDialogHost.cs`

```csharp
public interface IDialogHost
{
    bool IsAttached { get; }
    Task<AppDialogResult> ShowAsync(YuUI.Controls.Dialog dialog, CancellationToken cancellationToken);
    void Close(AppDialogResult result);
}
```

`Services/IAppThemeService.cs`（已加两个成员）

```csharp
public interface IAppThemeService
{
    ShellThemePreference Current { get; }
    event EventHandler? Changed;
    void Apply(ShellThemePreference preference);
}
```

## 3. 挂载方式（替代 WPF-UI 的 SetPresenter 模式）

宿主就是 `MainWindow` 本身。MainWindow 构造函数里 `InitializeComponent()` 之后按序调用：

```csharp
_navigationService.Attach(this);   // this : IPageHost
_snackbarNotifier.Attach(this);    // this : ISnackbarHost
_dialogService.Attach(this);       // this : IDialogHost
```

**Attach 只存在于具体类型上**（`AppNavigationService` / `ShellSnackbarNotifier` / `AppDialogService`），不在 `IAppNavigationService` 等接口上。因此 MainWindow 构造注入具体类型：

```csharp
AppNavigationService navigationService,
ShellSnackbarNotifier snackbarNotifier,
AppDialogService dialogService,
IAppThemeService themeService,          // 接口
IAppDialogService appDialogService,     // 接口
```
ShellHost 相应改成「具体类型单例 + 接口转发到同一实例」，避免两个实例（导航与对话框都要）：

```csharp
services.AddSingleton<AppNavigationService>();
services.AddSingleton<IAppNavigationService>(sp => sp.GetRequiredService<AppNavigationService>());
services.AddSingleton<AppDialogService>();
services.AddSingleton<IAppDialogService>(sp => sp.GetRequiredService<AppDialogService>());
```

> **为什么必须别名注册**：窗口 `Attach` 的是具体类型实例；VM 与退出确认通过接口拿实例。若两者不是同一个单例，接口那一个的宿主永远未挂载 → 退出确认会静默失效。

### 3.1 侧栏选中态同步（行为等价要求）

旧 `NavigationView` 在 `Navigate(typeof(XxxPage))` 时会自动选中对应导航项；自建版要复刻，但**不得**在壳层复制第二份「页面类型 ↔ 路由」映射。

- `AppNavigationService` 新增 `public event EventHandler<AppRoute>? Navigated;`，在 `Navigate` 里 `host.Show(page)` 成功**之后** raise（只 raise 一次，异常时不 raise）。事件只挂在具体类型上。
- `MainWindow` 订阅该事件，在 `NavList` 里按 `Tag == route` 选中对应项；用一个 `_isSyncingSelection` 布尔守卫，在 `SelectionChanged` 里先判断它，避免“选中→导航→再选中”循环。
- 初始导航改为 `_navigationService.Navigate(AppRoute.Dashboard)`（走服务让事件驱动选中），不要手工先选中。
- 窗口关闭时退订事件。

`AppNavigationService` 构造签名：`(IServiceProvider services, WorkspaceSessionService workspace)`；**不要在构造里取宿主**（MainWindow 本身在 DI 图里，避免循环依赖），Attach 后置天然避开。

未 Attach 时：
- 导航：抛 `InvalidOperationException("导航宿主未挂载")`（页面在窗口建立后才导航）
- 提示条：静默丢弃（与旧行为一致，启动早期不崩）
- 对话框：抛 `InvalidOperationException("对话框宿主未挂载")`

## 4. MainWindow 必须提供的命名元素

| x:Name | 类型 | 用途 |
| --- | --- | --- |
| `PageHost` | `ContentControl` | `IPageHost` 的内容区 |
| `NavList` | `ListBox` | 侧边导航；每项 `Tag` 为 `AppRoute` 枚举值，选中即导航 |
| `ThemeToggleButton` | `Button` | 主题切换（`IvIconButtonStyle`） |
| `ThemeToggleIcon` | `yu:Icon` | 图标随主题在 `IvIcons.WeatherSunny`?`WeatherMoon` 间切换（**见第 5 节：IvIcons 里没有天气图标，用 Bolt/Sparkles 与 Scan 的替代方案由壳层 lane 自定并登记**） |
| `SnackbarLayer` | `Grid`/`Border` | `ISnackbarHost` 的浮层容器（右下角、`Panel.ZIndex=100`、`IsHitTestVisible=False`） |
| `ShellDialog` | `yu:Dialog` | `IDialogHost` 的对话框 |
| `DialogLayer` | `Grid` | 对话框浮层（`Panel.ZIndex=200`） |
| `ShellTaskCenter` | `controls:TaskCenterPanel` | 保留原名与位置（`ZIndex=90`） |

导航项与顺序（不得增删、不得改文案与自动化 ID）：

| 顺序 | 文案 | 图标 | 自动化 ID | AppRoute |
| --- | --- | --- | --- | --- |
| 1 | 项目 | `IvIcons.Home` | `dv.nav.dashboard` | `AppRoute.Dashboard` |
| 2 | 图库 | `IvIcons.Images` | `dv.nav.gallery` | `AppRoute.Gallery` |
| 3 | 标记 | `IvIcons.Bookmark` | `dv.nav.labeling` | `AppRoute.Labeling` |
| 4 | 模型 | `IvIcons.Grid` | `dv.nav.models` | `AppRoute.Models` |
| 5 | 训练 | `IvIcons.Play` | `dv.nav.training` | `AppRoute.Training` |
| 6 | 推理 | `IvIcons.Bolt` | `dv.nav.inference` | `AppRoute.Inference` |
| 7 | 设置（底部） | `IvIcons.Settings` | `dv.nav.settings` | `AppRoute.Settings` |

样式：`ListBox` 隐式样式 + 每项 `Style="{StaticResource IvSidebarItemStyle}"`（yu-ui 已提供，32 高、左 2px 强调条）。

## 5. 已知缺口（必须登记，不许自己造视觉）

1. `IvIcons` 没有天气/月亮类图标：主题切换按钮用 `IvIcons.Bolt`（深色时）与 `IvIcons.Scan`（浅色时）之外的方案请**优先**用文字 ToolTip 表达，图标用 `IvIcons.Settings` 会误导 —— 由壳层 lane 在返回里登记最终选择。
2. `ThemedWindow` 自带 52px 标题栏（左侧图标+标题、右侧三个窗口按钮）。原来的 `ui:TitleBar` 里还有「项目名（居中）」与「主题切换（右）」两个内容，**必须挪到标题栏下方的应用工具条行**，不能丢。
3. `WindowBackdropType="Mica"` 无等价物：去掉毛玻璃，窗口底色用 `{DynamicResource IvBackgroundBrush}`。
4. `ui:NavigationView` 的汉堡折叠/无障碍上翻在自建版里没有：侧栏固定展开（宽度 168），不做折叠。

## 6. 文件所有权

| 负责人 | 文件 |
| --- | --- |
| 父代理（已完成） | `Services/Hosting/ISnackbarHost.cs`、`Services/Hosting/IPageHost.cs`、`Services/Hosting/IDialogHost.cs`、`Services/IAppThemeService.cs`、本文件 |
| P 服务层 | `Services/Navigation/AppNavigationService.cs`、`Services/ShellSnackbarNotifier.cs`、`Services/Dialogs/AppDialogService.cs`、`Services/WpfAppThemeService.cs`（改名为 `Services/AppThemeService.cs`）、`ShellHost.cs`、`DeepVisionSharp.Shell.csproj`、`Services/Labeling/LabelingKeyboardNavigation.cs` |
| Q 壳层 | `MainWindow.xaml`、`MainWindow.xaml.cs` |
| R 资源与收尾 | `App.xaml`、`App.xaml.cs`、`docs/yu-ui-migration.md`、`CHANGELOG.md`、`CHANGELOG.Developer.md` |

## 7. 返回要求

每个 lane 返回：文件清单、行为等价性说明（旧 → 新调用链）、无法等价点、自检输出
（`rg -n 'Wpf\.Ui|ui:|lepo\.co|SymbolRegular'` 在你负责的文件里为 0）。
