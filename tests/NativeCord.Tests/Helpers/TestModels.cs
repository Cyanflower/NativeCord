using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using NativeCord.Rest;

namespace NativeCord.Tests.Helpers;

/// <summary>
/// Minimal AOT-serializable user DTO used to test <see cref="RestClient"/> end-to-end.
/// </summary>
internal sealed record TestUser(string Id, string Username) : IJsonModel<TestUser>
{
    public static JsonTypeInfo<TestUser> JsonInfo => TestJsonContext.Default.TestUser;
}

/// <summary>
/// Minimal AOT-serializable request payload used to test JSON request bodies.
/// </summary>
internal sealed record TestMessagePayload(string Content, bool Tts = false) : IJsonModel<TestMessagePayload>
{
    public static JsonTypeInfo<TestMessagePayload> JsonInfo => TestJsonContext.Default.TestMessagePayload;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TestUser))]
[JsonSerializable(typeof(TestMessagePayload))]
internal sealed partial class TestJsonContext : JsonSerializerContext;