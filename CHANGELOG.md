# DeepVisionSharp

面向使用者的版本记录：功能、修复与行为变化。实现细节见 [CHANGELOG.Developer.md](CHANGELOG.Developer.md)。

设置页「关于」读取本文件。版本节格式固定为 `### vX.Y.Z (发布日期)`，后接可选 `tags:` 与 `-` 条目。

---

### v0.0.2-dev (unreleased)

tags: detection / avalonia

- 主程序改为 Avalonia。标注、训练、推理仍在同一个进程里，不启动 Python。
- 项目只做图像分类和目标检测。分类仍是整图标注、整图训练。
- 检测支持矩形框、无目标负样本、导入和导出 YOLO 数据集、进程内训练 YOLO26-det、推理画框。训练页显示 mAP50 和 mAP50-95。
- 检测训练的 mAP 统计会纳入低分候选，数值可能与旧版不同但更完整；训练日志与模型 `.info` 会记录评估分数下限。
- 标注页的框加了深色描边，当前图的标注状态直接显示在画布角落和样本条上。

### v0.0.1 (2026-10-16)

tags: initial

- DeepVisionSharp 初始公开版本：进程内图像分类，标注、训练、推理同一 WPF 进程完成。
