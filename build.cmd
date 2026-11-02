@echo off
rem Builds bin\DesktopPet.exe with the C# compiler that ships with Windows.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find csc.exe from .NET Framework 4.
    exit /b 1
)

if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /optimize+ /out:bin\DesktopPet.exe ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 exit /b 1

rem robocopy exit codes below 8 mean success (1 = files copied).
robocopy pets bin\pets sheet.png pet.ini /s /purge /njh /njs /nfl /ndl >nul
if errorlevel 8 exit /b 1
robocopy effects bin\effects sheet.png pet.ini /s /purge /njh /njs /nfl /ndl >nul
if errorlevel 8 exit /b 1
echo Built bin\DesktopPet.exe
exit /b 0
