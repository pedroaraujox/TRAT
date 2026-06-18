@echo off
setlocal

set "PACKAGE_ROOT=%~dp0"
set "TRAY_EXE=%PACKAGE_ROOT%tray\WebstationBackup.Agent.Tray.exe"

if not exist "%TRAY_EXE%" (
  echo Erro: Tray App nao encontrado em "%TRAY_EXE%"
  exit /b 1
)

start "" "%TRAY_EXE%"
