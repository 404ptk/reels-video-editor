# Reels Video Editor

Desktop short-form video editor (reels/shorts) built with `C#`, `.NET 8`, and `Avalonia UI`.

![Reels Video Editor - UI](readme_images/homepage.png)

## About the Project

`Reels Video Editor` is a desktop app focused on fast editing for vertical formats (`9:16`) with a straightforward workflow:

1. import clips,
2. arrange them on the timeline,
3. preview the result,
4. export to `mp4` via `FFmpeg`.

The project is developed iteratively — some modules are already production-ready, while others are intentional placeholders for upcoming stages.

## Current Features

### Available Now

- video import via drag & drop into Explorer,
- clip duration detection (`ffprobe`),
- thumbnail generation (`ffmpeg`),
- adding clips to the timeline with automatic linked audio,
- timeline zoom + lane height adjustment,
- playhead scrubbing and preview synchronization,
- preview play/pause/stop,
- audio mute / video hide in timeline context,
- `mp4` export (`libx264` + `aac`) with progress reporting,
- output format (`9:16` / `16:9`) and resolution selection.

### TODO

- effects panel,
- text panel,
- watermark panel.

## Architecture

The project follows **MVVM** with a clear separation of responsibilities.

### Layers

- `Views/` — UI layer (`.axaml` + code-behind),
- `ViewModels/` — state and interaction logic,
- `Services/` — integration/process logic (e.g. `FFmpeg` export),
- `Effects/` — dedicated place for video effect logic,
- `DragDrop/` — drag-and-drop contracts and payloads,
- `ViewModels/Timeline/Arrangement` — clip arrangement and timeline layout logic.

### Key Flows

- `MainWindowViewModel` composes modules and orchestrates `Timeline ↔ Preview ↔ Export` communication.
- `TimelineViewModel` manages clips, ticks, zoom, and playhead events.
- `PreviewViewModel` stores playback state and preview transforms.
- `TimelineExportService` builds filter graphs and runs export via `ffmpeg`.

## Repository Structure

- `ReelsVideoEditor.sln` — solution,
- `global.json` — pinned SDK version,
- `src/ReelsVideoEditor.App/` — desktop application,
- `src/ReelsVideoEditor.App/Services/Export/` — export pipeline,
- `src/ReelsVideoEditor.App/ViewModels/` — MVVM logic,
- `readme_images/` — documentation assets.

## Requirements

- `.NET SDK 8.x`,
- `ffmpeg` and `ffprobe` available in `PATH` **or** provided as local `ffmpeg.exe` / `ffprobe.exe` files.

## Run

```powershell
Set-Location ".\reels-video-editor"
dotnet restore
dotnet run --project .\src\ReelsVideoEditor.App\ReelsVideoEditor.App.csproj
```

## Roadmap

### Short Term

- finalize crop support in the rendering pipeline,

### Mid Term

- full effects panel with presets,
- text and watermark tools (positioning, styling, animations),
- improved multi-clip composition model in preview.

### Long Term

- export videos for social media platforms,
- automated tests for core modules (timeline/export).

## Status

The project is actively developed and currently focused on intensive preview and effects refinement.
