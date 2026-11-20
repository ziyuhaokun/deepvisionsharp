# Avalonia Shell 迁移说明

> 读者：接手本仓库、继续把生产壳从 WPF 迁到 Avalonia 的工程师。
> 本文只写能复核的事实：所有文件数 / 行数都给了统计命令，涉及「当前状态」的地方标了基线提交。
>
> **基线提交**：`9c82dda feat(avalonia): Shell 接真实业务数据 + 照抄全部剩余页面`。
> 写这份文档时仓库里有 4 条并行作业在改 Avalonia Shell（组合根与导航、启动流程、对话框、训练页 XAML），
> 非 `docs/` 下的文件随时可能变化。文中标 **【本轮】** 的条目在落笔时正被改动，采信前先读代码。

---

## 0. 先学会复核本文的数字

数字全部来自 `9c82dda`，命令在仓库根执行（Git Bash / pwsh 均可）：

```bash
# 某目录下 .cs 的文件数与总行数（把 9c82dda 换成 HEAD 可看当前工作区的已提交状态）
path=src/DeepVisionSharp.Shell.Avalonia; ext=cs
files=$(git ls-tree -r --name-only 9c82dda -- "$path" | grep "\.$ext$")
echo "files=$(echo "$files" | wc -l)"
for f in $files; do git show "9c82dda:$f"; done | wc -l
```

数端口、数页面、数对话框内容视图：

```bash
git ls-tree -r --name-only 9c82dda -- src/DeepVisionSharp.Presentation/Ports            | wc -l
git ls-tree -r --name-only 9c82dda -- src/DeepVisionSharp.Presentation/ViewModels       | wc -l
git ls-tree -r --name-only 9c82dda -- src/DeepVisionSharp.Shell.Avalonia/Views          | grep -cE '/[A-Za-z]+Page\.axaml$'
git ls-tree -r --name-only 9c82dda -- src/DeepVisionSharp.Shell.Avalonia/Views/Dialogs/Contents | grep -c '\.axaml$'
```

---

## 1. 现状总览：三个壳/层的关系

### 1.1 一句话分工

| 层 | 项目 | 状态 | 职责 |
| --- | --- | --- | --- |
| 业务层 | `Domain` / `Application` / `Infrastructure` / `Tiling` / `TorchSharp` / `Bootstrap` | 稳定 | 领域模型、用例、SQLite、推理、训练子进程。与 UI 无关 |
| 共享表现层 | `src/DeepVisionSharp.Presentation` | 稳定，两个壳共用 | ViewModel（`ObservableObject` / `RelayCommand`）、应用服务、**框架无关端口**。零 UI 引用 |
| 生产壳 | `src/DeepVisionSharp.Shell` | **生产在用，功能完整** | WPF + `yu-ui`(WPF)。7 个页面、16 个对话框内容视图、任务中心、启动期能力（文件日志/单实例/崩溃转储/异常上报） |
| 迁移中壳体 | `src/DeepVisionSharp.Shell.Avalonia` | **迁移中，不能替换生产壳** | Avalonia 12 + `yu-ui.avalonia`。容器、端口、7 个页面（基线时 3 个接真 VM）、26 个对话框内容视图 |
| 控件库 | `src/yu-ui`（WPF）/ `src/yu-ui.avalonia` | 两套并行 | 自绘壳层控件与设计令牌。**两套互不引用**，同名控件各自实现 |

### 1.2 依赖方向（箭头表示「谁引用谁」，被引用方永不知道引用方存在）

```
        ┌──────────────────────────┐        ┌────────────────────────────────┐
        │ DeepVisionSharp.Shell    │        │ DeepVisionSharp.Shell.Avalonia │
        │ (WPF, 生产)               │        │ (Avalonia, 迁移中)              │
        │  yu-ui (WPF)             │        │  yu-ui.avalonia                │
        └───────────┬──────────────┘        └───────────────┬────────────────┘
                    │                                       │
                    └───────────────┬───────────────────────┘
                                    ▼
                    ┌───────────────────────────────┐
                    │ DeepVisionSharp.Presentation  │  ← 只有 ViewModel / 服务 / 端口
                    │ net10.0，零 UI 依赖            │     不知道 WPF、Avalonia、yu-ui
                    └───────────────┬───────────────┘
                                    ▼
        ┌───────────────────────────────────────────────────────┐
        │ Application ──► Domain                                │
        │ Infrastructure ──► Application（SQLite/文件/推理）      │
        │ Bootstrap：AddDeepVision() = Application + Infra 的注册 │
        └───────────────────────────────────────────────────────┘
```

### 1.3 数字速查（基线 `9c82dda`，复核命令见 §0）

| 对象 | 数量 |
| --- | --- |
| `Presentation` 的 `.cs` | 127 个文件 / 25,884 行（其中 `ViewModels` 63 个文件，69 个类型，40 个以 `ViewModel` 结尾；`Services` 49 个文件；`Ports` 10 个文件 = 9 个接口 + 1 个枚举） |
| `Presentation` 的端口实现（Avalonia） | 10 个（`Services/` 下 15 个 `.cs`，其余 5 个是壳内服务 / `Hosting/IPageHost.cs` / `ShellWindowHost.cs`） |
| WPF Shell | 116 个 `.cs` / 8,997 行；86 个 `.xaml` / 13,458 行 |
| Avalonia Shell | 106 个 `.cs` / 11,892 行；61 个 `.axaml` / 12,230 行 |
| Avalonia 页面 | 7 个（`Views/*Page.axaml`），其中 3 个接真 VM、4 个用 stub |
| Avalonia 对话框内容视图 | 26 个（`Views/Dialogs/Contents/*.axaml`；WPF 对应 16 个） |
| `AppDialogKind` | 20 项，Avalonia 描述表 20 项全覆盖 |
| `yu-ui`（WPF） | 10 个 `.cs` / 974 行；30 个 `.xaml` / 3,091 行 |
| `yu-ui.avalonia` | 7 个 `.cs` / 710 行；21 个 `.axaml` / 1,836 行 |
| Avalonia stub 上下文（`ViewModels/Sample*.cs`） | 7 个文件 / 3,533 行（另有 `ViewModels/Dialogs/` 5 个文件 / 1,329 行） |
| 自动化测试 | 4 个测试工程，覆盖 Tiling / TorchSharp / Application / Infrastructure；Presentation 与两个 Shell 无测试 |

三条必须记住的规则：

