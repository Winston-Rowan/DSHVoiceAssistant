@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo   Diva Voice Assistant - 一键构建脚本
echo ============================================

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 dotnet 命令，请先安装 .NET 8 SDK:
    echo        https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0
    exit /b 1
)

echo [1/3] 还原 NuGet 包...
dotnet restore DSHVoiceAssistant.sln
if errorlevel 1 goto :err

echo [2/3] 编译解决方案（Release）...
dotnet build DSHVoiceAssistant.sln -c Release --no-restore
if errorlevel 1 goto :err

echo [3/3] 发布可执行版本（框架依赖）...
dotnet publish src\DSHVoiceAssistant\DSHVoiceAssistant.csproj -c Release --no-restore -o publish\win-x64
if errorlevel 1 goto :err

echo.
echo 构建成功！程序位于: publish\win-x64\DSHVoiceAssistant.exe
echo 提示: 运行需要本机安装 .NET 8 Desktop Runtime（一般安装 SDK 时已包含）。
exit /b 0

:err
echo.
echo [错误] 构建失败，请查看上方错误信息。
exit /b 1
