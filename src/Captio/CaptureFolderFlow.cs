using System.Windows;
using System.Windows.Input;
using Captio.Core.Library;
using Captio.Core.Storage;
using Microsoft.Win32;

namespace Captio;

/// <summary>
/// Changing the capture folder: native picker, validation, and an optional,
/// confirmed move of the captures already saved.
/// </summary>
internal static class CaptureFolderFlow
{
    /// <returns>true when the capture folder changed.</returns>
    public static bool Choose(AppServices services, Window owner)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Elegir la carpeta de capturas",
            InitialDirectory = services.CurrentFolder().Path ?? "",
        };
        if (dialog.ShowDialog(owner) != true) return false;

        // Picking the Windows Screenshots folder itself means "follow Windows".
        var chosen = dialog.FolderName;
        var configured = CaptureFolderResolver.SamePath(chosen, services.Folders.DefaultFolder) ? null : chosen;
        return Apply(services, owner, configured);
    }

    /// <summary>Back to the Windows Screenshots known folder.</summary>
    public static bool Reset(AppServices services, Window owner) => Apply(services, owner, null);

    private static bool Apply(AppServices services, Window owner, string? configured)
    {
        var target = services.Folders.Resolve(configured, ensureReady: true);
        if (!target.Ok)
        {
            MessageBox.Show(owner, target.Describe() + "\n\nLa carpeta actual no ha cambiado.", "Carpeta de capturas",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var current = services.CurrentFolder();
        if (CaptureFolderResolver.SamePath(current.Path, target.Path))
        {
            if (configured != services.Settings.CaptureFolder) services.UpdateSettings(s => s with { CaptureFolder = configured });
            return true;
        }

        var count = current.Path is null ? 0 : LibraryMigrator.CountCaptures(current.Path);
        if (count > 0)
        {
            var answer = MessageBox.Show(owner,
                $"Hay {count} {(count == 1 ? "captura" : "capturas")} en\n{current.Path}\n\n" +
                $"¿Quieres moverlas a\n{target.Path}?\n\n" +
                "Sí: se copian, se verifican y solo después se borran del origen.\n" +
                "No: se quedan donde están y la biblioteca mostrará solo la carpeta nueva.",
                "Mover capturas", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes) return Move(services, owner, current.Path!, target.Path!, configured);
        }

        services.UpdateSettings(s => s with { CaptureFolder = configured });
        return true;
    }

    private static bool Move(AppServices services, Window owner, string source, string destination, string? configured)
    {
        MigrationResult result;
        var previousCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            // The setting switches only after every file is verified, before sources are removed.
            result = services.Migrator.Migrate(source, destination,
                onVerified: () => services.UpdateSettings(s => s with { CaptureFolder = configured }));
        }
        finally
        {
            Mouse.OverrideCursor = previousCursor;
        }

        if (result is MigrationFailed failed)
        {
            MessageBox.Show(owner,
                $"No se movió ninguna captura: {failed.Reason}\n\nTus capturas siguen en\n{source}\ny la carpeta no ha cambiado.",
                "Mover capturas", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        var moved = ((MigrationSucceeded)result).Moved;
        services.Notify(moved == 1 ? "Se movió 1 captura a la nueva carpeta." : $"Se movieron {moved} capturas a la nueva carpeta.");
        return true;
    }
}
