@echo off
rem ============================================================================
rem  Q Pet - build both exes and copy them next to this file.
rem  Requirements: .NET SDK 6.0+ ( https://dotnet.microsoft.com/download )
rem  Just double-click this file. Messages are English on purpose (cmd codepage).
rem ============================================================================
setlocal
cd /d "%~dp0"
echo ============================================
echo  Q Pet - build (Release)
echo ============================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [!] .NET SDK not found.
  echo     Install it first: https://dotnet.microsoft.com/download
  echo     Then reopen this window and run build.cmd again.
  echo.
  pause
  exit /b 1
)
for /f "delims=" %%v in ('dotnet --version') do echo  dotnet SDK %%v
echo.
echo [1/2] building Pet.exe ...
dotnet build Pet.csproj -c Release -nologo -v q
if errorlevel 1 goto fail
echo [2/2] building PetSettings.exe ...
dotnet build PetSettings.csproj -c Release -nologo -v q
if errorlevel 1 goto fail
echo.
echo [*] copying into this folder ...
copy /y "build\pet\Pet.exe" "Pet.exe" >nul
if errorlevel 1 goto busy
copy /y "build\settings\PetSettings.exe" "PetSettings.exe" >nul
if errorlevel 1 goto busy
echo.
echo [OK] Done.
echo      double-click Pet.exe        -^> desktop pet
echo      double-click PetSettings.exe -^> settings
echo.
pause
exit /b 0

:busy
echo.
echo [X] Could not overwrite Pet.exe / PetSettings.exe.
echo     The pet is probably still running: right-click its tray icon and exit,
echo     or run: taskkill /im Pet.exe /f
echo.
pause
exit /b 1

:fail
echo.
echo [X] Build failed - see the messages above.
echo     No internet? NuGet restore needs a connection the first time.
echo.
pause
exit /b 1