1. **两个壳互不引用。** 因此端口实现各留一份（例：`Services/TrainingResourceMonitor.cs` 与
   `Services/AvaloniaSystemResourceMonitor.cs` 是同一份逻辑的两个副本，见 §7.6）。
2. **Presentation 只能通过端口碰 UI。** ViewModel 里不允许出现 `BitmapImage`、`Bitmap`、`Dispatcher`、
   `Application.Current`。图像/矩形这类宿主对象以 `object` / 值对象透传，由壳的绑定去消费。
3. **业务注册只有一处**：`DeepVisionSharp.Bootstrap.DependencyInjection.AddDeepVision()`。两个壳都调它，
   然后各自补端口实现、壳服务、页面。

---

## 2. 工程结构与构建

### 2.1 目标框架（`grep -rn "TargetFramework" --include=*.csproj src tests`）

| 项目 | TFM | 关键包 / 引用 |
| --- | --- | --- |
| `DeepVisionSharp.Domain` | `net10.0` | 无 UI |
| `DeepVisionSharp.Application` | `net10.0` | MediatR 抽象、`IProcessLauncher` / `ILocalFileDialogService` 等端口接口 |
| `DeepVisionSharp.Presentation` | `net10.0` | `CommunityToolkit.Mvvm 8.4.0`、`Microsoft.Extensions.{DI,Logging}.Abstractions 10.0.0`；只引用 Domain + Application |
| `DeepVisionSharp.Bootstrap` | `net10.0-windows` | Application + Infrastructure，只提供 `AddDeepVision()` |
| `DeepVisionSharp.Shell`（WPF） | `net10.0-windows`，`UseWPF=true`，`PlatformTarget=x64` | `yu-ui`、`Microsoft.Extensions.Hosting 10.0.0`、`CommunityToolkit.Mvvm 8.4.0` |
| `DeepVisionSharp.Shell.Avalonia` | `net10.0-windows` | `Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent 12.1.5`、`yu-ui.avalonia`、`System.Diagnostics.PerformanceCounter 10.0.0`、`Microsoft.Extensions.Hosting 10.0.0` |
| `yu-ui`（WPF） | `net10.0-windows` | WPF 控件库 |
| `yu-ui.avalonia` | **`net10.0`（不是 `-windows`）** | Avalonia 控件库；能被非 `-windows` TFM 的宿主（离屏渲染进程）加载 |
| `DeepVisionSharp.Infrastructure` / `TorchSharp` | `net10.0-windows` | — |
| `DeepVisionSharp.Tiling` | `net10.0` | — |
| `tests/*`（4 个） | `net10.0` / `net10.0-windows` | 只覆盖 Tiling、TorchSharp、Application、Infrastructure；**没有 Presentation / Shell 测试** |

`yu-ui.avalonia` 的 `net10.0` 不是笔误：离屏渲染宿主与非 Windows 目标进程要加载它。
Avalonia Shell 的组合根因此**故意不用** `Host.CreateDefaultBuilder()`（`AppComposition.cs` 有注释）——
默认建造器会给 `-windows` 宿主挂 EventLog provider，在被非 `-windows` 进程加载时直接抛异常。

### 2.2 构建与整齐度约束

- 解决方案文件是 `DeepVisionSharp.slnx`（11 个 src 工程 + 4 个测试工程）。
- 常规构建：`dotnet build DeepVisionSharp.slnx -c Debug --nologo`。
  **不要单独跑 `dotnet restore`**：`dotnet build` 已含增量还原，单跑只多花一轮全量还原时间。
- `Directory.Build.props` 全局生效：`LangVersion=14.0`、`TreatWarningsAsErrors=true`、
  `NuGetAudit=true`（`NU1901..NU1904` 只告警不挡构建）。**0 warning 是硬门槛**，新代码不允许带告警。
- `AvaloniaUseCompiledBindingsByDefault=false`（`DeepVisionSharp.Shell.Avalonia.csproj`）。
  绑定路径是照 deepvision 的字符串路径抄的，运行期解析；绑定错误只能在渲染验证里发现（§5.3）。

### 2.3 「Presentation 零 UI 依赖」怎么验

命令一：禁用的命名空间必须没有任何命中（`System.Windows.Input` 是 BCL 的 `ICommand`，**不算** UI 依赖）：

```bash
rg -n "using System\.Windows;|using System\.Windows\.(Media|Controls|Shapes|Documents|Threading|Interop)|using Avalonia|using YuUI" \
   src/DeepVisionSharp.Presentation -g "*.cs"
# 期望：无输出（exit=1）
```

命令二：项目引用里不许出现壳 / 控件库：

```bash
rg -n "Reference" src/DeepVisionSharp.Presentation/DeepVisionSharp.Presentation.csproj
# 期望：只有 Domain、Application + 三个非 UI 包（CommunityToolkit.Mvvm / DI.Abstractions / Logging.Abstractions）
```

两条都在基线上跑过，均为空 / 符合预期。注意 `Presentation/ViewModels/**` 里的注释会提到
「WPF 是 BitmapImage，Avalonia 是 Bitmap」，那是说明，不是依赖。

---

## 3. 端口清单（10 个）

**先澄清口径**：`src/DeepVisionSharp.Presentation/Ports/` 下有 10 个文件，但其中 `UiDispatchPriority.cs`
是枚举、不是端口；真正的 10 个端口里有两个定义在 `Application`（文件选择、进程启动天然属于应用层能力）。
下表按「Avalonia 侧 10 个实现」列出，与两个壳的注册一一对应。

