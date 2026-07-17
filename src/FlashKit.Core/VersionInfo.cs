using System.Reflection;

namespace FlashKit.Core;

/// <summary>Version stamped into the build. publish.sh passes git
/// describe output as InformationalVersion — the bare tag on releases
/// (the workflow pins it to the tag name), tag-N-gSHA on branch/local
/// builds. Unstamped builds (ci.sh, dotnet run) report "dev".</summary>
public static class VersionInfo
{
    public static string ClientVersion { get; } =
        typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "dev";
}
