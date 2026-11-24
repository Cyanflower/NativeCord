#pragma warning disable IDE0130

namespace NativeCord;

public static partial class EndPath
{
    public static class Api
    {
        public static class Application
        {
            public static string Current() => "applications/@me";

            public static string ActivityInstance(Snowflake applicationId, string instanceId)
                => $"applications/{applicationId}/activity-instances/{instanceId}";

            public static string Identity(Snowflake applicationId, Snowflake userId, string providerIssuedUserId)
                => $"applications/{applicationId}/users/{userId}/identities/{providerIssuedUserId}/profile";

            public static string IdentityByExternalId(Snowflake applicationId, string providerType, string providerIssuedUserId)
                => $"applications/{applicationId}/application-identities/{providerType}/{providerIssuedUserId}";

            public static string RoleConnectionMetadata(Snowflake applicationId)
                => $"applications/{applicationId}/role-connections/metadata";

            public static string Emojis(Snowflake applicationId) => $"applications/{applicationId}/emojis";

            public static string Emoji(Snowflake applicationId, Snowflake emojiId) => $"applications/{applicationId}/emojis/{emojiId}";

            public static string Entitlements(Snowflake applicationId) => $"applications/{applicationId}/entitlements";

            public static string Entitlement(Snowflake applicationId, Snowflake entitlementId) => $"applications/{applicationId}/entitlements/{entitlementId}";

            public static string ConsumeEntitlement(Snowflake applicationId, Snowflake entitlementId) => $"applications/{applicationId}/entitlements/{entitlementId}/consume";

            public static string Skus(Snowflake applicationId) => $"applications/{applicationId}/skus";

            public static string Commands(Snowflake applicationId) => $"applications/{applicationId}/commands";

            public static string Command(Snowflake applicationId, Snowflake commandId) => $"applications/{applicationId}/commands/{commandId}";

            public static string GuildCommands(Snowflake applicationId, Snowflake guildId) => $"applications/{applicationId}/guilds/{guildId}/commands";

            public static string GuildCommand(Snowflake applicationId, Snowflake guildId, Snowflake commandId) => $"applications/{applicationId}/guilds/{guildId}/commands/{commandId}";

            public static string GuildCommandPermissions(Snowflake applicationId, Snowflake guildId) => $"applications/{applicationId}/guilds/{guildId}/commands/permissions";

            public static string GuildCommandPermissions(Snowflake applicationId, Snowflake guildId, Snowflake commandId) => $"applications/{applicationId}/guilds/{guildId}/commands/{commandId}/permissions";
        }

        public static class Channel
        {
            public static string ById(Snowflake channelId) => $"channels/{channelId}";

            public static string MemberPermissions(Snowflake channelId, Snowflake userId) => $"channels/{channelId}/permissions/member/{userId}";

            public static string RolePermissions(Snowflake channelId, Snowflake roleId) => $"channels/{channelId}/permissions/role/{roleId}";

            public static string VoiceStatus(Snowflake channelId) => $"channels/{channelId}/voice-status";

            public static string Permission(Snowflake channelId, Snowflake overwriteId) => $"channels/{channelId}/permissions/{overwriteId}";

            public static string Invites(Snowflake channelId) => $"channels/{channelId}/invites";

            public static string Followers(Snowflake channelId) => $"channels/{channelId}/followers";

            public static string Typing(Snowflake channelId) => $"channels/{channelId}/typing";

            public static string Recipient(Snowflake channelId, Snowflake userId) => $"channels/{channelId}/recipients/{userId}";

            public static string Threads(Snowflake channelId) => $"channels/{channelId}/threads";

