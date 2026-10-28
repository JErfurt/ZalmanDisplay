# Откат: удаляет ZalmanDisplay и возвращает службу ZALMAN-CTM. Запускать от администратора.
$ErrorActionPreference = 'Continue'
$task = 'ZalmanDisplay'

Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue | Stop-ScheduledTask
Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
Get-Process ZalmanDisplay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Remove-Item 'C:\Program Files\ZalmanDisplay' -Recurse -Force -ErrorAction SilentlyContinue

Set-Service -Name 'ZALMAN-CTM' -StartupType Automatic
Start-Service -Name 'ZALMAN-CTM'
Write-Host 'Служба ZALMAN-CTM возвращена.' -ForegroundColor Green
Read-Host 'Enter - закрыть'
