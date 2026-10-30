# DeepVisionSharp

面向使用者的版本记录：功能、修复与行为变化。实现细节见 [CHANGELOG.Developer.md](CHANGELOG.Developer.md)。

设置页「关于」读取本文件。版本节格式固定为 `### vX.Y.Z (发布日期)`，后接可选 `tags:` 与 `-` 条目。

---

### v0.0.2-dev (unreleased)

tags: detection

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
