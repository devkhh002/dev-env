@echo off
setlocal
rem Build (PcSetup.exe) - downloads the C# compiler (Roslyn) and .NET Framework 4.8
rem reference assemblies into app\.tools (nothing is installed on this PC).
rem   build.cmd             -> ..\dist\PcSetup.exe
rem   build.cmd <out.exe>   -> that path (for tests)
set "APP=%~dp0"
set "TOOLS=%APP%.tools"
if defined DEVENV_TOOLS set "TOOLS=%DEVENV_TOOLS%"
set "CSC=%TOOLS%\roslyn\tasks\net472\csc.exe"
set "REFS=%TOOLS%\net48\build\.NETFramework\v4.8"
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%APP%..\dist\PcSetup.exe"
if not exist "%CSC%" call :get microsoft.net.compilers.toolset 5.9.0 roslyn || exit /b 1
if not exist "%REFS%\mscorlib.dll" call :get microsoft.netframework.referenceassemblies.net48 1.0.3 net48 || exit /b 1
for %%d in ("%OUT%") do if not exist "%%~dpd" mkdir "%%~dpd"
rem Build version = local time yyyyMMddHHmm (this PC uses yyyy-MM-dd dates). Stop if it is not 12 digits.
for /f "tokens=1-5 delims=/:-. " %%a in ("%date% %time: =0%") do set "BUILD=%%a%%b%%c%%d%%e"
echo %BUILD%| findstr /r "^[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]$" >nul || (echo cannot read build time: %BUILD% & exit /b 1)
> "%APP%src\BuildInfo.g.cs" echo namespace PcSetup { static class BuildInfo { public const string Version = "%BUILD%"; } }
pushd "%APP%"
"%CSC%" @build.rsp /reference:"%REFS%\mscorlib.dll" /lib:"%REFS%","%REFS%\Facades","%WINDIR%\System32\WinMetadata" /out:"%OUT%"
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" exit /b %RC%
if /i not "%~1"=="" goto :done
> "%APP%..\dist\version.txt" echo %BUILD%
set "HASH="
for /f "delims=" %%h in ('certutil -hashfile "%OUT%" SHA256 ^| findstr /v ":"') do if not defined HASH set "HASH=%%h"
set "HASH=%HASH: =%"
> "%APP%..\dist\PcSetup.exe.sha256" echo %HASH%
:done
echo built %OUT% (%BUILD%)
exit /b 0

:get
if not exist "%TOOLS%" mkdir "%TOOLS%"
set "PKG=%TOOLS%\%1.%2.nupkg"
curl.exe -sSfL -o "%PKG%" "https://api.nuget.org/v3-flatcontainer/%1/%2/%1.%2.nupkg" || exit /b 1
if not exist "%TOOLS%\%3" mkdir "%TOOLS%\%3"
tar.exe -xf "%PKG%" -C "%TOOLS%\%3" || exit /b 1
del "%PKG%"
exit /b 0
