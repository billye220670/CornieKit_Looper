# CornieKit Looper

A Windows video player for marking and looping video segments. Perfect for practicing music, learning dance moves, or reviewing specific parts of videos.

![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Quick Segment Marking** - Hold `R` while playing to record a segment, or press `1`/`2` to mark precise start/end points
- **Segment Boundary Editing** - Drag the green/red markers on the progress bar to adjust boundaries with real-time video preview
- **Auto Loop Playback** - Newly recorded segments automatically start looping
- **Multiple Loop Modes**:
  - Single: Loop current segment
  - Sequential: Play segments in order once
  - Random: Shuffle playback
  - Sequential Loop: Repeat all segments
- **Zoom & Pan** - `Alt + Scroll` to zoom into the video toward the cursor; middle-mouse drag to pan; `F` to reset
- **Frame Stepping** - Scroll wheel on the progress bar to step ±0.5s (hold `Ctrl` for ±0.1s, `Shift` for ±1.5s)
- **Volume Control** - On-screen volume slider with mute toggle; scroll wheel on the volume area for quick adjustment
- **Optimized for Large Files** - Smooth playback of 4GB+ video files without audio stuttering
- **Recent Files** - Quick access to recently opened videos
- **Inline Rename** - Click on selected segment name to rename (Windows Explorer style)
- **Auto-save** - Segments and zoom state saved to `.cornieloop` files alongside videos
- **Wide Format Support** - MP4, AVI, MKV, MOV, WMV, FLV, WebM and more
- **Drag & Drop** - Drop video files directly into the window

## Installation

### Option 1: Download Release (Recommended)
1. Download the latest `CornieKit_Looper_v1.3.0_win-x64.zip` from [Releases](https://github.com/billye220670/CornieKit_Looper/releases/latest)
2. Extract to any folder
3. Run `CornieKit.Looper.exe`

**System Requirements**: Windows 10/11 + [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Option 2: Build from Source
Requires .NET 8.0 SDK.

```bash
git clone https://github.com/billye220670/CornieKit_Looper.git
cd CornieKit_Looper
dotnet build -c Release
```

## Usage

1. **Open Video** - Drag & drop or right-click → Open Video
2. **Mark Segment** - While playing, hold `R` to mark start, release to mark end
3. **Precise Marking** - Press `1` to set start point, `2` to set end point (works paused or playing)
4. **Edit Boundaries** - Drag the green (start) or red (end) dots on the progress bar
5. **Play Segment** - Double-click segment in list to loop it
6. **Play All** - Click the power icon at the top of the segment panel
7. **Rename** - Click selected segment name, wait, then edit
8. **Delete** - Hover over segment, click the × button
9. **Zoom** - `Alt + Scroll` to zoom; middle-mouse drag to pan; `F` to reset

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `R` (hold) | Record segment (while playing) |
| `1` | Mark segment start point |
| `2` | Mark segment end point (creates segment) |
| `Space` | Play / Pause |
| `Tab` | Toggle segment panel |
| `Left Arrow` | Rewind 5 seconds |
| `Right Arrow` | Fast forward 5 seconds |
| `Up Arrow` | Play previous segment (cycle) |
| `Down Arrow` | Play next segment (cycle) |
| `F` | Reset zoom to fit |
| `Alt + Scroll` (video) | Zoom toward cursor |
| `Middle Drag` (video) | Pan when zoomed in |
| `Scroll` (progress bar) | Frame step ±0.5s |
| `Ctrl + Scroll` (progress bar) | Fine step ±0.1s |
| `Shift + Scroll` (progress bar) | Coarse step ±1.5s |
| `Scroll` (volume area) | Adjust volume ±5% |

## Tech Stack

- .NET 8.0 + WPF
- [LibVLCSharp](https://github.com/videolan/libvlcsharp) - Video playback (offscreen WriteableBitmap rendering)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM framework
- System.Text.Json - Data persistence

## License

MIT License - see [LICENSE](LICENSE) for details.
