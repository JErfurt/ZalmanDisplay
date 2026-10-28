using HidSharp;

namespace ZalmanDisplay;

/// <summary>
/// Экран Zalman (CTM), подключённый по USB HID.
/// Протокол восстановлен по TempComm.dll из официального ПО ZALMAN-CTM 2.2.
/// </summary>
public sealed class ZalmanDevice : IDisposable
{
    public const int Vid = 0x1C57;
    // ZALMAN-CTM: 0x3ED1, 0x4B8F, 0x3A7F; ZALMAN-TDM: 0x3ED2, 0x3ED6
    public static readonly int[] Pids = { 0x3ED1, 0x4B8F, 0x3A7F, 0x3ED2, 0x3ED6 };

    const int ReportLen = 65; // 1 байт Report ID (0) + 64 байта данных

    readonly HidDevice _dev;
    HidStream? _stream;

    ZalmanDevice(HidDevice dev) => _dev = dev;

    public string Name => $"{_dev.GetProductName()} ({_dev.VendorID:X4}:{_dev.ProductID:X4})";

    public static ZalmanDevice? Find()
    {
        var dev = DeviceList.Local.GetHidDevices(Vid)
            .FirstOrDefault(d => Pids.Contains(d.ProductID) && d.GetMaxOutputReportLength() >= ReportLen);
        return dev == null ? null : new ZalmanDevice(dev);
    }

    public bool IsOpen => _stream != null;

    /// <summary>Открыть устройство и выполнить рукопожатие (команда 0x01).</summary>
    public bool Open(out string? warning)
    {
        warning = null;
        Close();
        if (!_dev.TryOpen(out var s))
            return false;
        s.ReadTimeout = 5000;
        s.WriteTimeout = 5000;
        _stream = s;
        try
        {
            if (!Handshake())
                warning = "устройство не подтвердило рукопожатие, пробую слать данные всё равно";
        }
        catch (Exception ex)
        {
            warning = "ошибка рукопожатия: " + ex.Message;
        }
        return true;
    }

    bool Handshake()
    {
        var b = new byte[ReportLen];
        Random.Shared.NextBytes(b);
        int w = Random.Shared.Next(0x10000);
        b[0] = 0;
        b[1] = 0x01;
        b[2] = b[3] = b[4] = 0;
        b[5] = (byte)(w >> 8);
        b[6] = (byte)w;

        int a = w ^ (((b[7] + b[9]) << 8) + b[10] + b[8]);
        int d = ((b[5] + b[7]) << 8) + b[8] + b[6];
        int expected = (((b[9] << 8) + b[10]) ^ d) & 0xFFFF;
        b[11] = (byte)(a >> 8);
        b[12] = (byte)a;
        b[63] = XorSum(b);
        b[64] = WeightedSum(b);

        _stream!.Write(b);

        var r = new byte[ReportLen];
        int n = _stream.Read(r, 0, r.Length);
        if (n < ReportLen)
            return false;
        int got = (r[5] << 8) | r[6];
        return got == expected && r[63] == XorSum(r) && r[64] == WeightedSum(r);
    }

    // XOR от (i * buf[1+i]) для i = 0..59
    static byte XorSum(byte[] buf)
    {
        byte x = 0;
        for (int i = 0; i < 60; i++)
            x ^= (byte)(buf[1 + i] * i);
        return x;
    }

    // Сумма (i * buf[1+i]) для i = 0..62
    static byte WeightedSum(byte[] buf)
    {
        byte s = 0;
        for (int i = 0; i < 63; i++)
            s += (byte)(buf[1 + i] * i);
        return s;
    }

    /// <summary>Отправить показания (команда 0x02). Вызывать примерно раз в 500 мс.</summary>
    readonly byte[] _buf = new byte[ReportLen];

    public void Send(SensorSnapshot v)
    {
        var now = DateTime.Now;
        var b = _buf;
        Array.Clear(b);
        b[1] = 0x02;
        b[2] = Clamp(v.CpuTemp);
        b[3] = Clamp(v.GpuTemp);
        b[4] = (byte)now.Hour;
        b[5] = (byte)now.Minute;
        b[6] = (byte)now.Second;
        b[7] = (byte)(now.Millisecond / 10);
        b[8] = (byte)(now.Year / 100);
        b[9] = (byte)(now.Year % 100);
        b[10] = (byte)now.Month;
        b[11] = (byte)now.Day;
        b[12] = (byte)now.DayOfWeek;
        b[13] = Clamp(v.CpuLoad);
        Put16(b, 14, v.CpuClockMhz);
        b[16] = Clamp(v.GpuLoad);
        Put16(b, 17, v.GpuClockMhz);
        b[20] = Clamp(v.RamLoad);
        b[24] = Clamp(v.DiskLoad);
        Put16(b, 25, v.FanRpm);
        PutVolt(b, 27, v.CpuVoltage);
        Put16(b, 29, v.CpuPowerW * 10);
        Put16(b, 31, v.GpuFanRpm);
        PutVolt(b, 33, v.GpuVoltage);
        Put16(b, 35, v.GpuPowerW * 10);
        Put32(b, 41, v.NetUpBytesPerSec);
        Put32(b, 45, v.NetDownBytesPerSec);
        // b[49] — «режим дисплея» (SetDisplayMode в TempComm.dll). Официальное ПО шлёт 0;
        // перебор 0x00–0x07, 0x80–0x87, 0x10, 0x20, 0x40, 0xFF на CTM ничего не меняет.

        _stream!.Write(b);
    }

    static byte Clamp(float? f) => f is { } x ? (byte)Math.Clamp((int)x, 0, 255) : (byte)0;

    static void Put16(byte[] b, int at, float? f)
    {
        int x = f is { } v ? Math.Clamp((int)v, 0, 0xFFFF) : 0;
        b[at] = (byte)(x >> 8);
        b[at + 1] = (byte)x;
    }

    static void Put32(byte[] b, int at, float? f)
    {
        uint x = f is { } v ? (uint)Math.Clamp((double)v, 0, uint.MaxValue) : 0;
        b[at] = (byte)(x >> 24);
        b[at + 1] = (byte)(x >> 16);
        b[at + 2] = (byte)(x >> 8);
        b[at + 3] = (byte)x;
    }

    // Напряжение: целая часть, затем сотые (1.25 В -> 01 19)
    static void PutVolt(byte[] b, int at, float? f)
    {
        if (f is not { } v || v <= 0) return;
        b[at] = (byte)(int)v;
        b[at + 1] = (byte)(int)(v * 100 % 100);
    }

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Close();
}
