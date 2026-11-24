using System.Reflection;

namespace NativeCord;


public static class MagicString
{
    public static class Cdn
    {
        public const string AnimatedPrefix = "a_";
    }

    public static class Rest
    {
        public const string UserAgent = "DiscordBot";
        public const string RepoUri = "https://github.com/Cyanflower/NativeCord";

        public static string LibraryVersion { get; } = GetLibraryVersion();

        private static string GetLibraryVersion()
        {
            string? version = typeof(MagicString).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            return string.IsNullOrEmpty(version) ? "0.0.0" : version;
        }

        public static class RateLimitScope
        {
            public const string User = "user";
            public const string Shared = "shared";
            public const string Global = "global";
        }
        
    }
}