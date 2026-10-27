namespace DeepVisionSharp.Infrastructure.Services.Transfer;

/// <summary>Payload decoding rules shared by project and gallery imports.</summary>
internal static class TransferPayloads
{
    /// <summary>
    /// Returns the Base64 body of an image payload, dropping an optional data-URI prefix
    /// ("data:image/png;base64,..."). Blank input yields null.
    /// </summary>
    public static string? ExtractBase64(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        var commaIndex = data.IndexOf(',');
        return commaIndex >= 0 ? data[(commaIndex + 1)..] : data;
    }
}