| # | 端口 | 定义位置 | WPF 实现 | Avalonia 实现 | 语义差异 / 注意点 |
| --- | --- | --- | --- | --- | --- |
| 1 | `IUiDispatcher` | `Presentation/Ports/IUiDispatcher.cs` | `Shell/Services/WpfUiDispatcher.cs` | `Shell.Avalonia/Services/AvaloniaUiDispatcher.cs` | 契约一致：无优先级重载在已处 UI 线程时内联执行；带 `UiDispatchPriority` 的重载**总是排队**，保证 `ApplicationIdle` 等到布局完成。差异在 owner 来源：WPF 在构造时抓 `Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher`（无 `Application.Current` 的 STA 验证进程也能用）；Avalonia 直接用 `Dispatcher.UIThread` 单例 |
| 2 | `IUiTimerFactory`（+ `IUiTimer`） | `Ports/IUiTimerFactory.cs`、`Ports/IUiTimer.cs` | `WpfUiTimerFactory.cs` | `AvaloniaUiTimerFactory.cs` | 同一语义：`DispatcherTimer`，tick 落在 UI 线程，`Dispose` 之后 tick 置空 |
| 3 | `ITextInputFocusProbe` | `Ports/ITextInputFocusProbe.cs` | `WpfTextInputFocusProbe.cs` | `AvaloniaTextInputFocusProbe.cs` | WPF 沿逻辑树+视觉树向上判 `TextBoxBase`/`PasswordBox`/可编辑或展开的 `ComboBox`；Avalonia 沿**视觉树**判 `TextBox`/`ComboBox{IsEditable,IsDropDownOpen}`，`PasswordBox` 无对应（yu-ui `NumericBox` 内是 `PART_TextBox`，仍被覆盖） |
| 4 | `IClipboardService` | `Ports/IClipboardService.cs` | `WpfClipboardService.cs` | `AvaloniaClipboardService.cs` | 异常契约相同（文件不存在 / 空文件抛）。差异：WPF 同步 `Clipboard.SetImage(BitmapImage)` 并 `Freeze()`；Avalonia 走 `SetBitmapAsync` 且**必须保留最近一次 Bitmap 不释放**（粘贴时才读），失败路径自己 Dispose——这条容易写成 use-after-dispose。Avalonia 的 `SetText` 在剪贴板不可用 / 空串时静默返回，WPF 会把底层异常抛出 |
| 5 | `IImageSourceFactory` | `Ports/IImageSourceFactory.cs` | `WpfImageSourceFactory.cs`（返回 `BitmapImage`） | `AvaloniaImageSourceFactory.cs`（返回 **Avalonia `Bitmap`**） | 接口返回 `object?`，ViewModel 不读成员，绑定层直接当图片用。解码语义逐值对齐：缩略图按解码宽度降采样；像素尺寸从解码后的位图读；读文件共享模式 `FileShare.ReadWrite \| FileShare.Delete`（别的进程正在写也能读）。**不要**在 ViewModel 里把返回值 cast 成具体类型 |
| 6 | `IAppThemeService` | `Ports/IAppThemeService.cs` | `Shell/Services/AppThemeService.cs` | `AvaloniaAppThemeService.cs` | 偏好枚举 `ShellThemePreference` 共用；`System` 的判定都读注册表 `HKCU\...\Themes\Personalize\AppsUseLightTheme`。落地机制不同：WPF 调 `IvTheme.Apply(app, IvThemeMode)`；Avalonia 设 `Application.RequestedThemeVariant` |
| 7 | `ISnackbarHost` | `Ports/ISnackbarHost.cs` | **`Shell/MainWindow.xaml.cs` 自己实现**（`SnackbarLayer` 右下角浮层） | `AvaloniaSnackbarHost.cs`（独立单例）+ `MainWindow` 订阅 `ShowRequested` 自己画 | 语义一致：`IsAttached=false` 时服务丢弃早期提示。接线方式不同：WPF 由 `ShellSnackbarNotifier.Attach(this)` 直接挂在窗口上；Avalonia 由容器把 `AvaloniaSnackbarHost` 注册两次（具体类型 + 接口别名指向同一实例），窗口构造时 `snackbarNotifier.Attach(_snackbarHost)` |
| 8 | `ISystemResourceMonitor` | `Ports/ISystemResourceMonitor.cs` | `Shell/Services/TrainingResourceMonitor.cs` | `AvaloniaSystemResourceMonitor.cs` | 实现逐行同源（`PerformanceCounter` 采 CPU/内存 + `nvidia-smi` 采 GPU），仅类名/日志上下文不同。包来源不同：WPF 壳 `UseWPF=true`，`System.Diagnostics.PerformanceCounter` 由 WindowsDesktop 共享框架提供（bin 里没有这个 dll）；Avalonia 壳不是 WindowsDesktop 应用，所以在 csproj 显式引了 `System.Diagnostics.PerformanceCounter 10.0.0` |
| 9 | `ILocalFileDialogService` | `Application/Galleries/GalleryTransferContracts.cs` | `LocalFileDialogService.cs`（`Microsoft.Win32`）；`--ui-test` 时换 `StubLocalFileDialogService.cs`（读 `{DataRoot}/.ui-test/*.txt`） | `AvaloniaLocalFileDialogService.cs`（`TopLevel.StorageProvider`） | 过滤串沿用 WPF 的 `名称\|*.ext;*.ext2\|…`，Avalonia 侧在 `ParseFilter` 里翻译成 `FilePickerFileType`；`*.*` 被过滤掉（Avalonia 用 `FilePickerFileTypes.All` 表达）。**Avalonia 侧没有 `--ui-test` 的 stub 实现**（§7.3） |
| 10 | `IProcessLauncher` | `Application/Ports/IProcessLauncher.cs` | `ShellProcessLauncher.cs` | `AvaloniaProcessLauncher.cs` | 同语义（路径不存在或启动失败返回 `false`，不抛），两份独立副本 |

窗口相关的静态入口（不是端口，但必须知道）：Avalonia 侧的剪贴板、文件选择、焦点探针、对话框都要一个
`TopLevel` / `Window`，容器构造时拿不到，所以窗口在构造末尾登记到
`Shell.Avalonia/Services/ShellWindowHost.cs`（静态 `Window?`），关闭时清空。
WPF 侧没有这一层，直接用 `Application.Current.MainWindow` / 自身实例。

---

## 4. 组合根与启动序列

### 4.1 WPF（生产壳）

`src/DeepVisionSharp.Shell/App.xaml.cs` + `ShellHost.cs`：

