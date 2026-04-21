using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Controls;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Markup.Xaml;
using ReelsVideoEditor.App.ViewModels;
using ReelsVideoEditor.App.Views;

namespace ReelsVideoEditor.App;

public partial class App : Application
{
    private const string AppDirectoryName = "ReelsVideoEditor";
    private const string WindowStateFileName = "window-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            RestoreMainWindowLaunchState(mainWindow);
            AttachMainWindowLaunchStatePersistence(mainWindow);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static void RestoreMainWindowLaunchState(Window window)
    {
        var savedState = LoadMainWindowLaunchState();
        if (savedState is null)
        {
            window.WindowState = WindowState.Maximized;
            return;
        }

        var normalizedWidth = Math.Clamp(savedState.Width, 960, 10000);
        var normalizedHeight = Math.Clamp(savedState.Height, 640, 10000);

        window.Width = normalizedWidth;
        window.Height = normalizedHeight;

        if (savedState.HasPosition)
        {
            window.Position = new PixelPoint(savedState.X, savedState.Y);
        }

        window.WindowState = savedState.WindowState switch
        {
            WindowState.Minimized => WindowState.Normal,
            WindowState.FullScreen => WindowState.Maximized,
            _ => savedState.WindowState
        };
    }

    private static void AttachMainWindowLaunchStatePersistence(Window window)
    {
        PixelRect normalBounds = default;

        void CaptureNormalBounds()
        {
            if (window.WindowState != WindowState.Normal)
            {
                return;
            }

            var width = Math.Max(960, (int)Math.Round(window.Bounds.Width));
            var height = Math.Max(640, (int)Math.Round(window.Bounds.Height));
            normalBounds = new PixelRect(window.Position.X, window.Position.Y, width, height);
        }

        window.Opened += (_, _) => CaptureNormalBounds();
        window.PositionChanged += (_, _) => CaptureNormalBounds();
        window.SizeChanged += (_, _) => CaptureNormalBounds();
        window.PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.WindowStateProperty)
            {
                CaptureNormalBounds();
            }
        };

        window.Closing += (_, _) =>
        {
            CaptureNormalBounds();

            var stateToSave = window.WindowState;
            if (stateToSave == WindowState.Minimized)
            {
                stateToSave = WindowState.Normal;
            }

            var fallbackWidth = Math.Max(960, (int)Math.Round(window.Bounds.Width));
            var fallbackHeight = Math.Max(640, (int)Math.Round(window.Bounds.Height));
            var width = normalBounds.Width > 0 ? normalBounds.Width : fallbackWidth;
            var height = normalBounds.Height > 0 ? normalBounds.Height : fallbackHeight;
            var x = normalBounds.Width > 0 ? normalBounds.X : window.Position.X;
            var y = normalBounds.Height > 0 ? normalBounds.Y : window.Position.Y;

            SaveMainWindowLaunchState(new MainWindowLaunchState(stateToSave, x, y, width, height, true));
        };
    }

    private static MainWindowLaunchState? LoadMainWindowLaunchState()
    {
        try
        {
            var path = ResolveWindowStatePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var payload = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<MainWindowLaunchState>(payload, JsonOptions);
            if (state is null)
            {
                return null;
            }

            if (state.Width <= 0 || state.Height <= 0)
            {
                return null;
            }

            return state;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveMainWindowLaunchState(MainWindowLaunchState state)
    {
        try
        {
            var path = ResolveWindowStatePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(path, payload);
        }
        catch
        {
        }
    }

    private static string ResolveWindowStatePath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, AppDirectoryName, WindowStateFileName);
    }

    private sealed record MainWindowLaunchState(
        WindowState WindowState,
        int X,
        int Y,
        int Width,
        int Height,
        bool HasPosition);
}