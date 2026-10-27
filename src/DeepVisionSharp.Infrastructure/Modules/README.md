# Infrastructure module 装配

宿主继续调用 `AddDeepVisionInfrastructure()`；以下注册入口仅在 project 内部使用。

| Module | Caller-facing interface / seam | Implementation / adapter | 依赖与生命周期 |
| --- | --- | --- | --- |
| Data | Domain repository interface；Application 的图库/标注/导入导出/存储 Port | SQLite、项目文件、OpenCV 缩略图/模板匹配 | Singleton；先注册宿主 IDataRootProvider 时不覆盖；持久化守卫继续保护归档数据 |
| ModelArtifacts | IModelMetadataReader、IModelCheckpointFileService、ITrainedModelArtifactService、已有 export Port | metadata/sidecar、模型产物 | Singleton；使用 Data 的 repository/file store；不新增 Python adapter |
| Training | ITrainingJobLifecycleService、ITrainingProcessHost、ITrainingWorkspacePlanner、ITrainingRunCleanupService | 训练准备、进程内 TorchSharp adapter、完成、恢复与对账 | Singleton；依赖 Data 与 ModelArtifacts；Recovery hosted module 仍在 Reconcile 前注册 |
| Inference | IInferenceRunner、IInferenceStatusReader、IInferenceShutdown | TorchSharp adapter 与 registry | Singleton；仅注册分类 TorchSharp backend |

这些 module 复用现有 interface，不增加一个无行为的聚合 Port。此轮收拢装配所有权，implementation 仍在既有 Persistence/Services 路径；只有出现独立 consumer、第二个真实 adapter 或独立 native packaging 需求时，再考虑迁移命名空间与拆 project。
