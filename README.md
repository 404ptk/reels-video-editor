# Reels Video Editor

Starter desktop app in `.NET 8` + `Avalonia UI` for building a short-form video editor.

## Why this stack

- `Avalonia UI` gives a desktop UI that works well in VS Code.
- `dotnet watch run` gives a quick edit-refresh loop during development.
- `FFmpeg` is already installed on this machine, so we can later build export and transform pipelines without extra setup.

## Current status

- Stable SDK pinned with `global.json`
- Avalonia templates installed
- VS Code Avalonia extension installed
- Starter MVVM desktop app created
- Ready for next step: import video, preview pipeline, export job

## Project structure

- `ReelsVideoEditor.sln` — solution
- `src/ReelsVideoEditor.App` — desktop app shell
- `.vscode/tasks.json` — quick run/watch tasks

## Run

```powershell
cd c:\Users\PC\Documents\GITHUB\reels-video-editor

dotnet restore

dotnet run --project .\src\ReelsVideoEditor.App\ReelsVideoEditor.App.csproj
```

## Hot reload / fast iteration

```powershell
cd c:\Users\PC\Documents\GITHUB\reels-video-editor

dotnet watch run --project .\src\ReelsVideoEditor.App\ReelsVideoEditor.App.csproj
```

Notes:
- For code changes, `dotnet watch` applies standard .NET hot reload when supported.
- For XAML work, the installed Avalonia VS Code tooling improves the edit/preview loop.
- We can decide in the next step whether to build a real timeline preview directly in-app, or to use FFmpeg for first export-only iterations.