```
Program/App.OnStartup
 ├── DeepVisionLaunchOptions.Parse(args)        失败 → StartupMessageDialog.Show + Shutdown(-1)（--ui-test 时不弹窗）
 ├── ShellRuntimePaths.SelectForProcess(...)
 ├── RegisterExceptionHandlers()               DispatcherUnhandledException / UnhandledExceptionFilter /
 │                                             AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException
 ├── LocalDumpRegistration.EnsureEnabled()     WER LocalDumps（HKCU），--ui-test 跳过
 ├── SingleInstanceGuard.TryAcquire()          已有实例 → StartupMessageDialog.ShowAlreadyRunning + Shutdown
 ├── ShellHost.Create(options)                 Host.CreateDefaultBuilder + FileLoggerProvider + LogRetentionService
 │    ├── IDataRootProvider 覆盖（必须在 AddDeepVision 之前）
 │    ├── AddDeepVision() + AddLabelingInferenceWriteBack()
 │    ├── 6 个 WPF 端口（主题 / 调度器 / 计时器 / 焦点探针 / 剪贴板 / 图像解码）
 │    │   + 文件选择（带 --ui-test stub）+ 进程启动 + 异常上报
 │    ├── ShellSettingsService / ShellSnackbarNotifier / WorkspaceSessionService / ShellTaskCenterViewModel
 │    ├── AddTrainingWorkspace()（含 ISystemResourceMonitor 与训练页 VM）
 │    └── MainWindow(singleton) + 6 个页面 VM + 页面
 │        （ISnackbarHost / IDialogHost / IPageHost 不注册进 DI：MainWindow 构造尾自己 Attach）
 ├── await Host.StartAsync()
 ├── MainWindow.Show()
 └── OnExit（同步阻塞，8s 上限）：IInferenceShutdown.StopAndDisposeAsync → Host.StopAsync(3s) → Dispose
        → 释放单实例互斥量 → ShellExceptionJournal.ReportedCount>0 时 ExitCode=70
```

WPF 的 `App` 还提供了两个验证入口：`StartupArgsOverride`（验证宿主预置参数）和 `virtual CreateHost(...)`
（验证宿主只替换叶子注册）。`Views/YuUiStyleProbeWindow.xaml` 是控件视觉基准页。

### 4.2 Avalonia（迁移中）

`src/DeepVisionSharp.Shell.Avalonia/Program.cs` + `App.axaml.cs` + `Composition/AppComposition.cs`：

```
Program.Main
 └── BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)
      （AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()）
App.OnFrameworkInitializationCompleted          ← 没有 OnStartup，全部在这里
 ├── DeepVisionLaunchOptions.Parse(desktop.Args)   **没有 try/catch**（见 §4.3）
 ├── AppComposition.Create(options)
 │    ├── ShellRuntimePaths.FromOptions + SelectForProcess
 │    ├── new HostBuilder()（不是 CreateDefaultBuilder，见 §2.1）
 │    │    └── logging: ClearProviders + SimpleConsole(SingleLine) + AddDebug
 │    ├── IDataRootProvider 覆盖（必须在 AddDeepVision 之前）
 │    ├── AddDeepVision() + AddLabelingInferenceWriteBack() + AddAvaloniaShell(options)
 │    └── AddAvaloniaShell：10 个端口实现（snackbar 注两次：具体类型 + 接口别名，同实例）
 │         + IAppDialogService
 │         ShellSettingsService / ShellSnackbarNotifier / WorkspaceSessionService
 │         ShellTaskCenterViewModel / MainWindowViewModel
 │         DashboardViewModel(singleton) / GalleryViewModel(transient) / ModelsViewModel(transient)
 │         DashboardPage / GalleryPage / ModelsPage（transient）
 │         AvaloniaNavigationService(具体类型 + IAppNavigationService 同实例) / MainWindow(singleton)
 ├── host.Start()
 ├── desktop.MainWindow = sp.GetRequiredService<MainWindow>()
 └── desktop.Exit += OnDesktopExit → host.StopAsync(3s) + Dispose（同步阻塞，异常写 Trace）
MainWindow 构造
 ├── Resolve 导航 / VM / snackbar / theme / settings（容器为 null 时退回自建）
 ├── InitializeComponent()
 ├── 给 NavRail 里的 RadioButton 按 Content 文案挂路由（RoutesByLabel）
 ├── navigation.Attach(this)（IPageHost；宿主不进构造函数，避免 DI 循环）
 ├── snackbarNotifier.Attach(_snackbarHost)；ShellWindowHost.Window = this
 ├── 应用主题；navigation.Navigate(AppRoute.Dashboard)
 └── 无容器路径：new MainWindow() → DesignTimeComposition.TryCreateServices()（同一进程只试一次），
      建不起来就 null，页面各自退回 Sample*Context stub
```

### 4.3 两边尚未对齐的部分（全部为基线事实）

| # | WPF 有 | Avalonia 基线 | 影响 |
| --- | --- | --- | --- |
| 1 | `FileLoggerProvider` + `LogRetentionService`（`Presentation/Logging`、`Shell/Logging`） | 只有 console/debug | 现场排障没有落盘日志，日志保留策略也无从执行 |
| 2 | `SingleInstanceGuard` | 无 | 双击图标会起第二个进程，同一个 SQLite 数据根并写 |
| 3 | `LocalDumpRegistration`（WER LocalDumps） | 无 | 原生崩溃没有 minidump |
| 4 | 4 个未处理异常钩子 + `UnhandledExceptionReporter` + 异常日志 + 退出码 70 | 无（`App` 里只有 `Trace.WriteLine`） | UI 线程异常直接带走进程 |
| 5 | 启动失败弹 `StartupMessageDialog` 并 `Shutdown(-1)` | `DeepVisionLaunchOptions.Parse` 的异常没人接 | `--data-root` 少路径、`--ui-test` 无数据根 → 未处理异常 |
| 6 | 退出时 `IInferenceShutdown.StopAndDisposeAsync`（先停推理引擎） | 只 `host.StopAsync` | 推理子进程/句柄可能留着 |
| 7 | `options.IsUiTest` → `StubLocalFileDialogService`、异常提示全部静默 | 仍是真文件选择器 | 无自动化入口 |
| 8 | `PageDisposalScheduler`：页面卸载 5s 后 dispose 页面 VM | 导航把页面缓存在 `Dictionary<AppRoute, Control>` 里，永不释放 | 接上 4 个路由后，`Transient` 的页面 VM 事实上变成单例，切换页面不再重建（§7.5） |
| 9 | `StartupArgsOverride` + `virtual CreateHost` 验证入口 | `DesignTimeComposition`（设计时/无头用） | 两套验证入口语义不同，不能互相替代 |

表 1–6 项对应本轮 F3 的任务范围；7–9 见 §7。

---

## 5. 照抄原则与验收方法

### 5.1 唯一视觉真值：deepvision

`C:/Users/yu/codes/products/vision/deepvision/src/Vision.DeepVision.Shell.Avalonia` 是**只读**的视觉真值仓库
（同源的兄弟产品）。规则：

1. **逐值照抄**：宽高、内外边距、字号、行高、圆角、颜色令牌、`ControlTheme` 的每个 `Setter` 都按源写，
   不改「看着差不多」的值，不为了塞下内容调小字号或删内容。
