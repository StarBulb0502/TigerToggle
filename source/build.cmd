@echo off
setlocal
set "compiler=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" set "compiler=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%compiler%" (
  echo Microsoft .NET Framework 4 compiler was not found.
  exit /b 1
)
pushd "%~dp0" || exit /b 1
"%compiler%" @build.rsp
set "build_result=%errorlevel%"
popd
exit /b %build_result%
