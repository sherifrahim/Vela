using System.IO;
using System.Runtime.CompilerServices;
using Vela.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Vela.Tests;

internal static class TestEnvironment
{
    public static string Root { get; private set; } = "";

    [ModuleInitializer]
    public static void Initialize()
    {
        Root = Path.Combine(Path.GetTempPath(), "Vela.Tests", $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        // Paths.Root is intentionally static for the app. Redirect it before Settings is initialized so
        // migration tests can never read or write the user's real %LOCALAPPDATA%\Vela directory.
        Unsafe.AsRef(in Paths.Root) = Root;
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(Root, "claude"));
    }
}
