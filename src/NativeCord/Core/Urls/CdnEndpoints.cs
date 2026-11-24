using System.Globalization;
using System.Runtime.CompilerServices;

namespace NativeCord.Core.Urls;

internal sealed class CdnEndpoints(BaseUrl baseUrl)
{
    private readonly BaseUrl _baseUrl = baseUrl;

    private static DiscordUrl BuildCdnUrl(string baseUrl, string endpoint, ImageFormat format, ImageSize? imageSize = null)
    {
        if (imageSize > ImageSize.Size4K)
            imageSize = ImageSize.Size4K;
        if (imageSize < ImageSize.Size16)
            imageSize = ImageSize.Size16;

        return format switch
        {
            // png, jpg, webp(static) and gif do not allow animated parameter
            ImageFormat.Png or ImageFormat.Jpg or ImageFormat.WebP or ImageFormat.Gif => imageSize is { } size
                ? new DiscordUrl($"{baseUrl}/{endpoint}.{format.FormatString}?size={(int)size}")
                : new DiscordUrl($"{baseUrl}/{endpoint}.{format.FormatString}"),

            // apng and lottie(json) ignore size parameter
            ImageFormat.AnimatedPng or ImageFormat.Lottie
                => new DiscordUrl($"{baseUrl}/{endpoint}.{format.FormatString}"),

            // webp(animated) requires ?animated=true, and size prefix is &
            ImageFormat.AnimatedWebP => imageSize is { } size
                ? new DiscordUrl($"{baseUrl}/{endpoint}.{format.FormatString}?animated=true&size={(int)size}")
                : new DiscordUrl($"{baseUrl}/{endpoint}.{format.FormatString}?animated=true"),

            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The enumeration value is undefined.")
        };
    }

    internal DiscordUrl CustomEmoji(Snowflake emojiId, bool isAnimated = false, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.CustomEmoji(emojiId), ImageFormatConverter.FromStd(format, isAnimated), imageSize);

    internal DiscordUrl GuildIcon(Snowflake guildId, ImageHash guildIcon, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildIcon(guildId, guildIcon), ImageFormatConverter.FromStd(format, guildIcon.IsAnimated), imageSize);

    internal DiscordUrl GuildSplash(Snowflake guildId, ImageHash guildSplash, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildSplash(guildId, guildSplash), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl GuildDiscoverySplash(Snowflake guildId, ImageHash guildDiscoverySplash, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildDiscoverySplash(guildId, guildDiscoverySplash), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl GuildBanner(Snowflake guildId, ImageHash guildBanner, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildBanner(guildId, guildBanner), ImageFormatConverter.FromStd(format, guildBanner.IsAnimated), imageSize);

    internal DiscordUrl UserBanner(Snowflake userId, ImageHash userBanner, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.UserBanner(userId, userBanner), ImageFormatConverter.FromStd(format, userBanner.IsAnimated), imageSize);

    internal DiscordUrl UserAvatar(Snowflake userId, ImageHash userAvatar, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.UserAvatar(userId, userAvatar), ImageFormatConverter.FromStd(format, userAvatar.IsAnimated), imageSize);

    // DefaultUserAvatar does not support size. only png
    internal DiscordUrl DefaultUserAvatar(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 5);
        return BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.DefaultUserAvatar(index), ImageFormat.Png);
    }

    internal DiscordUrl GuildMemberAvatar(Snowflake guildId, Snowflake userId, ImageHash memberAvatar, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildMemberAvatar(guildId, userId, memberAvatar), ImageFormatConverter.FromStd(format, memberAvatar.IsAnimated), imageSize);

    // AvatarDecoration does not support size, only png
    internal DiscordUrl AvatarDecoration(ImageHash avatarDecorationDataAsset)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.AvatarDecoration(avatarDecorationDataAsset), ImageFormat.Png);

    internal DiscordUrl ApplicationIcon(Snowflake applicationId, ImageHash icon, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.ApplicationIcon(applicationId, icon), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl ApplicationCover(Snowflake applicationId, ImageHash cover, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.ApplicationCover(applicationId, cover), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl ApplicationAsset(Snowflake applicationId, ImageHash assetId, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.ApplicationAsset(applicationId, assetId), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl AchievementIcon(Snowflake applicationId, Snowflake achievementId, ImageHash iconHash, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.AchievementIcon(applicationId, achievementId, iconHash), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl StorePageAsset(Snowflake applicationId, ImageHash assetId, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.StorePageAsset(applicationId, assetId), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl StickerPackBanner(Snowflake stickerPackBannerAssetId, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.StickerPackBanner(stickerPackBannerAssetId), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl TeamIcon(Snowflake teamId, ImageHash teamIcon, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.TeamIcon(teamId, teamIcon), ImageFormatConverter.FromStatic(format), imageSize);

    // Sticker endpoint does not support size. This is an endpoint attribute rather than a format attribute.
    internal DiscordUrl Sticker(Snowflake stickerId, StickerImageFormat stickerImageFormat)
        => stickerImageFormat is StickerImageFormat.Gif
            ? BuildCdnUrl(_baseUrl.Media, EndPath.Cdn.Sticker(stickerId), ImageFormat.Gif)
            : BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.Sticker(stickerId), ImageFormatConverter.FromSticker(stickerImageFormat));

    internal DiscordUrl RoleIcon(Snowflake roleId, ImageHash roleIcon, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.RoleIcon(roleId, roleIcon), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl GuildScheduledEventCover(Snowflake scheduledEventId, ImageHash scheduledEventCoverImage, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildScheduledEventCover(scheduledEventId, scheduledEventCoverImage), ImageFormatConverter.FromStatic(format), imageSize);

    internal DiscordUrl GuildMemberBanner(Snowflake guildId, Snowflake userId, ImageHash memberBanner, StdImageFormat format = StdImageFormat.AnimatedWebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildMemberBanner(guildId, userId, memberBanner), ImageFormatConverter.FromStd(format, memberBanner.IsAnimated), imageSize);
        
    internal DiscordUrl GuildTagBadge(Snowflake guildId, ImageHash badgeHash, StaticImageFormat format = StaticImageFormat.WebP, ImageSize? imageSize = null)
        => BuildCdnUrl(_baseUrl.Cdn, EndPath.Cdn.GuildTagBadge(guildId, badgeHash), ImageFormatConverter.FromStatic(format), imageSize);
}