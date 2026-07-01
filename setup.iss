[Setup]
AppName=Print Tracker App
AppVersion=1.0.19
DefaultDirName={autopf}\PrintTrackerApp
DefaultGroupName=Print Tracker App
UninstallDisplayIcon={app}\PrintTrackerApp.exe
OutputBaseFilename=PrintTrackerApp_Setup_v1.0.19
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
OutputDir=.\

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Print Tracker App"; Filename: "{app}\PrintTrackerApp.exe"
Name: "{autodesktop}\Print Tracker App"; Filename: "{app}\PrintTrackerApp.exe"; Tasks: desktopicon

[Run]
Filename: "icacls"; Parameters: """C:\Windows\System32\spool\PRINTERS"" /grant *S-1-1-0:(OI)(CI)(RX) *S-1-5-32-545:(OI)(CI)(RX) /T"; Flags: runhidden; StatusMsg: "Configuring Print Spooler permissions..."
Filename: "{app}\PrintTrackerApp.exe"; Description: "{cm:LaunchProgram,Print Tracker App}"; Flags: nowait postinstall skipifsilent shellexec

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PrintTrackerApp"; ValueData: """{app}\PrintTrackerApp.exe"" -background"; Flags: uninsdeletevalue

[Code]
var
  DownloadPage: TDownloadWizardPage;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Successfully downloaded file to {local:%s}', [FileName]));
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function IsWebView2Installed: Boolean;
var
  version: String;
begin
  // Check machine level (for 64-bit Windows)
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', version) then begin
    Result := True;
    Exit;
  end;
  // Check machine level (for 32-bit Windows)
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', version) then begin
    Result := True;
    Exit;
  end;
  // Check user level
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', version) then begin
    Result := True;
    Exit;
  end;
  Result := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    if not IsWebView2Installed then
    begin
      DownloadPage.Clear;
      DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
          begin
            MsgBox('Failed to install WebView2 runtime.', mbError, MB_OK);
          end;
        except
          if DownloadPage.AbortedByUser then
            Log('Aborted by user.')
          else
            MsgBox('Failed to download WebView2 runtime.', mbError, MB_OK);
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;
