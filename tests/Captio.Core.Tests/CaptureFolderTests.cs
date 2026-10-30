using System.Security.AccessControl;
using System.Security.Principal;
using Captio.Core.Settings;
using Captio.Core.Storage;

namespace Captio.Core.Tests;

public sealed class CaptureFolderTests : IDisposable
{
    private readonly string _root = TempDir.Create();

    public void Dispose() => TempDir.Delete(_root);

    /// <summary>Stands in for Windows: returns a fixed path and creates exactly it on request.</summary>
    private sealed class FakeKnownFolders(string? path) : IKnownFolders
    {
        public int CreateRequests { get; private set; }

        public string? GetScreenshotsFolder(bool create)
        {
            if (path is null) return null;
            if (create)
            {
                CreateRequests++;
                Directory.CreateDirectory(path);
            }
            return path;
        }
    }

    [Fact]
    public void Windows_resolves_the_screenshots_known_folder()
    {
        var path = new WindowsKnownFolders().GetScreenshotsFolder(create: false);

        Assert.NotNull(path);
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.DoesNotContain(@"\Captio", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_uses_the_known_folder_exactly_as_windows_reports_it()
    {
        // Pictures redirected into OneDrive, localized name and all.
        var redirected = Path.Combine(_root, "OneDrive", "Imágenes", "Capturas de pantalla");
        var resolver = new CaptureFolderResolver(new FakeKnownFolders(redirected));

        var check = resolver.Resolve(configured: null, ensureReady: true);

        Assert.True(check.Ok);
        Assert.Equal(redirected, check.Path);
    }

    [Fact]
    public void Fresh_install_creates_only_the_native_folder_and_no_captio_folder()
    {
        var pictures = Path.Combine(_root, "Pictures");
        var screenshots = Path.Combine(pictures, "Screenshots");
        var known = new FakeKnownFolders(screenshots);
        var resolver = new CaptureFolderResolver(known);

        var peek = resolver.Resolve(null, ensureReady: false);
        Assert.Equal(screenshots, peek.Path);
        Assert.False(Directory.Exists(pictures)); // looking does not create anything

        var ready = resolver.Resolve(null, ensureReady: true);

        Assert.True(ready.Ok);
        Assert.Equal(1, known.CreateRequests);
        Assert.Equal(["Screenshots"], Directory.GetDirectories(pictures).Select(Path.GetFileName));
        Assert.False(Directory.Exists(Path.Combine(pictures, "Captio")));
    }

    [Fact]
    public void Unresolvable_known_folder_is_reported_without_fallback()
    {
        var check = new CaptureFolderResolver(new FakeKnownFolders(null)).Resolve(null, ensureReady: true);

        Assert.Equal(FolderProblem.Unresolvable, check.Problem);
        Assert.Null(check.Path);
    }

    [Fact]
    public void Custom_folder_is_used_and_never_created()
    {
        var custom = Path.Combine(_root, "Mis capturas");
        var resolver = new CaptureFolderResolver(new FakeKnownFolders(Path.Combine(_root, "Screenshots")));

        Assert.Equal(FolderProblem.NotFound, resolver.Resolve(custom, ensureReady: true).Problem);
        Assert.False(Directory.Exists(custom));

        Directory.CreateDirectory(custom);
        var check = resolver.Resolve(custom, ensureReady: true);
        Assert.True(check.Ok);
        Assert.Equal(custom, check.Path);
    }

    [Fact]
    public void Relative_paths_are_rejected()
    {
        Assert.Equal(FolderProblem.NotAbsolute, CaptureFolderResolver.Validate(@"capturas\nuevas").Problem);
        Assert.Null(AppSettings.NormalizeFolder(@"capturas\nuevas"));
        Assert.Null(AppSettings.NormalizeFolder(@"\sin\unidad"));
    }

    [Fact]
    public void Folder_without_write_permission_is_rejected()
    {
        var locked = Path.Combine(_root, "solo-lectura");
        var info = Directory.CreateDirectory(locked);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        try
        {
            var check = CaptureFolderResolver.Validate(locked);

            Assert.Equal(FolderProblem.NotWritable, check.Problem);
            Assert.Empty(Directory.EnumerateFileSystemEntries(locked));
        }
        finally
        {
            acl.RemoveAccessRule(rule);
            info.SetAccessControl(acl);
        }
    }

    [Fact]
    public void Write_check_leaves_no_file_behind()
    {
        Assert.True(CaptureFolderResolver.Validate(_root).Ok);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void Configured_folder_persists_and_reset_returns_to_known_folder()
    {
        var settingsPath = Path.Combine(_root, "cfg", "settings.json");
        var custom = Directory.CreateDirectory(Path.Combine(_root, "Capturas")).FullName;
        var known = Path.Combine(_root, "Screenshots");
        var store = new SettingsStore(settingsPath);
        var resolver = new CaptureFolderResolver(new FakeKnownFolders(known));

        store.Save(new AppSettings { CaptureFolder = custom });
        Assert.Equal(custom, resolver.Resolve(store.Load().CaptureFolder, ensureReady: false).Path);

        store.Save(store.Load() with { CaptureFolder = null });
        Assert.Null(store.Load().CaptureFolder);
        Assert.Equal(known, resolver.Resolve(store.Load().CaptureFolder, ensureReady: false).Path);
    }

    [Fact]
    public void Same_path_ignores_case_and_trailing_separator()
    {
        Assert.True(CaptureFolderResolver.SamePath(@"C:\Fotos\Capturas\", @"c:\fotos\capturas"));
        Assert.False(CaptureFolderResolver.SamePath(@"C:\Fotos", null));
    }
}
