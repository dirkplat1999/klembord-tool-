@echo off
rem Bouwt KlembordTool.exe met de C#-compiler die standaard in Windows zit (.NET Framework 4).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo .NET Framework 4 compiler niet gevonden.
    pause
    exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:KlembordTool.exe ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Web.Extensions.dll ^
    src\*.cs
if errorlevel 1 (
    echo.
    echo Bouwen mislukt.
    pause
    exit /b 1
)
echo KlembordTool.exe is klaar.
