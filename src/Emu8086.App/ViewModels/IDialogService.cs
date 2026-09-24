using Emu8086.App.Services;

namespace Emu8086.App.ViewModels;

public enum ConfirmResult { Yes, No, Cancel }

public sealed record NewProjectRequest(string Name, string ParentDirectory, ProjectTemplate Template);

/// <summary>Window-level interactions the view model needs (implemented by the main window).</summary>
public interface IDialogService
{
    string? PickProjectFile();
    string? PickSourceFile();
    string? PickSaveFile(string defaultName, string extension);
    NewProjectRequest? AskNewProject();
    string? AskText(string title, string prompt, string initial);
    ConfirmResult Confirm(string message);
    void ShowMessage(string message);
    void ShowAbout();
}
