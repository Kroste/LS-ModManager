using FluentAssertions;
using LSModManager.ViewModels;
using Xunit;

namespace LSModManager.Tests;

/// <summary>Semver-Vergleich für die Update-Prüfung (FS/LS-Mods nutzen 4-teilige Versionen wie „8.1.0.3").</summary>
public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("8.1.0.3", "8.1.0.2", true)]
    [InlineData("8.2.0.0", "8.1.0.3", true)]
    [InlineData("2.0.0.0", "1.99.99.99", true)]
    [InlineData("8.1.0.3", "8.1.0.3", false)] // gleich
    [InlineData("8.1.0.2", "8.1.0.3", false)] // niedriger
    [InlineData("1.0", "1.0.0.0", false)] // gleich, andere Länge
    public void IsVersionNewer_VergleichtSemver(string catalog, string installed, bool expected)
    {
        MainWindowViewModel.IsVersionNewer(catalog, installed).Should().Be(expected);
    }

    [Theory]
    [InlineData("kein-versionstring", "1.0")]
    [InlineData("1.0", "kein-versionstring")]
    [InlineData("", "1.0")]
    public void IsVersionNewer_UngueltigeVersion_LiefertFalse(string catalog, string installed)
    {
        MainWindowViewModel.IsVersionNewer(catalog, installed).Should().BeFalse();
    }
}
