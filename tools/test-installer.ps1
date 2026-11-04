# Teste destrutivo de instalação/remoção apenas no perfil descartável do CI.
param([Parameter(Mandatory)][string]$Setup)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Use um runner hospedado do GitHub.' }
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$dest = Join-Path $env:LOCALAPPDATA 'Programs/Automatic Grammar Correction'
$prefs = Join-Path $env:LOCALAPPDATA 'AutomaticGrammarCorrection/settings.json'
$entry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{DA6CEAC5-4864-4C13-ACFB-89CDBD224E7F}_is1'
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'automatic grammar correction.lnk'
if ((Test-Path $dest) -or (Test-Path $entry) -or (Test-Path $link) -or (Test-Path $prefs)) { throw 'Perfil de teste não está limpo.' }
function RunWizard([string]$Path) {
    $p = Start-Process -FilePath $Path -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "$Path retornou $($p.ExitCode)" }
}
RunWizard $Setup
$app = Join-Path $dest 'AutomaticGrammarCorrection.exe'
foreach ($file in @($app, $link, (Join-Path $dest 'pt_BR/pt_BR.dic'), (Join-Path $dest 'coreclr.dll'))) {
    if (!(Test-Path -LiteralPath $file)) { throw "Arquivo ausente: $file" }
}
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($link)
if ($shortcut.TargetPath -ne $app) { throw 'O atalho aponta para outro executável.' }
$info = Get-ItemProperty -LiteralPath $entry
if ($info.DisplayVersion -ne (Get-Item $app).VersionInfo.ProductVersion.Split('+')[0]) { throw 'Versões divergentes no registro.' }
$uninstall = $info.UninstallString.Trim('"')
if ((Split-Path $uninstall -Parent) -ne $dest -or !(Test-Path -LiteralPath $uninstall)) { throw 'Desinstalador inválido.' }
New-Item -ItemType Directory -Force (Split-Path $prefs) | Out-Null
[IO.File]::WriteAllText($prefs, '{"AutoEnabled":false,"Apps":{"editor-teste.exe":false}}')
$before = (Get-FileHash $prefs).Hash
RunWizard $Setup
if ((Get-FileHash $prefs).Hash -ne $before) { throw 'A reinstalação alterou as preferências.' }
RunWizard $uninstall
if ((Test-Path $app) -or (Test-Path $link) -or (Test-Path $entry)) { throw 'A remoção deixou o aplicativo, o atalho ou o registro de desinstalação.' }
if ((Get-FileHash $prefs).Hash -ne $before) { throw 'A desinstalação alterou as preferências.' }
'Instalação, reinstalação, atalho e desinstalação verificados.'
