@echo off
rem Builds tools\launcher\dist\TheFourthSealLauncher.exe with the C# compiler
rem that comes with Windows (.NET Framework 4.8) - nothing to install.
rem The first build also draws the seal icon (src\seal.ico) and builds again
rem with it.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "OUT=%~dp0dist\TheFourthSealLauncher.exe"
if not exist "%~dp0dist" mkdir "%~dp0dist"
rem The CurseForge key lives in src\ApiKey.cs, which is never published.
if not exist "%~dp0src\ApiKey.cs" copy "%~dp0src\ApiKey.cs.example" "%~dp0src\ApiKey.cs" >nul
set "ICON="
if exist "%~dp0src\seal.ico" set "ICON=/win32icon:%~dp0src\seal.ico"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu %ICON% ^
  /out:"%OUT%" ^
  /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:Microsoft.VisualBasic.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  "%~dp0src\*.cs"
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
if not exist "%~dp0src\seal.ico" (
  "%OUT%" --write-icon "%~dp0src\seal.ico"
  call "%~f0"
  exit /b %errorlevel%
)
echo Built %OUT%
