# CLAUDE.md - CornieKit Looper

## Project Overview

**CornieKit Looper** is a Windows desktop video player built with .NET 8.0 WPF that enables users to mark and loop specific segments of videos. Primary use cases include music practice, dance learning, and video review.

**Version**: 1.0.0
**Repository**: https://github.com/billye220670/CornieKit_Looper
**Language**: C# (.NET 8.0)
**Platform**: Windows 10/11

## Quick Start for AI Assistants

### Key User Workflows
1. **Open video** → Auto-plays immediately
2. **Hold R while playing** → Records segment (min 200ms)
3. **Release R** → Auto-loops the newly created segment
4. **Double-click segment** → Loop that segment only
5. **Click "Play All Segments"** → Play all with selected loop mode
6. **Click selected segment name → wait 500ms** → Rename mode

### Critical Behavior Flags
- `_playingSingleSegment` (bool): `true` = loop current segment only, `false` = use LoopMode for all segments
- `_isScrubbing` (bool): Prevents position updates during manual slider drag
- `IsEditing` (LoopSegmentViewModel): Controls TextBox vs TextBlock visibility for rename

## Architecture

### Tech Stack
- **UI**: WPF (XAML + code-behind)
- **MVVM**: CommunityToolkit.Mvvm (source generators)
- **Video**: LibVLCSharp 3.9.5 (VLC wrapper)
- **DI**: Microsoft.Extensions.DependencyInjection
- **Persistence**: System.Text.Json

### MVVM Structure
```
ViewModels/
  ├── MainViewModel.cs          # Main window logic, 360 lines
  ├── LoopSegmentViewModel.cs   # Segment list item wrapper
  └── RecentFileItem.cs         # Recent file menu item

Services/
  ├── VideoPlayerService.cs     # LibVLC wrapper, position timer (50ms)
  ├── SegmentManager.cs         # Segment collection, loop mode logic
  ├── DataPersistenceService.cs # JSON save/load *.cornieloop files
  └── RecentFilesService.cs     # Recent files (max 10, %LocalAppData%)

Models/
  ├── LoopSegment.cs            # Segment data (Id, Name, Start/End times)
  ├── VideoMetadata.cs          # Wrapper for segment list + loop mode
  └── LoopMode.cs               # Enum: Single, Sequential, Random, SequentialLoop

Converters/
  ├── BoolToVisibilityConverter.cs
  ├── InverseBoolToVisibilityConverter.cs
  ├── EmptyStringToVisibilityConverter.cs
  └── RecordingBorderConverter.cs
```

### Dependency Injection (App.xaml.cs)
```csharp
services.AddSingleton<VideoPlayerService>();
services.AddSingleton<SegmentManager>();
services.AddSingleton<DataPersistenceService>();
services.AddSingleton<RecentFilesService>();
services.AddSingleton<MainViewModel>();
services.AddSingleton<MainWindow>();
```

## Key Implementation Details

### Video Playback (VideoPlayerService.cs)

**LibVLC Options** (Line 37-43):
```csharp
"--aout=mmdevice",         // Windows Multimedia Device (uses WASAPI)
"--audio-resampler=soxr",  // High-quality SoX Resampler
"--file-caching=300",      // Low latency
"--no-audio-time-stretch"  // Disable time stretching for quality
```

**Audio Quality Notes**:
- `mmdevice` is the correct Windows audio output module (not `wasapi`)
- `soxr` is the highest quality resampler, avoiding the low-quality "ugly" fallback
- Alternative resamplers: `speex_resampler` (faster), `samplerate` (libsamplerate)

**Position Monitoring** (50ms timer):
- Fires `PositionChanged` event
- Checks if playback exceeded segment end time
- Auto-seeks back to segment start if looping

**Seek Method**: Uses `Position` (0.0-1.0 float) instead of `Time` (milliseconds) to avoid LibVLC pause-state deadlock.

### Segment Recording (MainViewModel.cs:189-226)

**R Key Logic**:
```
OnRecordKeyDown():
  - Ignored if !IsPlaying (paused state)
  - Records _recordingStartTime = CurrentTime
  - Sets IsRecording = true

OnRecordKeyUp():
  - Validates duration > 200ms
  - Creates segment via SegmentManager
  - Sets _playingSingleSegment = true
  - Auto-starts looping new segment
```