2. 颜色/圆角/尺寸走 `{DynamicResource Iv*}`；只有源里本来就是硬编码常量时才照抄常量。
3. **页面根节点不要写 implicit `TextBlock` 样式**：Avalonia 里文字基线来自 `ThemedWindow` 的
   `ControlTheme`（`FontFamily=IvFontFamily`、`FontSize=IvFontSizeBody`=14，见
   `yu-ui.avalonia/Themes/Generic.axaml`），页面靠继承吃基线。用 implicit 样式压它会连带压掉图标按钮和主按钮配色
   （WPF 侧的历史坑，见 `docs/yu-ui-migration.md` §1）。
4. 源控件缺失时**在页内留几何**而不是换成「差不多的图标」，例：
   `Views/InferencePage.axaml` 顶部用 `StreamGeometry` 定义 `InferenceIcon.Stop` / `InferenceIcon.ChartMultiple`。
5. 不一致处一律在返回报告/本文 §7 记录，不许默默改行为。

### 5.2 迁移一个页面的推荐顺序

1. 读 deepvision 对应页面 + 它引用的 `Views/<Section>/*.axaml`，把控件树抄成 `.axaml`；
2. 用 `Sample<Xxx>Context`（`ViewModels/Sample*.cs`）提供与源 VM 同名的绑定路径，先让页面画得出来；
3. 接真 VM：容器注册（VM 生命周期照 WPF `ShellHost` 抄）+ 导航 `CreatePage` 建页 + code-behind 只换
   `DataContext` 来源（`_viewModel ?? new SampleXxxContext()`）；
4. 渲染取证（§5.3）+ 逐块与 deepvision 截图比对。

### 5.3 离屏渲染取证（可复制执行）

工具入口已入仓：`scripts/agent-render/render-avalonia-real.ps1`（pwsh 7，仅 Windows）；
预设是根目录 `agent-render.json` 的 `AvaloniaRealShell`，工厂是 `scripts/agent-render/ViewFactoryAvaloniaReal.cs`。
完整参数与目录说明见 `scripts/agent-render/README.md`。

```powershell
$env:AGENT_RENDER_ROOT = 'C:\tools\agent-render'   # 含 scripts\render.ps1 的工具仓库根
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\agent-render\render-avalonia-real.ps1
```

默认跑 7 条路由（`dashboard,gallery,models,labeling,training,inference,settings`）并额外出一张 stub training；
产物落在 `.outputs/agent-render/<timestamp>/`，其中 `summary/` 保留每页原始 `summary.json`，
`binding-errors/` 保留绑定错误清单，`report.json` 汇总 status / layoutIssueCount / bindingIssueCount。

已知坑（脚本已经处理，手工拼命令时别漏）：

- **SQLite 原生库要同步。** 离屏宿主的 `out/` 里没有 `SQLite.Interop.dll`，第一次渲染会在打开数据根时失败；
  `Sync-NativeDeps` 会补到 `out/` 与 `out/runtimes/win-x64/native/` 并重渲一次。
- **离屏宿主自己不等待异步首屏**。真实 VM 的首屏加载是异步的，工厂必须自己 `Dispatcher.UIThread.RunJobs()`
  轮询到 `IsLoading == false` 再出图（`ViewFactoryAvaloniaReal.cs` 的 `WaitUntilReady` 就是这么做的）。
- 出图里没有 `Popup` / `ToolTip`；Avalonia 栈不支持交互步骤，只能出初始截图。
- 数据根默认是 `%LOCALAPPDATA%\DeepVisionSharp\Data`（只读使用，别写）；截图依赖本机数据，不是跨机器 fixture。

### 5.4 已知坑：独立渲染 `UserControl` 会产生文本重叠伪影

**现象**：直接 `-ViewType ...Views.TrainingPage`（或任何页面）渲染单页时，标题与正文互相压在一起，
harness 还会报「文本重叠 / 被裁切」类 layout issue；同一个页面从 `MainWindow` 里导航进去渲染就正常。

**原因**：页面本身不声明文字基线。`FontFamily=IvFontFamily` / `FontSize=IvFontSizeBody`(14) 是
`yu-ui.avalonia/Themes/Generic.axaml` 里 `controls:ThemedWindow` 的 `ControlTheme` 给的，页面靠**继承**吃这两个值。
单页渲染没有窗口这一层，文字退回默认字体与字号，行高与照抄时算好的像素框对不上，就互相压。

**做法**：`-ViewType` 一律传 `DeepVisionSharp.Shell.Avalonia.MainWindow`，用 `DV_RENDER_PAGE` 之类的外部开关
让工厂先 `Navigate(<route>)` 再出图。独立单页渲染只用于「这页还画不出来」的临时排查，且结论不可作为验收证据。

---

## 6. 页面迁移状态表（7 个页面）

行数为裁剪后的实际值（`wc -l`）。「已接真 VM」= 容器注册了 `Presentation` 的 ViewModel 并由 code-behind
注入 `DataContext`；`ViewModels/Sample<Xxx>Context.cs` 只作设计时/兜底。
「在窗渲染」= `-ViewType MainWindow` 走导航进入（§5.4 唯一有效证据），产物见 `.outputs/agent-render/<timestamp>/shots/inwin-*.png`（§7.7）。
`binding` 列是 1600×1000 在窗渲染的 `bindingIssueCount`，全部为 **0**（基线 `ac0c2e0`）。

