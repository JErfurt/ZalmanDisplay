# Пробный запуск: временно останавливает службу ZALMAN-CTM, запускает ZalmanDisplay,
# а по Ctrl+C возвращает службу обратно. Тип запуска службы не меняется.
# Все аргументы передаются в ZalmanDisplay.exe как есть, например: try-run.ps1 --fake-cpu 55
$ExeArgs = $args
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\ZalmanDisplay.exe'
$log = Join-Path $PSScriptRoot 'try-run.log'

try {
    Write-Host 'Останавливаю службу ZALMAN-CTM...' -ForegroundColor Yellow
    Stop-Service -Name 'ZALMAN-CTM' -Force
    Write-Host 'Служба остановлена. Запускаю ZalmanDisplay. Для выхода нажмите Ctrl+C (не закрывайте окно крестиком).' -ForegroundColor Green
    & $exe @ExeArgs 2>&1 | Tee-Object -FilePath $log
}
finally {
    Write-Host 'Запускаю службу ZALMAN-CTM обратно...' -ForegroundColor Yellow
    Start-Service -Name 'ZALMAN-CTM'
    Write-Host 'Служба снова работает. Окно можно закрыть.' -ForegroundColor Green
    Read-Host 'Enter - закрыть'
}
