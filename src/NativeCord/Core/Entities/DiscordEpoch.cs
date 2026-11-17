namespace NativeCord;

public static class DiscordEpoch
{
    /// <summary>
    /// Discord Epoch: 2015-01-01T00:00:00.000Z, Value is ulong Ms Timestamp
    /// </summary>
    public const ulong Timestamp = 1420070400000UL; // 建议用 ulong，因为 Snowflake 本身是 ulong

    // Snowflake 64位拆解的位移与掩码常量
    public const int TimestampShift = 22;
    public const int InternalWorkerIdShift = 17;
    public const int InternalProcessIdShift = 12;

    // 5 bits
    public const ulong InternalWorkerIdMask = 0x3E0000UL;
    // 5 bits
    public const ulong InternalProcessIdMask = 0x1F000UL;
    // 12 bits
    public const ulong IncrementMask = 0xFFFUL;
}