| 页面 | 文件（行） | 已照抄 | 已接真 VM | 在窗渲染 | binding | 已知缺陷 / 备注 |
| --- | --- | --- | --- | --- | --- | --- |
| 项目 Dashboard | `DashboardPage.axaml` 512 | 是 | **是** `DashboardViewModel`（singleton） | `inwin-dashboard.png` | **0** | 真实数据 2 项目 / 1000 样本；长项目描述按源设定省略号（源同行为） |
| 图库 Gallery | `GalleryPage.axaml` 543 | 是 | **是** `GalleryViewModel`（transient） | `inwin-gallery.png` | **0** | 真实数据 1 图库 / 1000 样本 / 1000 已标注；页面被导航缓存 → transient VM 事实单例 |
| 模型 Models | `ModelsPage.axaml` 441 | 是 | **是** `ModelsViewModel`（transient） | `inwin-models.png` | **0** | 长路径按源设定省略号 |
| 标记 Labeling | `LabelingPage.axaml` 574 | 是 | **是** `LabelingViewModel` | `inwin-labeling.png` | **0** | 裁掉 73 条无后端绑定（AiJudge/TemplateMatch/多边形分割/OCR）；`AnnotationCanvas` 已改绑 `BoxShapeViewModel`。本轮补齐真交互：拖拽建框、选中/移动/缩放、Delete/Esc/Ctrl+Z/Y、滚轮缩放/中键平移、快捷键、样本右键、检测轨缺失按钮；实机鼠标手势仍需人工点验；未进图库时如实显示「暂无样本」 |
| 训练 Training | `TrainingPage.axaml` 291 + `Controls/Training/TrainingRunPanel.axaml` | 是 | **是** `TrainingViewModel` | `inwin-training.png`（真 VM，3 个历史任务）+ `inwin-training-stub.png`（stub 视觉基线） | **0** | 裁掉 147 条无后端绑定；补回「模型架构」「检测参数」；修掉分类项目进不了分类增强的三层可见性门；状态轮询/指标轮询走 Presentation 真实现。本轮补真 VM 日志自动滚动、Hyperparameters/EarlyStopping 输入、per-class 指标表、配置预览摘要；训练任务进 TaskCenter 因 WPF 也没有，未擅自加 |
| 推理 Inference | `InferencePage.axaml` 1125 | 是 | **是** `InferenceViewModel` | `inwin-inference.png` | **0** | 裁掉 IR 后端选择器 / 会话异常阈值等 6 条；`Stop`、`ChartMultiple` 用页内几何；`NumericBox` 步进按钮 30px 溢出属源本身行为。本轮把右键 OK/NG/清除、采纳预测框、缩放写回真 VM、NMS IoU 输入接真命令；报告图表因没有跨平台 renderer，未做 |
| 设置 Settings | `SettingsPage.axaml` 474 | 是 | **是** `SettingsViewModel` | `inwin-settings.png` | **0** | 整节「Python 环境」是 Avalonia 侧自行发明（WPF 与 Presentation 均 0 命中）→ 已整块删除；本轮补 `CHANGELOG.md` 到输出目录，修掉「关于」4 条绑定为 null（见 §7.9）；F12 解锁等仍是 stub 行为 |

裁剪后的体量：7 个页面共 3,960 行（裁剪收口时 3,928 行，本轮补交互控件后 +32 行；基线 4,609 行），嵌套控件与对话框内容视图 43 个 `.axaml`。

接线落点固定三处：`Composition/AppComposition.cs`（注册 VM）、
`Services/AvaloniaNavigationService.cs`（`CreatePage` 建页）、页面 code-behind（`DataContext` 来源）。

## 7. 已知偏差与待办

### 7.1 4 个路由已接线（原缺口已关闭）

`AvaloniaNavigationService.CreatePage` 现覆盖全部 7 个路由，`AppComposition` 注册了 7 个页面的 ViewModel。
历史事实留档：基线 `9c82dda` 时只有 Dashboard/Gallery/Models 三个 `case`，其余点下去是占位页。

接线后仍需注意 §7.5：页面实例被导航缓存，transient 注册的 ViewModel 事实上是单例。

### 7.1.1 「发明 UI」是比抄错更严重的一类问题

裁剪过程中抓到整节「Python 环境」（设置页，12 条绑定）是 Avalonia 侧**自行发明**的：WPF 参考实现
`Shell/Views/Pages/SettingsPage.xaml` 与整个 `Presentation` 里 grep `Python` 均为 0 命中，
`git log --all -S IsPythonEnvironmentSelected` 无任何提交。它渲染正常、绑定也「对得上 stub」，
所以只有拿 WPF 侧当尺子才查得出来。

**判据（后续所有照抄工作必须执行）**：留下来的每个元素，必须能在 WPF 侧或 `Presentation` 侧找到对应实现；
两侧都找不到 = 发明 → 整块删除，哪怕它渲染完美、binding 为 0。

配套三条禁令：
1. 不许用 `IsVisible=False` 把假 UI 藏起来（仍占资源、仍骗绑定、下次照抄还会被抄进来）。
2. 不许为了让假 UI 不报错，往 `Presentation` 塞恒 false / 空集合 / `UnavailableCommand` 的「死属性」——
   那是撒谎的接口面，比留着绑定错误危害更大。
3. 规格文档（`temp/yuui/trim-spec.md`）里如果写了不存在的成员，直接改掉，别让错规格误导下一波
   （本次已发现两处：`IsPythonEnvironmentSelected` 系列 12 个成员、`HasAdvancedModifications` 系列 4 个成员）。

### 7.2 图标缺口：`IvIcons` 没有 `Stop` / `ChartMultiple`

- 两套 `IvIcons`（`yu-ui/Icons/IvIcons.cs` 与 `yu-ui.avalonia/Icons/IvIcons.g.cs`）各 77 个几何，**缺 `Stop`、
  `ChartMultiple`**（deepvision 的 `DvIcon` 里有）。
- WPF 训练页把 `Stop24` 降级成 `IvIcons.Close`（`Views/Pages/TrainingPageResources.xaml:547` 有注明）；
  Avalonia 训练页同样用 `IvIcons.Close`。
- Avalonia 推理页不降级，改用页内几何（`Views/InferencePage.axaml:22-25`）。
- 待办：要么给两套 `IvIcons` 补齐这两个几何（源几何在 deepvision 的图标表里），要么统一降级策略。

### 7.3 启动期能力缺口

见 §4.3 表 1–6（文件日志、单实例、崩溃转储、异常钩子、启动失败提示、退出时停推理引擎）与第 7 项
（`--ui-test` 的 stub 文件选择器）。另有一条容易忽略的：**Avalonia 的 `Parse` 没有 try/catch**，
非法启动参数（例如 `--ui-test` 不带 `--data-root`）会直接抛在 `OnFrameworkInitializationCompleted` 里。

### 7.4 对话框：20 种类型已映射，26 个内容视图里（基线）10 个不可达

- `AppDialogKind` 20 项，`AvaloniaDialogService` 的 20 项描述表与 WPF `DialogCatalog` 逐项一致
  （标题/主按钮/关闭按钮/次按钮文案），共用 `Presentation/Services/Dialogs/DialogDescriptor`。
