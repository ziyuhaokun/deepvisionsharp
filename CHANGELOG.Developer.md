# DeepVisionSharp 开发者变更

实现细节、类名、路径与内部开关。使用者请看 [CHANGELOG.md](CHANGELOG.md)。

版本节格式与用户版相同：`### vX.Y.Z (发布日期)`，可选 `tags:`，后接 `-` 条目。本文件不复制到安装包。

### v0.0.2-dev (unreleased)

tags: detection / classification / refactor

- 新增 Detection 项目类型与 ModelFamily：`ProjectTypes` / `ProjectWorkflowKind` / `ProjectCapabilityCatalog` 双类型；`TaskCompatibilityCatalog` 按 workflow × family 配对；`ResolvedTaskProfile` 的 overlay 增加 `ExportKind.DetectionYoloFolders`、`InferenceKind.DetectionServe`、`InferenceWriteBackKind.BoundingBoxes`、`MetricProfileKind.DetectionMap`。`ModelTypeNames` 增加 `yolo26n-det` … `yolo26x-det`，拆出 `IsKnownModelType` / `IsDetectionModelType`，并审计全部 `IsClassificationModelType` 调用点。
- 标注契约支持检测：`AnnotationDocument` / `AnnotationSaveItem` / `IAnnotationWorkflowService` 增加 `AnnotationShape` 与 `IsReviewed`；sidecar 写 Labelme `shapes` 与 `flags.dvReviewed`，旧分类 sidecar 无 shapes 仍可读；标签索引、按标签选样本与统计从 shape labels 派生；删除标签并清理时同时移除对应框。数据库 `Annotations.DocumentJson` 原样承载，不升 schema。
- 新增 `DeepVisionSharp.Domain.Datasets.YoloDatasetFormat`（类别文件与 YOLO txt 解析/格式化）与 `IYoloDatasetImportService`：解析 `classes.txt` 或 `data.yaml` 的 `names`，按 `images/`→`labels/` 镜像读取，暂存图片与生成的 sidecar 后复用现有图库导入事务，缺失标签文件按负样本处理。
- 新增 `IDetectionDatasetExportService`：输出 `detection-dataset/{train,val}/{images,labels}` 与 `dataset-manifest.json`（含框数、fingerprint、split_method）；空 txt 表示负样本；自动划分复用 `hash-rank-v1` 但不按类别分组；未 reviewed、缺图、未知标签框分别计数。
- TorchSharp 检测链路：`Yolo26DetectModel` 支持 n/s/m/l/x 并修复 `NumClasses` 未赋值；`DetectionLetterbox` 复用 `Tiling.ImageFits.Letterbox` 做像素与框变换；`DetectionTensorDecoder` 张量化 decode + `DetectionNms`；`DetectionMetrics`（mAP50 / mAP50-95 与每类 P/R/F1）；`TorchSharpDetTrainer`（letterbox、水平翻转、早停、原子 best/last、检测 `.info`）；`TorchSharpDetectEngine`（反 letterbox、NMS、LRU 缓存）。`TorchSharpModelBundle` 增加 `num_classes` / `max_detections` / `nms_iou` / `strides`，检测允许单类。
- Infrastructure：新增 `TorchSharpDetectionTrainingBackend`（单飞、设备独占、心跳、DV_COMPLETED `task: "detect"`）、`DetectionModelArtifactHandler`（允许单类，可从 dataset-manifest 恢复类别）、`TorchSharpDetectionInferenceBackend`；`IInferenceEngineControl` 增加复合实现，训练独占时同时清空分类与检测缓存；环境检查覆盖检测后端；`ModelMetadataReader` 按 family 放宽；`InferenceModelCache` 双引擎清理。
- Shell：项目类型视觉与筛选支持检测；新增 `BoxOverlayCanvas` 框图层与检测工具/框列表卡片，支持画框、选中、移动、缩放、删除、撤销/重做、标记无目标；检测工具栏提供 YOLO 数据集导入；训练页按项目 workflow 过滤模型、增加 NMS IoU / 评估置信度 / 最大框数 / 水平翻转参数与 mAP 指标卡；推理页增加画框叠加、框坐标明细、NMS IoU 参数与检测版批量 HTML 报告；图库批量推理为检测模型提供「追加预测框」写回。
- 测试：新增能力矩阵、YOLO 文本格式、Labelme shapes 往返、检测划分、框几何 / NMS / 指标、检测数据集契约、检测写回合并与 Slow 训练器端到端测试。
- TorchSharp 分类后端新增 YOLO26-cls（n/s/m/l/x）网络与工厂分派：通道表与 C3k 分档按 Ultralytics `yolo26-cls.yaml` 对齐，五档参数总量与官方一致；产品模型类型入口已接入。
- 切图从分类热路径拆出：新增 `DeepVisionSharp.Tiling`（`TilePlanner` / `ImageFits` / `CoordinateMaps`，纯几何、无 OpenCV）。`TileRequest` 覆盖 WholeImage / SlidingWindow（EdgeAlign·Drop·Pad） / Grid / Crop；`ImageFitKind` 覆盖 Stretch / Letterbox / Cover / Pad / ShortEdge（含 maxLongEdge） / LongEdge，外加 `PixelSizes.AlignUp`。分类训练与推理仍只走 `ToClassificationTensor` 线性拉伸。删除无调用方的 `ToLetterboxTensor`；推理性能字段去掉 crops / NMS / 切块摘要。
- 崩溃弹窗、批量推理 HTML 报告与安装测试产物统一为 DeepVisionSharp 产品名；训练后端 key 记录与代码对齐为 torchsharp:classification-train / torchsharp:classification。
- DEBUG 推理报告复制改为进程内剪贴板（`copy: false`），进程退出后不残留。
- 崩溃弹窗默认不复制报告到剪贴板，复制时使用进程内剪贴板（退出后不残留）；DEBUG 推理日志只记文件名与摘要，不再写入绝对路径或完整报告；维护密码长度不同时不再抛 FixedTimeEquals 异常。
- 安装打包工具源码内置到 `packaging/installer/`（RuntimePackage Engine、CLI、SetupHost 与打包回归测试）；默认入口只用仓库内工具，取消相邻仓库自动查找，保留 `-PackagerRepo` / `-PythonRepo` 显式覆盖和 `-Shell wpf` 兼容。安装格式、产品标识、安装目录与无 Python 运行时保持不变。
- 1.0 只保留图像分类；移除五类归档项目、旧 Python/Anomalib 兼容、OPT 与旧 JSON 导入。
- 删除归档自动推理弹窗、标注卡片与写回策略；标注工具栏只保留分类。
- 删除模板匹配、自动贴合与 Imaging.OpenCv 工程；产品 solution 收敛为 6 个 project。
- 数据集导出只保留分类文件夹；删除 VOC、YOLO-Seg、语义 Mask 导出与 DatasetExportNameMap。
- 删除检测与 Anomalib 推理/导出后端、INT8 量化入口及剩余 Anomalib 兼容 DTO。
- 删除无消费者的检测训练辅助（spatial contract、workspace layout、resume identity、VOC precheck、pretrained checkpoint validator）。
- 阶段 1 合计净删约 12,300 行；核心测试 82 项、Workflow GUI 16 项通过。
- 阶段 2 收敛项目类型、模型目录、训练配置与标注模型：删除异常系统图库、检测空间策略/增强、检测 sidecar 与框/多边形编辑，标注只保留整图类别；阶段 2 合计净删约 13,500 行。
- 数据库 schema 升级到 v10 并删除归档的 Galleries.SystemKind 列；设置文件移除 PythonPath，旧 appsettings.json 中的 pythonPath 被忽略。
- 删除遗留模型类型常量与判断（检测/分割/Anomalib）、未消费的 BackendKeys 及对应分支；训练后端 key 只剩 torchsharp:classification-train，推理只剩 torchsharp:classification。
- 训练执行按 A 方案硬化：进程内训练单飞；OutputRoot 写入阶段/epoch 心跳面包屑，正常退出清理、崩溃残留；启动恢复用面包屑生成含阶段与 epoch 的失败原因，并回填 best/last 权重路径；训练异常完整堆栈写入训练日志。
- 新增训练恢复演练：核心测试覆盖面包屑与恢复文案，Workflow 覆盖中断失败、完成日志恢复成功、活动会话不回收。
- 图库统计改为增量更新：标注保存、图库导入、批量推理写回不再逐次全表 COUNT，导入从 O(n²) 降为 O(n)；维护入口与导入结束仍做一次全量对账。
- 项目包与图库包导入改为按样本单行读取标注行，避免整库 Base64 图像同时驻留内存（此前数百张 5MP 图即可耗尽内存）。
- 项目包导入失败或取消时回滚已创建的项目；取消语义由"留下半成品"改为"回滚并抛出取消"，重试不再产生重复的导入项目。
- 推理加载缺少元数据（model-manifest.json / .info，或类别少于 2、缺输入尺寸）的模型时直接报错并给出路径，不再静默返回零结果。
- 推理模型缓存改为 LRU（默认 3）并在训练启动前释放，避免切换模型或反复保存模型后显存持续增长；正在推理的模型不会被淘汰。
- 训练改为后台执行：启动命令在任务进入 Preparing 后即返回，数据集导出、训练与检查点保存由 TrainingRunWorkerHostedService 承接，终态仍经 TrainingJobStatusWriter 事件回传。
- 训练启动不再占用命令生命周期；"启动训练"按钮在准备完成后恢复，训练期间禁止删除项目的门禁租约改由后台任务持有。
- 关闭应用时正在训练的任务会先尝试取消并落 Cancelled；超出宿主停机窗口则仍由崩溃恢复在下次启动判失败。
- 修复训练后端在单飞等待阶段被取消时未释放运行令牌的问题（队列中的任务被取消后会残留一个已取消的会话）。
- 图库与项目导入期间持有项目门禁租约，导入过程中删除项目会被拒绝；此前会留下无外键的孤儿样本并被导入重建目录。
- 训练失败时保留已生成的 best/last 权重路径，"保存为模型"在中断后仍可用；此前失败任务只认 .pt 探测，识别不到 best.torchsharp。
- 统一"可推理模型"口径：单图推理与批量/标注推理一致，仅 Ready/Published 可选，Creating/Archived 不再出现在推理下拉框。
- 批量推理在后台完成时自动刷新图库计数与项目进度，不再等到下次进入页面。
- 关闭应用时若训练正在运行，退出确认文案提示"退出将取消当前训练"。
- 验证体系新增仓库内离屏渲染器与 `-Channel Headless`（Core + Workflow + Visual），视觉通道不再依赖外部 wpf-offscreen-render 插件；绑定监听带自检探针，监听失效会判失败而不是报"0 问题"。
- 验证体系新增端到端演练（项目→图库→导入→标注→数据集导出→训练→模型入库→批量推理写回）与 `-Channel Native`（真实 TorchSharp 训练 + 推理，opt-in，不进入 All/Headless）。
- 训练默认配置收敛为 256×256 / batch 8；启动前按架构、输入尺寸与批量给出显存风险提示（不阻断，仅提示）。
- 训练评估新增每类 precision/recall/F1 与混淆矩阵，写入训练日志、`DV_METRICS` 与模型 `.info.metrics`；全局准确率掩盖的"全判 OK"退化可直接看到。
- 自动划分改为固定种子的洗牌切分（种子写入 `dataset-manifest.json`），消除按文件名排序把早期工况全部划入验证集的偏置。
- 验证预览图改为第 1、末轮与每 5 轮生成，并响应取消；不再每轮做 9 次前向。
- 新增 torchvision 兼容的 ResNet v2（resnet_tiny_v2/resnet18_v2/resnet34_v2）与预训练权重加载：离线转换脚本（只需 numpy，不装 torch）+ C# 读取器 + nano 的通道切片迁移；训练启动时自动查找权重，找到则用 ImageNet 归一化并在 `.info` 记录 `preprocessing`。
- 分类训练默认改用 v2 架构（nano 为宽度缩减的 ResNet18）；权重缺失时回退随机初始化 + `[0,1]` 归一化，旧模型按 `.info.preprocessing` 自述版本加载，不需要重训。
- 增强面板按后端能力禁用 HSV/Scale/Translate/Erasing/AutoAugment，并标注"仅翻转可用"；新增分类训练基线文档 docs/plans/classification-baseline.md。
- 训练支持早停：超参数页新增"早停耐心"（0 = 关闭），连续 N 轮验证集无提升即停止并保留最佳权重。
- 指标页新增"分类指标（最新一轮）"表格，展示每类 precision/recall/F1/样本数，直接暴露"全判 OK"这类退化。
- 自动划分改为按样本哈希排序（`hash-rank-v1`）：排序键只取图库 ID 与文件名（不区分大小写），与类别遍历顺序、样本输入顺序无关；增删一张图最多让一个已有样本在 train/val 间换边，多轮训练的验证指标可比。每类验证数量口径不变；升级后首次训练会按新规则重新划分一次；`dataset-manifest.json` 新增 `split_method`。
- 训练权重 `best.torchsharp`、其 `.info` 与模型库 `.info` 改为同目录临时文件写入、刷盘后替换；进程在保存中途崩溃时目标文件保持旧版本完整，不再出现半截权重被崩溃恢复当作可用模型。
- 决定与后续阶段见 docs/adr/0002-classification-only-1.0.md、docs/adr/0003-inprocess-training-hardening.md、docs/plans/p0-import-metadata-inference.md、docs/plans/training-background-execution.md、docs/plans/flow-consistency-hardening.md、docs/plans/headless-gui-channel.md、docs/plans/end-to-end-test-channels.md、docs/plans/classification-first-batch.md。

