param([string]$Dotnet = 'dotnet', [string]$OutputFolder = (Join-Path $PSScriptRoot 'packages'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\VanBaz\VanBaz.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$output = [IO.Path]::GetFullPath($OutputFolder)
$stage = Join-Path $output ('portable-stage-' + [guid]::NewGuid().ToString('N'))
$zip = Join-Path $output ('VanBaz-' + $version + '-win-x64-portable.zip')
if (Test-Path -LiteralPath $zip) { throw 'Package exists; choose another output folder.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& $Dotnet publish $project -c Release -r win-x64 --self-contained false '-p:RestoreSources=https://api.nuget.org/v3/index.json' '-p:DebugType=None' '-p:DebugSymbols=false' "-p:PathMap=$PSScriptRoot=/_/VanBaz" -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Portable build failed.' }
$notice = @'
ВанБаз

Для запуска нужен .NET Desktop Runtime 8 для Windows x64:
https://dotnet.microsoft.com/ru-ru/download/dotnet/8.0
На странице Microsoft выберите «Среда выполнения рабочего стола .NET»,
затем установщик Windows x64. Если эта среда уже установлена, повторная установка не нужна.

После установки .NET распакуйте архив в папку, где разрешена запись,
и запустите VanBaz.exe. ВанБаз работает без установки; .NET в архив не входит.
Платформа 1С устанавливается отдельно.
После запуска программа создаст папку Data для списка баз и настроек.
Для обновления закройте ВанБаз и замените файлы программы из нового ZIP. Сохраните Data.
Не запускайте программу прямо из окна архиватора.

Исходники и новые версии: https://github.com/andrei-karpov/VanBaz
'@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $notice, [Text.UTF8Encoding]::new($true))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zip) + "`n"), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Archive=$zip;Megabytes=[Math]::Round((Get-Item -LiteralPath $zip).Length/1MB,1);Sha256=$hash;Folder=$stage}
