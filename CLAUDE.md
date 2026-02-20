# CLAUDE.md - CornieKit Looper

## Project Overview

**CornieKit Looper** is a Windows desktop video player built with .NET 8.0 WPF that enables users to mark and loop specific segments of videos. Primary use cases include music practice, dance learning, and video review.

**Version**: 1.3.0
**Repository**: https://github.com/billye220670/CornieKit_Looper
**Language**: C# (.NET 8.0)
**Platform**: Windows 10/11

## Quick Start for AI Assistants

### Key User Workflows
1. **Open video** → Auto-plays immediately
2. **Hold R while playing** → Records segment (min 200ms) → Auto-loops new segment
3. **Double-click segment** → Enters playback mode, loops that segment only
4. **Click power icon (top-left of segment panel)** → Toggle between playback mode on/off
5. **Press 1 then 2** (not in playback mode) → Mark start/end points → Creates new segment
6. **During playback: Press 1 then 2** → Updates current segment boundaries (green/red markers)
7. **Click segment name → wait 500ms** → Rename mode
8. **Drag green/red markers** → Adjust segment boundaries with real-time video preview
9. **Left/Right Arrow** → Seek ±5 seconds
10. **Up/Down Arrow** → Select previous/next segment and auto-play
11. **Alt + Mouse Wheel** → Zoom toward cursor (zooms into video)
12. **Middle Mouse Drag** → Pan when zoomed in (hand cursor)
13. **F key** → Reset zoom to fit
14. **Mouse Wheel on progress bar** → Frame step ±0.5s (Ctrl=0.1s, Shift=1.5s)
15. **Click/drag volume slider** → Adjust volume; icon click = mute toggle

### Critical Behavior Flags
- `IsPlayingAllSegments` (bool): `true` = in playback mode (showing markers), `false` = no segment playback active
- `_playingSingleSegment` (bool): `true` = looping single segment, `false` = using LoopMode for segment sequencing
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

**Rendering Architecture** (current):
- Uses LibVLC **offscreen video callbacks** (`SetVideoCallbacks`) to decode into a pinned `byte[]` buffer
- `OnVideoDisplay` callback writes pixels into a WPF `WriteableBitmap` on the UI thread
- `VideoSource` property (`ImageSource`) exposed to ViewModel/View — bound to a WPF `Image` element
- Replaces the old `LibVLCSharp.WPF.VideoView` hardware HWND approach, which prevented transforms (zoom/pan)

**LibVLC Options** (Line 38-43):
```csharp
"--aout=directsound",         // DirectSound audio output
"--directx-audio-float32",    // Force 32-bit float audio (high quality)
"--file-caching=300",         // Low latency (300ms)
"--no-audio-time-stretch"     // Disable time stretching for quality
```

**Audio Quality Notes** (v1.0.3):
- `directsound` is more stable than `mmdevice` for Windows audio output
- `--directx-audio-float32` forces 32-bit floating point audio, preventing low-bitrate sound
- Previous `soxr` resampler approach was unreliable (LibVLC might not load it correctly)
- Letting DirectSound handle audio conversion is more consistent
- If audio quality issues occur, verify same video sounds good in VLC desktop player

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

### Numeric Key Marking and Marker Dragging (MainWindow.xaml + MainViewModel.cs)

**Problem**: Users could only record segments by holding R during playback. No way to precisely mark time points or adjust segment boundaries after creation.

**Solution 1 - Numeric Key Marking**:
```
OnKey1Pressed():
  - Marks current playback time as segment start point
  - Works in paused OR playing state (unlike R key)
  - Shows blue marker on progress bar
  - Sets StatusMessage with guidance

OnKey2Pressed():
  - Validates that start point was marked (press 1 first)
  - Creates segment from marked start to current time
  - Auto-loops the newly created segment
  - Clears the blue marker after creating segment
```