### v0.0.1 (2026-10-16)

tags: initial / versioning

- 重置产品版本为 0.0.1，同步程序、安装包与许可元数据。
- 清除继承的历史更新记录，建立 DeepVisionSharp 初始版本记录。
- 移除设置维护入口中的硬编码访问密码，改为受控环境变量凭据并使用定时无关比较。
- 移除训练页的 Python 预热调用，Python 兼容诊断改为明确报告当前 TorchSharp 运行时不支持 Python。
- 收口训练设置与 DEBUG 页面中的 Python 文案，避免将历史兼容入口误认为当前训练依赖。
- 移除推理页不可达的 Python IR 切换选项与状态模型，统一使用进程内 C# TorchSharp 引擎。
- 删除未消费的 Python runtime settings 适配器与解释器自动探测，旧 appsettings.json 字段仍可读取。
- 训练配置统一归一到 TorchSharp 进程内引擎，移除隐藏的执行引擎选择器与 Python session 状态。
- 删除 Python 独立日志分区和过时的运行时提示；旧 backend key、sidecar 与 Anomalib DTO 保留为兼容数据结构。
- 收敛为 7 个 project：profiler 并入 Imaging.OpenCv 并保留 namespace，删除无消费者的 Imaging primitives 与 project，移除 TorchSharp 的无效图像 project 引用。
- TrainingWorkspaceCoordinator 统一拥有训练页子图、跨面板同步和生命周期，根 TrainingViewModel 构造依赖从 18 个降至 5 个。
- 离开训练页时取消页面加载、模型库刷新和草稿防抖保存，丢弃旧激活周期的状态事件、延迟选中与排队日志；不停止后端训练。
- Infrastructure 沿现有 Port 分成 Data、ModelArtifacts、Training、Inference 四组装配；保持注册 lifetime、backend/hosted 顺序和宿主 DataRoot override。
- Python/Anomalib 历史 contract 移入 LegacyCompatibility，保持完整类型名与数据形状；删除设置页 Python 操作，保留历史 PythonPath 读写兼容。
- 标注改用完整图片名 sidecar，兼容旧文件并隔离同 stem 冲突；统一导入导出、迁移与修复读取，补齐文件迁移中途失败的回滚。
- 项目操作保护覆盖训练启动与删除竞争；Preparing/Running 或启动未返回期间拒绝删除，界面返回可处理的阻塞原因。
- 分类训练显式分派 SGD/Adam/AdamW，隐藏 backend 尚未实现的 AMP/Cache/Workers；保留历史配置字段。
- 训练启动续体校验页面周期与 workspace；单图推理由 runner 调度，取消后不发布迟到结果，真实运算返回后才恢复空闲。
- 分类报表保留任意类别名称，将通用准确率与 OK/NG 指标分开；修复空图表绘制时序与导出格式提示。
- TorchSharp 模型按 key 延迟单次载入，失败可重试并释放部分资源，Dispose 等待在途操作；预热走真实载入路径并显示失败原因。
- 标注保存跟随实际图片位置，事务覆盖标注、索引、样本状态与派生统计；sidecar 原子替换失败或数据库提交失败时恢复旧状态。
- 图片与图库包导入统一按样本校验、提交与回滚，取消时保留已完成样本；训练完成及列表延迟刷新不再覆盖用户后来选择的任务。
- 补齐实例分割与语义分割导出 Singleton 注册，恢复统一导出 pipeline 与 handler 的完整依赖链。
- 分割导出按 split 预分配唯一名称，隔离同 stem、大小写及已有后缀冲突，图片与标签 / mask 保持一一对应；保留语义 mask 后画覆盖行为。
- 删除八个无消费者源码文件及废弃基类、结果类型和消息；保留归档 helper 与实际分类训练链路，清理旧发布验证压缩包约 3.40 GiB。
