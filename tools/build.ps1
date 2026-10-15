param([switch]$PrepareTools)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$buildEnvironment = @{
    DOTNET_CLI_HOME = (Join-Path $projectRoot '.dotnet')
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    NUGET_PACKAGES = (Join-Path $projectRoot 'artifacts/packages')
    APPDATA = (Join-Path $projectRoot 'artifacts/build-appdata')
}
$previousEnvironment = @{}
try {
    foreach ($key in $buildEnvironment.Keys) {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $buildEnvironment[$key], 'Process')
    }
    if ($PrepareTools) { node (Join-Path $PSScriptRoot 'prepare-tools.mjs'); if ($LASTEXITCODE -ne 0) { throw 'Motorlar hazırlanamadı.' } }
    dotnet restore src/VideoStudio.csproj --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Geri yükleme başarısız.' }
    dotnet publish src/VideoStudio.csproj -c Release --no-restore --self-contained false -o release/VideoStudio -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
    Write-Host 'Hazır: release/VideoStudio/VideoStudio.exe'
} finally {
    foreach ($key in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process') }
}
