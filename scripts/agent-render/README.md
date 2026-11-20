# Avalonia 真实数据离屏渲染

这套脚本把 7 条页面路由逐个导航进真实的 `MainWindow` 出图，并额外出一张 stub training 视觉基线。
预设在仓库根 `agent-render.json` 的 `AvaloniaRealShell`，视图工厂是同目录的 `ViewFactoryAvaloniaReal.cs`。

## 前置条件

- Windows + PowerShell 7（`#requires -Version 7.0`）。
- agent-render 工具仓库根（含 `scripts/render.ps1`）：用环境变量 `AGENT_RENDER_ROOT` 指定，或调用时传 `-AgentRenderRoot`。
- 已构建 Avalonia Shell 与 `DeepVisionSharp.UI.Avalonia`（脚本只读它们的 `bin/Debug` 输出，不在本脚本里构建/还原）。
- 真实数据默认读当前机器的 `%LOCALAPPDATA%\DeepVisionSharp\Data`。**截图依赖本机数据，不是跨机器 fixture**；换机器复现时先准备等价数据，不能只靠提交图片。
- 配置文件与脚本里不放密钥；生成物也不要提交。

## 用法

```powershell
$env:AGENT_RENDER_ROOT = 'C:\tools\agent-render'   # 指向含 scripts\render.ps1 的工具仓库根
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\agent-render\render-avalonia-real.ps1
```

父代理已串行编译、不想重复构建时，去掉 `-Build`；需要让预设里的 `build` 命令先跑一遍时加 `-Build`。

常用参数：

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `-Pages` | `dashboard,gallery,models,labeling,training,inference,settings` | 逗号分隔的页面名；包含 `training` 时额外生成 `inwin-training-stub.png` |
| `-Build` | 关 | 通过 `agent-render.json` 的 `build` 命令构建业务仓库；与离屏宿主自身的构建无关 |
| `-OutRoot` | `<仓库根>/.outputs/agent-render` | 取证产物根目录；实际写入它的 `<timestamp>/` 子目录 |
| `-AgentRenderRoot` | `$env:AGENT_RENDER_ROOT` | agent-render 工具仓库根，必须包含 `scripts/render.ps1` |

指定页面和产物根目录：

```powershell
$env:AGENT_RENDER_ROOT = 'C:\tools\agent-render'
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\agent-render\render-avalonia-real.ps1 `
  -Pages 'dashboard,training' -OutRoot 'C:\work\deepvisionsharp\.outputs\agent-render'
```

设置页分区取证：`DV_RENDER_SETTINGS_CATEGORY` 可设为 `Appearance` / `Storage` / `About`（Advanced 需要调试解锁，脚本不处理）；
`DV_RENDER_SETTINGS_THEME` 可设为 `Light` / `Dark`，在页面加载后覆盖本机持久化主题，避免「预设写 light、截图是深色」。
例如：

```powershell
$env:DV_RENDER_SETTINGS_CATEGORY='Storage'
$env:DV_RENDER_SETTINGS_THEME='Light'
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\agent-render\render-avalonia-real.ps1 -Pages settings
```

截图文件名仍为 `inwin-settings.png`，需要留档时自行改名。

## 产物

每次运行写入 `<OutRoot>/<timestamp>/`（`yyyyMMdd-HHmmss`）：

- `shots/inwin-<page>.png`：每页截图；stub 图额外压一条红底声明带，图上自证是非真实运行。
- `binding-errors/`：每页的 `binding-errors.json` / `binding-errors.txt`（宿主生成了才复制）。
- `summary/`：每页原始 `summary.json`，含 `status` / `layoutIssueCount` / `bindingIssueCount`。
- `report.json`：本次所有页面的汇总，含每页截图、绑定错误清单和 summary 路径。

`.gitignore` 已忽略 `/.outputs/`。PNG、日志、report、summary 都是本地取证物，确认结论后保留在忽略目录即可，**不要提交**。

## 实现约束

- `ViewFactoryAvaloniaReal.cs` 中的 `ResourceSources` 是唯一资源字典来源；包装脚本不再传 `-ResourceDictionary`，避免两处漂移。
- 脚本动态收集 Shell 输出目录下的业务 DLL（FreeSql 需要反射加载 provider），并固定只把 `src/DeepVisionSharp.UI.Avalonia/bin/Debug/net10.0/DeepVisionSharp.UI.Avalonia.dll` 作为 `DeepVisionSharp.UI.Avalonia.dll` 的唯一来源，过滤 Shell 输出下的同名副本。
- `Sync-HostRuntimeFiles` 会把 `SQLite.Interop.dll` 与 `CHANGELOG.md` 同步到离屏宿主的 `out/`；否则真实数据查询失败会伪装成「空列表」，设置页「关于」会报 4 条绑定错误。
- 页面必须从 `MainWindow` 导航进入；直接渲染裸 `UserControl` 会丢掉窗口级字体/样式并产生假 layout issue。
