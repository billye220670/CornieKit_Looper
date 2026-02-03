# CornieKit Looper - Design Document

## Architecture Overview

### Technology Stack
- **Framework**: .NET 8.0 + WPF
- **Pattern**: MVVM (Model-View-ViewModel)
- **Video Engine**: LibVLCSharp 3.9.5
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **MVVM Toolkit**: CommunityToolkit.Mvvm 8.4.0

### Project Structure
```
CornieKit.Looper/
├── Models/              # Data models
│   ├── LoopSegment.cs
│   ├── LoopMode.cs
│   └── VideoMetadata.cs
├── ViewModels/          # MVVM ViewModels
│   ├── MainViewModel.cs
│   └── LoopSegmentViewModel.cs
├── Services/            # Business logic
│   ├── VideoPlayerService.cs
│   ├── SegmentManager.cs
│   └── DataPersistenceService.cs
├── Converters/          # XAML value converters
├── Helpers/             # Utility classes
└── Views/               # XAML views
    └── MainWindow.xaml
```

## Core Components

### 1. Models

#### LoopSegment
```csharp
public class LoopSegment
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

#### VideoMetadata
Stored as JSON file with `.cornieloop` extension in the same directory as the video file.

### 2. Services

#### VideoPlayerService
- Wraps LibVLCSharp MediaPlayer
- Handles video playback, seeking, and position monitoring
- Implements segment looping logic
- 50ms timer for position monitoring

#### SegmentManager
- Manages collection of segments
- Handles segment ordering and selection
- Implements different loop modes
- Notifies UI of changes via events

#### DataPersistenceService
- Saves/loads segment data as JSON
- Computes file hash (first 8KB of video) for validation
- Handles file I/O errors gracefully

### 3. ViewModels

#### MainViewModel
- Main application state
- Commands for user actions
- Handles Space key events
- Coordinates between services
- Two-way binding with View

#### LoopSegmentViewModel
- Wrapper for LoopSegment model
- UI-specific properties (IsSelected, IsPlaying)
- Display formatting

## Key Features Implementation

### Space Key Segment Marking
1. User presses Space → Record start time
2. Show recording indicator
3. User releases Space → Record end time
4. Validate minimum duration (200ms)
5. Create segment and add to list
6. Auto-save metadata

### Loop Playback
- 50ms position timer checks current time
- When current time >= segment end time → seek to start time
- For sequential modes, advance to next segment
- For random mode, pick random segment (excluding current)

### Performance Optimizations
- LibVLC hardware acceleration enabled
- Reduced caching delays (300ms)
- Minimal clock jitter/synchro
- Async file operations
- UI updates on Dispatcher thread

## Data Flow

```
User Input → ViewModel Command → Service → Model Update → Event → ViewModel → UI Update
```

Example: Creating a segment
1. User presses Space → MainWindow.KeyDown
2. Call MainViewModel.OnSpaceKeyDown()
3. Record start time
4. User releases Space → MainWindow.KeyUp
5. Call MainViewModel.OnSpaceKeyUp()
6. SegmentManager.AddSegment()
7. SegmentsChanged event
8. ViewModel updates ObservableCollection
9. ListView refreshes automatically

## File Format

`.cornieloop` JSON structure:
```json
{
  "videoFilePath": "C:\\Videos\\movie.mp4",
  "videoFileHash": "A1B2C3D4E5F6G7H8",
  "segments": [
    {
      "id": "guid",
      "name": "Segment 1",
      "startTime": "00:00:05",
      "endTime": "00:00:15",
      "order": 0,
      "createdAt": "2026-02-03T16:00:00Z"
    }
  ],
  "defaultLoopMode": "Single",
  "lastModified": "2026-02-03T16:00:00Z"
}
```

## UI Layout

```
┌──────────────────────────────────────────────────────────┐
│  Menu Bar (File, Help)                                   │
├────────────────────────────────┬─────────────────────────┤
│                                │  Segment List           │
│  Video Player Area             │  - Segment 1            │
│  (LibVLCSharp.VideoView)       │    00:05 - 00:15        │
│                                │  - Segment 2            │
│  [Recording Indicator]         │    00:45 - 01:00        │
│                                │                         │
│                                │  [Play All] [Loop ▼]    │
├────────────────────────────────┴─────────────────────────┤
│  ▶ ■  00:05 / 02:30  ━━━━●━━━━  Status message         │
├──────────────────────────────────────────────────────────┤
│  C:\Videos\movie.mp4                                     │
└──────────────────────────────────────────────────────────┘
```

## Error Handling

- Video load failures → MessageBox + status message
- Invalid segment durations → Silent ignore
- File hash mismatch → Load segments anyway (non-critical)
- Missing video file → Clear UI state
- JSON parse errors → Log and continue with empty state

## Future Considerations

- Segment thumbnail generation (extract frames)
- Export segments as separate video files
- Keyboard shortcut customization
- Cloud sync (OneDrive integration)
- Multi-language support (resource files)
- Plugin architecture for extensions