- **基线**：Avalonia 侧有 26 个 `Views/Dialogs/Contents/*.axaml`，WPF 侧只有 16 个内容视图。
  多出来的 10 个（`AiJudge{BatchStart,ModelSettings,Review,Standards}`、
  `{Detection,InstanceSeg,SemanticSeg,Variation}AutoInference`、`TemplateMatch`、`StartTrainingConfirm`）
  在基线的 Avalonia 仓库里**没有任何代码引用**（`rg` 只命中它们自己的 axaml/cs），`IAppDialogService` 也到不了。
  它们是照 deepvision 提前抄的素材，接功能前不要当成「已迁移」。本轮 F4 正在把它们接进描述表；
  交接前用下面这条命令复核，`unused` 应为 0：

  ```bash
  cd src/DeepVisionSharp.Shell.Avalonia
  for f in Views/Dialogs/Contents/*.axaml; do b=$(basename "$f" .axaml);
    c=$(rg -l "$b" --glob '*.cs' --glob '*.axaml' . | grep -v 'Views/Dialogs/Contents' | wc -l);
    [ "$c" -eq 0 ] && echo "unused: $b"; done
  ```
- 待办：按钮语义（主/次顺序、默认按钮、Esc=取消、危险确认里 Esc 不得确认、取消令牌传播、
  主按钮 `IsEnabled` 绑定）尚未逐项核对，证据只有 4 张对话框截图（`preview/avalonia-dialog-*.png`）。

### 7.5 页面生命周期：Avalonia 侧页面永不释放

`AvaloniaNavigationService` 用 `Dictionary<AppRoute, Control> _pages` 按路由缓存页面，切回同一导航项不重建
（有意为之，为了保留页内状态）；但 WPF 那套 `PageDisposalScheduler`（卸载 5s 后 dispose 页面 VM）没有对应实现。
现在只有 3 个页面受影响，接上另外 4 个之后会长期持有多套 VM 图（`TrainingViewModel`、`LabelingViewModel` 都是重对象）。
待办：接路由前先定生命周期策略（缓存 + 显式 dispose，或照 WPF 的调度器抄一份）。

### 7.6 重复实现（两个壳互不引用带来的必要代价，但要有意识地维护）

- `Shell/Services/TrainingResourceMonitor.cs` ↔ `Shell.Avalonia/Services/AvaloniaSystemResourceMonitor.cs`
- `Shell/Services/ShellProcessLauncher.cs` ↔ `Shell.Avalonia/Services/AvaloniaProcessLauncher.cs`
- 同名控件在多个目录各留一份（命名空间不同、内容非逐字节一致）：
  `BusyRing`（Misc / Training / Inference，3 份）、`ParamHelpButton`（Misc / Training）、
  `CanvasViewToolbar` 与 `CanvasCornerFloat`（Inference / Labeling）。
  改一处**不会**同步其它，视觉回归要逐份看。`ImageLightboxOverlay` 已收敛为
  `Controls/Training/ImageLightboxOverlay.axaml`，`Controls/Misc` 下未被引用的重复份已删。
- 少数文件带着从 deepvision 一起复制过来的注释头，写着 `InCube.UI` 或「check CLAUDE.md」——
  本仓库没有 `CLAUDE.md`。基线命中 5 个文件：`Controls/Labeling/CanvasViewToolbar.axaml{,.cs}`、
  `Controls/Labeling/CanvasCornerFloat.axaml{,.cs}`、`Controls/Training/MetricLineChart.cs`。清掉即可。

### 7.7 渲染取证已入仓

- 根目录 `agent-render.json` 的 `AvaloniaRealShell`、仓库内工厂 `scripts/agent-render/ViewFactoryAvaloniaReal.cs`
  与包装脚本 `scripts/agent-render/render-avalonia-real.ps1` 已替代仓库外临时文件；默认 7 条路由 + stub training。
- 工厂内的 `ResourceSources` 是唯一资源字典来源；脚本不再传 `-ResourceDictionary`，避免两处漂移。
- 脚本动态收集业务 DLL，`yu-ui.avalonia.dll` 只取 `src/yu-ui.avalonia/bin/Debug/net10.0`，并由 `Sync-NativeDeps`
  同步 `SQLite.Interop.dll`，避免「查询失败 + 空列表」的假象。
- 产物落 `.outputs/agent-render/<timestamp>/`（已 gitignore），包含 `shots/`、`binding-errors/`、`summary/`、`report.json`；
  截图依赖本机 `%LOCALAPPDATA%`，不是跨机器 fixture，生成物不要提交。

### 7.8 任务中心已挂，灯箱仍在页面内

- 任务中心：`MainWindow.axaml` 已按 deepvision 同位挂载 `TaskCenterPanel`（`Grid.Row=1`、`Grid.Column=1`、
  右下角、`Margin 0,0,16,16`，**不是浮层遮罩**），`DataContext` 注入 `ShellTaskCenterViewModel`。
- 灯箱：deepvision 的灯箱挂在 `TrainingRunPanel` **内部**（页面级，`ZIndex=1000`）。因此**不给它加 shell 级状态**
  （`IsImageLightboxOpen` / `ImageLightboxSource` / `CloseImageLightboxCommand` 那套是发明状态）。
  `Controls/Training/ImageLightboxOverlay.axaml` 已把 `Source` 绑到控件自身 `ImageSource`，
  `Controls/Misc` 下未被引用的重复份已删除；灯箱仍留在页面内，没上提到 shell 层。

### 7.8.1 编排教训（多 lane 并行时必看）

一次五 lane 并行的实测事故，写下来避免重演：

1. **同一个 csproj 不许并行 build**。三条 lane 各自「每改一个文件就 build」，实测堆积 **27 个 dotnet 进程**，
   `obj/bin` 锁互抢，其中一条 lane 的命令 4 分钟无输出。正确做法：**写入并行、编译串行**，由父代理统一编译。
2. **终验必须独占通道**。不要交给正在改代码的 lane——它此刻的渲染必然是半成品。
3. **给 lane 的渲染脚本必须自带硬超时 + 失败即退**，否则 spawn 的渲染宿主会越堆越多。
4. 单条 bash 超过 3 分钟无输出就放弃重来，不要等它自然结束。
5. 任务书里的「每改一个文件就 build」是**错的要求**，应为「每 5~8 个文件批量 build 一次」。
6. 对 lane 的实测结论要复核再采信：本次两路各超时 30 分钟，但留下的代码都可编译；
   而 lane 报的「X 已修好」必须由父代理用命令复验。

