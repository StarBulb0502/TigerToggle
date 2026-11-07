@echo off
setlocal
set "compiler=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" set "compiler=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%compiler%" (
  echo Microsoft .NET Framework 4 compiler was not found.
  exit /b 1
)
"%compiler%" /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:"%~dp0app.manifest" /win32icon:"%~dp0app.ico" /resource:"%~dp0mascot.png",AppNetworkSwitch.Mascot /out:"%~dp0..\AppNetworkSwitch.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:Microsoft.CSharp.dll "%~dp0Program.cs" "%~dp0Firewall.cs" "%~dp0MainForm.cs" "%~dp0ThemeControls.cs"
exit /b %errorlevel%
