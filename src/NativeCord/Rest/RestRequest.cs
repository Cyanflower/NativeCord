namespace NativeCord.Rest;

// 刻意不携带响应泛型 <TRes>：响应类型由 RestClient.SendAsync<TRes> 决定，避免“发什么”和“收什么”耦合在一起

/// <summary>
/// Allocation-free universal descriptor for a single Discord REST API request.
/// </summary>
public readonly struct RestRequest
{
    /// <summary>
    /// Target endpoint route, rate-limit metadata, and default authentication flags.
    /// </summary>
    public required RestRoute Route { get; init; }

    /// <summary>
    /// Request payload; defaults to <see cref="RestBody.Empty"/>.
    /// </summary>
    public RestBody Body { get; init; }

    /// <summary>
    /// Optional reason written to the <c>X-Audit-Log-Reason</c> header (automatically URL-encoded).
    /// </summary>
    public string? AuditLogReason { get; init; }

    /// <summary>
    /// Optional token overriding the client's default authorization for this request
    /// (for example, OAuth2 Bearer or Basic credentials).
    /// </summary>
    public BotToken? OverrideToken { get; init; }
}