            public static string ThreadFromMessage(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/messages/{messageId}/threads";

            public static string ThreadMembers(Snowflake channelId) => $"channels/{channelId}/thread-members";

            public static string ThreadMember(Snowflake channelId, Snowflake userId) => $"channels/{channelId}/thread-members/{userId}";

            public static string CurrentThreadMember(Snowflake channelId) => $"channels/{channelId}/thread-members/@me";

            public static string PublicArchivedThreads(Snowflake channelId) => $"channels/{channelId}/threads/archived/public";

            public static string PrivateArchivedThreads(Snowflake channelId) => $"channels/{channelId}/threads/archived/private";

            public static string JoinedPrivateArchivedThreads(Snowflake channelId) => $"channels/{channelId}/users/@me/threads/archived/private";

            public static string Messages(Snowflake channelId) => $"channels/{channelId}/messages";

            public static string Message(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/messages/{messageId}";

            public static string CrosspostMessage(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/messages/{messageId}/crosspost";

            public static string MessageReactions(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/messages/{messageId}/reactions";

            public static string MessageReactions(Snowflake channelId, Snowflake messageId, string emoji) => $"channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(emoji)}";

            public static string MessageReactionMe(Snowflake channelId, Snowflake messageId, string emoji) => $"channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(emoji)}/@me";

            public static string MessageReactionUser(Snowflake channelId, Snowflake messageId, string emoji, Snowflake userId) => $"channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(emoji)}/{userId}";

            public static string BulkDeleteMessages(Snowflake channelId) => $"channels/{channelId}/messages/bulk-delete";

            public static string Pins(Snowflake channelId) => $"channels/{channelId}/messages/pins";

            public static string Pin(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/messages/pins/{messageId}";

            [Obsolete("This endpoint is deprecated. Use Pins instead.")]
            public static string DeprecatedPins(Snowflake channelId) => $"channels/{channelId}/pins";

            [Obsolete("This endpoint is deprecated. Use Pin instead.")]
            public static string DeprecatedPin(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/pins/{messageId}";

            public static string PollAnswers(Snowflake channelId, Snowflake messageId, int answerId) => $"channels/{channelId}/polls/{messageId}/answers/{answerId}";

            public static string EndPoll(Snowflake channelId, Snowflake messageId) => $"channels/{channelId}/polls/{messageId}/expire";

            public static string SendSoundboardSound(Snowflake channelId) => $"channels/{channelId}/send-soundboard-sound";

            public static string Webhooks(Snowflake channelId) => $"channels/{channelId}/webhooks";
        }

        public static class Guild
        {
            public static string ById(Snowflake guildId) => $"guilds/{guildId}";

            public static string Preview(Snowflake guildId) => $"guilds/{guildId}/preview";

            public static string Channels(Snowflake guildId) => $"guilds/{guildId}/channels";

            public static string ActiveThreads(Snowflake guildId) => $"guilds/{guildId}/threads/active";

            public static string Members(Snowflake guildId) => $"guilds/{guildId}/members";

            public static string Member(Snowflake guildId, Snowflake userId) => $"guilds/{guildId}/members/{userId}";

            public static string MemberSearch(Snowflake guildId) => $"guilds/{guildId}/members/search";

            public static string MemberPermissions(Snowflake guildId, Snowflake userId) => $"guilds/{guildId}/permissions/member/{userId}";

            public static string CurrentMember(Snowflake guildId) => $"guilds/{guildId}/members/@me";

            public static string CurrentMemberNick(Snowflake guildId) => $"guilds/{guildId}/members/@me/nick";

            public static string MemberRole(Snowflake guildId, Snowflake userId, Snowflake roleId) => $"guilds/{guildId}/members/{userId}/roles/{roleId}";

            public static string Bans(Snowflake guildId) => $"guilds/{guildId}/bans";

            public static string Ban(Snowflake guildId, Snowflake userId) => $"guilds/{guildId}/bans/{userId}";

            public static string BulkBan(Snowflake guildId) => $"guilds/{guildId}/bulk-ban";

            public static string Roles(Snowflake guildId) => $"guilds/{guildId}/roles";

            public static string Role(Snowflake guildId, Snowflake roleId) => $"guilds/{guildId}/roles/{roleId}";

            public static string RolePermissions(Snowflake guildId, Snowflake roleId) => $"guilds/{guildId}/permissions/role/{roleId}";

            public static string RoleMemberCounts(Snowflake guildId) => $"guilds/{guildId}/roles/member-counts";

            public static string Prune(Snowflake guildId) => $"guilds/{guildId}/prune";

            public static string Regions(Snowflake guildId) => $"guilds/{guildId}/regions";

            public static string Invites(Snowflake guildId) => $"guilds/{guildId}/invites";

            public static string Integrations(Snowflake guildId) => $"guilds/{guildId}/integrations";

            public static string Integration(Snowflake guildId, Snowflake integrationId) => $"guilds/{guildId}/integrations/{integrationId}";

            public static string Widget(Snowflake guildId) => $"guilds/{guildId}/widget";

            public static string WidgetJson(Snowflake guildId) => $"guilds/{guildId}/widget.json";

            public static string VanityUrl(Snowflake guildId) => $"guilds/{guildId}/vanity-url";

            public static string WidgetImage(Snowflake guildId) => $"guilds/{guildId}/widget.png";

            public static string WelcomeScreen(Snowflake guildId) => $"guilds/{guildId}/welcome-screen";

            public static string Onboarding(Snowflake guildId) => $"guilds/{guildId}/onboarding";

            public static string IncidentActions(Snowflake guildId) => $"guilds/{guildId}/incident-actions";

            public static string AuditLogs(Snowflake guildId) => $"guilds/{guildId}/audit-logs";

            public static string AutoModerationRules(Snowflake guildId) => $"guilds/{guildId}/auto-moderation/rules";

            public static string AutoModerationRule(Snowflake guildId, Snowflake ruleId) => $"guilds/{guildId}/auto-moderation/rules/{ruleId}";

            public static string ScheduledEvents(Snowflake guildId) => $"guilds/{guildId}/scheduled-events";

            public static string ScheduledEvent(Snowflake guildId, Snowflake eventId) => $"guilds/{guildId}/scheduled-events/{eventId}";

            public static string ScheduledEventUsers(Snowflake guildId, Snowflake eventId) => $"guilds/{guildId}/scheduled-events/{eventId}/users";

            public static string TemplateByCode(string code) => $"guilds/templates/{code}";

            public static string Templates(Snowflake guildId) => $"guilds/{guildId}/templates";

            public static string Template(Snowflake guildId, string code) => $"guilds/{guildId}/templates/{code}";

            public static string Emojis(Snowflake guildId) => $"guilds/{guildId}/emojis";

            public static string Emoji(Snowflake guildId, Snowflake emojiId) => $"guilds/{guildId}/emojis/{emojiId}";

            public static string Stickers(Snowflake guildId) => $"guilds/{guildId}/stickers";

            public static string Sticker(Snowflake guildId, Snowflake stickerId) => $"guilds/{guildId}/stickers/{stickerId}";

            public static string SoundboardSounds(Snowflake guildId) => $"guilds/{guildId}/soundboard-sounds";

            public static string SoundboardSound(Snowflake guildId, Snowflake soundId) => $"guilds/{guildId}/soundboard-sounds/{soundId}";

            public static string Webhooks(Snowflake guildId) => $"guilds/{guildId}/webhooks";

            public static string MessagesSearch(Snowflake guildId) => $"guilds/{guildId}/messages/search";

            public static string CurrentUserVoiceState(Snowflake guildId) => $"guilds/{guildId}/voice-states/@me";

            public static string UserVoiceState(Snowflake guildId, Snowflake userId) => $"guilds/{guildId}/voice-states/{userId}";
        }

        public static class User
        {
            public static string Current() => "users/@me";

            public static string ById(Snowflake userId) => $"users/{userId}";

            public static string CurrentGuilds() => "users/@me/guilds";

            public static string CurrentGuildMember(Snowflake guildId) => $"users/@me/guilds/{guildId}/member";

            public static string CurrentGuild(Snowflake guildId) => $"users/@me/guilds/{guildId}";

            public static string CurrentChannels() => "users/@me/channels";

            public static string CurrentConnections() => "users/@me/connections";

            public static string CurrentApplicationRoleConnection(Snowflake applicationId) => $"users/@me/applications/{applicationId}/role-connection";

            public static string ApplicationIdentities(Snowflake userId, Snowflake applicationId) => $"users/{userId}/application-identities/{applicationId}";

            public static string DeleteApplicationIdentity(Snowflake userId, Snowflake applicationId, string providerType, string providerIssuedUserId)
                => $"users/{userId}/application-identities/{applicationId}/{providerType}/{providerIssuedUserId}/delete";
        }

        public static class Webhook
        {
            public static string ById(Snowflake webhookId) => $"webhooks/{webhookId}";

            public static string ById(Snowflake webhookId, string token) => $"webhooks/{webhookId}/{token}";

            public static string Slack(Snowflake webhookId, string token) => $"webhooks/{webhookId}/{token}/slack";

            public static string GitHub(Snowflake webhookId, string token) => $"webhooks/{webhookId}/{token}/github";

            public static string Message(Snowflake webhookId, string token, Snowflake messageId) => $"webhooks/{webhookId}/{token}/messages/{messageId}";
        }

        public static class Invite
        {
            public static string ByCode(string code) => $"invites/{code}";

            public static string TargetUsers(string code) => $"invites/{code}/target-users";

            public static string TargetUser(string code, Snowflake userId) => $"invites/{code}/target-users/{userId}";

            public static string TargetUsersBulkAdd(string code) => $"invites/{code}/target-users/bulk-add";

            public static string TargetUsersBulkDelete(string code) => $"invites/{code}/target-users/bulk-delete";

            public static string TargetUsersJobStatus(string code) => $"invites/{code}/target-users/job-status";
        }

        public static class Lobby
        {
            public static string Lobbies() => "lobbies";

            public static string ById(Snowflake lobbyId) => $"lobbies/{lobbyId}";

            public static string Members(Snowflake lobbyId) => $"lobbies/{lobbyId}/members/bulk";

            public static string Member(Snowflake lobbyId, Snowflake userId) => $"lobbies/{lobbyId}/members/{userId}";

            public static string CurrentMember(Snowflake lobbyId) => $"lobbies/{lobbyId}/members/@me";

            public static string ChannelLinking(Snowflake lobbyId) => $"lobbies/{lobbyId}/channel-linking";

            public static string Messages(Snowflake lobbyId) => $"lobbies/{lobbyId}/messages";

            public static string MessageModerationMetadata(Snowflake lobbyId, Snowflake messageId) => $"lobbies/{lobbyId}/messages/{messageId}/moderation-metadata";

            public static string CurrentMemberInvites(Snowflake lobbyId) => $"lobbies/{lobbyId}/members/@me/invites";

            public static string MemberInvites(Snowflake lobbyId, Snowflake userId) => $"lobbies/{lobbyId}/members/{userId}/invites";
        }

        public static class StageInstance
        {
            public static string Instances() => "stage-instances";

            public static string Instance(Snowflake channelId) => $"stage-instances/{channelId}";
        }

        public static class Sticker
        {
            public static string ById(Snowflake stickerId) => $"stickers/{stickerId}";

            public static string Packs() => "sticker-packs";

            public static string Pack(Snowflake packId) => $"sticker-packs/{packId}";
        }

        public static class Sku
        {
            public static string Subscriptions(Snowflake skuId) => $"skus/{skuId}/subscriptions";

            public static string Subscription(Snowflake skuId, Snowflake subscriptionId) => $"skus/{skuId}/subscriptions/{subscriptionId}";
        }

        public static class Voice
        {
            public static string Regions() => "voice/regions";
        }

        public static class Soundboard
        {
            public static string DefaultSounds() => "soundboard-default-sounds";
        }

        public static class Gateway
        {
            public static string Base() => "gateway";

            public static string Bot() => "gateway/bot";
        }

        public static class OAuth2
        {
            public static string CurrentApplication() => "oauth2/applications/@me";

            public static string CurrentAuthorization() => "oauth2/@me";
        }

        public static class Interaction
        {
            public static string Callback(Snowflake interactionId, string interactionToken) => $"interactions/{interactionId}/{interactionToken}/callback";

            public static string Webhook(Snowflake applicationId, string interactionToken) => $"webhooks/{applicationId}/{interactionToken}";

            public static string WebhookMessage(Snowflake applicationId, string interactionToken) => $"webhooks/{applicationId}/{interactionToken}/messages/@original";

            public static string WebhookMessage(Snowflake applicationId, string interactionToken, Snowflake messageId) => $"webhooks/{applicationId}/{interactionToken}/messages/{messageId}";
        }
    }
}
