using Vela.Services;
using Xunit;

namespace Vela.Tests;

public sealed class LauncherTests
{
    [Theory]
    [InlineData("Settings", "settings", 120)]
    [InlineData("Settings", "set", 100)]
    [InlineData("WSL Settings", "set", 80)]
    [InlineData("Visual Studio Code", "vsc", 70)]
    [InlineData("Notepad", "pad", 50)]
    [InlineData("Notepad", "xyz", 0)]
    [InlineData("Configurações", "configuracoes", 120)]
    public void ScoresByWhereTheTextMatches(string name, string query, int expected) =>
        Assert.Equal(expected, Launcher.Score(name, Launcher.Fold(query)));

    [Theory]
    [InlineData("12*(3+4)", "84")]
    [InlineData("2^10", "1024")]
    [InlineData("50% * 80", "40")]
    [InlineData("7 / 2", "3.5")]
    [InlineData("10 x 3 =", "30")]
    [InlineData("1.5+1.25", "2.75")]
    public void Calculates(string text, string expected)
    {
        Assert.True(Launcher.Calculate(text, out var r));
        Assert.Equal(expected, r);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("42")]
    [InlineData("-5")]
    [InlineData("1/0")]
    [InlineData("(1+2")]
    public void IgnoresWhatIsNotASum(string text) => Assert.False(Launcher.Calculate(text, out _));

    [Fact]
    public void ShortensPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(@"~\Documents", Launcher.ShortPath(home + @"\Documents"));
        var deep = Launcher.ShortPath(@"D:\Projects\Clients\Acme\Website\src\components\header");
        Assert.StartsWith(@"D:\Projects\…\", deep);
        Assert.EndsWith(@"components\header", deep);
        Assert.True(deep.Length <= 44);
    }
}
