#pragma warning disable IDE0130

namespace NativeCord;

public static partial class Endpoints
{
    public static class Cdn
    {
        public static string CustomEmoji(Snowflake emojiId) => $"emojis/{emojiId}";
        public static string GuildIcon(Snowflake guildId, ImageHash guildIcon) => $"icons/{guildId}/{guildIcon}";
        public static string GuildSplash(Snowflake guildId, ImageHash guildSplash) => $"splashes/{guildId}/{guildSplash}";
        public static string GuildDiscoverySplash(Snowflake guildId, ImageHash guildDiscoverySplash) => $"discovery-splashes/{guildId}/{guildDiscoverySplash}";
        public static string GuildBanner(Snowflake guildId, ImageHash guildBanner) => $"banners/{guildId}/{guildBanner}";
        public static string UserBanner(Snowflake userId, ImageHash userBanner) => $"banners/{userId}/{userBanner}";
        public static string DefaultUserAvatar(int index) => $"embed/avatars/{index}";
        public static string UserAvatar(Snowflake userId, ImageHash userAvatar) => $"avatars/{userId}/{userAvatar}";
        public static string GuildMemberAvatar(Snowflake guildId, Snowflake userId, ImageHash memberAvatar) => $"guilds/{guildId}/users/{userId}/avatars/{memberAvatar}";
        public static string AvatarDecoration(ImageHash avatarDecorationDataAsset) => $"avatar-decoration-presets/{avatarDecorationDataAsset}";
        public static string ApplicationIcon(Snowflake applicationId, ImageHash icon) => $"app-icons/{applicationId}/{icon}";
        public static string ApplicationCover(Snowflake applicationId, ImageHash coverImage) => $"app-icons/{applicationId}/{coverImage}";
        public static string ApplicationAsset(Snowflake applicationId, ImageHash assetId) => $"app-assets/{applicationId}/{assetId}";
        public static string AchievementIcon(Snowflake applicationId, Snowflake achievementId, ImageHash iconHash) => $"app-assets/{applicationId}/achievements/{achievementId}/icons/{iconHash}";
        public static string StorePageAsset(Snowflake applicationId, ImageHash assetId) => $"app-assets/{applicationId}/store/{assetId}";
        public static string StickerPackBanner(Snowflake stickerPackBannerAssetId) => $"app-assets/{MagicNumber.StickersApplicationId}/store/{stickerPackBannerAssetId}";
        public static string TeamIcon(Snowflake teamId, ImageHash teamIcon) => $"team-icons/{teamId}/{teamIcon}";
        public static string Sticker(Snowflake stickerId) => $"stickers/{stickerId}";
        public static string RoleIcon(Snowflake roleId, ImageHash roleIcon) => $"role-icons/{roleId}/{roleIcon}";
        public static string GuildScheduledEventCover(Snowflake scheduledEventId, ImageHash scheduledEventCoverImage) => $"guild-events/{scheduledEventId}/{scheduledEventCoverImage}";
        public static string GuildMemberBanner(Snowflake guildId, Snowflake userId, ImageHash memberBanner) => $"guilds/{guildId}/users/{userId}/banners/{memberBanner}";
        public static string GuildTagBadge(Snowflake guildId, ImageHash badgeHash) => $"guild-tag-badges/{guildId}/{badgeHash}";
    }
}
