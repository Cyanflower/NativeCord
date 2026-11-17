namespace NativeCord;

/// <summary>
/// The canonical image format used when building CDN URLs.
/// </summary>
internal enum ImageFormat
{
    /// <summary>
    /// Static PNG. Supports size query.
    /// </summary>
    Png = 1,

    /// <summary>
    /// Animated PNG (APNG, stickers only). Does not support size query.
    /// </summary>
    AnimatedPng = 2,

    /// <summary>
    /// Static JPG/JPEG. Supports size query.
    /// </summary>
    Jpg = 3,

    /// <summary>
    /// Forces a static WebP; animated images yield their first frame. Supports size query.
    /// To request an animated WebP, use <see cref="AnimatedWebP"/> instead.
    /// </summary>
    WebP = 4,

    /// <summary>
    /// Prefers an animated WebP; non-animated images fall back to a static WebP. Supports size query.
    /// </summary>
    AnimatedWebP = 5,

    /// <summary>
    /// Animated GIF. Cannot be used for static images.
    /// </summary>
    Gif = 6,

    /// <summary>
    /// Lottie JSON (stickers only). Does not support size query.
    /// </summary>
    Lottie = 7
}

internal static class ImageFormatExtensions
{
    extension(ImageFormat format)
    {
        /// <summary>
        /// The file extension, without the leading dot, used in CDN URLs.
        /// </summary>
        /// <remarks>
        /// <see cref="ImageFormat.Png"/> and <see cref="ImageFormat.AnimatedPng"/> both map to
        /// <c>"png"</c> on purpose: an APNG is detected by its file content, not by the extension.
        /// </remarks>
        internal string FormatString => format switch
        {
            ImageFormat.Png => "png",
            ImageFormat.AnimatedPng => "png",
            ImageFormat.Jpg => "jpg",
            ImageFormat.WebP => "webp",
            ImageFormat.AnimatedWebP => "webp",
            ImageFormat.Gif => "gif",
            ImageFormat.Lottie => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The enumeration value is undefined.")
        };
    }
}
