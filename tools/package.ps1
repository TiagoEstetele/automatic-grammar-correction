param([string]$Dotnet = 'dotnet', [string]$Iscc)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project=Join-Path $root 'src/AGC/AGC.csproj'
$out=Join-Path $root 'artifacts'
$stage=Join-Path $out ('stage-'+[Guid]::NewGuid().ToString('N'))
& $Dotnet restore $project --locked-mode
if ($LASTEXITCODE) {throw 'Falha no restore do aplicativo'}
& $Dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE) {throw 'Falha na publicação'}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
New-Item -ItemType Directory -Path (Join-Path $stage 'assets') -Force | Out-Null
foreach ($asset in @('avatar.png','mark.svg','painel.png','revisao.png','bandeja.png')) { Copy-Item -LiteralPath (Join-Path $root "assets/$asset") -Destination (Join-Path $stage 'assets') }
New-Item -Force -ItemType Directory -Path (Join-Path $stage 'docs/media') | Out-Null
Copy-Item -Path (Join-Path $root 'docs/media/*') -Destination (Join-Path $stage 'docs/media')
$version=([xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
if (!$Iscc) { $Iscc = & (Join-Path $PSScriptRoot 'get-iscc.ps1') }
& $Iscc /Qp "/DPublishDir=$stage" "/DArtifactDir=$out" "/DAppVersion=$version" (Join-Path $root 'installer/agc.iss')
if ($LASTEXITCODE) { throw "Falha ao compilar o instalador ($LASTEXITCODE)" }
$setup = Join-Path $out "AGC-Setup-$version-win-x64.exe"
Get-FileHash -LiteralPath $setup -Algorithm SHA256
