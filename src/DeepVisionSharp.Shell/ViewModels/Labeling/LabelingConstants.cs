namespace DeepVisionSharp.Shell.ViewModels.Labeling;

internal static class LabelingConstants
{
    public const int MaxHistoryEntries = 50;
    public const int SampleThumbnailMaxEdge = 200;
    public const int SamplePageSize = 10;
    public const int TemplatePreviewMaxEdge = 120;
    public const string ToolRectangle = "rectangle";

    public const string ToolPolygon = "polygon";
    public const string ToolSelect = "select";
    public const string ImageClassOk = "OK";
    public const string ImageClassNg = "NG";

    public static readonly string[] Palette =
    [
        "#3B82F6",
        "#EF4444",
        "#22C55E",
        "#F59E0B",
        "#A855F7",
        "#14B8A6",
        "#F97316",
        "#EC4899",
        "#64748B",
        "#10B981"
    ];
}
