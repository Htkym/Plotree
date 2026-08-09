using Plotree.Models;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Plotree.Services;

/// <summary>Load/save .plotree files and show open/save pickers.</summary>
public class ProjectFileService
{
    public const string FileExtension = ".plotree";

    /// <summary>Shows an open picker. Returns the chosen path, or null when cancelled.</summary>
    public async Task<string?> PickOpenAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(FileExtension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>Shows a save picker. Returns the chosen path, or null when cancelled.</summary>
    public async Task<string?> PickSaveAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(Loc.Get("Picker_ProjectType"), [FileExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>Shows a save picker for an export format. Returns the chosen path, or null when cancelled.</summary>
    public async Task<string?> PickExportAsync(string suggestedName, string typeDescription, string extension)
    {
        var file = await PickExportFileAsync(suggestedName, typeDescription, extension);
        return file?.Path;
    }

    /// <summary>
    /// Shows a save picker for an export format and preserves the picker's file access grant.
    /// </summary>
    public async Task<StorageFile?> PickExportFileAsync(
        string suggestedName,
        string typeDescription,
        string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(typeDescription, [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        return await picker.PickSaveFileAsync();
    }

    public async Task<PlotProject> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return ProjectSerializer.Deserialize(json);
    }

    public async Task SaveAsync(PlotProject project, string path)
    {
        var json = ProjectSerializer.Serialize(project);
        await File.WriteAllTextAsync(path, json);
    }
}
