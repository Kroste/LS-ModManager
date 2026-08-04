using System.IO;
using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

public sealed class ModPathServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ModPathServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lsmm-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

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
        {
            // Steam-Präfix-Kandidaten entstehen nur, wenn Steam auf dem Runner
            // installiert ist. CI-Runner haben kein Steam — dann testen wir nur
            // die generellen Home-Kandidaten. Auf Dev-Systemen mit Steam prüfen
            // wir dass BEIDE Documents-Ordnernamen (XP-Style + Standard) rauskommen.
            var steamCandidates = candidates.Where(c => c.Contains("compatdata")).ToList();
            if (steamCandidates.Any())
            {
                steamCandidates.Should().Contain(c => c.Contains(Path.Combine("steamuser", "My Documents")));
                steamCandidates.Should().Contain(c => c.Contains(Path.Combine("steamuser", "Documents")));
            }
            candidates.Should().Contain(c => c.Contains(".local/share/FarmingSimulator2025"));
        }
        if (OperatingSystem.IsWindows())
            candidates.Should().Contain(c => c.Contains("My Games"));
    }

    [Fact]
    public void ParseLibraryFolders_ExtrahiertAllePathEintraege()
    {
        var steamRoot = Path.Combine(_tempDir, "SteamRoot");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        Directory.CreateDirectory(steamApps);
        File.WriteAllText(Path.Combine(steamApps, "libraryfolders.vdf"),
            """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"/var/home/user/.local/share/Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"/run/media/system/Games/SteamLibrary"
                    "label"		""
                }
            }
            """);

        var results = ModPathService.ParseLibraryFolders(steamRoot).ToList();

        results.Should().Contain("/var/home/user/.local/share/Steam");
        results.Should().Contain("/run/media/system/Games/SteamLibrary");
        // Bazzite/Fedora-Atomic-Regel: /var/home/... → auch /home/... anbieten
        results.Should().Contain("/home/user/.local/share/Steam");
    }

    [Fact]
    public void ParseLibraryFolders_LiefertNichts_WennKeinVdfExistiert()
    {
        var steamRoot = Path.Combine(_tempDir, "leer");
        Directory.CreateDirectory(steamRoot);
        ModPathService.ParseLibraryFolders(steamRoot).Should().BeEmpty();
    }

    [Fact]
    public void ParseLibraryFolders_ToleriertZerbrocheneVdf()
    {
        var steamRoot = Path.Combine(_tempDir, "kaputt");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            "kein VDF, nur Text");

        ModPathService.ParseLibraryFolders(steamRoot).Should().BeEmpty();
    }
}
