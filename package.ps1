param(
    [switch]$SkipBuild,
    [switch]$Sign
)

$ErrorActionPreference = "Stop"

$SolutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppName = "BPlayer"
$Publisher = "CN=BPlayer"
$Version = "1.0.1.0"

$PublishDir = "$SolutionDir\BPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$PackageDir = "$SolutionDir\BPlayer.Package"
$AssetsDir = "$PackageDir\Assets"
$ManifestPath = "$PackageDir\Package.appxmanifest"
$OutputDir = "$SolutionDir\Output"
$MsixPath = "$OutputDir\${AppName}_${Version}_x64.msix"

# Step 1: Build
if (-not $SkipBuild) {
    Write-Host "Building $AppName (Release, win-x64, self-contained)..." -ForegroundColor Cyan
    dotnet publish "$SolutionDir\BPlayer\BPlayer.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

# Step 2: Prepare package layout
Write-Host "Preparing package layout..." -ForegroundColor Cyan
$LayoutDir = "$OutputDir\layout"
if (Test-Path $LayoutDir) { Remove-Item -Path $LayoutDir -Recurse -Force }
New-Item -ItemType Directory -Path $LayoutDir -Force | Out-Null

# Copy everything from publish (files + subdirectories, excluding libvlc which is handled separately)
Get-ChildItem -Path $PublishDir -Exclude "libvlc" | Copy-Item -Destination $LayoutDir -Recurse -Force
# Copy native LibVLC directory (x64 only, including plugins/lua/hrtfs)
$vlcSrc = "$PublishDir\libvlc"
if (Test-Path $vlcSrc) {
    Copy-Item -Path "$vlcSrc\win-x64" -Destination "$LayoutDir\libvlc\win-x64" -Recurse -Force
}

# Copy assets
$AppxAssetsDir = "$LayoutDir\Assets"
New-Item -ItemType Directory -Path $AppxAssetsDir -Force | Out-Null
Get-ChildItem -Path $AssetsDir -File | Copy-Item -Destination $AppxAssetsDir

# Copy and update manifest
$ManifestContent = Get-Content -Path $ManifestPath -Raw
$ManifestContent = $ManifestContent -replace '\$targetnametoken\$', $AppName
$ManifestContent = $ManifestContent -replace '\$targetentrypoint\$', $AppName
$ManifestContent | Set-Content -Path "$LayoutDir\AppxManifest.xml"

# Step 3: Create MSIX
Write-Host "Creating MSIX package..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$makeAppx = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName

if (-not $makeAppx) {
    $makeAppx = Get-ChildItem -Path "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\makeappx.exe" |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
}

if (-not $makeAppx) {
    throw "makeappx.exe not found. Install Windows SDK."
}

& $makeAppx pack /d "$LayoutDir" /p "$MsixPath" /l
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed" }

# Step 4: Sign (optional)
if ($Sign) {
    Write-Host "Signing package..." -ForegroundColor Cyan
    $signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Microsoft SDKs\ClickOnce\SignTool\signtool.exe" |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw "signtool.exe not found" }

    & $signtool sign /a /fd SHA256 /v "$MsixPath"
    if ($LASTEXITCODE -ne 0) { throw "Signing failed" }
}

# Cleanup
Remove-Item -Path $LayoutDir -Recurse -Force

Write-Host "`nPackage created: $MsixPath" -ForegroundColor Green
Write-Host "`nTo upload to the Store, submit $MsixPath or the publish folder directly." -ForegroundColor Green