**Solution 2 - Marker Dragging**:
- When segment is selected, green (start) and red (end) markers appear on progress bar
- User can drag markers left/right to adjust times
- Real-time video preview updates during drag (50ms throttle to prevent excessive seeks)
- Changes committed only on mouse release
- Prevents crossing (start can't go right of end, end can't go left of start)

**Implementation Notes**:
- MarkerCanvas: Transparent Canvas overlay on progress bar grid (ClipToBounds="True")
- Markers drawn as dynamically created Ellipse elements (not static XAML)
- Redraw triggered by: property changes (via PropertyChanged event), window resize
- Dragging behavior reuses `_isScrubbing` and related state from existing slider logic
- Uses Position (0-1) property for seeking, consistent with existing scrubbing implementation

**Canvas Positioning**:
```
MarkerCanvas.ActualWidth = progress bar width in pixels
Position % * Canvas.ActualWidth = X coordinate of marker
```

**Color Scheme**:
- Blue (#4A90E2): Pending start marker (awaiting end point)
- Green (#27AE60): Segment start boundary
- Red (#E74C3C): Segment end boundary
- All with semi-transparent shadow for visibility on any video

### Data Persistence (DataPersistenceService.cs)

**File Format**: `{VideoFileName}.cornieloop` (JSON)
```json
{
  "VideoFilePath": "C:\\path\\video.mp4",
  "VideoFileHash": "abc123...",
  "Segments": [...],
  "DefaultLoopMode": "Single",
  "ZoomLevel": 1.0,
  "ViewCenterX": 0.5,
  "ViewCenterY": 0.5
}
```

**Hash Validation**: Prevents loading segments from different file with same name.

### Zoom/Pan System (MainViewModel.cs + MainWindow.xaml.cs)

**Zoom** (`ZoomAtPoint`):
- Triggered by `Alt + Mouse Wheel` on the video area
- Computes normalized cursor position accounting for `Stretch=Uniform` letterboxing via `GetRenderedVideoRect()`
- Zoom range: 1.0x – 8.0x; clamped by `ClampCenter()`
- State persisted in `.cornieloop` file (`ZoomLevel`, `ViewCenterX`, `ViewCenterY`)

**Pan** (middle-mouse drag):
- `VideoOverlayGrid_PreviewMouseDown/Move/Up` handle middle-mouse capture
- Converts pixel delta to normalized video-fraction delta, accounts for current zoom level
- Cursor shows `Cursors.Hand` during panning

**Transforms applied via**:
```csharp
VideoScaleTransform.ScaleX/Y = zoom;
VideoTranslateTransform.X = -(centerX - 0.5) * renderW * zoom;
VideoTranslateTransform.Y = -(centerY - 0.5) * renderH * zoom;
```
- `RenderTransformOrigin="0.5,0.5"` on the `Image` element

**`F` key**: calls `ResetZoom()` to restore 1:1 fit.

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
- Playback speed control

## Build & Release

### Build Commands
```bash
# Development
dotnet build

# Release (framework-dependent)
dotnet publish -c Release -r win-x64 --self-contained false -o publish

# Zip for release
powershell Compress-Archive -Path 'publish\*' -DestinationPath 'CornieKit_Looper_v1.0.3_win-x64.zip'
```

### Version Bumping
1. Update `<Version>` in `src/CornieKit.Looper/CornieKit.Looper.csproj`
2. Update `MainWindow.xaml.cs` About dialog (Line 108-115)
3. Git tag: `git tag v1.x.x && git push --tags`
4. Create GitHub release with zip

## Important Files

| File | Purpose | Lines |
|------|---------|-------|
| `MainViewModel.cs` | Core app logic, playback state, marker management, zoom/pan | ~1100 |
| `VideoPlayerService.cs` | LibVLC wrapper, offscreen WriteableBitmap rendering, position timer | ~280 |
| `MainWindow.xaml.cs` | UI event handlers, marker rendering, drag logic, zoom/pan | ~1650 |
| `MainWindow.xaml` | Main UI layout, segment panel, power button, volume control | ~810 |
| `SegmentManager.cs` | Segment collection, loop mode logic | ~150 |

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
- **2026-02-20**: UI interaction polish
  - **Volume slider click-anywhere**: Added transparent overlay `Border` over `VolumeSlider` (same pattern as progress bar scrubbing). `VolumeSlider` set `IsHitTestVisible="False"`. Handlers: `VolumeSliderOverlay_MouseDown/Up/Move` + `UpdateVolumeSliderFromMouse`. Clicking anywhere on the track jumps to that position.
  - **Volume scroll wheel hot zone**: Changed `VideoOverlayGrid_PreviewMouseWheel` volume-adjust trigger from `IsMouseOverNamedElement(element, "BottomPanel")` to `IsMouseOverNamedElement(element, "VolumePanel")`. Only the volume icon + slider area triggers volume scroll; rest of bottom panel is neutral.
  - **Progress bar scroll zone enlarged**: Added `x:Name="ProgressBarGrid"` and `MinHeight="36"` to the progress bar container `Grid` (was ~20px from Slider height). `IsMouseOverProgressBar` now also checks for `"ProgressBarGrid"` name. Scroll wheel triggers frame-step within the full 36px row.
  - **Pan cursor**: Changed middle-mouse drag cursor from `Cursors.ScrollAll` (4-way arrow) to `Cursors.Hand`.
  - **Files Modified**: MainWindow.xaml, MainWindow.xaml.cs
- **2026-02-15**: Offscreen rendering overhaul + zoom/pan + volume control UI
  - **Offscreen Rendering**:
    - Replaced `LibVLCSharp.WPF.VideoView` (HWND-based hardware overlay) with LibVLC software video callbacks (`SetVideoCallbacks`)
    - VLC decodes into a pinned `byte[]` buffer; `OnVideoDisplay` writes to a `WriteableBitmap` on the UI thread
    - Exposes `VideoSource` (`ImageSource`) from `VideoPlayerService`, bound to a WPF `<Image>` element
    - Enables WPF `RenderTransform` (zoom/pan) on the video — impossible with the old HWND overlay
    - `VideoMetadata.cs` gains `ZoomLevel`, `ViewCenterX`, `ViewCenterY` for persistence
  - **Zoom/Pan System** (`MainViewModel.cs` + `MainWindow.xaml.cs`):
    - `Alt + Mouse Wheel` → zoom toward cursor using `GetRenderedVideoRect()` for letterbox-aware coordinates
    - Middle-mouse drag → pan with `Cursors.Hand`; delta converted from pixels to normalized video fractions
    - `F` key → `ResetZoom()` restores 1:1 fit
    - Zoom state saved in `.cornieloop` file; restored on next open
    - `ZoomPanChanged` event triggers `UpdateVideoTransforms()` which applies `ScaleTransform` + `TranslateTransform`
  - **Volume Control UI** (`MainWindow.xaml` + `MainWindow.xaml.cs`):
    - Added `VolumePanel` (StackPanel with volume icon button + `VolumeSlider`) to bottom controls row
    - `VolumeSliderStyle`: minimalist 2px track + 10px round thumb
    - Icon button toggles mute (saves pre-mute level to `_preMuteVolume`)
    - `VolumeHUD`: center-screen overlay showing icon + percentage, auto-hides after 1s
    - `partial void OnCurrentVolumeChanged(int value)` in ViewModel calls `_videoPlayer.SetVolume()` automatically
    - Scroll wheel on volume panel adjusts ±5%; rapid downward scroll = instant mute
  - **Frame Stepping** (scroll wheel on progress bar):
    - Default: ±0.5s; `Ctrl`: ±0.1s; `Shift`: ±1.5s
    - Throttled to 50ms with `_frameStepTimer` accumulator to batch rapid scrolls
  - **Scroll Wheel Routing** (`VideoOverlayGrid_PreviewMouseWheel`):
    - Progress bar area → frame step
    - Volume panel → volume adjust
    - Other UI elements → ignored
    - Video area → zoom (previously volume)
  - **Files Modified**: VideoPlayerService.cs (rendering overhaul), MainViewModel.cs (VideoSource, zoom/pan, volume), MainWindow.xaml (volume panel, zoom Image/transforms), MainWindow.xaml.cs (zoom, pan, volume, frame step), VideoMetadata.cs (zoom fields)
  - **Playback State Management**:
    - New `IsPlayingAllSegments` property tracks whether segment playback is active (true for list mode or single loop)
    - Separated playback state from UI selection: clicking list items no longer affects markers
    - Only currently playing segment shows visual markers (green start/red end dots)
    - Markers follow `_segmentManager.CurrentSegment`, not `SelectedSegment`
  - **List Playback Mode**:
    - List playback (`IsPlayingAllSegments = true`) persists across video save/load in cornieloop file
    - Opening video with saved list playback state correctly restores it
    - Double-clicking segment or pressing 1/2 keys enters list playback mode
    - Clicking power button (top of segment panel) toggles list playback on/off
  - **Numeric Key Behavior (1/2 keys)**:
    - When `IsPlayingAllSegments == true`: Press 1/2 updates current segment boundaries (green/red dots)
    - When `IsPlayingAllSegments == false`: Press 1 marks pending start (blue dot), press 2 creates new segment with auto-swap
    - Creating new segment with 1/2 keys automatically enters list playback mode
  - **UI Improvements**:
    - Replaced play/pause button with power indicator icon (power.png)
    - Icon color: Gray (#666666) = playback off, Blue (#4A90E2) = playback on
    - Removed `IsSelected` visual feedback from list items (gray selection highlight gone)
    - List items only show hover state and playing segment highlight
  - **Visual Marker Refinements**:
    - Removed white stroke/edge from segment marker dots (cleaner appearance)
    - Reduced color saturation: Green RGB(95,184,120), Red RGB(232,122,112)
    - Markers only visible during list playback, hidden when stopped
  - **Stop/Pause Behavior**:
    - Changed `PlayAllSegments()` stop from `Stop()` to `Pause()` - prevents white screen, pauses at current frame
    - Exit playback mode with `Pause()` + `StopSegmentLoop()` for smooth UX
  - **Bug Fixes**:
    - Fixed UI thread race condition in `OnSegmentLoopCompleted()` - wrapped marker updates in `Dispatcher.Invoke()`
    - Segment markers now correctly update when playing list transitions to next segment
    - Marker position properly reflects currently playing segment after loop transition
  - **Implementation Details**:
    - New `GetPlayingSegmentVm()` helper finds ViewModel for currently playing segment
    - Refactored `UpdateSegmentMarkers()` to check `IsPlayingAllSegments` and use `GetPlayingSegmentVm()`
    - Updated marker drawing in `DrawSegmentMarker()` - removed white stroke, adjusted colors
    - Key handlers (OnKey1/OnKey2) now use `GetPlayingSegmentVm()` instead of `SelectedSegment`
    - All segment operations (record, double-click, 1/2 keys) set `IsPlayingAllSegments = true`
    - All stop/pause operations set `IsPlayingAllSegments = false` and hide markers
  - **Files Modified**:
    - MainViewModel.cs: Refactored playback state management, marker logic, key handlers (1000+ lines)
    - MainWindow.xaml: Replaced play button UI with power icon, removed IsSelected visual trigger
    - MainWindow.xaml.cs: Fixed OnSegmentLoopCompleted thread safety, simplified marker colors
- **2026-02-11**: Added numeric key marking and segment boundary dragging
  - **Numeric Key Marking** (1, 2):
    - Press 1 to mark segment start point (works in paused state)
    - Press 2 to mark end point and auto-create segment
    - Shows blue circular marker on progress bar when start point is marked
    - Status messages guide user through the marking process
    - Allows precise manual segment creation without needing to hold R during playback
  - **Segment Boundary Visualization**:
    - Selected segment displays green (start) and red (end) circular markers on progress bar
    - Markers are only shown for the currently selected segment (reduces visual clutter)
    - Clean visual design with dot + vertical line for each marker
  - **Marker Dragging**:
    - Drag segment boundary markers to adjust start/end times in real-time
    - Video automatically previews the new marker position during drag (throttled to 50ms)
    - Controls remain visible while dragging (auto-hide timer paused)
    - Changes saved when mouse released
    - Prevents invalid state (start can't cross end, end can't cross start)
  - **Implementation Details**:
    - MarkerCanvas added to progress bar grid (transparent, clip-to-bounds)
    - New marker properties in MainViewModel:
      - `PendingStartMarkerPosition` (0-100): Position of blue "pending" marker
      - `IsPendingStartMarkerVisible`: Controls visibility of pending marker
      - `SelectedSegmentStartPosition` / `SelectedSegmentEndPosition`: Positions of green/red markers
      - `AreSegmentMarkersVisible`: Controls visibility of segment boundary markers
    - Marker positions updated via `OnSelectedSegmentChanged` handler in ViewModel
    - Dynamically created Ellipse shapes with mouse event handlers for dragging
    - Canvas redraws on property changes and window resize
  - **Keyboard Shortcuts**:
    - Updated About dialog to document the new 1 and 2 key shortcuts
    - Keys respect TextBox focus (no interference with rename mode)
  - **Files Modified**:
    - MainViewModel.cs: Added 6 new properties, 7 new methods for marker management
    - MainWindow.xaml: Added MarkerCanvas to progress bar grid
    - MainWindow.xaml.cs: Added marker rendering, dragging logic, keyboard handlers
- **2026-02-05**: Added keyboard arrow key navigation shortcuts
  - **Left/Right Arrow**: Seek backward/forward 5 seconds with boundary checks
  - **Up/Down Arrow**: Cycle through segments and auto-play (like double-click)
  - Added `SeekRelative(int seconds)` in MainViewModel for time navigation
  - Added `SelectedSegment` property with ListView binding for keyboard selection
  - Updated About dialog with new keyboard shortcuts
  - **Implementation notes**:
    - Arrow keys respect TextBox focus (no interference with rename mode)
    - Up/Down selection automatically triggers `PlaySegmentCommand` for seamless navigation
    - Seeking uses `Position` property to avoid LibVLC pause-state issues
- **2026-02-05 (v1.0.3)**: Fixed persistent audio quality issues with comprehensive solution
  - **Root cause**: Previous `mmdevice + soxr` config was unreliable; soxr might not load correctly
  - **Solution**: Switched to `--aout=directsound` with `--directx-audio-float32`
  - DirectSound is more stable and 32-bit float prevents low-bitrate sound
  - Reduced audio caching from 3000ms to 300ms for better responsiveness
  - Added 10-second timeout for video loading to prevent hangs
  - Implemented cancellation support for video loading operations
  - Fixed timer lifecycle in Play/Pause operations
  - Enhanced EndReached event handling for seamless looping
  - **Key learning**: Don't rely on external resamplers; use native audio API with high-quality output format
- **2026-02-04**: Fixed audio quality issues by correcting LibVLC audio configuration
  - Replaced invalid `--aout=wasapi` with proper `--aout=mmdevice`
  - Added `--audio-resampler=soxr` to use high-quality resampler
  - Fixes buzzing/crackling audio that sounded like "cheap recorder"
  - **Note**: This fix was incomplete; v1.0.3 provides the final solution
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

### "Audio quality is poor / buzzing / crackling / sounds low-bitrate"
- **v1.0.3 Solution**: Use `--aout=directsound` with `--directx-audio-float32`
- Verify LibVLC options in VideoPlayerService.cs line 38-43
- Compare audio with VLC desktop player to confirm it's a config issue
- **Don't** use `--audio-resampler=soxr` - it may not load reliably
- **Don't** use `mmdevice` - DirectSound is more consistent
- Enable verbose logging: `new LibVLC(true, options)` to diagnose actual audio path
- Check that video file itself isn't corrupted or low-quality source

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

**Last Updated**: 2026-02-20
**By**: Claude Sonnet 4.6
