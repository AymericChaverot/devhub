using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using DevHub.Core.Models;
using Microsoft.Win32;

namespace DevHub.Core.Services;

/// <summary>
/// Reads and writes environment variables directly in the registry (HKCU\Environment and the
/// HKLM Session Manager key). Machine-scope writes from a non-elevated process are batched
/// into a .reg file and applied through one elevated "reg import" (single UAC prompt).
/// Every successful change broadcasts WM_SETTINGCHANGE so running apps pick it up.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvironmentVariableService : IEnvironmentVariableService
{
    private const string UserKeyPath = "Environment";
    private const string MachineKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string MachineKeyFullPath = @"HKEY_LOCAL_MACHINE\" + MachineKeyPath;

    private const int ErrorCancelled = 1223;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool IsElevated { get; } = ComputeIsElevated();

    public IReadOnlyList<EnvVariable> GetVariables(EnvVariableScope scope)
    {
        using var key = OpenKey(scope, writable: false)
            ?? throw new InvalidOperationException($"The {scope} environment registry key is missing.");

        var variables = new List<EnvVariable>();
        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0)
            {
                continue;
            }

            var kind = key.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            {
                continue;
            }

            var value = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
            variables.Add(new EnvVariable(name, value, scope, kind == RegistryValueKind.ExpandString));
        }

        return variables.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task SetVariableAsync(EnvVariableScope scope, string name, string value, CancellationToken cancellationToken = default)
    {
        ValidateName(name);

        if (scope == EnvVariableScope.Machine && !IsElevated)
        {
            await RunElevatedRegImportAsync(
                RegFileWriter.Build(MachineKeyFullPath, new Dictionary<string, string> { [name] = value }),
                cancellationToken);
        }
        else
        {
            using var key = OpenKey(scope, writable: true)
                ?? throw new InvalidOperationException($"The {scope} environment registry key is missing.");

            // Convention used by Windows itself: values referencing %OTHER% are stored expandable.
            key.SetValue(name, value, value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        }

        BroadcastChange();
    }

    public async Task DeleteVariableAsync(EnvVariableScope scope, string name, CancellationToken cancellationToken = default)
    {
        ValidateName(name);

        if (scope == EnvVariableScope.Machine && !IsElevated)
        {
            await RunElevatedRegImportAsync(
                RegFileWriter.Build(MachineKeyFullPath, new Dictionary<string, string>(), [name]),
                cancellationToken);
        }
        else
        {
            using var key = OpenKey(scope, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }

        BroadcastChange();
    }

    public void BroadcastChange()
    {
        // Fire-and-forget with a timeout so one hung window cannot block the app.
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment",
            SmtoAbortIfHung, 5000, out _);
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var backup = new EnvVariableBackup
        {
            ExportedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            User = GetVariables(EnvVariableScope.User).ToDictionary(v => v.Name, v => v.Value),
            System = GetVariables(EnvVariableScope.Machine).ToDictionary(v => v.Name, v => v.Value),
        };

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, cancellationToken);
    }

    public async Task<EnvVariableBackup> ReadBackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<EnvVariableBackup>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The file does not contain a DevHub environment backup.");
    }

    public async Task<int> RestoreAsync(EnvVariableBackup backup, bool restoreUser, bool restoreSystem, CancellationToken cancellationToken = default)
    {
        var written = 0;

        if (restoreUser && backup.User.Count > 0)
        {
            using var key = OpenKey(EnvVariableScope.User, writable: true)
                ?? throw new InvalidOperationException("The user environment registry key is missing.");
            foreach (var (name, value) in backup.User)
            {
                key.SetValue(name, value, value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                written++;
            }
        }

        if (restoreSystem && backup.System.Count > 0)
        {
            if (IsElevated)
            {
                using var key = OpenKey(EnvVariableScope.Machine, writable: true)
                    ?? throw new InvalidOperationException("The system environment registry key is missing.");
                foreach (var (name, value) in backup.System)
                {
                    key.SetValue(name, value, value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                }
            }
            else
            {
                await RunElevatedRegImportAsync(RegFileWriter.Build(MachineKeyFullPath, backup.System), cancellationToken);
            }

            written += backup.System.Count;
        }

        if (written > 0)
        {
            BroadcastChange();
        }

        return written;
    }

    private static RegistryKey? OpenKey(EnvVariableScope scope, bool writable) =>
        scope == EnvVariableScope.User
            ? Registry.CurrentUser.OpenSubKey(UserKeyPath, writable)
            : Registry.LocalMachine.OpenSubKey(MachineKeyPath, writable);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('='))
        {
            throw new ArgumentException("Variable names must be non-empty and cannot contain '='.", nameof(name));
        }
    }

    /// <summary>Applies a .reg script with an elevated "reg import" — a single UAC prompt.</summary>
    private static async Task RunElevatedRegImportAsync(string regContent, CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"devhub-env-{Guid.NewGuid():N}.reg");

        // reg.exe reads .reg files as UTF-16LE ("Version 5.00" header implies Unicode).
        await File.WriteAllTextAsync(scriptPath, regContent, System.Text.Encoding.Unicode, cancellationToken);
        try
        {
            Process process;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"import \"{scriptPath}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                }) ?? throw new InvalidOperationException("reg.exe did not start.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                throw new OperationCanceledException("The elevation prompt was declined.", ex);
            }

            using (process)
            {
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"reg import failed with exit code {process.ExitCode}.");
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch (IOException)
            {
                // Best effort; the temp file is harmless if it lingers.
            }
        }
    }

    private static bool ComputeIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static readonly IntPtr HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
