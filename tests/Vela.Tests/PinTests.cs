using Vela.Core;
using Vela.Shell;
using Xunit;

namespace Vela.Tests;

public sealed class PinTests
{
    [Fact]
    public void StorePinsMoveToAumidAndDuplicatesMerge()
    {
        var pins = new List<PinnedApp>
        {
            new() { Name = "Notepad", LaunchPath = @"C:\Windows\notepad.exe", TargetPath = @"C:\Windows\notepad.exe" },
            new() { Name = "Old", LaunchPath = @"C:\Program Files\WindowsApps\Demo_1.0.0.0_x64__abc\app\demo.exe", Aumid = "Demo_abc!App" },
            new() { Name = "New", LaunchPath = @"C:\Program Files\WindowsApps\Demo_2.0.0.0_x64__abc\app\demo.exe", Aumid = "Demo_abc!App" },
        };

        Assert.True(PackagedApps.FixPins(pins));

        Assert.Equal(2, pins.Count);
        Assert.Equal(@"C:\Windows\notepad.exe", pins[0].LaunchPath);           // desktop apps untouched
        Assert.Equal("Old", pins[1].Name);                                       // first position kept
        Assert.Equal(@"shell:AppsFolder\Demo_abc!App", pins[1].LaunchPath);
        Assert.Null(pins[1].TargetPath);
        Assert.False(PackagedApps.FixPins(pins));                                // stable afterwards
    }

    [Fact]
    public void OnlyWindowsAppsPathsArePackaged()
    {
        Assert.True(PackagedApps.IsPackagedPath(@"C:\Program Files\WindowsApps\Claude_6.1.0.0_x64__p\app\claude.exe"));
        Assert.False(PackagedApps.IsPackagedPath(@"C:\Program Files\Brave\brave.exe"));
        Assert.False(PackagedApps.IsPackagedPath(null));
    }
}
