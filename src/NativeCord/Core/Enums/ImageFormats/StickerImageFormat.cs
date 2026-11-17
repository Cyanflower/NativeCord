namespace NativeCord;

/// <summary>
/// Sticker format types, as defined by "Sticker Format Types" in the Discord API reference.
/// </summary>
/// <remarks>
/// Values: PNG = 1, APNG = 2, LOTTIE = 3, GIF = 4.
/// These values are only meaningful within this enum; in particular <see cref="Gif"/> is 4 here
/// but 5 in <see cref="StdImageFormat"/>. Convert with <see cref="ImageFormatConverter.FromSticker"/>
/// instead of casting.
/// </remarks>
public enum StickerImageFormat
{
    /// <summary>
    /// Static PNG. Supports size query.
    /// </summary>
    Png = 1,

    /// <summary>
    /// Animated PNG (APNG). Does not support size query.
    /// </summary>
    APng = 2,

    /// <summary>
    /// Lottie JSON. Does not support size query.
    /// </summary>
    Lottie = 3,

    /// <summary>
    /// Animated GIF.
    /// </summary>
    Gif = 4
}