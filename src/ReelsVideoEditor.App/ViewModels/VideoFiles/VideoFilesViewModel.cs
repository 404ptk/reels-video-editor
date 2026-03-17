using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;

namespace ReelsVideoEditor.App.ViewModels.VideoFiles;

public sealed class VideoFilesViewModel : ViewModelBase
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v"
    };

    public string Title { get; } = "Explorer";

    public string DropHintTitle { get; } = "Drop files here";

    public string DropHintSubtitle { get; } = "Video files will appear in the explorer list";

    public ObservableCollection<VideoFileItem> Files { get; } = [];

    public bool HasFiles => Files.Count > 0;

    public VideoFilesViewModel()
    {
        Files.CollectionChanged += OnFilesChanged;
    }

    public void AddDroppedFiles(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                continue;
            }

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension))
            {
                continue;
            }

            if (ContainsPath(filePath))
            {
                continue;
            }

            Files.Add(new VideoFileItem(Path.GetFileName(filePath), filePath));
        }
    }

    private bool ContainsPath(string path)
    {
        foreach (var file in Files)
        {
            if (string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasFiles));
    }
}

public sealed record VideoFileItem(string Name, string Path);
