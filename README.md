# CornieKit Looper

A Windows video player for marking and looping video segments. Perfect for practicing music, learning dance moves, or reviewing specific parts of videos.

![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Quick Segment Marking** - Hold `R` key while playing to mark a segment
- **Auto Loop Playback** - Newly recorded segments automatically start looping
- **Multiple Loop Modes**:
  - Single: Loop current segment
  - Sequential: Play segments in order once
  - Random: Shuffle playback
  - Sequential Loop: Repeat all segments
- **Optimized for Large Files** - Smooth playback of 4GB+ video files without audio stuttering
- **File Association** - Set as default program to open videos by double-clicking
- **Recent Files** - Quick access to recently opened videos
- **Inline Rename** - Click on selected segment name to rename (Windows Explorer style)
- **Auto-save** - Segments saved to `.cornieloop` files alongside videos
- **Wide Format Support** - MP4, AVI, MKV, MOV, WMV, FLV, WebM and more
- **Drag & Drop** - Drop video files directly into the window

## Installation

### Option 1: Download Release (Recommended)
1. Download the latest `CornieKit_Looper_v1.0.3_win-x64.zip` from [Releases](https://github.com/billye220670/CornieKit_Looper/releases/latest)
2. Extract to any folder
3. Run `CornieKit.Looper.exe`
4. (Optional) Run `RegisterFileAssociation.ps1` as Administrator to associate video files

**System Requirements**: Windows 10/11 + [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Option 2: Build from Source
Requires .NET 8.0 SDK.

```bash
git clone https://github.com/billye220670/CornieKit_Looper.git
cd CornieKit_Looper
dotnet build -c Release
```

## Usage

1. **Open Video** - Drag & drop or `File > Open Video`
2. **Mark Segment** - While playing, hold `R` to mark start, release to mark end
3. **Play Segment** - Double-click segment in list to loop it
4. **Play All** - Click "Play All Segments" button
5. **Rename** - Click selected segment name, wait, then edit
6. **Delete** - Hover over segment, click the × button

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `R` (hold) | Record segment (while playing) |
| `Space` | Play / Pause |
| `Tab` | Toggle segment panel |
| `Left Arrow` | Rewind 5 seconds |
| `Right Arrow` | Fast forward 5 seconds |
| `Up Arrow` | Play previous segment (cycle) |
| `Down Arrow` | Play next segment (cycle) |

## Tech Stack

- .NET 8.0 + WPF
- [LibVLCSharp](https://github.com/videolan/libvlcsharp) - Video playback
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM framework
- System.Text.Json - Data persistence

## License

MIT License - see [LICENSE](LICENSE) for details.
