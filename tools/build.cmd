@echo off
setlocal
set ROOT=%~dp0..
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found
  exit /b 1
)
if not exist "%ROOT%\dist" mkdir "%ROOT%\dist"

echo [1/3] icon
"%CSC%" /nologo /target:exe /out:"%ROOT%\tools\make-icon.exe" /r:System.dll /r:System.Drawing.dll "%ROOT%\tools\make-icon.cs" >nul
if errorlevel 1 goto fail
"%ROOT%\tools\make-icon.exe" "%ROOT%\dist\app.ico"
if errorlevel 1 goto fail

echo [2/3] app
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:"%ROOT%\dist\app.ico" /out:"%ROOT%\dist\ClaudeTrafficLight.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "%ROOT%\src\*.cs"
if errorlevel 1 goto fail

echo [3/3] setup
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:"%ROOT%\dist\app.ico" /out:"%ROOT%\dist\ClaudeTrafficLightSetup.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll /resource:"%ROOT%\dist\ClaudeTrafficLight.exe",Payload "%ROOT%\setup\*.cs" "%ROOT%\src\Json.cs" "%ROOT%\src\Model.cs" "%ROOT%\src\Log.cs" "%ROOT%\src\HooksSetup.cs" "%ROOT%\src\AppInfo.cs" "%ROOT%\src\Ui.cs" "%ROOT%\src\Win32.cs"
if errorlevel 1 goto fail

echo BUILD OK
exit /b 0

:fail
echo BUILD FAILED
exit /b 1
