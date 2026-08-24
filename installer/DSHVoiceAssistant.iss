; DSH 语音助手 安装脚本（Inno Setup 6）
; 编译：ISCC.exe DSHVoiceAssistant.iss

[Setup]
AppId={{8C3F1A2B-9D4E-4F5A-8B6C-1D2E3F4A5B6C}
AppName=DSH 语音助手
AppVersion=1.0.0
AppPublisher=Winston-Rowan & DSH
AppPublisherURL=https://github.com/Winston-Rowan/DSHVoiceAssistant
DefaultDirName={autopf}\DSH 语音助手
DefaultGroupName=DSH 语音助手
UninstallDisplayIcon={app}\DSHVoiceAssistant.exe
SetupIconFile=..\src\DSHVoiceAssistant\Assets\DSHWhale.ico
OutputDir=..\release
OutputBaseFilename=DSHVoiceAssistant-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
; 使用内置英文向导（自定义弹窗均为中文，不依赖第三方语言文件）
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; 安装包内容（release-stage 已剔除用户配置/日志，首次运行自动生成默认配置）
Source: "..\release-stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 小白使用说明（安装完成后可勾选查看）
Source: "使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\DSH 语音助手"; Filename: "{app}\DSHVoiceAssistant.exe"
Name: "{autoprograms}\DSH 语音助手"; Filename: "{app}\DSHVoiceAssistant.exe"

[Run]
Filename: "{app}\DSHVoiceAssistant.exe"; Description: "立即启动 DSH 语音助手"; Flags: nowait postinstall skipifsilent
Filename: "{app}\使用说明.txt"; Description: "查看使用说明（2 分钟上手）"; Flags: nowait postinstall skipifsilent unchecked

[Code]
{ ---------- DSH 前置检测：未安装则询问是否安装，否则退出本安装 ---------- }

const
  DSH_DOWNLOAD_URL = 'https://github.com/Winston-Rowan/DSH-Desktop/releases';

function IsDSHInstalled(): Boolean;
var
  Roots: array[0..2] of Integer;
  I: Integer;
begin
  // 常见安装位置（文件级检测）
  Result := FileExists('D:\DSH\DSH Desktop\DSH Desktop.exe')
    or FileExists(ExpandConstant('{localappdata}\Programs\DSH Desktop\DSH Desktop.exe'))
    or FileExists(ExpandConstant('{autopf}\DSH Desktop\DSH Desktop.exe'))
    or FileExists(ExpandConstant('{localappdata}\DSH Desktop\DSH Desktop.exe'));
  if Result then Exit;

  // 注册表卸载项检测（HKCU / HKLM 32 / HKLM 64）
  Roots[0] := HKCU;
  Roots[1] := HKLM32;
  Roots[2] := HKLM64;
  for I := 0 to 2 do
  begin
    if RegKeyExists(Roots[I], 'Software\Microsoft\Windows\CurrentVersion\Uninstall\DSH Desktop') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ 在本机 DSH 更新缓存中查找官方安装包（updates\<版本>\DSH-Desktop-*-windows.exe）
  Inno 6.7+ 的 FindFirst 为双参数签名（FileName, var FindRec） }
function FindCachedDSHInstaller(): String;
var
  Base, SubDir, Pattern: String;
  DirRec, FileRec: TFindRec;
begin
  Result := '';
  Base := ExpandConstant('{userappdata}\DSH Desktop\updates');
  if not DirExists(Base) then Exit;

  if FindFirst(Base + '\*', DirRec) then
  begin
    repeat
      if (DirRec.Attributes and $10) <> 0 then // FILE_ATTRIBUTE_DIRECTORY
      begin
        SubDir := Base + '\' + DirRec.Name;
        Pattern := SubDir + '\DSH-Desktop-*-windows.exe';
        if FindFirst(Pattern, FileRec) then
        begin
          Result := SubDir + '\' + FileRec.Name;
          FindClose(FileRec);
          Break;
        end;
      end;
    until not FindNext(DirRec);
    FindClose(DirRec);
  end;
end;

function InitializeSetup(): Boolean;
var
  Choice: Integer;
  Installer: String;
  ErrCode: Integer;
begin
  Result := True;
  if IsDSHInstalled() then Exit;

  Choice := MsgBox('未检测到 DSH（DeepSeek Harness 桌面版）运行环境。' + #13#10 +
    'DSH 语音助手依赖 DSH 提供指令执行能力，需要先安装 DSH。' + #13#10 + #13#10 +
    '是否立即安装 DSH？' + #13#10 +
    '（选择"是"将运行本机缓存的 DSH 官方安装包，若不存在则打开下载页面；' + #13#10 +
    '选择"否"将退出本安装程序）',
    mbConfirmation, MB_YESNO);
  if Choice <> IDYES then
  begin
    Result := False; // 用户拒绝安装 DSH → 退出本安装
    Exit;
  end;

  Installer := FindCachedDSHInstaller();
  if Installer <> '' then
    ShellExec('open', Installer, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode)
  else
    ShellExec('open', DSH_DOWNLOAD_URL, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);

  MsgBox('请先完成 DSH 的安装，然后重新运行本安装程序。', mbInformation, MB_OK);
  Result := False; // 退出本安装，等待 DSH 就绪后重装
end;
