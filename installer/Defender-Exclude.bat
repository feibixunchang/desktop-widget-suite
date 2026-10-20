@echo off
rem ==========================================================
rem  WidgetHub - add Microsoft Defender exclusions
rem  The 10 widgets are unsigned small programs; Defender may
rem  misjudge them as PUA and delete them. This script adds
rem  safe exclusions for the WidgetHub install folders.
rem  (Run as administrator is required.)
rem ==========================================================
setlocal

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator privilege...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set TARGETS=%LOCALAPPDATA%\Programs\WidgetHubSuite
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopCalendar
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopMonitor
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopMusic
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopRadio
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopTimer
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopVideo
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\DesktopWallet
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\RssWidget
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\NeonMouseTrail
set TARGETS=%TARGETS%;%LOCALAPPDATA%\Programs\XiaoPuHeWeather
set TARGETS=%TARGETS%;%USERPROFILE%\Documents\WidgetHub

echo Adding Defender exclusions...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$t='%TARGETS%'.Split(';'); foreach($p in $t){ if(Test-Path $p){ try{ Add-MpPreference -ExclusionPath $p -ErrorAction Stop; Write-Host ('  OK   ' + $p) } catch { Write-Host ('  FAIL ' + $p) } } }"

echo.
echo Done. You can close this window.
timeout /t 4 >nul
exit /b 0
