param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$Notes = "Bug fixes and multi-monitor stability improvements.",

    [Parameter(Mandatory = $false)]
    [switch]$LocalOnly
)

$ErrorActionPreference = "Stop"
$cleanVersion = $Version.TrimStart('v', 'V')
$tag = "v$cleanVersion"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " Building & Publishing CabSpan $tag" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# 1. Update Version in CabSpan.csproj
$csprojPath = Join-Path $PSScriptRoot "CabSpan.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<Version>.*?</Version>', "<Version>$cleanVersion</Version>"
$csproj = $csproj -replace '<AssemblyVersion>.*?</AssemblyVersion>', "<AssemblyVersion>$cleanVersion.0</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>.*?</FileVersion>', "<FileVersion>$cleanVersion.0</FileVersion>"
Set-Content -Path $csprojPath -Value $csproj -Encoding UTF8

# 2. Publish single-file portable executable to dist/
$distDir = Join-Path $PSScriptRoot "dist"
dotnet publish $csprojPath -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $distDir

$exePath = Join-Path $distDir "CabSpan.exe"
Get-ChildItem -Path $distDir -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force
$readmeTxtPath = Join-Path $distDir "README.txt"
Copy-Item -Path (Join-Path $PSScriptRoot "README.md") -Destination $readmeTxtPath -Force
$zipPath = Join-Path $distDir "CabSpan-$tag.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $exePath, $readmeTxtPath -DestinationPath $zipPath

$exeHash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash
$fullNotes = "$Notes`n`n---`n**SHA256:** ``SHA256: $exeHash``"

Write-Host "`n[OK] Built $exePath and $zipPath" -ForegroundColor Green
Write-Host "     SHA256: $exeHash" -ForegroundColor DarkGray

# 3. Publish to GitHub Releases if `gh` CLI is authenticated
if ($LocalOnly) {
    Write-Host "[-LocalOnly] Skipping GitHub release upload. Artifacts ready in .\dist\" -ForegroundColor Cyan
} elseif (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "Publishing release $tag to GitHub Releases..." -ForegroundColor Yellow
    gh release create $tag $exePath $zipPath --title "CabSpan $tag" --notes $fullNotes
    Write-Host "[PUBLISHED] CabSpan $tag is live! All users will now receive the in-app update notification." -ForegroundColor Green
} else {
    Write-Host "GitHub CLI (gh) not found. Upload $exePath or $zipPath to https://github.com/itsjuztin/CabSpan/releases/new?tag=$tag" -ForegroundColor Yellow
}
