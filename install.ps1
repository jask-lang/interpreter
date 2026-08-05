#Requires -Version 5.1

$ErrorActionPreference = "Stop"

# detect arch
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") {
    "arm64"
} elseif ($env:PROCESSOR_ARCHITECTURE -eq "AMD64" -or $env:PROCESSOR_ARCHITEW6432 -eq "AMD64") {
    "x64"
} else {
    Write-Host "Error: Unsupported architecture ($env:PROCESSOR_ARCHITECTURE)."
    exit 1
}

$runtimeIdentifier = "win-$arch"
$installDir = Join-Path $env:LOCALAPPDATA "bin"
$distDir = "./dist"

Write-Host "Building jask interpreter executable for $runtimeIdentifier..."

# build via native AOT into a temporary build output directory
dotnet publish -c Release -r $runtimeIdentifier -o $distDir

Write-Host "Build successful. Installing executable to $installDir..."

# ensure target directory exists
if (-not (Test-Path -LiteralPath $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# copy binary to user bin directory
Copy-Item -LiteralPath (Join-Path $distDir "jask.exe") -Destination (Join-Path $installDir "jask.exe") -Force

# clean up build artifacts
Remove-Item -LiteralPath $distDir -Recurse -Force

# add to user PATH if not already present
$currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($currentPath -split ";" | Where-Object { $_ -eq $installDir }) {
    Write-Host "$installDir is already in your user PATH."
} else {
    [Environment]::SetEnvironmentVariable("Path", "$currentPath;$installDir", "User")
    Write-Host "Added $installDir to your user PATH."
}

Write-Host ""
Write-Host "Installation complete. The 'jask' executable is ready for use!"