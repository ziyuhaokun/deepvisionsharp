namespace DeepVisionSharp.Domain.ValueObjects;

public static class AnnotationClassLabels
{
    public const string ImageClassFlagKey = "imageClass";

    /// <summary>Detection review mark: the sample was inspected, even when it contains no box.</summary>
    public const string ReviewedFlagKey = "dvReviewed";

    public const string Ok = "OK";

    public const string Ng = "NG";
}
