# DeepVisionSharp

面向使用者的版本记录：功能、修复与行为变化。实现细节见 [CHANGELOG.Developer.md](CHANGELOG.Developer.md)。

设置页「关于」读取本文件。版本节格式固定为 `### vX.Y.Z (发布日期)`，后接可选 `tags:` 与 `-` 条目。

---

### v0.0.2-dev (unreleased)

tags: detection / ui / avalonia

- 主程序和 Windows 安装向导统一使用 Avalonia。标签统计和批量推理报告在 Avalonia 内显示图表。

- 新增 Avalonia 版界面外壳（开发中，未替换现有 WPF 外壳）：把 deepvision 的界面整份搬过来，7 个页面（项目/图库/标记/模型/训练/推理/设置）与 26 个对话框已能跑在真实数据上；项目列表、图库、模型、标注、训练、推理、设置页的界面数据绑定实测无错误。
- 抽取与界面框架无关的共享表现层，WPF 与 Avalonia 两个外壳共用同一套业务逻辑；现有 WPF 版的界面与功能行为不变。
- Avalonia 外壳补齐与 WPF 一致的启动期行为：文件日志与日志保留、单实例、崩溃转储、未处理异常提示、启动参数。
- 裁剪上游界面中本产品不具备的入口（AI 判定、模板匹配、多边形/分割标注、OCR、异常检测、训练参数预设等），并删除设置页里并不存在的「Python 环境」分区；不保留点了没反应的按钮。
- 修复分类项目进不了「分类增强」的问题；训练设置补回「模型架构」与「检测参数」两组真实参数。
- 新增 `docs/avalonia-migration.md`：迁移现状、端口对照、验收方法与下线 WPF 外壳的前置条件清单。

- 新增 WPF 设计系统 `yu-ui`（`src/yu-ui`）：令牌、77 个图标、14 个控件样式与 7 个自绘控件（ThemedWindow / Dialog / FormSection / FormRow / NumericBox / Icon / 占位文本），视觉与令牌逐值来自 deepvision 的 `InCube.UI.Avalonia`。此版本只接线令牌，界面外观不变。
- 新增 `YuUiStyleProbeWindow`（DEBUG 验收页）与 `docs/yu-ui-migration.md`（WPF-UI → yu-ui 控件映射与分批迁移依据）。
- 界面换用自研 yu-ui 设计系统：外壳、页面、对话框统一浅色风格；移除对 WPF-UI 的依赖。上面两条 yu-ui 条目属于接线阶段（外观未变），本条是收口后用户实际看到的外观。

- 新增目标检测项目类型：矩形框标注、标记「无目标」负样本、导入 YOLO 数据集、导出 YOLO 数据集、进程内训练 YOLO26-det、推理画框与批量报告。
- 检测训练展示每类 precision / recall / F1、mAP50、mAP50-95；模型卡片显示 mAP，最佳权重按 mAP50 选取。
- 批量推理新增「追加预测框」写回策略，可把预测框采纳为标注后再人工微调；未采纳时不改动现有标注。
- 图像分类项目、旧标注与旧模型保持原有行为；分类数据集导出与训练指标不变。

- 崩溃提示与批量推理 HTML 报告使用产品名 DeepVisionSharp。DEBUG 推理报告复制到剪贴板后，随进程退出清除。
- 分类训练与推理不再携带切图 / letterbox 路径；整张图拉伸到网络输入。切图几何独立为工具模块，当前产品流程不调用。
- 切图工具覆盖整图、滑窗（贴边 / 丢弃 / 填充末片）、均匀网格与居中裁；适配覆盖 Stretch / Letterbox / Cover / Pad / 短边 / 长边，以及 stride 对齐。

### v0.0.1 (2026-10-16)

tags: initial

- DeepVisionSharp 初始公开版本：进程内图像分类，标注、训练、推理同一 WPF 进程完成。
