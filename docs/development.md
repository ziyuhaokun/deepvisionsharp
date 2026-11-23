# 开发与验证

在仓库根目录使用 Windows x64、.NET 10 SDK 和 PowerShell 7。官方权重由 C# 下载和转换。普通推理与训练使用应用自带的 TorchSharp 运行时，GPU 训练需要 NVIDIA 驱动。

## 验证命令

| 要验证的内容 | 命令 |
| --- | --- |
| 构建主解决方案并运行快速回归，Windows CI 使用同一入口 | `.\scripts\verify.ps1` |
| 修改损失、训练器或 checkpoint 后运行生成数据训练 | `.\scripts\verify.ps1 -Mode Slow` |
| 修改权重转换、模型结构或预处理后运行官方权重验证 | `.\scripts\verify.ps1 -Mode Pretrained` |
| 项目包服务的 SQLite 往返、取消和失败保留测试 | `dotnet test tests/DeepVisionSharp.Infrastructure.Tests --filter FullyQualifiedName~ProjectPackageRoundTripTests` |

快速回归与真实权重测试走 CPU，不要求安装 GPU 驱动。直接运行 `dotnet test` 时，真实权重用例默认显示为跳过。使用 `-Mode Pretrained` 时，缺少权重或样图会明确报错。

如果受限工作区里的 VSTest 报 `Win32Exception (5): Access is denied`，请在能正常启动 VSTest testhost 的工作环境重新执行上述命令。可在 Windows CI 查看同一入口的结果。

## 准备真实权重测试

以下命令用 C# 准备 YOLO26-N 的检测与分类权重。权重和样图均来自 Ultralytics v8.4.0。

```powershell
.\scripts\fetch_pretrained.ps1 -Arch yolo26n
.\scripts\fetch_pretrained.ps1 -Arch yolo26n-cls
New-Item -ItemType Directory -Force temp/weights | Out-Null
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/ultralytics/ultralytics/v8.4.0/ultralytics/assets/bus.jpg' -OutFile temp/weights/bus.jpg
.\scripts\verify.ps1 -Mode Pretrained
```

验证覆盖官方权重的下载与转换、检测和分类的张量映射、分类骨干迁移，以及真实图片的分类和检测输出。可用 `DV_PRETRAINED_ROOT` 指定已有权重的根目录，例如 `<root>/yolo26n/index.json`。离线准备时，可给 `fetch_pretrained.ps1` 传入 `-SourcePath` 指向本地官方权重文件。

## 训练数据集清单

新导出的分类和检测数据集使用 `dataset-manifest.json` v2。清单按训练时的类别索引顺序保存类别，并记录每张图片的拆分归属、相对路径和图片 SHA-256。分类记录实际类别索引，检测记录实际导出的 YOLO 标签文本 SHA-256。`split_overlap_count` 统计同时出现在 train 和 val 的图库样本数，按图库 ID 和完整原文件名识别样本。自动拆分记录算法和 seed，手动指定图库角色记录 `purpose-assignment`。

v2 指纹根据类别与导出样本内容计算。旧数据集继续按 v1 清单读取，旧指纹的计算口径与 v2 不同。摘要反映导出当时读取的文件，训练加载时沿用已有目录布局。硬链接仍共享图片内容，导出后原位修改源图片可能改变训练看到的内容。

## 分类训练产物

分类训练在输出目录保存 `classification-run.json`，记录实际应用的预训练权重、加载张量数、预处理、输入规格、类别顺序和数据集指纹。`best` 与 `last` 的 `.info` 使用同一份训练配置。模型入库时如需补写 `.info`，必须能从原输出目录读取与任务匹配的训练记录。恢复出的元数据不包含无法确认的 checkpoint 轮次和验证指标。历史 checkpoint 有有效 `.info` 时可直接入库。

## 检测训练产物

YOLO26 检测训练在输出目录保存 `detection-run.json`。除了任务、类别、输入尺寸和数据集指纹，记录还包含实际初始化来源、加载张量数、letterbox 预处理、stride、SPPF shortcut、NMS IoU 和最大框数。`best` 或 `last` 缺少可用的 `.info` 时，模型库用匹配的原训练记录补写，无法确认的轮次和 mAP 留空。有有效 `.info` 的旧检测产物可直接入库。

## 训练完成与重启恢复

分类和检测后端在校验 `best`、`last` 和各自的 `.info` 后，先原子发布 `training-completed.json`，再把任务状态写入 SQLite。完成记录对应本次任务，保存 UTC 完成时间和产物相对路径。发布前收到取消时保留已写入的 checkpoint，任务记为取消。记录发布后若状态暂时写不进去，启动和定时对账会重试。没有完成记录的历史任务仍可用 `DV_COMPLETED` 日志恢复。

## 训练状态写入

训练任务的状态变更（进入运行、成功、失败、取消）通过 `ITrainingJobRepository.SaveIfStatusAsync` 写入。仓储用一条 `UPDATE ... WHERE Status IN (...)` 同时检查当前状态并写入，允许的当前状态由 `TrainingJobTransitions` 推出。后端、Worker、停止命令和对账持有旧快照时，不能覆盖较新的状态，先到的终态保留。被拒绝的写入只记日志，不发状态事件。

## 数据库 schema 迁移

`SqliteDb` 打开 `deepvision.db` 时比对版本。版本以库内 `PRAGMA user_version` 为准，数据目录的 `schema.version` 只作旧库兜底，两者取较高值。低于 `SqliteSchemaVersions.Current` 时，先把库文件复制为 `deepvision.db.pre-v{目标版本}.bak`，再迁移，最后把两个位置都写成当前版本。复制发生在打开连接之前，避免 WAL 下拷到半截。同一目标版本的备份已存在时保留原件，不覆盖。已经是当前版本的库不复制、不改写。备份留在数据目录，覆盖安装不清理该目录，回滚时手动改回 `deepvision.db`。

## 本地产物

- `temp/` 存放一次性探针、日志、测试样图和其他调试数据。
- `.outputs/agent-render/` 存放需要保留的离屏渲染结果。参见 [离屏渲染说明](../scripts/agent-render/README.md)。
- `scripts/Output/` 存放安装包。参见 [打包说明](../packaging/README.md)。
