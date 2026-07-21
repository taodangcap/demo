using ShowCuePlayer.Models;
using System.Text.Json;

namespace ShowCuePlayer.Services;

/// <summary>Contract for saving/loading .7zyx project files.</summary>
public interface IProjectService
{
    ProjectModel Current { get; }
    Task<ProjectModel> NewProjectAsync();
    Task<ProjectModel?> OpenAsync(string filePath);
    Task SaveAsync(string? filePath = null);
    Task SaveAsAsync(string filePath);
    string? CurrentFilePath { get; }
    event EventHandler ProjectChanged;
}

public sealed class ProjectService : IProjectService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProjectModel Current { get; private set; } = new();
    public string? CurrentFilePath { get; private set; }
    public event EventHandler? ProjectChanged;

    public Task<ProjectModel> NewProjectAsync()
    {
        Current = new ProjectModel { Name = "Untitled Project" };
        CurrentFilePath = null;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(Current);
    }

    public async Task<ProjectModel?> OpenAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var json = await File.ReadAllTextAsync(filePath);
        var project = JsonSerializer.Deserialize<ProjectModel>(json, JsonOptions);
        if (project is null) return null;
        project.FilePath = filePath;
        Current = project;
        CurrentFilePath = filePath;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public async Task SaveAsync(string? filePath = null)
    {
        var path = filePath ?? CurrentFilePath;
        if (string.IsNullOrEmpty(path)) return;
        await SaveAsAsync(path);
    }

    public async Task SaveAsAsync(string filePath)
    {
        await _saveLock.WaitAsync();
        try
        {
        Current.DateModified = DateTime.UtcNow;
        Current.FilePath = filePath;
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Project path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        CurrentFilePath = filePath;
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
