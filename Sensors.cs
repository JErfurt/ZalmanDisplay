using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace ZalmanDisplay;

public sealed class SensorSnapshot
{
    public float? CpuTemp, CpuLoad, CpuClockMhz, CpuPowerW, CpuVoltage;
    public float? GpuTemp, GpuLoad, GpuClockMhz, GpuPowerW, GpuVoltage, GpuFanRpm;
    public float? RamLoad, DiskLoad, FanRpm;
    public float? NetUpBytesPerSec, NetDownBytesPerSec;
}

/// <summary>
/// Чтение датчиков через LibreHardwareMonitorLib.
/// Ради минимальной нагрузки опрашиваются только CPU и один GPU (NVIDIA — через NVML), нужные датчики
/// находятся один раз при запуске, а память читается напрямую через WinAPI.
/// Диски, сеть и материнская плата не опрашиваются (их опрос — самая дорогая часть).
/// </summary>
public sealed class Sensors : IDisposable
{
    readonly Computer _pc;
    readonly Nvml? _nvml;
    readonly IHardware? _cpu, _gpu;
    readonly ISensor? _cpuTemp, _cpuLoad, _cpuPower, _cpuVolt;
    readonly ISensor[] _cpuClocks = Array.Empty<ISensor>();
    readonly ISensor? _gpuTemp, _gpuLoad, _gpuClock, _gpuPower, _gpuVolt, _gpuFan;

    public string? CpuName => _cpu?.Name;
    public string? GpuName => _nvml?.Name ?? _gpu?.Name;

    public Sensors()
    {
        // NVIDIA читаем напрямую через NVML; LibreHardwareMonitor для GPU — только если её нет
        _nvml = Nvml.TryOpen();
        _pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = _nvml == null };
        _pc.Open();

        _cpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Как и в оригинале: если GPU несколько, предпочитаем NVIDIA
        _gpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
               ?? _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
               ?? _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);

        if (_cpu != null)
        {
            _cpu.Update();
            // AMD: "Core (Tctl/Tdie)", Intel: "CPU Package"
            _cpuTemp = Find(_cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core Average")
                       ?? _cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature);
            _cpuLoad = Find(_cpu, SensorType.Load, "CPU Total");
            _cpuPower = Find(_cpu, SensorType.Power, "Package", "CPU Package");
            _cpuVolt = Find(_cpu, SensorType.Voltage, "Core (SVI2 TFN)", "CPU Core", "Core (SVI3 TFN)", "Core #1 VID");
            _cpuClocks = _cpu.Sensors
                .Where(x => x.SensorType == SensorType.Clock && !x.Name.Contains("Effective")
                            && (x.Name.StartsWith("Core #") || x.Name.StartsWith("P-Core") || x.Name.StartsWith("E-Core")))
                .ToArray();
        }

        if (_gpu != null)
        {
            _gpu.Update();
            _gpuTemp = Find(_gpu, SensorType.Temperature, "GPU Core");
            _gpuLoad = Find(_gpu, SensorType.Load, "GPU Core", "D3D 3D");
            _gpuClock = Find(_gpu, SensorType.Clock, "GPU Core");
            _gpuPower = Find(_gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Core");
            _gpuVolt = Find(_gpu, SensorType.Voltage, "GPU Core");
            _gpuFan = _gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Fan);
        }
    }

    /// <summary>Обновить показания в существующем объекте (без выделений памяти).</summary>
    public void Read(SensorSnapshot s)
    {
        if (_cpu != null)
        {
            _cpu.Update();
            s.CpuTemp = _cpuTemp?.Value;
            s.CpuLoad = _cpuLoad?.Value;
            s.CpuPowerW = _cpuPower?.Value;
            s.CpuVoltage = _cpuVolt?.Value;
            float max = 0;
            foreach (var c in _cpuClocks)
                if (c.Value > max) max = c.Value.Value;
            s.CpuClockMhz = max;
        }

        if (_nvml != null)
        {
            _nvml.Read(s);
        }
        else if (_gpu != null)
        {
            _gpu.Update();
            s.GpuTemp = _gpuTemp?.Value;
            s.GpuLoad = _gpuLoad?.Value;
            s.GpuClockMhz = _gpuClock?.Value;
            s.GpuPowerW = _gpuPower?.Value;
            s.GpuVoltage = _gpuVolt?.Value;
            s.GpuFanRpm = _gpuFan?.Value;
        }

        s.RamLoad = MemoryLoad();
    }

    static ISensor? Find(IHardware hw, SensorType type, params string[] names)
    {
        foreach (var n in names)
            foreach (var x in hw.Sensors)
                if (x.SensorType == type && x.Name == n)
                    return x;
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    static float? MemoryLoad()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m.dwMemoryLoad : null;
    }

    /// <summary>Вывести все датчики всего железа (для подбора имён).</summary>
    public static void Dump(TextWriter w)
    {
        var pc = AllComputer();
        foreach (var hw in pc.Hardware)
        {
            UpdateAll(hw);
            w.WriteLine($"[{hw.HardwareType}] {hw.Name}");
            foreach (var x in AllSensors(hw))
                w.WriteLine($"    {x.SensorType,-12} {x.Name,-28} {x.Value}");
        }
        pc.Close();
    }

    /// <summary>Сколько миллисекунд занимает опрос каждого устройства.</summary>
    public static void Profile(TextWriter w, int rounds = 20)
    {
        var pc = AllComputer();
        foreach (var hw in pc.Hardware)
        {
            UpdateAll(hw); // прогрев
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < rounds; i++)
                UpdateAll(hw);
            w.WriteLine($"{sw.Elapsed.TotalMilliseconds / rounds,8:0.00} мс  [{hw.HardwareType}] {hw.Name}");
        }
        pc.Close();
    }

    static Computer AllComputer()
    {
        var pc = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
            IsMotherboardEnabled = true, IsStorageEnabled = true, IsNetworkEnabled = true,
        };
        pc.Open();
        return pc;
    }

    static void UpdateAll(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            UpdateAll(sub);
    }

    static IEnumerable<ISensor> AllSensors(IHardware h) =>
        h.Sensors.Concat(h.SubHardware.SelectMany(AllSensors));

    public void Dispose()
    {
        _nvml?.Dispose();
        _pc.Close();
    }
}
