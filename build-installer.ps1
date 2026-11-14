$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"
$sedPath = Join-Path $distDir "SpotifyLyricsOverlaySetup.sed"
$setupPath = Join-Path $distDir "SpotifyLyricsOverlaySetup.exe"
$stagingDir = Join-Path $env:TEMP "SLOInstaller"
$payloadDir = Join-Path $stagingDir "payload"
$stagingSedPath = Join-Path $stagingDir "setup.sed"
$stagingSetupPath = Join-Path $stagingDir "SpotifyLyricsOverlaySetup.exe"

$requiredFiles = @(
    "SpotifyLyricsOverlay.exe",
    "install.ps1",
    "uninstall.ps1"
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $publishDir $file

    if (-not (Test-Path $path)) {
        throw "Required installer file not found: $path"
    }
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path $stagingDir
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item -Force -Path (Join-Path $publishDir $file) -Destination $payloadDir
}

$payloadDirForSed = $payloadDir.TrimEnd("\") + "\"
$setupPathForSed = $stagingSetupPath

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Spotify Lyrics Overlay installed successfully.
TargetName=$setupPathForSed
FriendlyName=Spotify Lyrics Overlay Installer
AppLaunched=%InstallProgram%
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles

[Strings]
InstallProgram="powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1"
FILE0="SpotifyLyricsOverlay.exe"
FILE1="install.ps1"
FILE2="uninstall.ps1"

[SourceFiles]
SourceFiles0=$payloadDirForSed

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@

Set-Content -Encoding ASCII -Path $stagingSedPath -Value $sed
Copy-Item -Force -Path $stagingSedPath -Destination $sedPath

$nativeErrorPreference = $null

if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $nativeErrorPreference = $global:PSNativeCommandUseErrorActionPreference
    $global:PSNativeCommandUseErrorActionPreference = $false
}

try {
    & iexpress.exe /N /Q $stagingSedPath
}
finally {
    if ($null -ne $nativeErrorPreference) {
        $global:PSNativeCommandUseErrorActionPreference = $nativeErrorPreference
    }
}

if (-not (Test-Path $stagingSetupPath)) {
    throw "Installer was not created: $stagingSetupPath"
}

Copy-Item -Force -Path $stagingSetupPath -Destination $setupPath
Get-Item $setupPath
