using RuntimePackage.Engine.Models;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RuntimePackage.Engine.Installation;

[SupportedOSPlatform("windows")]
public class EnvironmentVariableIntegrationService
{
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001a;
    private const int SmtoAbortIfHung = 0x0002;

    public virtual IReadOnlyList<InstalledEnvironmentVariableRecord> ApplyEnvironmentVariables(
        IReadOnlyList<EnvironmentVariableDescriptor> descriptors,
        string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var records = new List<InstalledEnvironmentVariableRecord>();
        try
        {
            foreach (var descriptor in descriptors)
            {
                var name = NormalizeName(descriptor.Name);
                var target = ParseTarget(descriptor.Target);
                var value = ExpandValue(descriptor.Value, installDirectory);
                var previousValue = Environment.GetEnvironmentVariable(name, target);

                Environment.SetEnvironmentVariable(name, value, target);
                records.Add(new InstalledEnvironmentVariableRecord
                {
                    Name = name,
                    Value = value,
                    Target = target.ToString(),
                    HadPreviousValue = previousValue is not null,
                    PreviousValue = previousValue,
                    WrittenAtUtc = DateTime.UtcNow
                });
            }

            NotifyEnvironmentChangedIfNeeded(records.Count);
            return records;
        }
        catch
        {
            RestoreEnvironmentVariables(records);
            throw;
        }
    }

    public virtual void RestoreEnvironmentVariables(IEnumerable<InstalledEnvironmentVariableRecord>? records)
    {
        if (records is null)
        {
            return;
        }

        var changed = 0;
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Name))
            {
                continue;
            }

            var target = ParseTarget(record.Target);
            var currentValue = Environment.GetEnvironmentVariable(record.Name, target);
            if (!string.Equals(currentValue, record.Value, StringComparison.Ordinal))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(
                record.Name,
                record.HadPreviousValue ? record.PreviousValue : null,
                target);
            changed++;
        }

        NotifyEnvironmentChangedIfNeeded(changed);
    }

    protected static string ExpandValue(string value, string installDirectory)
    {
        var expanded = value
            .Replace("{installDir}", installDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{app}", installDirectory, StringComparison.OrdinalIgnoreCase);

        return Environment.ExpandEnvironmentVariables(expanded);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("环境变量名称不能为空。");
        }

        return name.Trim();
    }

    private static EnvironmentVariableTarget ParseTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return EnvironmentVariableTarget.User;
        }

        if (Enum.TryParse<EnvironmentVariableTarget>(target, ignoreCase: true, out var parsed) &&
            parsed is EnvironmentVariableTarget.User or EnvironmentVariableTarget.Machine or EnvironmentVariableTarget.Process)
        {
            return parsed;
        }

        throw new InvalidOperationException($"不支持的环境变量作用域：{target}");
    }

    private static void NotifyEnvironmentChangedIfNeeded(int changedCount)
    {
        if (changedCount <= 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        _ = SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            UIntPtr.Zero,
            "Environment",
            SmtoAbortIfHung,
            5000,
            out _);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        int hWnd,
        int msg,
        UIntPtr wParam,
        string lParam,
        int fuFlags,
        int uTimeout,
        out UIntPtr lpdwResult);
}
