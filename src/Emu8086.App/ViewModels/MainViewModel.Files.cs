using System.IO;
using Emu8086.App.Services;

namespace Emu8086.App.ViewModels;

public sealed partial class MainViewModel
{
    private const string FirstProjectBaseName = "HelloWorld";
    private const string FirstProjectTemplate = "hello_com";

    /// <summary>Called once the window is shown: opens the last project or creates the first-run sample.</summary>
    public void Startup(string[] args)
    {
        var settings = SettingsService.Current;
        string? toOpen = args.FirstOrDefault(File.Exists);
        if (toOpen != null)
        {
            if (toOpen.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase)) OpenProjectPath(toOpen);
            else OpenDocument(toOpen);
            return;
        }

        if (!settings.FirstRunDone)
        {
            settings.FirstRunDone = true;
            CreateFirstRunProject();
            SettingsService.Save();
            return;
        }
        if (settings.LastProject != null && File.Exists(settings.LastProject)) OpenProjectPath(settings.LastProject);
    }

    private void CreateFirstRunProject()
    {
        var template = ProjectService.Templates().FirstOrDefault(t => t.Id == FirstProjectTemplate);
        if (template == null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.DefaultProjectsDirectory);
            string name = ProjectService.UniqueName(AppPaths.DefaultProjectsDirectory, FirstProjectBaseName);
            var project = ProjectService.Create(name, AppPaths.DefaultProjectsDirectory, template);
            OpenProjectPath(project.FilePath);
            Log(Loc.Instance.Format("output.firstRun", project.Directory), OutputKind.Info);
        }
        catch (IOException e)
        {
            Log(e.Message, OutputKind.Error);
        }
    }

    private void NewProject()
    {
        var request = _dialogs.AskNewProject();
        if (request == null) return;
        try
        {
            var project = ProjectService.Create(request.Name, request.ParentDirectory, request.Template);
            OpenProjectPath(project.FilePath);
            Log(Loc.Instance.Format("output.projectCreated", project.Name, project.Directory), OutputKind.Success);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(e.Message);
        }
    }

    private void OpenProject()
    {
        string? path = _dialogs.PickProjectFile();
        if (path != null) OpenProjectPath(path);
    }

    public void OpenProjectPath(string path)
    {
        if (!ConfirmCloseAll()) return;
        var project = ProjectService.Open(path);
        if (project == null)
        {
            _dialogs.ShowMessage(Loc.Instance.Format("error.openProject", path));
            return;
        }
        Session.Stop();
        Documents.Clear();
        Project = project;
        RefreshProjectFiles();
        SettingsService.AddRecent(path);
        OnPropertyChanged(nameof(RecentProjects));
        if (File.Exists(project.MainFilePath)) OpenDocument(project.MainFilePath);
    }

    public void RefreshProjectFiles()
    {
        ProjectFiles.Clear();
        if (Project == null) return;
        foreach (var file in Project.SourceFiles)
        {
            bool isMain = string.Equals(file, Project.MainFilePath, StringComparison.OrdinalIgnoreCase);
            ProjectFiles.Add(new ProjectFileItem(Path.GetRelativePath(Project.Directory, file), file, isMain));
        }
    }

    private void SetMainFile(ProjectFileItem? item)
    {
        if (Project == null || item == null) return;
        Project.MainFile = item.Name;
        ProjectService.Save(Project);
        RefreshProjectFiles();
    }

    private void OpenFile()
    {
        string? path = _dialogs.PickSourceFile();
        if (path != null) OpenDocument(path);
    }

    private void NewFile()
    {
        if (Project == null) return;
        string? name = _dialogs.AskText(Loc.Instance["newFile.title"], Loc.Instance["newFile.prompt"], "file.asm");
        if (string.IsNullOrWhiteSpace(name) || !ProjectService.IsValidName(name)) return;
        if (!Path.HasExtension(name)) name += ".asm";
        string path = Path.Combine(Project.Directory, name);
        if (!File.Exists(path)) File.WriteAllText(path, "");
        RefreshProjectFiles();
        OpenDocument(path);
    }

    private void OpenExample(string path)
    {
        // Examples open as copies inside the project folder (or Documents) so edits never touch the originals.
        string targetDir = Project?.Directory ?? Path.Combine(AppPaths.DocumentsDirectory, "Examples");
        Directory.CreateDirectory(targetDir);
        string target = Path.Combine(targetDir, Path.GetFileName(path));
        if (!File.Exists(target)) File.Copy(path, target);
        RefreshProjectFiles();
        OpenDocument(target);
    }

    public DocumentViewModel? OpenDocument(string path)
    {
        var existing = Documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectedDocument = existing;
            return existing;
        }
        try
        {
            var doc = new DocumentViewModel(path, File.ReadAllText(path));
            doc.Explain = line => ExplainLine(doc, line);
            doc.BreakpointsChanged += () => SyncBreakpoints(doc);
            Documents.Add(doc);
            SelectedDocument = doc;
            return doc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(e.Message);
            return null;
        }
    }

    private bool SaveDocument(DocumentViewModel? doc)
    {
        if (doc == null) return true;
        try
        {
            doc.Save();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage(e.Message);
            return false;
        }
    }

    public bool SaveAll() => Documents.Where(d => d.IsDirty).All(SaveDocument);

    private bool ConfirmClose(DocumentViewModel doc)
    {
        if (!doc.IsDirty) return true;
        return _dialogs.Confirm(Loc.Instance.Format("confirm.save", Path.GetFileName(doc.FilePath))) switch
        {
            ConfirmResult.Yes => SaveDocument(doc),
            ConfirmResult.No => true,
            _ => false,
        };
    }

    public bool ConfirmCloseAll() => Documents.ToList().All(ConfirmClose);

    private void CloseDocument(DocumentViewModel? doc)
    {
        if (doc == null || !ConfirmClose(doc)) return;
        int index = Documents.IndexOf(doc);
        Documents.Remove(doc);
        if (doc == _buildDocument) _buildDocument = null;
        SelectedDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
    }
}
