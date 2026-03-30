# Reels Video Editor

Desktop short-form video editor (reels/shorts) built with `C#`, `.NET 8`, and `Avalonia UI`.

![Reels Video Editor - UI](readme_images/homepage.png)

![Reels Video Editor - Text Editor](readme_images/text-editor.png)

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
- text clip editing (content, font, color, size),
- draggable text presets from Text panel to timeline,
- custom text presets with local persistence per user machine,
- custom preset management (save, rename/update, delete with confirmation),
- accurate `mp4` export (`libx264` + `aac`) with progress reporting,
- output format (`9:16` / `16:9`) and resolution selection.

## Export Pipeline

- The app currently exports through the accurate pipeline (`ExportAccurateAsync`).
- Video is rendered frame-by-frame (30 FPS) from timeline preview layers, including text overlays.
- Audio clips are mixed in a separate pass (`amix`) and encoded as `aac`.
- Final output is muxed to `mp4` with `+faststart`.
- A legacy filter-graph export path still exists in code (`ExportAsync`) but is not the default path used by UI export.

### TODO

- effects panel,
- watermark panel.

## Text Presets

- Built-in presets are available out of the box (`Sunset`, `Ocean`, `Mint`).
- Users can create and manage their own presets from the Text editor.
- Custom presets are stored locally in `LocalApplicationData/ReelsVideoEditor/text-presets.json`.
- Presets are machine-local by design (a fresh setup on another computer starts clean).

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

### Mid Term

- full effects panel with presets,
- watermark tools (positioning, styling, animations),
- improved multi-clip composition model in preview.

### Long Term

- export videos for social media platforms,
- automated tests for core modules (timeline/export).

## Status

The project is actively developed and currently focused on preview quality and effects panel implementation.
