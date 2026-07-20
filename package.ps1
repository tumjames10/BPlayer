param(
    [switch]$SkipBuild,
    [switch]$Sign
)

$ErrorActionPreference = "Stop"

$SolutionDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppName = "BPlayer"
$Publisher = "CN=BPlayer"
$Version = "1.0.2.0"

$PublishDir = "$SolutionDir\BPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$PackageDir = "$SolutionDir\BPlayer.Package"
$AssetsDir = "$PackageDir\Assets"
$ManifestPath = "$PackageDir\Package.appxmanifest"
$OutputDir = "$SolutionDir\Output"
$MsixPath = "$OutputDir\${AppName}_${Version}_x64.msix"
$MsixUploadPath = "$OutputDir\${AppName}_${Version}_x64.msixupload"

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

# Step 3: Create MSIX package
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

Write-Host "Packing .msix..." -ForegroundColor Cyan
& $makeAppx pack /d "$LayoutDir" /p "$MsixPath" /l
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed" }

Write-Host "Creating .msixupload (wrapper ZIP with .msix)..." -ForegroundColor Cyan
$MsixUploadTemp = "$OutputDir\${AppName}_${Version}_x64_temp.zip"

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($MsixUploadTemp, 'Create')
$null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $MsixPath, (Split-Path -Leaf $MsixPath))
$zip.Dispose()

if (Test-Path $MsixUploadPath) { Remove-Item -Path $MsixUploadPath -Force }
Rename-Item -Path $MsixUploadTemp -NewName (Split-Path -Leaf $MsixUploadPath)

# Step 4: Sign (optional) — sign the .msix, then rebuild .msixupload
if ($Sign) {
    Write-Host "Signing package..." -ForegroundColor Cyan
    $signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Microsoft SDKs\ClickOnce\SignTool\signtool.exe" |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw "signtool.exe not found" }

    & $signtool sign /a /fd SHA256 /v "$MsixPath"
    if ($LASTEXITCODE -ne 0) { throw "Signing failed" }

    & $signtool sign /a /fd SHA256 /v "$MsixUploadPath"
    if ($LASTEXITCODE -ne 0) { throw "Signing msixupload failed" }
}

# Cleanup
Remove-Item -Path $LayoutDir -Recurse -Force

Write-Host "`nPackages created:" -ForegroundColor Green
Write-Host "  $MsixPath" -ForegroundColor Green
Write-Host "  $MsixUploadPath" -ForegroundColor Green
Write-Host "`nUpload BPlayer_${Version}_x64.msixupload to the Store." -ForegroundColor Green
