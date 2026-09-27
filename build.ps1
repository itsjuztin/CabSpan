# Quick dev build — publishes straight to dist\ so you can run the
# updated exe immediately without manually copying files.
#
# Usage:
#   .\build.ps1          (release build → dist\)
#   .\build.ps1 -Debug   (debug build   → dist\)

param(
    [switch]$Debug
)

$ErrorActionPreference = "Stop"
$config    = if ($Debug) { "Debug" } else { "Release" }
$distDir   = Join-Path $PSScriptRoot "dist"
$csproj    = Join-Path $PSScriptRoot "CabSpan.csproj"

Write-Host ""
Write-Host "  Building CabSpan [$config] → dist\" -ForegroundColor Cyan
Write-Host ""

dotnet publish $csproj -c $config -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -o $distDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n  [FAILED] Build exited with code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe  = Get-Item (Join-Path $distDir "CabSpan.exe")
$size = [math]::Round($exe.Length / 1KB)

Write-Host ""
Write-Host "  [OK] dist\CabSpan.exe  ($size KB)  $($exe.LastWriteTime)" -ForegroundColor Green
Write-Host ""
