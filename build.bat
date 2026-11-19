@echo off
rem Builds Vigil.exe with the C# compiler that ships with Windows (.NET Framework 4). Nothing to install.
rem The libraries in lib\ and the window's page in ui\ go inside the exe as resources: one file to download.
cd /d "%~dp0"
setlocal enabledelayedexpansion
set LIBS=
for %%f in (lib\*.dll) do set LIBS=!LIBS! /resource:%%f,%%~nxf
for %%f in (ui\*.html ui\*.css ui\*.js) do set LIBS=!LIBS! /resource:%%f,ui/%%~nxf
for %%f in (ui\fonts\*.woff2) do set LIBS=!LIBS! /resource:%%f,ui/fonts/%%~nxf
echo Compiling Vigil...
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ /platform:x64 /target:winexe /out:Vigil.exe ^
  /win32icon:icon\vigil.ico /win32manifest:vigil.manifest /resource:icon\vigil.ico,Vigil.ico ^
  /reference:lib\LibreHardwareMonitorLib.dll /reference:lib\Microsoft.Web.WebView2.Core.dll /reference:lib\Microsoft.Web.WebView2.WinForms.dll ^
  /reference:System.Management.dll /reference:System.Web.Extensions.dll %LIBS% ^
  Vigil.cs MainForm.cs AssemblyInfo.cs
if %errorlevel% neq 0 (
    echo Compilation failed.
    pause
    exit /b %errorlevel%
)
"%~dp0Vigil.exe" --selftest
if %errorlevel% neq 0 (
    echo Self-test failed.
    pause
    exit /b %errorlevel%
)
echo Packing Vigil-portable.zip (the same exe plus Vigil.ini)...
powershell -NoProfile -Command "$d = Join-Path $env:TEMP 'vigil-portable'; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory $d | Out-Null; Copy-Item Vigil.exe $d; Set-Content (Join-Path $d 'Vigil.ini') '; Vigil settings. While this file sits next to Vigil.exe, nothing goes to the registry.'; Compress-Archive (Join-Path $d '*') Vigil-portable.zip -Force; Remove-Item $d -Recurse -Force"
echo Done: Vigil.exe and Vigil-portable.zip
