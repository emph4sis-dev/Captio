using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Captio.Core.Startup;

/// <summary>
/// Starting Captio when the user signs in. An unpackaged build uses the per-user Run
/// key; a packaged build could swap in a StartupTask-based implementation.
/// </summary>
public interface ILaunchAtLogin
{
    /// <summary>The real state in the system, including changes made outside Captio.</summary>
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>Per-user startup entries; no administrator rights involved.</summary>
public interface IStartupRegistry
{
    string? GetCommand(string name);

    void SetCommand(string name, string command);

    void DeleteCommand(string name);

    /// <summary>The flag Windows writes when the user disables the entry in Task Manager or Settings.</summary>
    byte[]? GetApproval(string name);

    void DeleteApproval(string name);
}

public static class StartupCommand
{
    public const string AutostartArgument = "--autostart";

    public static string Build(string executablePath, string arguments)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("The executable path must be absolute.", nameof(executablePath));
        if (executablePath.Contains('"')) throw new ArgumentException("The executable path cannot contain quotes.", nameof(executablePath));
        return $"\"{executablePath}\" {arguments}".TrimEnd();
    }

    /// <summary>Extracts the executable from a Run command, quoted or not.</summary>
    public static string? ParseExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : command.Split(' ')[0];
    }
}

public sealed class RunKeyLaunchAtLogin(IStartupRegistry registry, string executablePath, string valueName = "Captio") : ILaunchAtLogin
{
    public bool IsEnabled
    {
        get
        {
            var executable = StartupCommand.ParseExecutable(registry.GetCommand(valueName));
            if (executable is null || !string.Equals(executable, executablePath, StringComparison.OrdinalIgnoreCase)) return false;

            // Odd first byte = disabled by the user in Windows.
            var approval = registry.GetApproval(valueName);
            return approval is not { Length: > 0 } || (approval[0] & 1) == 0;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            registry.SetCommand(valueName, StartupCommand.Build(executablePath, StartupCommand.AutostartArgument));
            registry.DeleteApproval(valueName);
        }
        else
        {
            registry.DeleteCommand(valueName);
            registry.DeleteApproval(valueName);
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistry(
    string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string approvalKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run") : IStartupRegistry
{
    public string? GetCommand(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(name) as string;
    }

    public void SetCommand(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteCommand(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetApproval(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(approvalKeyPath);
        return key?.GetValue(name) as byte[];
    }

    public void DeleteApproval(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(approvalKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
