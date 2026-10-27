using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Modules;

internal static class DataModule
{
    internal static IServiceCollection AddDataModule(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IDataRootProvider)))
        {
            services.AddSingleton<IDataRootProvider>(_ => new DefaultDataRootProvider());
        }
        services.AddSingleton<SqliteDb>();
        services.AddSingleton<ProjectOperationGate>();
        services.AddSingleton<IProjectFileStore, ProjectFileStore>();
        services.AddSingleton<IProjectSqliteTransferService, ProjectSqliteTransferService>();
        services.AddSingleton<IClassificationDatasetExportService, ClassificationDatasetExportService>();
        services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
        services.AddSingleton<IGalleryRepository, SqliteGalleryRepository>();
        services.AddSingleton<IGalleryTransferService, GalleryTransferService>();
        services.AddSingleton<IAnnotationWorkflowService, AnnotationWorkflowService>();
        services.AddSingleton<IGalleryAnnotationLabelQueryService, GalleryAnnotationLabelQueryService>();
        services.AddSingleton<IProjectLabelStatisticsQueryService, ProjectLabelStatisticsQueryService>();
        services.AddSingleton<ISampleMigrationService, SampleMigrationService>();
        services.AddSingleton<IProjectLabelService, ProjectLabelService>();
        services.AddSingleton<IModelRepository, SqliteModelRepository>();
        services.AddSingleton<ITrainingJobRepository, SqliteTrainingJobRepository>();
        services.AddSingleton<ISampleFileMover, SampleFileMover>();
        services.AddSingleton<ISampleThumbnailService, OpenCvSampleThumbnailService>();
        services.AddSingleton<IDataIntegrityDebugService, DataIntegrityDebugService>();
        services.AddSingleton<IStorageCleanupService, StorageCleanupService>();
        return services;
    }
}
