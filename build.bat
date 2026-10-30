@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "CONFIG=Release"
set "PROJECT=%~dp0YgpPlaytimeSync\YgpPlaytimeSync.csproj"
set "OUT=%~dp0bin\%CONFIG%"
set "LOG=%~dp0build.log"

echo.
echo YGP Playtime Sync - Dev build
echo.

set "SDK="
if defined PLAYNITE_DIR if exist "%PLAYNITE_DIR%\Playnite.SDK.dll" set "SDK=%PLAYNITE_DIR%\Playnite.SDK.dll"
if not defined SDK if exist "%LOCALAPPDATA%\Playnite\Playnite.SDK.dll" set "SDK=%LOCALAPPDATA%\Playnite\Playnite.SDK.dll"
if not defined SDK goto :nosdk

echo Playnite SDK: %SDK%

where dotnet >nul 2>&1
if not errorlevel 1 goto :usedotnet
goto :findmsbuild

:usedotnet
echo Builder: dotnet
dotnet build "%PROJECT%" -c %CONFIG% -nologo -p:PlayniteSdkPath="%SDK%" > "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
goto :built

:findmsbuild
set "MSBUILD="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :notool
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD goto :notool
echo Builder: MSBuild
"%MSBUILD%" "%PROJECT%" -restore -p:Configuration=%CONFIG% -nologo -v:minimal -p:PlayniteSdkPath="%SDK%" > "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
goto :built

:built
type "%LOG%"
echo.
if not "%RC%"=="0" goto :fail
if not exist "%OUT%\YgpPlaytimeSync.dll" goto :fail

if not exist "%OUT%\Resources" mkdir "%OUT%\Resources"
copy /y "%~dp0YgpPlaytimeSync\Resources\pluginicon.png" "%OUT%\Resources\" >nul
copy /y "%~dp0YgpPlaytimeSync\extension.yaml" "%OUT%\" >nul

echo BUILD OK
echo.
echo Output folder: %OUT%
echo.
echo To test in Playnite:
echo   1. Open Playnite ^> Settings ^> For developers ^> External extensions ^> Add.
echo   2. Select the folder above and restart Playnite.
echo   If the plugin is already installed from a .pext, remove it first (same extension Id).
goto :end

:nosdk
echo ERROR: Playnite.SDK.dll not found.
echo Install Playnite, or set the PLAYNITE_DIR environment variable to your Playnite folder
echo (the folder that contains Playnite.SDK.dll, e.g. a portable install).
set "RC=1"
goto :end

:notool
echo ERROR: no build tool found.
echo Install the .NET SDK (https://dotnet.microsoft.com/download) or the Visual Studio Build Tools.
set "RC=1"
goto :end

:fail
echo BUILD FAILED. See build.log in this folder.
set "RC=1"

:end
echo.
if /i not "%~1"=="nopause" pause
exit /b %RC%
