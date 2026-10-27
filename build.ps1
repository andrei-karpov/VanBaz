param([string]$Dotnet = 'dotnet', [switch]$Smoke)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
Push-Location $PSScriptRoot
try {
    & $Dotnet restore 'src\VanBaz\VanBaz.csproj' --configfile 'NuGet.Config' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    & $Dotnet build 'src\VanBaz\VanBaz.csproj' -c Release --no-restore --nologo -o 'app'
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if ($Smoke) {
        $env:VANBAZ_APP_FOLDER = Join-Path $PSScriptRoot 'app'
        & $Dotnet restore 'tests\Smoke\Smoke.csproj' --configfile 'NuGet.Config' --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Smoke restore failed' }
        & $Dotnet build 'tests\Smoke\Smoke.csproj' -c Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Smoke build failed' }
        & $Dotnet 'tests\Smoke\bin\Release\net8.0-windows\1cv8.dll'
        if ($LASTEXITCODE -ne 0) { throw 'Smoke checks failed' }
    }
} finally { Pop-Location }
