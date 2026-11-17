namespace NativeCord;

/// <summary>
/// Image formats accepted by regular (non-sticker) Discord image endpoints.
/// </summary>
/// <remarks>
/// The numeric values are only meaningful within this enum and do not match
/// <see cref="StickerImageFormat"/> or <see cref="StaticImageFormat"/>.
/// Convert with <see cref="ImageFormatConverter.FromStd"/> instead of casting.
/// </remarks>
public enum StdImageFormat
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
    /// Forces a static WebP; animated images yield their first frame. Supports size query.
    /// To request an animated WebP, use <see cref="AnimatedWebP"/> instead.
    /// </summary>
    WebP = 3,

    /// <summary>
    /// Prefers an animated WebP; non-animated images fall back to a static WebP. Supports size query.
    /// </summary>
    AnimatedWebP = 4,

    /// <summary>
    /// Animated GIF. Only valid for animated images.
    /// </summary>
    Gif = 5
}