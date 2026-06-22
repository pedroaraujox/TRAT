@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

if not exist "appsettings.Local.json" (
    echo Configuracao local nao encontrada. Iniciando primeira configuracao...
    powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Primeira-Configuracao.ps1" -PackageRoot "%SCRIPT_DIR%"
    if errorlevel 1 (
        echo Falha ao criar a configuracao local.
        popd
        exit /b 1
    )
)

if not exist "ControlPlane.Api.exe" (
    echo Executavel do painel nao encontrado em "%SCRIPT_DIR%".
    popd
    exit /b 1
)

if not exist "logs" mkdir "logs"

set "PANEL_URL=http://localhost:5080"
set "PANEL_PID_FILE=%SCRIPT_DIR%controlplane.pid"
set "PANEL_STDOUT_LOG_FILE=%SCRIPT_DIR%logs\controlplane.stdout.log"
set "PANEL_STDERR_LOG_FILE=%SCRIPT_DIR%logs\controlplane.stderr.log"

powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Parar-Painel-Local.ps1" -PackageRoot "%SCRIPT_DIR%" >nul 2>nul
powershell.exe -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $process = Start-Process -FilePath '%SCRIPT_DIR%ControlPlane.Api.exe' -WorkingDirectory '%SCRIPT_DIR%' -ArgumentList '--urls','%PANEL_URL%' -PassThru -WindowStyle Normal -RedirectStandardOutput '%PANEL_STDOUT_LOG_FILE%' -RedirectStandardError '%PANEL_STDERR_LOG_FILE%'; Set-Content -Path '%PANEL_PID_FILE%' -Value $process.Id -Encoding ASCII"

if errorlevel 1 (
    echo Falha ao iniciar o painel local.
    popd
    exit /b 1
)

timeout /t 2 /nobreak >nul
start "" "%PANEL_URL%"
echo Painel iniciado em %PANEL_URL%
echo Logs: %PANEL_STDOUT_LOG_FILE% e %PANEL_STDERR_LOG_FILE%

popd
exit /b 0
