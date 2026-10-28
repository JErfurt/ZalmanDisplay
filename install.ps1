# Установка ZalmanDisplay вместо службы ZALMAN-CTM. Запускать от администратора.
# Откат: uninstall.ps1
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'publish'
$dst = 'C:\Program Files\ZalmanDisplay'
$task = 'ZalmanDisplay'
$log = Join-Path $PSScriptRoot 'install.log'
Start-Transcript -Path $log -Force | Out-Null

try {
    Write-Host '1. Останавливаю пробные запуски и старую задачу...'
    Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue | Stop-ScheduledTask -ErrorAction SilentlyContinue
    Get-Process ZalmanDisplay -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    Write-Host "2. Копирую программу в $dst"
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force

    Write-Host '3. Останавливаю и отключаю службу ZALMAN-CTM (не удаляю)'
    Stop-Service -Name 'ZALMAN-CTM' -Force -ErrorAction SilentlyContinue
    Set-Service -Name 'ZALMAN-CTM' -StartupType Disabled

    Write-Host '4. Создаю задачу автозапуска (при старте Windows, от SYSTEM, без окна)'
    $action = New-ScheduledTaskAction -Execute (Join-Path $dst 'ZalmanDisplay.exe') -Argument '--quiet' -WorkingDirectory $dst
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -MultipleInstances IgnoreNew -StartWhenAvailable
    Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Description 'Вывод температуры на экран Zalman (замена ZALMAN-CTM)' -Force | Out-Null

    Write-Host '5. Запускаю'
    Start-ScheduledTask -TaskName $task
    Start-Sleep -Seconds 3
    $p = Get-Process ZalmanDisplay -ErrorAction SilentlyContinue
    if ($p) { Write-Host "Готово: ZalmanDisplay работает (PID $($p.Id))" -ForegroundColor Green }
    else { Write-Host 'ZalmanDisplay не запустился — смотрите install.log' -ForegroundColor Red }
}
catch {
    Write-Host "ОШИБКА: $_" -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
    Read-Host 'Enter - закрыть'
}
