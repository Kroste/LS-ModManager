# Kroste-Release (Windows/PowerShell): prueft den Git-Zustand, erstellt einen
# annotierten Tag vX.Y.Z und pusht ihn (loest die Release-Action aus).
# ASCII-only (Windows-PowerShell-5.1-ANSI-Falle).
$ErrorActionPreference = 'Stop'

function Fail($msg) { Write-Host "FEHLER: $msg" -ForegroundColor Red; exit 1 }

# Version aus <Version> in Directory.Build.props oder csproj lesen (NetScanner-Stil).
$versionMatch = Select-String -Path 'Directory.Build.props','*/*.csproj' `
    -Pattern '(?<=<Version>)[^<]+' -AllMatches -ErrorAction SilentlyContinue |
    Select-Object -First 1
$version = if ($versionMatch) { $versionMatch.Matches[0].Value } else { '' }

if (-not $version) {
    $last = (git describe --tags --abbrev=0 --match 'v*' 2>$null)
    if (-not $last) { $last = 'v0.0.0' }
    $lastNum = $last.TrimStart('v')
    $parts = $lastNum -split '\.'
    $suggest = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    $version = Read-Host "Neue Version [$suggest]"
    if (-not $version) { $version = $suggest }
}

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    Fail "'$version' ist keine gueltige SemVer-Version (X.Y.Z)."
}
$tag = "v$version"

if (git status --porcelain) { Fail "Uncommittete Aenderungen — erst committen." }
if (git log --branches --not --remotes --oneline) { Fail "Ungepushte Commits — erst pushen." }

$tagExists = git rev-parse $tag 2>$null
if ($tagExists) {
    $answer = Read-Host "Tag $tag existiert bereits. Loeschen und neu setzen? [j/N]"
    if ($answer -ne 'j' -and $answer -ne 'J') { Write-Host 'Abgebrochen.'; exit 0 }
    git tag -d $tag
    git push origin ":refs/tags/$tag" 2>$null
}

git tag -a $tag -m "Release $tag"
git push origin $tag
Write-Host "Tag $tag gepusht - die Release-Action baut jetzt die Pakete." -ForegroundColor Green