### Progress Bar Scrubbing (MainWindow.xaml.cs:120-160)

**Problem**: WPF Slider's Thumb captures mouse, preventing track clicks.

**Solution**: Transparent Border overlay with manual mouse capture
```xaml
<Slider IsHitTestVisible="False" />
<Border Background="Transparent"
        MouseLeftButtonDown="SliderOverlay_MouseDown" />
```

**State Management**:
- `_isScrubbing`: Pauses `PositionChanged` updates
- `_wasPlayingBeforeScrub`: Restores play/pause state after scrub
- Throttle: Max one seek per 50ms

### Loop Mode Logic (SegmentManager.cs)

**OnSegmentLoopCompleted** (MainViewModel.cs:292):
```csharp
if (_playingSingleSegment)
    return;  // Don't advance to next segment

var nextSegment = _segmentManager.GetNextSegment();
// Uses LoopMode to determine next segment
```

**LoopMode.GetNextSegment()**:
- `Single`: Returns current segment (already looped by VideoPlayerService)
- `Sequential`: Next in order, stops at end
- `Random`: Random segment
- `SequentialLoop`: Next in order, wraps to first

### Rename Feature (MainWindow.xaml.cs:116-138)

**Windows Explorer-style Click-to-Rename**:
1. User clicks on **already selected** segment name
2. 500ms DispatcherTimer starts
3. Timer fires → Sets `IsEditing = true`
4. TextBox appears via binding, auto-focuses and selects text
5. Enter/LostFocus → Saves, Escape → Cancels

**Double-click cancels timer** to prevent rename during play action.

### Data Persistence (DataPersistenceService.cs)

**File Format**: `{VideoFileName}.cornieloop` (JSON)
```json
{
  "VideoFilePath": "C:\\path\\video.mp4",
  "VideoFileHash": "abc123...",
  "Segments": [...],
  "DefaultLoopMode": "Single"
}
```

**Hash Validation**: Prevents loading segments from different file with same name.

## Known Issues & Limitations

### LibVLC Quirks
1. **Pause-state seek freeze**: Using `mediaPlayer.Time = ...` while paused causes deadlock. **Solution**: Use `Position` property.
2. **Pause() is toggle**: Calling `Pause()` when already paused resumes playback. **Solution**: Check `IsPlaying` state before calling.

### UI Limitations
1. **Hover delete button flicker**: Fixed by binding to `ListViewItem.IsMouseOver` instead of EventTriggers.
2. **Slider thumb-only drag**: Fixed with transparent overlay and `IsHitTestVisible="False"`.

### Features Not Implemented (from DESIGN.md)
- Segment thumbnails
- Export segments to separate files
- Keyboard shortcut customization
- Volume control UI (LibVLC supports it via `SetVolume()`)
- Playback speed control

## Build & Release

### Build Commands
```bash
# Development
dotnet build

# Release (framework-dependent)
dotnet publish -c Release -r win-x64 --self-contained false -o publish

# Zip for release
powershell Compress-Archive -Path 'publish\*' -DestinationPath 'CornieKit_Looper_v1.0.0_win-x64.zip'
```

### Version Bumping
1. Update `<Version>` in `src/CornieKit.Looper/CornieKit.Looper.csproj`
2. Update `MainWindow.xaml.cs` About dialog (Line 108-115)
3. Git tag: `git tag v1.x.x && git push --tags`
4. Create GitHub release with zip

## Important Files

| File | Purpose | Lines |
|------|---------|-------|
| `MainViewModel.cs` | Core app logic | 360 |
| `VideoPlayerService.cs` | LibVLC wrapper | 180 |
| `MainWindow.xaml.cs` | UI event handlers | 260 |
| `MainWindow.xaml` | Main UI layout | 335 |
| `SegmentManager.cs` | Segment collection | ~150 |

## Design Decisions

### Why R instead of Space for recording?
Space conflicts with play/pause. R key is dedicated to recording, Space toggles playback.

### Why auto-play on load?
User workflow: Open video → immediately start playing → mark segments while watching. Auto-play reduces friction.

### Why auto-loop after recording?
User mental model: "I just marked this segment, I want to practice it now." Auto-loop matches intent.

