#include "Build.generated.iss"

[Setup]
AppId={{32B33618-826D-4351-8D68-BE757A47A147}
AppName=ClickShow
AppVersion={#AppVersion}
AppPublisher=March7thGo
AppPublisherURL=https://github.com/March7thGo/ClickShow
DefaultDirName={autopf}\ClickShow
DefaultGroupName=ClickShow
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir=..\artifacts\installers
#if Edition == "Full"
OutputBaseFilename=ClickShow-{#AppVersion}-Full
#else
OutputBaseFilename=ClickShow-{#AppVersion}
#endif
SetupIconFile=..\src\Assets\ClickShow.ico
UninstallDisplayIcon={app}\ClickShow.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Check-Dependencies.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\ClickShow"; Filename: "{app}\ClickShow.exe"

[Run]
Filename: "{app}\ClickShow.exe"; Description: "立即运行 ClickShow"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
var
  DesktopCheck: TNewCheckBox;
  DeleteSettings: Boolean;
  OldDirectory: String;
  OldFiles: TArrayOfString;

procedure InitializeWizard;
begin
  DesktopCheck := TNewCheckBox.Create(WizardForm);
  DesktopCheck.Parent := WizardForm.FinishedPage;
  DesktopCheck.Caption := '创建桌面图标';
  DesktopCheck.Checked := False;
  DesktopCheck.Left := WizardForm.RunList.Left;
  DesktopCheck.Top := WizardForm.RunList.Top;
  DesktopCheck.Width := ScaleX(260);
  WizardForm.RunList.Top := WizardForm.RunList.Top + ScaleY(28);
  WizardForm.RunList.Height := WizardForm.RunList.Height - ScaleY(28);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Script, Args: String;
begin
  Result := '';
#if Edition == "Slim"
  ExtractTemporaryFile('Check-Dependencies.ps1');
  Script := ExpandConstant('{tmp}\Check-Dependencies.ps1');
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script + '" -DotNetVersion "{#DotNetVersion}" -AppSdkVersion "{#AppSdkVersion}"';
  if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := 30;
  if Code <> 0 then begin
    Result := '精简版需要 .NET {#DotNetVersion} x64 及 Windows App Runtime 1.8 x64（最低 {#AppSdkVersion}）。请安装依赖后重试，或使用完整版。';
    if MsgBox(Result + #13#10 + '是否打开微软官方下载页面？', mbConfirmation, MB_YESNO) = IDYES then begin
      ShellExecAsOriginalUser('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, Code);
      ShellExecAsOriginalUser('open', 'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads', '', '', SW_SHOWNORMAL, ewNoWait, Code);
    end;
    Exit;
  end;
#endif
  // 正常退出旧实例，再交给 Restart Manager 检查文件占用。
  OldDirectory := GetPreviousData('InstallDirectory', ExpandConstant('{app}'));
  LoadStringsFromFile(OldDirectory + '\installed-files.txt', OldFiles);
  if FileExists(OldDirectory + '\ClickShow.exe') then
    if not ExecAsOriginalUser(OldDirectory + '\ClickShow.exe', '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := '无法结束旧版 ClickShow，请从托盘退出后重试。';
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'InstallDirectory', ExpandConstant('{app}'));
end;

function InManifest(Name: String; Files: TArrayOfString): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 0 to GetArrayLength(Files) - 1 do
    if CompareText(Name, Files[I]) = 0 then begin Result := True; Exit; end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Files: TArrayOfString; I, Code: Integer; Relative: String; SameDirectory: Boolean;
begin
  if CurStep = ssPostInstall then begin
    // 自启路径更新成功后再清理旧目录；授权失败时保留原有效入口及程序。
    if not ExecAsOriginalUser(ExpandConstant('{app}\ClickShow.exe'), '--repair-startup', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      MsgBox('自启路径更新失败，旧目录已保留。请打开设置重新配置开机自启。', mbError, MB_OK);
      Exit;
    end;
    LoadStringsFromFile(ExpandConstant('{app}\installed-files.txt'), Files);
    SameDirectory := CompareText(OldDirectory, ExpandConstant('{app}')) = 0;
    // 只移除上一版明确登记的文件，不递归删除用户选择的安装目录。
    for I := 0 to GetArrayLength(OldFiles) - 1 do begin
      Relative := OldFiles[I];
      if (not SameDirectory or not InManifest(Relative, Files)) and (Relative <> '') and (Pos('..', Relative) = 0) and (Pos(':', Relative) = 0) and (Relative[1] <> '\') and (Relative[1] <> '/') then
        DeleteFile(OldDirectory + '\' + Relative);
    end;
    if not SameDirectory and (GetArrayLength(OldFiles) > 0) then begin
      DeleteFile(OldDirectory + '\installed-files.txt');
      DeleteFile(OldDirectory + '\unins000.exe');
      DeleteFile(OldDirectory + '\unins000.dat');
      RemoveDir(OldDirectory + '\Assets');
      RemoveDir(OldDirectory);
    end;
  end;
  if (CurStep = ssDone) and (DesktopCheck.Checked or FileExists(ExpandConstant('{autodesktop}\ClickShow.lnk'))) then
    CreateShellLink(ExpandConstant('{autodesktop}\ClickShow.lnk'), 'ClickShow', ExpandConstant('{app}\ClickShow.exe'), '', ExpandConstant('{app}'), ExpandConstant('{app}\ClickShow.exe'), 0, SW_SHOWNORMAL);
end;

function InitializeUninstall: Boolean;
var Code: Integer; Args: String;
begin
  DeleteSettings := False;
  if not UninstallSilent then
    DeleteSettings := MsgBox('是否同时删除当前用户的 ClickShow 设置？默认保留。', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  Result := Exec(ExpandConstant('{app}\ClickShow.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then MsgBox('无法结束 ClickShow，请先从托盘退出。', mbError, MB_OK);
  if Result then begin
    Args := '--cleanup';
    if DeleteSettings then Args := Args + ' --delete-settings';
    Result := Exec(ExpandConstant('{app}\ClickShow.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
    if not Result then MsgBox('无法清理当前用户的自启配置，卸载已停止。', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    DeleteFile(ExpandConstant('{autodesktop}\ClickShow.lnk'));
  end;
end;
