namespace NativeCord;

/// <summary>
/// Base URLs and API version configuration for Discord endpoints.
/// </summary>
public sealed record BaseUrl
{
    public static BaseUrl Default { get; } = new();

    public int ApiVersion { get; init; } = 10;
    public int VoiceGatewayVersion { get; init; } = 8;
    public string Rest { get; init; } = "https://discord.com/api";
    public string Cdn { get; init; } = "https://cdn.discordapp.com";
    public string Media { get; init; } = "https://media.discordapp.net";
    public string Invite { get; init; } = "https://discord.gg";

    /// <summary>
    /// Builds the versioned REST API base URI ending with a trailing slash for <see cref="HttpClient.BaseAddress"/>.
    /// </summary>
    public Uri GetRestApiBaseUri()
        // HttpClient.BaseAddress 必须以 '/' 结尾，这样后续拼接相对路径时才不会把 '/v10' 这一段吞掉
        => new($"{Rest.TrimEnd('/')}/v{ApiVersion}/");
}