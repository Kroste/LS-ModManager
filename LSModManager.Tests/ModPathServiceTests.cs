using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

public sealed class ModPathServiceTests
{
    [Fact]
    public void EnumerateCandidates_LiefertMindestensEinenKandidat()
    {
        var svc = new ModPathService(new AppSettingsService());
        var candidates = svc.EnumerateCandidates().ToList();
        candidates.Should().NotBeEmpty();
    }

    [Fact]
    public void EnumerateCandidates_EnthaeltPlattformspezifischeSegmente()
    {
        var svc = new ModPathService(new AppSettingsService());
        var candidates = svc.EnumerateCandidates().ToList();

        candidates.Should().OnlyContain(c => c.Contains("FarmingSimulator2025"));
        candidates.Should().OnlyContain(c => c.EndsWith("mods"));

        if (OperatingSystem.IsLinux())
            candidates.Should().Contain(c => c.Contains("compatdata")
                || c.Contains(".local/share/FarmingSimulator2025"));
        if (OperatingSystem.IsWindows())
            candidates.Should().Contain(c => c.Contains("My Games"));
    }
}
