param([string]$Dotnet = 'dotnet')
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
$zip=Join-Path $out "automatic-grammar-correction-$version-win-x64.zip"
# Tar do Windows produz ZIP e preserva todos os diretórios do runtime.
Push-Location $stage
try {
    tar.exe -a -cf $zip .
    if ($LASTEXITCODE) {throw 'Falha ao criar o ZIP'}
} finally {Pop-Location}
Get-FileHash $zip -Algorithm SHA256

