param(
    [string]$OutputFolder = (Join-Path $PSScriptRoot 'packages'),
    [string]$AppFolder = (Join-Path $PSScriptRoot 'app')
)
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path -LiteralPath $AppFolder).Path
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $app 'VanBaz.dll')).Version.ToString(3)
$files = @('VanBaz.exe', 'VanBaz.dll', 'VanBaz.deps.json', 'VanBaz.runtimeconfig.json')
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
$stage = Join-Path $OutputFolder ('package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$hashes = @{}
try {
    foreach ($file in $files) {
        $source = Join-Path $app $file
        $hashes[$file] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage $file)
    }
    $manifest = @{ Version = $version; Files = $hashes } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $stage 'vanbaz-package.json'), $manifest, [Text.UTF8Encoding]::new($false))
    $zip = Join-Path $OutputFolder ('VanBaz-' + $version + '.zip')
    if (Test-Path -LiteralPath $zip) { throw 'Package exists; choose a new OutputFolder.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
    $channel = @{ Version = $version; PackageUrl = [IO.Path]::GetFileName($zip); Size = (Get-Item -LiteralPath $zip).Length; Sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash; Notes = "VanBaz $version" } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $OutputFolder 'vanbaz-update.json'), $channel, [Text.UTF8Encoding]::new($false))
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Select-Object Path, Hash
} finally {
    foreach ($file in ($files + 'vanbaz-package.json')) { $path = Join-Path $stage $file; if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
    if (-not (Get-ChildItem -LiteralPath $stage -Force)) { Remove-Item -LiteralPath $stage }
}
