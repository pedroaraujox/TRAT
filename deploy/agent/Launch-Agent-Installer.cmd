@echo off
setlocal

set "PACKAGE_ROOT=%~dp0"
set "INSTALLER_EXE=%PACKAGE_ROOT%installer\WebstationBackup.Agent.Installer.exe"

if not exist "%INSTALLER_EXE%" (
  echo Erro: Installer GUI nao encontrado em "%INSTALLER_EXE%"
  exit /b 1
)

start "" "%INSTALLER_EXE%"
