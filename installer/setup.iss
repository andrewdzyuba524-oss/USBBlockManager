; ================================================================
; USB Block Manager — Inno Setup script
; ================================================================

#define MyAppName "USB Block Manager"
#define MyAppVersion "1.0"
#define MyAppPublisher "Fsin Yanao Administrator KING"
#define MyAppExeName "USBBlockManager.exe"
#define MyServiceName "USBBlockService"
#define MyServiceExeName "USBBlockService.exe"
#define MyTaskName "USBBlockManager GUI"
#define MyDataDir "C:\ProgramData\USBBlockManager"

[Setup]
AppId={{7B3F1C2A-9D4E-4B6F-8A1C-5F2E3D4A6B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\USBBlockManager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=USBBlockManagerSetup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=6.1sp1
SetupIconFile=compiler:SetupClassicIcon.ico
LicenseFile=License.txt

; Встроенный механизм закрытия приложений отключаем —
; мы сами всё убиваем в PrepareToInstall до копирования файлов.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные значки:"

[Files]
; --- Программа ---
Source: "..\src\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace
Source: "..\service\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

; --- HardwareIdTool (утилита для клиента) ---
Source: "..\tools\HardwareIdTool\bin\Release\net8.0-windows\win-x64\publish\HardwareIdTool.exe"; DestDir: "{app}"; Flags: ignoreversion restartreplace

; --- Лицензия (вкладывается издателем под конкретного клиента) ---
; Если файл license.json есть в папке installer — он упакуется в установщик
; и распакуется в C:\ProgramData\USBBlockManager\licensing\license.json.
Source: "license.json"; DestDir: "{#MyDataDir}\licensing"; Flags: ignoreversion restartreplace skipifsourcedoesntexist

; --- XML-задача для планировщика ---
Source: "USBBlockManager-GUI-Autostart.xml"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Активация (Hardware ID)"; Filename: "{app}\HardwareIdTool.exe"; Comment: "Показать Hardware ID для получения лицензии"
Name: "{group}\Удаление {#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; --- Регистрация службы Windows ---
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\{#MyServiceExeName}"" DisplayName= ""{#MyAppName} Service"" start= auto"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description {#MyServiceName} ""Служба блокировки USB-накопителей с белым списком"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden waituntilterminated

; --- Задача планировщика для автозапуска GUI в трее (через XML) ---
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""{#MyTaskName}"" /XML ""{tmp}\USBBlockManager-GUI-Autostart.xml"" /F"; Flags: runhidden waituntilterminated

; --- Запуск GUI после установки ---
Filename: "{sys}\cmd.exe"; Parameters: "/c start """" ""{app}\{#MyAppExeName}"""; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent runhidden

[UninstallRun]
; --- Порядок важен: GUI → служба → задача → служба delete ---
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "KillGui"
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM {#MyServiceExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "KillService"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#MyTaskName}"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteTask"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"

[UninstallDelete]
Type: filesandordirs; Name: "{#MyDataDir}"
Type: filesandordirs; Name: "{app}"

[Code]
// ============================================================
// Секретный токен. Должен совпадать с токеном в GUI
// (src\MainWindow.xaml.cs, константа UninstallToken).
// Если пользователь запустит unins000.exe без этого токена
// (например, из Параметров Windows) — удаление будет запрещено.
// ============================================================
const
  UninstallToken = '{7E2A-91F4-BD03-USB-BLOCK-2026}';

// Проверяет, передан ли в командной строке установщика
// параметр /PASSWORD=<UninstallToken>.
function HasValidToken(): Boolean;
var
  I: Integer;
  S: String;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    S := ParamStr(I);
    if CompareText(S, '/PASSWORD=' + UninstallToken) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

// Inno вызывает эту функцию при попытке удаления.
// False — удаление не запустится.
function InitializeUninstall(): Boolean;
begin
  if HasValidToken() then
  begin
    Result := True;
    Exit;
  end;

  MsgBox(
    'Удаление USB Block Manager защищено паролем администратора.' + #13#10 + #13#10 +
    'Чтобы удалить программу, откройте её и выберите:' + #13#10 +
    '   Файл → Удалить программу...' + #13#10 + #13#10 +
    'Потребуется ввести пароль администратора.',
    mbInformation, MB_OK);

  Result := False;
end;

// Жёстко останавливаем службу и убиваем всё, что держит файлы в {app}.
// Вызывается ДО копирования файлов — чтобы Windows освободила DLL runtime
// (coreclr.dll, clrjit.dll, WindowsBase.dll и т.д.) и Inno смог их перезаписать.
procedure KillEverything();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM USBBlockManager.exe', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec(ExpandConstant('{sys}\sc.exe'), 'stop USBBlockService', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM USBBlockService.exe', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Sleep(4000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  KillEverything();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    KillEverything();
  end;
end;