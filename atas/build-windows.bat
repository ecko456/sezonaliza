@echo off
setlocal
title ATAS indikatory - build
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [CHYBA] Nenalezen .NET SDK. Nainstalujte .NET 10 SDK z https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

rem --- slozka s ATAS DLL (lze zadat jako 1. parametr) ---
set "ATASDIR=%~1"
if not "%ATASDIR%"=="" goto :found
for %%D in ("C:\Program Files (x86)\ATAS Platform" "C:\Program Files\ATAS Platform" "C:\Program Files\ATAS X" "C:\Program Files (x86)\ATAS X" "%LOCALAPPDATA%\Programs\ATAS X" "%LOCALAPPDATA%\Programs\ATAS Platform") do (
  if exist "%%~D\ATAS.Indicators.dll" set "ATASDIR=%%~D"
)
if not "%ATASDIR%"=="" goto :found
echo Nenasel jsem slozku s ATAS.Indicators.dll.
echo (Najdete ji: pravy klik na zastupce ATAS - Otevrit umisteni souboru)
set /p "ATASDIR=Vlozte cestu ke slozce: "
:found
if not exist "%ATASDIR%\ATAS.Indicators.dll" (
  echo [CHYBA] V "%ATASDIR%" neni ATAS.Indicators.dll
  pause
  exit /b 1
)
echo ATAS: %ATASDIR%

set "TFM=%~2"
if "%TFM%"=="" set "TFM=net10.0"

set "DEST=%APPDATA%\ATAS\Indicators"
if not exist "%DEST%" mkdir "%DEST%"

for %%P in (DeltaStrengthBar WickAbsorption) do (
  echo.
  echo === %%P ===
  dotnet build "%%P\%%P.csproj" -c Release -p:AtasDir="%ATASDIR%" -p:AtasTfm=%TFM% -o "out\%%P"
  if errorlevel 1 goto :fail
  copy /y "out\%%P\%%P.dll" "%DEST%\" >nul
  echo Zkopirovano do %DEST%
)

echo.
echo HOTOVO. Restartujte ATAS a pridejte indikatory "Delta Strength Bar" a "Wick Absorption".
pause
exit /b 0

:fail
echo.
echo [CHYBA] Build selhal. Zkopirujte prosim chybove hlasky vyse (radky s "error") a poslete je.
echo Starsi ATAS (ne ATAS X) na .NET 8:  build-windows.bat "cesta k ATAS" net8.0
pause
exit /b 1
