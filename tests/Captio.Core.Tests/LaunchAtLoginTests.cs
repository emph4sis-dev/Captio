using Captio.Core.Startup;
using Microsoft.Win32;

namespace Captio.Core.Tests;

public class LaunchAtLoginTests
{
    private const string Exe = @"C:\Program Files\Captio Test\Captio.exe";

    private sealed class FakeRegistry : IStartupRegistry
    {
        public Dictionary<string, string> Commands { get; } = [];
        public Dictionary<string, byte[]> Approvals { get; } = [];

        public string? GetCommand(string name) => Commands.GetValueOrDefault(name);
        public void SetCommand(string name, string command) => Commands[name] = command;
        public void DeleteCommand(string name) => Commands.Remove(name);
        public byte[]? GetApproval(string name) => Approvals.GetValueOrDefault(name);
        public void DeleteApproval(string name) => Approvals.Remove(name);
    }

    [Fact]
    public void Disabled_by_default()
    {
        Assert.False(new RunKeyLaunchAtLogin(new FakeRegistry(), Exe).IsEnabled);
    }

    [Fact]
    public void Enable_writes_quoted_path_with_autostart_flag_and_disable_removes_it()
    {
        var registry = new FakeRegistry();
        var launch = new RunKeyLaunchAtLogin(registry, Exe);

        launch.SetEnabled(true);
        Assert.Equal("\"C:\\Program Files\\Captio Test\\Captio.exe\" --autostart", registry.Commands["Captio"]);
        Assert.True(launch.IsEnabled);

        launch.SetEnabled(false);
        Assert.Empty(registry.Commands);
        Assert.False(launch.IsEnabled);
    }

    [Fact]
    public void Entry_removed_outside_captio_reads_as_disabled()
    {
        var registry = new FakeRegistry();
        var launch = new RunKeyLaunchAtLogin(registry, Exe);
        launch.SetEnabled(true);

        registry.Commands.Clear();

        Assert.False(launch.IsEnabled);
    }

    [Fact]
    public void Entry_disabled_in_windows_reads_as_disabled_and_enabling_clears_it()
    {
        var registry = new FakeRegistry();
        var launch = new RunKeyLaunchAtLogin(registry, Exe);
        launch.SetEnabled(true);

        registry.Approvals["Captio"] = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.False(launch.IsEnabled);

        registry.Approvals["Captio"] = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.True(launch.IsEnabled);

        registry.Approvals["Captio"] = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        launch.SetEnabled(true);
        Assert.True(launch.IsEnabled);
    }

    [Fact]
    public void Entry_for_another_executable_is_not_ours()
    {
        var registry = new FakeRegistry();
        registry.Commands["Captio"] = "\"D:\\Viejo\\Captio.exe\" --autostart";

        Assert.False(new RunKeyLaunchAtLogin(registry, Exe).IsEnabled);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Captio Test\\Captio.exe\" --autostart", @"C:\Program Files\Captio Test\Captio.exe")]
    [InlineData("C:\\Program Files\\Captio Test\\Captio.exe --autostart", @"C:\Program Files\Captio Test\Captio.exe")]
    [InlineData("C:\\Captio\\Captio.exe", @"C:\Captio\Captio.exe")]
    [InlineData("", null)]
    public void Parses_executable_from_commands(string command, string? expected)
    {
        Assert.Equal(expected, StartupCommand.ParseExecutable(command));
    }

    [Fact]
    public void Rejects_relative_or_quoted_executable_paths()
    {
        Assert.Throws<ArgumentException>(() => StartupCommand.Build(@"bin\Captio.exe", ""));
        Assert.Throws<ArgumentException>(() => StartupCommand.Build("C:\\a\"b\\Captio.exe", ""));
    }

    [Fact]
    public void Real_registry_round_trip_in_an_isolated_test_key()
    {
        // Never touches the user's real Run key.
        var basePath = $@"Software\CaptioTests\{Guid.NewGuid():N}";
        var registry = new WindowsStartupRegistry($@"{basePath}\Run", $@"{basePath}\Approved");
        var launch = new RunKeyLaunchAtLogin(registry, Exe);
        try
        {
            Assert.False(launch.IsEnabled);
            launch.SetEnabled(true);
            Assert.True(launch.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey($@"{basePath}\Run"))
                Assert.Equal("\"C:\\Program Files\\Captio Test\\Captio.exe\" --autostart", key!.GetValue("Captio"));

            launch.SetEnabled(false);
            Assert.False(launch.IsEnabled);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(basePath, throwOnMissingSubKey: false);
            using var parent = Registry.CurrentUser.OpenSubKey(@"Software\CaptioTests");
            if (parent is { SubKeyCount: 0, ValueCount: 0 }) Registry.CurrentUser.DeleteSubKey(@"Software\CaptioTests", throwOnMissingSubKey: false);
        }
    }
}
