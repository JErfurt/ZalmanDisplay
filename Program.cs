using System.Diagnostics;
using ZalmanDisplay;

// Использование:
//   ZalmanDisplay.exe              — слать данные на экран и печатать их в консоль
//   ZalmanDisplay.exe --quiet      — то же без вывода (для автозапуска)
//   ZalmanDisplay.exe --interval 500   — период обновления, мс (по умолчанию 1000)
//   ZalmanDisplay.exe --sensors    — показать все датчики и выйти
//   ZalmanDisplay.exe --profile    — сколько стоит опрос каждого устройства
//   ZalmanDisplay.exe --fake-cpu 55    — подставить температуру CPU (проверка экрана)

Console.OutputEncoding = System.Text.Encoding.UTF8;
bool quiet = args.Contains("--quiet");
int interval = 1000;
int ix = Array.IndexOf(args, "--interval");
if (ix >= 0 && ix + 1 < args.Length)
    int.TryParse(args[ix + 1], out interval);
float? fakeCpu = null;
ix = Array.IndexOf(args, "--fake-cpu");
if (ix >= 0 && ix + 1 < args.Length && float.TryParse(args[ix + 1], out var fc))
    fakeCpu = fc;

if (args.Contains("--sensors"))
{
    Sensors.Dump(Console.Out);
    return;
}
if (args.Contains("--profile"))
{
    Sensors.Profile(Console.Out);
    return;
}

// Работаем в фоне и не мешаем играм/работе
using (var me = Process.GetCurrentProcess())
    me.PriorityClass = ProcessPriorityClass.BelowNormal;

using var sensors = new Sensors();
Log($"CPU: {sensors.CpuName}, GPU: {sensors.GpuName}");

var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

var v = new SensorSnapshot();
var proc = Process.GetCurrentProcess();
var statsAt = DateTime.UtcNow;
var statsCpu = proc.TotalProcessorTime;

ZalmanDevice? dev = null;
while (!stop.IsCancellationRequested)
{
    sensors.Read(v);
    if (fakeCpu != null)
        v.CpuTemp = fakeCpu;

    try
    {
        if (dev == null || !dev.IsOpen)
        {
            dev?.Dispose();
            dev = ZalmanDevice.Find();
            if (dev != null && dev.Open(out var warn))
            {
                Log($"подключено: {dev.Name}");
                if (warn != null) Log(warn);
            }
            else
            {
                dev = null;
            }
        }
        dev?.Send(v);
    }
    catch (Exception ex)
    {
        Log("ошибка USB: " + ex.Message + " — переподключусь");
        dev?.Close();
    }

    if (!quiet)
    {
        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss}  CPU {F(v.CpuTemp)}°C {F(v.CpuLoad)}% {F(v.CpuClockMhz)}МГц {F(v.CpuPowerW, "0.0")}Вт | " +
            $"GPU {F(v.GpuTemp)}°C {F(v.GpuLoad)}% {F(v.GpuClockMhz)}МГц | RAM {F(v.RamLoad)}% | " +
            $"{(dev?.IsOpen == true ? "экран OK" : "экран не найден")}");

        // Раз в 30 с — собственная нагрузка программы
        if (DateTime.UtcNow - statsAt >= TimeSpan.FromSeconds(30))
        {
            proc.Refresh();
            var cpuMs = (proc.TotalProcessorTime - statsCpu).TotalMilliseconds;
            var wallMs = (DateTime.UtcNow - statsAt).TotalMilliseconds;
            Console.WriteLine($"=== нагрузка программы: {cpuMs / wallMs * 100 / Environment.ProcessorCount:0.000}% всего CPU " +
                              $"({cpuMs / wallMs * 100:0.00}% одного ядра), память {proc.WorkingSet64 / 1048576} МБ ===");
            statsAt = DateTime.UtcNow;
            statsCpu = proc.TotalProcessorTime;
        }
    }

    stop.Token.WaitHandle.WaitOne(interval);
}
dev?.Dispose();

void Log(string msg)
{
    if (!quiet) Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {msg}");
}

static string F(float? x, string fmt = "0") => x?.ToString(fmt) ?? "—";