### 7.9 发布物缺件：Avalonia 的 CHANGELOG 已补齐，license 待确认

`Presentation/Services/ChangeLogService.cs` 从 `AppDomain.CurrentDomain.BaseDirectory` 读 `CHANGELOG.md`
（其次向上 5 级找仓库根）。WPF Shell 的 csproj 复制 `license.txt` 与 `CHANGELOG.md`；
**Avalonia Shell 现在也已加 `CHANGELOG.md` 的 `CopyToOutputDirectory`**，否则接真 `SettingsViewModel` 后
设置页「关于」拿不到数据（离屏渲染曾报 4 条 `CurrentChangeLogEntry.* Value is null`，已修到 0）。

`license.txt` 当前只由 WPF 壳复制；Avalonia 设置页源码未发现读取它，且不复制不会造成绑定错误。
若发行包要求 Avalonia 输出目录也带 license，需产品/打包流程明确后再加，不作为 UI 迁移的一部分。

### 7.10 自动化测试现状

4 个测试工程覆盖 Tiling / TorchSharp / Application / Infrastructure，`Presentation` 与两个 Shell 都没有测试；
WPF 侧的 FlaUI 历史用例在本仓库里只剩注释（`App.xaml.cs`、`StubLocalFileDialogService.cs`、
`Presentation/Automation/UiAutomationIds.cs`），没有对应工程。

`DeepVisionSharp.TorchSharp.Tests` 已补仓库内确定性数据集夹具（`TestSupport/DvsDatasetGen.cs`），
不再依赖 gitignored 的 `dvs-dataset-gen`；Solution 构建实测 `0 warning / 0 error`。
本机 `dotnet test` 会在 `testhost.exe` 启动阶段报 `Win32Exception (5): Access is denied`，
Tiling 测试工程同样如此，判定为当前开发环境的 VSTest 进程权限问题，**不能据此宣称测试已通过**。
夹具产物已用 PowerShell + System.Drawing 单独验证：PNG 可解码、manifest 与 YOLO 标签格式正确。

---

## 8. 下线 WPF Shell 的前置条件清单

以下每条都要求「Avalonia 侧有等价实现 + 有验证证据」，全部勾上才允许讨论切换默认启动项目。
本清单的 `[x]` 只表示**代码/构建/在窗渲染已有客观证据**；标注「实机待验」的项仍需人工点验，不能据此直接切换。

**功能对齐**

- [x] 7 个路由全部接真 VM（标记/训练/推理/设置），`CreatePage` 不再返回 `null`
- [x] 7 个页面在 `MainWindow` 里导航渲染通过：`bindingIssueCount == 0`，4 条 layout 告警均归类为源本身行为或长文本省略
- [x] 训练页「主指标说明」卡：复核确认不是塌陷，而是历史表内 ScrollViewer 在视口下方被裁切；XAML 与源一致，不改
- [ ] 页面生命周期策略落地：切走后的 VM 能被回收（§7.5）
- [x] 任务中心挂到主窗口右下角；代码与 MainWindow 在窗渲染均已核对（§7.8）
- [ ] 灯箱显示真实图像仍需实机打开验证；`Source` 绑定与重复控件已收口，binding 实测 0
- [x] 20 种 `AppDialogKind` 映射与按钮语义核对表已闭合（115 项检查）
- [ ] 提示条 4 种语义 + 连续弹出替换行为实机待验
- [ ] 主题切换（浅/深/跟随系统）与标题栏图标联动实机待验
- [ ] 快捷键处理（`ITextInputFocusProbe` 让路逻辑）实机待验；代码已补，未逐控件点验
- [ ] 剪贴板：复制文本、复制图像（含「粘贴时才读」的生命周期）实机待验

**启动期与鲁棒性**

- [x] 文件日志落盘 + 日志保留：实现已落（`b2268c5`）
- [x] 单实例互斥：实现已落（`b2268c5`）；双开行为实机待验
- [x] WER LocalDumps 注册：实现已落（`b2268c5`）
- [x] 4 个未处理异常钩子 + 异常日志 + 用户可见提示；`--ui-test` 静默：实现已落（`b2268c5`）
- [x] 启动参数非法提示与 `Shutdown(-1)`：实现已落（`b2268c5`）
- [ ] 退出时先停推理引擎再停宿主，实机待验
- [ ] `--data-root` / `--verification-root` 的跨机 fixture 隔离仍是第二阶段；当前取证使用本机真实数据

**工程与流程**

- [x] 渲染工厂与 `agent-render.json` 进仓库（§7.7）；7 条路由已由父代理串行渲染确认
- [x] `dotnet build DeepVisionSharp.slnx -c Debug` 0 warning / 0 error
- [x] TorchSharp 测试不再依赖 gitignored 的 `dvs-dataset-gen`：测试工程内置确定性夹具，Solution 构建恢复绿色
- [ ] 图标缺口收口（`Stop` / `ChartMultiple`：补齐几何或统一降级策略）
- [ ] 26 个对话框内容视图清账：可达的接上，不可达的删掉或标注「源有、暂不接线」
- [ ] 重复控件（`BusyRing` / `ParamHelpButton` / `CanvasViewToolbar` / `CanvasCornerFloat`）收敛或明确「各自维护」
- [ ] 复制过来的注释头（`InCube.UI` / `CLAUDE.md`）清理
- [ ] 站点/打包脚本（`packaging/`、`scripts/build_installer.ps1`）改为产出 Avalonia Shell 的可执行名与图标
- [x] 输出目录补齐 Avalonia 的 `CHANGELOG.md`（设置页「关于」，见 §7.9；binding 实测 0）
- [ ] license 是否也必须随 Avalonia 输出目录发布，待打包/产品流程确认
- [ ] 单实例、文件名、AssemblyName（`DeepVisionSharp.Avalonia`）、进程名切换的兼容性确认（装过旧版的机器如何升级）

**验证留档**

- [ ] 每个页面一张在窗截图（1600×1000，light + dark）与 deepvision 对应页面并排比对；当前只有 light 在窗截图
- [x] 每个页面一条 `summary.json` 记录（`status` / `layoutIssueCount` / `bindingIssueCount`）
- [ ] 一次完整人工回归：新建项目 → 导入 → 标注 → 训练 → 推理 → 导出，全流程在 Avalonia Shell 上跑通
- [ ] 回滚方案：WPF Shell 在下线后至少保留一个 release 周期，`DefaultStartup` 可一键切回
