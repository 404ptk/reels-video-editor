using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace ReelsVideoEditor.App.ViewModels;

public sealed partial class ProjectBrowserViewModel : ViewModelBase
{
    public Action? OnNewProjectRequested { get; set; }

    [RelayCommand]
    private void NewProject()
    {
        OnNewProjectRequested?.Invoke();
    }
}