### Why 500ms rename delay?
Mimics Windows Explorer. Prevents accidental rename on selection click, allows double-click to play.

### Why 200ms minimum segment duration?
Filters accidental taps. Real segments are typically >1 second.

### Why Position instead of Time for seek?
LibVLC's `Time` property has threading issues in paused state. `Position` (0.0-1.0) is more stable.

## Development Workflow

### User never builds, only tests
User's workflow: **Edit code → Tell AI → User builds and reports results**

When making changes:
1. **Don't** invoke build commands
2. **Do** tell user "修改完成，你可以构建测试了"
3. **Do** wait for user feedback before next change

### Recent Changes (Session History)
- **2026-02-04**: Fixed audio quality issues by correcting LibVLC audio configuration
  - Replaced invalid `--aout=wasapi` with proper `--aout=mmdevice`
  - Added `--audio-resampler=soxr` to use high-quality resampler
  - Fixes buzzing/crackling audio that sounded like "cheap recorder"
- **2026-02-04**: Added YouTube-style UI enhancements
  - Auto-hiding controls (3s delay when playing, hover to show)
  - Tab key to toggle segment panel visibility
  - Improved keyboard focus handling (Space in TextBox no longer triggers play/pause)
- Changed recording key from Space to R
- Removed play/delete buttons from list items
- Added hover-to-show delete button (×)
- Implemented double-click to play single segment
- Added recent files menu
- Added auto-play on video load
- Added auto-loop after recording segment
- Implemented click-to-rename (Windows Explorer style)

## Troubleshooting Common Issues

### "Video freezes during scrub"
- Check `_isScrubbing` flag is set in `OnScrubStart()`
- Verify throttling in `OnPlaybackPositionChanged()` (50ms)
- Ensure `SeekByPosition()` uses `Position` not `Time`

### "Play/pause button out of sync"
- `IsPlaying` property bound to VideoPlayerService events
- DataTrigger switches between ▶/⏸ symbols
- Check event subscriptions in MainViewModel constructor (Line 70-72)

### "Rename mode stuck"
- `IsEditing` should reset on Enter/Escape/LostFocus
- Check `RenameTextBox_KeyDown` and `RenameTextBox_LostFocus` handlers

### "Recent files not saving"
- Files stored in `%LocalAppData%\CornieKit.Looper\recent_files.json`
- Check `RecentFilesService.Save()` exception handling

### "Audio quality is poor / buzzing / crackling"
- Verify LibVLC options use `--aout=mmdevice` (NOT `--aout=wasapi`)
- Ensure `--audio-resampler=soxr` is set (avoids "ugly" resampler fallback)
- Enable verbose logging: `new LibVLC(true, options)` to check actual resampler
- Try alternatives: `speex_resampler`, `directsound + directx-audio-float32`
- Check video file isn't corrupted by playing in VLC desktop app

## Future Enhancement Ideas

From `docs/DESIGN.md` Phase 5-6:
- Segment thumbnail preview (extract frame at midpoint)
- Batch video processing (load multiple videos)
- Export segments as separate video files (ffmpeg integration)
- Playback statistics (track which segments played most)
- Cloud sync (OneDrive/Google Drive API)
- Audio file support (MP3, WAV for music practice)
- Multi-language UI (i18n resources)

## Questions for User

If user asks to implement features, clarify:
1. **Rename**: Click-to-rename or dedicated rename button?
2. **Delete**: Confirmation dialog or direct delete?
3. **Loop mode**: Apply to single segment play or only "Play All"?
4. **Auto-play**: Start from beginning or last position?

## Testing Scenarios

### Manual Test Cases
1. Open video → Should auto-play
2. Hold R (paused) → Should ignore
3. Hold R <200ms (playing) → Should not create segment
4. Hold R >200ms → Should create and auto-loop segment
5. Double-click segment → Should loop that segment only
6. Click "Play All" → Should respect loop mode setting
7. Drag progress bar → Should not freeze, restore play/pause state
8. Click selected segment name → Wait → Should enter rename mode
9. Hover segment → Should show × button
10. Close & reopen video → Should restore segments

---

**Last Updated**: 2026-02-04
**By**: Claude Sonnet 4.5
