; Manter o AppId entre versões permite atualizar a instalação existente.
#ifndef PublishDir
  #error PublishDir ausente
#endif
#ifndef AppVersion
  #error AppVersion ausente
#endif
#define AppExe "AutomaticGrammarCorrection.exe"
[Setup]
AppId={{DA6CEAC5-4864-4C13-ACFB-89CDBD224E7F}
AppName=automatic grammar correction
AppVersion={#AppVersion}
AppPublisher=TiagoEstetele
AppPublisherURL=https://github.com/TiagoEstetele/automatic-grammar-correction
AppSupportURL=https://github.com/TiagoEstetele/automatic-grammar-correction/issues
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=TiagoEstetele
VersionInfoDescription=Instalador do automatic grammar correction
DefaultDirName={localappdata}\Programs\Automatic Grammar Correction
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableWelcomePage=no
UninstallDisplayIcon={app}\{#AppExe}
AppMutex=Local\AutomaticGrammarCorrection.Tray
CloseApplications=yes
RestartApplications=no
WizardStyle=modern dynamic
SetupIconFile=..\assets\app.ico
WizardImageFile=wizard.png
Compression=lzma2/fast
SolidCompression=yes
OutputDir={#ArtifactDir}
OutputBaseFilename=AGC-Setup-{#AppVersion}-win-x64

[Languages]
Name: "ptBR"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
WelcomeLabel2=Este assistente instalará o [name/ver] para o seu usuário do Windows.%n%nO dicionário de português e o runtime estão incluídos. Após a instalação, o corretor funciona offline.
SetupAppRunningError=O %1 está em execução. No ícone da bandeja, escolha “Sair do AGC” e clique em OK para continuar.
UninstallAppRunningError=No ícone da bandeja, escolha “Sair do AGC” e clique em OK para desinstalar o %1.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\automatic grammar correction"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\automatic grammar correction"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o painel do AGC"; Flags: postinstall nowait skipifsilent
