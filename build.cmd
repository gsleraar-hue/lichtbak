@echo off
rem Builds bin\Lichtbak.exe with the C# compiler that ships with the .NET Framework.
rem No SDK, no packages. Install with: bin\Lichtbak.exe --install
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set CSC=%FW%\csc.exe

set ROOT=%~dp0
if not exist "%ROOT%bin" mkdir "%ROOT%bin"

if not exist "%ROOT%build\lichtbak.ico" (
  echo [icon] build\lichtbak.ico
  "%CSC%" /nologo /out:"%ROOT%bin\MakeIcon.exe" /reference:System.Drawing.dll "%ROOT%tools\MakeIcon.cs" || goto fail
  "%ROOT%bin\MakeIcon.exe" "%ROOT%build\lichtbak.ico" "%ROOT%build\lichtbak.png" || goto fail
)

echo [1/1] Lichtbak.exe
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /nowarn:1690 ^
  /win32icon:"%ROOT%build\lichtbak.ico" ^
  /out:"%ROOT%bin\Lichtbak.exe" ^
  /lib:"%FW%\WPF" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:Microsoft.VisualBasic.dll ^
  /reference:PresentationCore.dll ^
  /reference:WindowsBase.dll ^
  /reference:System.Xaml.dll ^
  "%ROOT%src\*.cs"
if errorlevel 1 goto fail

rem The same exe; named "-setup" it opens the install window instead of the viewer.
copy /y "%ROOT%bin\Lichtbak.exe" "%ROOT%bin\Lichtbak-setup.exe" >nul

echo.
echo Done:
echo   %ROOT%bin\Lichtbak.exe
echo   %ROOT%bin\Lichtbak-setup.exe
exit /b 0

:fail
echo.
echo Build failed.
exit /b 1
