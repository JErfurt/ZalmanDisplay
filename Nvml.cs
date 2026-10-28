using System.Runtime.InteropServices;
using System.Text;

namespace ZalmanDisplay;

/// <summary>
/// Прямое чтение GPU NVIDIA через nvml.dll (идёт с драйвером).
/// Один опрос — доли миллисекунды, против ~60 мс у LibreHardwareMonitor.
/// </summary>
public sealed class Nvml : IDisposable
{
    const string Dll = "nvml.dll";

    [DllImport(Dll)] static extern int nvmlInit_v2();
    [DllImport(Dll)] static extern int nvmlShutdown();
    [DllImport(Dll)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport(Dll)] static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport(Dll)] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport(Dll)] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization u);
    [DllImport(Dll)] static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] static extern int nvmlDeviceGetFanSpeedRPM(IntPtr device, ref FanSpeedInfo info);

    [StructLayout(LayoutKind.Sequential)]
    struct Utilization { public uint Gpu, Memory; }

    [StructLayout(LayoutKind.Sequential)]
    struct FanSpeedInfo { public uint Version, Fan, Speed; }

    const int NVML_TEMPERATURE_GPU = 0;
    const int NVML_CLOCK_GRAPHICS = 0;

    readonly IntPtr _dev;
    bool _rpmSupported = true;

    public string Name { get; }

    Nvml(IntPtr dev, string name)
    {
        _dev = dev;
        Name = name;
    }

    public static Nvml? TryOpen()
    {
        try
        {
            if (nvmlInit_v2() != 0)
                return null;
            if (nvmlDeviceGetHandleByIndex_v2(0, out var dev) != 0)
            {
                nvmlShutdown();
                return null;
            }
            var buf = new byte[96];
            var name = nvmlDeviceGetName(dev, buf, (uint)buf.Length) == 0
                ? Encoding.ASCII.GetString(buf, 0, Array.IndexOf(buf, (byte)0) is var n and >= 0 ? n : buf.Length)
                : "NVIDIA GPU";
            return new Nvml(dev, name);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Read(SensorSnapshot s)
    {
        s.GpuTemp = nvmlDeviceGetTemperature(_dev, NVML_TEMPERATURE_GPU, out var t) == 0 ? t : null;
        s.GpuLoad = nvmlDeviceGetUtilizationRates(_dev, out var u) == 0 ? u.Gpu : null;
        s.GpuClockMhz = nvmlDeviceGetClockInfo(_dev, NVML_CLOCK_GRAPHICS, out var c) == 0 ? c : null;
        s.GpuPowerW = nvmlDeviceGetPowerUsage(_dev, out var mw) == 0 ? mw / 1000f : null;

        if (_rpmSupported)
        {
            var f = new FanSpeedInfo { Version = (uint)Marshal.SizeOf<FanSpeedInfo>() | (1u << 24) };
            try
            {
                s.GpuFanRpm = nvmlDeviceGetFanSpeedRPM(_dev, ref f) == 0 ? f.Speed : null;
            }
            catch (EntryPointNotFoundException)
            {
                _rpmSupported = false;
            }
        }
    }

    public void Dispose() => nvmlShutdown();
}
