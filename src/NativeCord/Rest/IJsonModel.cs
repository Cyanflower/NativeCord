using System.Text.Json.Serialization.Metadata;

namespace NativeCord.Rest;

public interface IJsonModel<TSelf> where TSelf : IJsonModel<TSelf>
{
    static abstract JsonTypeInfo<TSelf> JsonInfo { get; }
}