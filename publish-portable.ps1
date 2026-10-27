param([string]$Dotnet = 'dotnet', [string]$OutputFolder = (Join-Path $PSScriptRoot 'packages'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\VanBaz\VanBaz.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$output = [IO.Path]::GetFullPath($OutputFolder)
$stage = Join-Path $output ('portable-stage-' + [guid]::NewGuid().ToString('N'))
$zip = Join-Path $output ('VanBaz-' + $version + '-win-x64-portable.zip')
if (Test-Path -LiteralPath $zip) { throw 'Package exists; choose another output folder.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& $Dotnet publish $project -c Release -r win-x64 --self-contained true '-p:RestoreSources=https://api.nuget.org/v3/index.json' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:PathMap=$PSScriptRoot=/_/VanBaz" -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Portable build failed.' }
$notice = @'
ВанБаз

Распакуйте архив целиком в папку, где разрешена запись, и запустите VanBaz.exe.
Windows x64. Среда .NET включена в сборку. Платформа 1С устанавливается отдельно.
После запуска программа создаст папку Data для списка баз и настроек.
Для обновления закройте ВанБаз и замените файлы программы из нового ZIP. Сохраните Data.
Не запускайте программу прямо из окна архиватора.

Исходники и новые версии: https://github.com/andrei-karpov/VanBaz
'@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $notice, [Text.UTF8Encoding]::new($true))
# Runtime redistribution notices supplied by Microsoft's runtime packs.
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\VanBaz\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($download in $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies) {
    if ($download.name -notlike 'Microsoft.*.App.Runtime.win-x64') { continue }
    $runtimeVersion = $download.version.Trim('[',']').Split(',')[0]
    foreach ($cache in $assets.packageFolders.PSObject.Properties.Name) {
        $pack = Join-Path $cache ($download.name.ToLowerInvariant() + '\' + $runtimeVersion)
        if (-not (Test-Path -LiteralPath $pack)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match 'LICENSE|THIRD.PARTY|NOTICE' })) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $stage ($download.name + '-' + $file.Name))
        }
    }
}
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object { $_.FullName.Substring($stage.Length+1).Replace('\','/') }) + 'portable-files.json'
[IO.File]::WriteAllText((Join-Path $stage 'portable-files.json'), ($files | Sort-Object | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zip) + "`n"), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Archive=$zip;Megabytes=[Math]::Round((Get-Item -LiteralPath $zip).Length/1MB,1);Sha256=$hash;Folder=$stage}
