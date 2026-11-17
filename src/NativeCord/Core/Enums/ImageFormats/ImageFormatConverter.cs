namespace NativeCord;

/// <summary>
/// Converts the API-specific image format enums to the canonical <see cref="ImageFormat"/>.
/// </summary>
internal static class ImageFormatConverter
{
    /// <summary>
    /// Converts a format requested from a regular (non-sticker) image endpoint.
    /// </summary>
    /// <param name="format">The requested format.</param>
    /// <param name="isAnimated">Whether the target resource is animated.</param>
    /// <returns>The canonical image format to use in a CDN URL.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="format"/> is <see cref="StdImageFormat.Gif"/> while <paramref name="isAnimated"/> is <see langword="false"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a defined <see cref="StdImageFormat"/> value.
    /// </exception>
    internal static ImageFormat FromStd(StdImageFormat format, bool isAnimated)
    {
        return format switch
        {
            StdImageFormat.Png => ImageFormat.Png,
            StdImageFormat.Jpg => ImageFormat.Jpg,
            StdImageFormat.WebP => ImageFormat.WebP,
            // Non-animated images cannot be requested as animated WebP; fall back to static WebP.
            StdImageFormat.AnimatedWebP => isAnimated ? ImageFormat.AnimatedWebP : ImageFormat.WebP,
            StdImageFormat.Gif => isAnimated
                ? ImageFormat.Gif
                : throw new NotSupportedException("The resource is not an animated image, so GIF cannot be requested."),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The enumeration value is undefined.")
        };
    }

    /// <summary>
    /// Converts a format requested from a static-image-only endpoint.
    /// </summary>
    /// <param name="format">The requested format.</param>
    /// <returns>The canonical image format to use in a CDN URL.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a defined <see cref="StaticImageFormat"/> value.
    /// </exception>
    internal static ImageFormat FromStatic(StaticImageFormat format)
    {
        return format switch
        {
            StaticImageFormat.Png => ImageFormat.Png,
            StaticImageFormat.Jpg => ImageFormat.Jpg,
            StaticImageFormat.WebP => ImageFormat.WebP,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The enumeration value is undefined.")
        };
    }

    /// <summary>
    /// Converts a sticker format.
    /// </summary>
    /// <param name="format">The sticker format.</param>
    /// <returns>The canonical image format to use in a CDN URL.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is not a defined <see cref="StickerImageFormat"/> value.
    /// </exception>
    internal static ImageFormat FromSticker(StickerImageFormat format)
    {
        return format switch
        {
            StickerImageFormat.Png => ImageFormat.Png,
            StickerImageFormat.APng => ImageFormat.AnimatedPng,
            StickerImageFormat.Lottie => ImageFormat.Lottie,
            StickerImageFormat.Gif => ImageFormat.Gif,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The enumeration value is undefined.")
        };
    }
}
