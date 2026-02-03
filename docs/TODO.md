# Development TODO List

## Phase 1: Basic Player ✅
- [x] Create WPF project structure
- [x] Integrate LibVLCSharp
- [x] Implement basic playback controls
- [x] Design main window layout
- [x] File open dialog
- [x] Drag & drop support

## Phase 2: Segment Marking ✅
- [x] Space key monitoring (KeyDown/KeyUp)
- [x] Time point recording
- [x] Data models (LoopSegment, VideoMetadata)
- [x] Right-side segment list UI
- [x] Recording visual feedback

## Phase 3: Loop Playback ✅
- [x] Position monitoring and auto-jump
- [x] Loop mode switching
- [x] Segment click to jump
- [x] Play all segments functionality

## Phase 4: Data Persistence ✅
- [x] JSON serialization/deserialization
- [x] File read/write logic
- [x] Auto-save/load functionality
- [x] File hash validation

## Phase 5: Advanced Features
- [ ] Segment rename dialog
- [ ] Segment drag & drop reordering
- [ ] Keyboard shortcut system
- [ ] Error handling improvements
- [ ] Logging system
- [ ] Volume control UI
- [ ] Playback speed control

## Phase 6: Testing & Optimization
- [ ] Performance testing with large files
- [ ] Memory usage optimization
- [ ] UI/UX improvements
- [ ] Edge case testing
- [ ] Unit tests
- [ ] Integration tests

## Future Enhancements
- [ ] Segment thumbnail preview
- [ ] Playback statistics
- [ ] Custom keyboard shortcuts
- [ ] Multi-video playlist
- [ ] Cloud sync support
- [ ] Audio file support
- [ ] Multi-language interface
- [ ] Export segments to new video files
- [ ] Batch processing
- [ ] Plugin system

## Bug Fixes Needed
- [ ] Fix drag & drop to load actual file (currently opens dialog)
- [ ] Handle video playback errors gracefully
- [ ] Improve seek accuracy for different video formats
- [ ] Fix potential memory leaks
- [ ] Handle missing codec scenarios

## Known Issues
- Drag & drop currently doesn't load the dropped file properly
- ComboBox items need proper value binding for LoopMode enum
- Need to add keyboard shortcuts for common operations
- Missing proper error messages for user-facing errors
