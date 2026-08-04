using FluentAssertions;
using LSModManager.Services;
using Xunit;

namespace LSModManager.Tests;

/// <summary>
/// Verifiziert die Plattform-Asset-Wahl aus dem GitHub-Release-JSON.
/// Namensschema aus release.yml — falsche Wahl bedeutet App lädt sich das
/// falsche Paket runter und der Installer scheitert.
/// </summary>
public sealed class UpdateAssetPickerTests
{
    [Fact]
    public void PickAsset_LiefertPassendesFuerAktuellePlattform()
    {
        var release = MakeRelease();

        var picked = UpdateService.PickAssetForCurrentPlatform(release, "0.2.0");

        picked.Should().NotBeNull();
        if (OperatingSystem.IsWindows())
            picked!.Name.Should().EndWith("-win-x64.zip");
        else if (OperatingSystem.IsLinux())
            // Ohne APPIMAGE-env in Tests → tarball bevorzugt (kein AppImage).
            picked!.Name.Should().EndWith("-linux-x64.tar.gz");
    }

    [Fact]
    public void PickAsset_LeereAssetListe_LiefertNull()
    {
        var release = new UpdateService.GithubRelease { TagName = "v0.2.0" };
        UpdateService.PickAssetForCurrentPlatform(release, "0.2.0").Should().BeNull();
    }

    [Fact]
    public void PickAsset_LinuxOhneTarball_FaelltAufAppImageZurueck()
    {
        if (!OperatingSystem.IsLinux()) return;
        var release = new UpdateService.GithubRelease
        {
            TagName = "v0.2.0",
            Assets = new List<UpdateService.GithubAsset>
            {
                new() { Name = "LSModManager-0.2.0-x86_64.AppImage", DownloadUrl = "https://ex/x.AppImage" },
            },
        };
        UpdateService.PickAssetForCurrentPlatform(release, "0.2.0")
            !.Name.Should().EndWith(".AppImage");
    }

    private static UpdateService.GithubRelease MakeRelease() => new()
    {
        TagName = "v0.2.0",
        Assets = new List<UpdateService.GithubAsset>
        {
            new() { Name = "LSModManager-0.2.0-win-x64.zip", DownloadUrl = "https://ex/win.zip" },
            new() { Name = "LSModManager-0.2.0-linux-x64.tar.gz", DownloadUrl = "https://ex/lin.tgz" },
            new() { Name = "LSModManager-0.2.0-x86_64.AppImage", DownloadUrl = "https://ex/app.AppImage" },
        },
    };
}
