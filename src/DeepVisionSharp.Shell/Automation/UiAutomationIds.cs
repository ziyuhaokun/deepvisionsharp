namespace DeepVisionSharp.Shell.Automation;

/// <summary>
/// Canonical AutomationId values for FlaUI and XAML bindings.
/// UiTests reference this type directly — do not mirror constants elsewhere.
/// </summary>
public static class UiAutomationIds
{
    public static class Nav
    {
        public const string Toggle = "dv.nav.toggle";
        public const string Dashboard = "dv.nav.dashboard";
        public const string Gallery = "dv.nav.gallery";
        public const string Labeling = "dv.nav.labeling";
        public const string Models = "dv.nav.models";
        public const string Training = "dv.nav.training";
        public const string Inference = "dv.nav.inference";
        public const string Settings = "dv.nav.settings";
    }

    public static class Title
    {
        public const string ProjectName = "dv.title.project-name";
    }

    public static class Dashboard
    {
        public const string ImportProject = "dv.dashboard.import-project";
        public const string CreateProject = "dv.dashboard.create-project";
        public const string ProjectCardsHost = "dv.dashboard.project-cards";

        public const string ProjectCardPrefix = "dv.dashboard.project-card.";
        public const string ProjectEnterPrefix = "dv.dashboard.project-enter.";

        public static string ProjectCard(Guid projectId) => $"{ProjectCardPrefix}{projectId:D}";

        public static string ProjectEnter(Guid projectId) => $"{ProjectEnterPrefix}{projectId:D}";
    }

    public static class Gallery
    {
        public const string Create = "dv.gallery.create";
        public const string ImportGallery = "dv.gallery.import";
        public const string CardsHost = "dv.gallery.cards";
        public const string Search = "dv.gallery.search";

        public const string CardPrefix = "dv.gallery.card.";
        public const string EnterPrefix = "dv.gallery.enter.";
        public const string EditPrefix = "dv.gallery.edit.";
        public const string MorePrefix = "dv.gallery.more.";

        public static string Card(Guid galleryId) => $"{CardPrefix}{galleryId:D}";

        public static string Enter(Guid galleryId) => $"{EnterPrefix}{galleryId:D}";

        public static string Edit(Guid galleryId) => $"{EditPrefix}{galleryId:D}";

        public static string More(Guid galleryId) => $"{MorePrefix}{galleryId:D}";

        public const string MenuImportImages = "dv.gallery.menu.import-images";
        public const string MenuImportFolder = "dv.gallery.menu.import-folder";
        public const string MenuDelete = "dv.gallery.menu.delete";
    }

    public static class Labeling
    {
        public const string BackToGallery = "dv.labeling.back-to-gallery";
        public const string Canvas = "dv.labeling.canvas";
        public const string ImportImages = "dv.labeling.import-images";
        public const string AddLabel = "dv.labeling.label.add";
        public const string ActiveLabel = "dv.labeling.label.active";
        public const string LabelsHost = "dv.labeling.labels";
        public const string AnnotationsHost = "dv.labeling.annotations";
        public const string SamplePrev = "dv.labeling.sample.prev";
        public const string SampleNext = "dv.labeling.sample.next";
        public const string SamplePosition = "dv.labeling.sample.position";
        public const string ToolSelect = "dv.labeling.tool.select";
    }

    public static class Models
    {
        public const string Create = "dv.models.create";
        public const string CardsHost = "dv.models.cards";
        public const string CardPrefix = "dv.models.card.";

        public static string Card(Guid modelId) => $"{CardPrefix}{modelId:D}";
    }

    public static class Training
    {
        public const string CreateJob = "dv.training.create-job";
        public const string Refresh = "dv.training.refresh";
        public const string JobsHost = "dv.training.jobs";
        public const string Start = "dv.training.start";
        public const string Resume = "dv.training.resume";
        public const string Stop = "dv.training.stop";
        public const string SaveAsModel = "dv.training.save-as-model";
        public const string BlockReason = "dv.training.block-reason";
        public const string TabBasic = "dv.training.tab.basic";
        public const string TabDataset = "dv.training.tab.dataset";
        public const string GalleriesHost = "dv.training.galleries";
        public const string LibraryModel = "dv.training.library-model";
        public const string JobStatus = "dv.training.job-status";
        public const string ValidationRatio = "dv.training.validation-ratio";

        public const string JobPrefix = "dv.training.job.";
        public const string GallerySelectPrefix = "dv.training.gallery.";

        public static string Job(Guid jobId) => $"{JobPrefix}{jobId:D}";

        public static string GallerySelect(Guid galleryId) => $"{GallerySelectPrefix}{galleryId:D}.select";
    }

    public static class Inference
    {
        public const string ImageSource = "dv.inference.image-source";
        public const string RunSelected = "dv.inference.run-selected";
        public const string RunAll = "dv.inference.run-all";
        public const string Stop = "dv.inference.stop";
        public const string Model = "dv.inference.model";
        public const string Device = "dv.inference.device";
        public const string ResultHeadline = "dv.inference.result-headline";
        public const string ResultRows = "dv.inference.result-rows";
        public const string Status = "dv.inference.status";
    }

    public static class Settings
    {
        public const string Root = "dv.settings.root";
    }

    public static class Dialog
    {
        public const string Host = "dv.dialog.host";
        public const string Primary = "dv.dialog.primary";
        public const string Secondary = "dv.dialog.secondary";
        public const string Close = "dv.dialog.close";
        public const string GalleryName = "dv.dialog.gallery-name";
        public const string GalleryDescription = "dv.dialog.gallery-description";
        public const string LabelName = "dv.dialog.label-name";
        public const string ProjectName = "dv.dialog.project-name";
        public const string ProjectDescription = "dv.dialog.project-description";
        public const string ProjectType = "dv.dialog.project-type";
        public const string ModelName = "dv.dialog.model-name";
        public const string ModelType = "dv.dialog.model-type";
        public const string ModelNote = "dv.dialog.model-note";
        public const string InferenceGallery = "dv.dialog.inference-gallery";
        public const string InferenceLoadGallery = "dv.dialog.inference-load-gallery";
        public const string InferenceImportExternal = "dv.dialog.inference-import-external";
    }
}
