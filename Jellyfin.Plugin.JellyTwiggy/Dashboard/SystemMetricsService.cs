using System.Globalization;

namespace Jellyfin.Plugin.JellyTwiggy.Dashboard;

/// <summary>Current system load; values are null where the platform doesn't provide them.</summary>
public record SystemMetricsDto(
    double? CpuPercent,
    double? MemoryPercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond);

/// <summary>Reads CPU, memory and disk load from Linux /proc, as deltas between calls.</summary>
public class SystemMetricsService
{
    private const int SectorBytes = 512;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Sample? _last;
    private SystemMetricsDto? _lastResult;

    /// <summary>Gets the current metrics; the first call waits briefly for a second sample.</summary>
    public async Task<SystemMetricsDto> GetMetricsAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new SystemMetricsDto(null, null, null, null, null, null);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastResult is not null && _last is not null && DateTime.UtcNow - _last.Time < MinInterval)
            {
                return _lastResult;
            }

            var previous = _last ?? ReadSample();
            if (_last is null)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            var current = ReadSample();
            _last = current;
            _lastResult = Compute(previous, current);
            return _lastResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SystemMetricsDto Compute(Sample previous, Sample current)
    {
        var seconds = (current.Time - previous.Time).TotalSeconds;
        double? cpu = null;
        if (current.CpuTotal > previous.CpuTotal)
        {
            var total = current.CpuTotal - previous.CpuTotal;
            cpu = Math.Clamp(100.0 * (total - (current.CpuIdle - previous.CpuIdle)) / total, 0, 100);
        }

        double? memory = current.MemoryTotal > 0 ? 100.0 * (current.MemoryTotal - current.MemoryAvailable) / current.MemoryTotal : null;
        double? read = seconds > 0 ? Math.Max(0, current.SectorsRead - previous.SectorsRead) * SectorBytes / seconds : null;
        double? write = seconds > 0 ? Math.Max(0, current.SectorsWritten - previous.SectorsWritten) * SectorBytes / seconds : null;
        return new SystemMetricsDto(
            cpu is null ? null : Math.Round(cpu.Value, 1),
            memory is null ? null : Math.Round(memory.Value, 1),
            current.MemoryTotal > 0 ? current.MemoryTotal - current.MemoryAvailable : null,
            current.MemoryTotal > 0 ? current.MemoryTotal : null,
            read,
            write);
    }

    private static Sample ReadSample()
    {
        var sample = new Sample { Time = DateTime.UtcNow };
        try
        {
            // "cpu  user nice system idle iowait irq softirq steal ...": idle time is idle + iowait.
            var cpu = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(ParseLong).ToArray();
            sample.CpuTotal = cpu.Sum();
            sample.CpuIdle = cpu.ElementAtOrDefault(3) + cpu.ElementAtOrDefault(4);
        }
        catch (IOException)
        {
        }

        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] == "MemTotal:")
                {
                    sample.MemoryTotal = ParseLong(parts[1]) * 1024;
                }
                else if (parts.Length >= 2 && parts[0] == "MemAvailable:")
                {
                    sample.MemoryAvailable = ParseLong(parts[1]) * 1024;
                }
            }
        }
        catch (IOException)
        {
        }

        try
        {
            // Fields: major minor name reads merged sectorsRead ms writes merged sectorsWritten ...
            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 10 && IsPhysicalDisk(parts[2]))
                {
                    sample.SectorsRead += ParseLong(parts[5]);
                    sample.SectorsWritten += ParseLong(parts[9]);
                }
            }
        }
        catch (IOException)
        {
        }

        return sample;
    }

    // Whole physical disks have a backing device; partitions, loop, ram, dm and md devices don't.
    private static bool IsPhysicalDisk(string name) => Directory.Exists($"/sys/block/{name}/device");

    private static long ParseLong(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private sealed class Sample
    {
        public DateTime Time { get; init; }

        public long CpuTotal { get; set; }

        public long CpuIdle { get; set; }

        public long MemoryTotal { get; set; }

        public long MemoryAvailable { get; set; }

        public long SectorsRead { get; set; }

        public long SectorsWritten { get; set; }
    }
}
