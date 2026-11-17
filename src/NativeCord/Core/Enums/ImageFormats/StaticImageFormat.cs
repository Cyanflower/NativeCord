namespace NativeCord;

/// <summary>
/// Image formats accepted by image endpoints that only serve static images.
/// </summary>
/// <remarks>
/// The numeric values are only meaningful within this enum.
/// Convert with <see cref="ImageFormatConverter.FromStatic"/> instead of casting.
/// </remarks>
public enum StaticImageFormat
{
    /// <summary>
    /// Static PNG. Supports size query.
    /// </summary>
    Png = 1,

    /// <summary>
    /// Static JPG/JPEG. Supports size query.
    /// </summary>
    Jpg = 2,

    /// <summary>
    /// Static WebP. Supports size query.
    /// </summary>
    WebP = 3
}