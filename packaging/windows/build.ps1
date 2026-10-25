# Builds the standalone Windows x64 release of FADER into dist/windows/.
# The result is a single self-contained Fader.exe; users do not need .NET installed.
#
#   powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1             # test + build
#   powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1 -SkipTests  # build only
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $root "src\Fader.App\Fader.App.csproj"
$outDir = Join-Path $root "dist\windows"

# Prefer a per-user SDK install (dotnet-install.ps1) over the machine-wide one.
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

if (-not $SkipTests) {
    & $dotnet test (Join-Path $root "Fader.sln") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

& $dotnet publish $project -p:PublishProfile=win-x64 -p:DebugType=none -o $outDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $outDir "Fader.exe"
if (-not (Test-Path $exe)) { throw "Fader.exe was not produced." }

Copy-Item (Join-Path $root "LICENSE") (Join-Path $outDir "LICENSE.txt")

$size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Set-Content -Path (Join-Path $outDir "Fader.exe.sha256") -Value "$hash  Fader.exe" -Encoding ascii

Write-Host ""
Write-Host "Built $exe ($size MB)"
Write-Host "SHA256 $hash"
