$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$releaseRoot = Join-Path $projectRoot 'dist\EdgeOpacity-win-x64'
dotnet publish (Join-Path $projectRoot 'EdgeOpacity.csproj') -c Release -p:Platform=x64 -o $releaseRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
foreach ($requiredFile in @('EdgeOpacity.exe', 'EdgeOpacity.pri', 'Microsoft.UI.Xaml.dll', 'coreclr.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $releaseRoot $requiredFile))) { throw "Missing release file: $requiredFile" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.txt') -Destination $releaseRoot -Force
Write-Host "Ready: $releaseRoot\EdgeOpacity.exe"
