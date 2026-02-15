param(
    [string]$Configuration = "Release"
)

$process = Get-Process "Macro Deck 2" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Closing Macro Deck..."
    Stop-Process -Name "Macro Deck 2" -Force
    Start-Sleep -Seconds 2
}

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\SpotifyAlbumCoverPlugin\SpotifyAlbumCoverPlugin.csproj"
$buildDir = Join-Path $root "src\SpotifyAlbumCoverPlugin\bin\$Configuration\net8.0-windows"
$deployDir = Join-Path $env:APPDATA "Macro Deck\plugins\felba.SpotifyAlbumCoverPlugin"
$packagePath = Join-Path $root "SpotifyAlbumCoverPlugin.macroDeckPlugin"
$tmp = Join-Path $root "_macrodeck_pkg_tmp"

Write-Host "Building..."
dotnet build $project -c $Configuration

Write-Host "Deploying to Macro Deck plugin folder..."
New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
Get-ChildItem -Path $buildDir -File | Copy-Item -Destination $deployDir -Force

Write-Host "Packaging .macroDeckPlugin..."
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Path $tmp | Out-Null
Copy-Item -Path (Join-Path $buildDir "SpotifyAlbumCoverPlugin.dll") -Destination $tmp -Force
Copy-Item -Path (Join-Path $buildDir "SpotifyAlbumCoverPlugin.deps.json") -Destination $tmp -Force
Copy-Item -Path (Join-Path $buildDir "ExtensionManifest.json") -Destination $tmp -Force
Copy-Item -Path (Join-Path $buildDir "ExtensionIcon.png") -Destination $tmp -Force

$zip = [System.IO.Path]::ChangeExtension($packagePath, ".zip")
if (Test-Path $zip) { Remove-Item -Force $zip }
if (Test-Path $packagePath) { Remove-Item -Force $packagePath }
Compress-Archive -Path (Join-Path $tmp "*") -DestinationPath $zip
Rename-Item -Path $zip -NewName (Split-Path -Leaf $packagePath)
Remove-Item -Recurse -Force $tmp

Write-Host "Done: $packagePath"