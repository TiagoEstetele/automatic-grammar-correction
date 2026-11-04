# Compilador usado apenas no build. O instalador produzido não acessa a rede.
$ErrorActionPreference = 'Stop'
$version = '6.7.4'
$expected = 'a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c'
$cache = Join-Path $env:LOCALAPPDATA "AGC-build/InnoSetup-$version"
$compiler = Join-Path $cache 'ISCC.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $download = Join-Path ([IO.Path]::GetTempPath()) ("agc-iscc-" + [guid]::NewGuid() + '.exe')
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-$version.exe" -OutFile $download
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) {
            throw 'O compilador recebido não corresponde ao SHA-256 da versão fixada.'
        }
        $proc = Start-Process -FilePath $download -ArgumentList "/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /DIR=`"$cache`"" -WindowStyle Hidden -Wait -PassThru
        if ($proc.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw "Falha ao preparar o compilador ($($proc.ExitCode))." }
    } finally {
        if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
    }
}
$compiler
