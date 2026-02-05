# CornieKit Looper v1.0.3 开发日志

**发布日期**: 2026-02-05
**版本**: v1.0.3
**提交**: 1589f8e

## 问题诊断

### 音质问题重现
用户反馈音质"像是很廉价录音记录出来的，位数很低"。这是第三次遇到此问题，之前的修复不够彻底。

### 历史配置问题
Git 仓库中保存的是旧配置：
```csharp
"--aout=mmdevice",
"--audio-resampler=speex_resampler",  // 低质量重采样器
"--file-caching=3000",                 // 过高延迟
```

工作区修改曾使用 `soxr`，但：
1. 可能未正确加载
2. 修改未提交，导致 git 操作时丢失

## 解决方案

### 最终配置
```csharp
var options = new[]
{
    "--aout=directsound",           // DirectSound 音频输出
    "--directx-audio-float32",       // 强制 32 位浮点音频
    "--file-caching=300",            // 低延迟缓存
    "--no-audio-time-stretch"        // 禁用音频拉伸
};
```

### 关键改动
1. **音频输出切换**: `mmdevice` → `directsound`
   - DirectSound 更稳定且兼容性好
   - 配合 `--directx-audio-float32` 强制高质量输出

2. **移除重采样器依赖**: 不再指定 `soxr` 或 `speex_resampler`
   - 让 DirectSound 自行处理音频转换
   - 避免重采样器加载失败的风险

3. **降低缓存延迟**: `3000ms` → `300ms`
   - 减少音频缓冲延迟
   - 提升播放响应速度

## 附加改进

### 视频加载优化
```csharp
// 添加 10 秒超时机制
var timeout = DateTime.Now.AddSeconds(10);
while (_mediaPlayer.Length == 0)
{
    if (DateTime.Now > timeout)
        throw new TimeoutException("Video metadata loading timeout");
    await Task.Delay(10);
}
```

### 加载取消支持
```csharp
// 支持取消正在进行的视频加载
private CancellationTokenSource? _loadCancellationToken;

public async Task LoadVideoAsync(string filePath)
{
    _loadCancellationToken?.Cancel();
    _loadCancellationToken = new CancellationTokenSource();
    var token = _loadCancellationToken.Token;
    // ...
}
```

### 定时器生命周期修复
```csharp
public void Play()
{
    _mediaPlayer?.Play();
    _positionTimer?.Start();  // 播放时启动
}

public void Pause()
{
    _mediaPlayer?.Pause();
    _positionTimer?.Stop();   // 暂停时停止
}
```

### 循环播放优化
```csharp
private void OnEndReached(object? sender, EventArgs e)
{
    _positionTimer?.Stop();

    // 使用 ThreadPool 避免 LibVLC 内部线程阻塞
    ThreadPool.QueueUserWorkItem(_ =>
    {
        _mediaPlayer?.Stop();
        Thread.Sleep(50);

        if (_activeLoopSegment != null)
            Seek(_activeLoopSegment.StartTime);
        else
            SeekByPosition(0);

        Play();
    });
}
```

## 技术要点

### 为什么使用 DirectSound？
1. **内置于 Windows**: 无需额外驱动
2. **32 位浮点支持**: `--directx-audio-float32` 保证高质量
3. **稳定性**: 比 WASAPI/MMDevice 更少边缘问题
4. **兼容性**: 在所有 Windows 版本上工作良好

### 为什么移除 soxr？
1. **加载不确定性**: LibVLC 可能回退到 "ugly" 重采样器
2. **配置复杂度**: `--soxr-quality=vhq` 等选项不被支持
3. **依赖 DirectSound**: 让底层音频 API 处理更可靠

### LibVLC 音频架构
```
Video File → Decoder → [Resampler] → Audio Output → Sound Card
                            ↓              ↓
                    (可选，自动)    DirectSound
                                   + Float32
```

## 文件修改清单

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `VideoPlayerService.cs` | 重构 | 音频配置、定时器、循环处理 |
| `MainViewModel.cs` | 增强 | 加载取消、超时处理 |
| `CornieKit.Looper.csproj` | 版本 | 1.0.0 → 1.0.3 |
| `MainWindow.xaml.cs` | 版本 | About 对话框版本号 |
| `README.md` | 文档 | 更新下载链接 |

## 测试建议

1. **音频质量测试**
   - 使用高质量音乐视频（FLAC/AAC）
   - 对比 VLC 播放器音质
   - 测试长时间播放稳定性

2. **大文件测试**
   - 4GB+ 视频文件
   - 检查缓存延迟
   - 验证无音频卡顿

3. **循环测试**
   - 片段结束后立即循环
   - 视频末尾自动重播
   - 多片段顺序播放

## 发布流程

```bash
# 1. 更新版本号
# CornieKit.Looper.csproj, MainWindow.xaml.cs

# 2. 提交代码
git add .
git commit -m "Fix audio quality issues and improve video loading"

# 3. 构建发布版本
dotnet publish -c Release -r win-x64 --self-contained false -o publish

# 4. 创建压缩包
powershell "Compress-Archive -Path 'publish\*' -DestinationPath 'CornieKit_Looper_v1.0.3_win-x64.zip' -Force"

# 5. 打标签并推送
git tag v1.0.3
git push origin main
git push origin v1.0.3

# 6. 创建 GitHub Release
gh release create v1.0.3 CornieKit_Looper_v1.0.3_win-x64.zip \
  --title "v1.0.3 - Audio Quality Fix" \
  --notes "..."
```

## 经验总结

### ✅ 正确实践
1. **使用 DirectSound + Float32** 而非复杂的重采样配置
2. **降低缓存延迟** 提升响应速度
3. **提交所有修复** 防止 git 操作丢失更改
4. **详细日志记录** 帮助未来调试

### ❌ 避免的错误
1. 依赖可能失败的重采样器加载
2. 过高的音频缓存（3000ms）
3. 未提交的临时修复
4. 启用 verbose 日志后忘记移除

### 🔍 调试技巧
- 运行 `new LibVLC(true, options)` 查看实际加载的模块
- 在命令行运行查看 LibVLC 日志
- 对比 VLC 桌面版音质确认问题
- 使用进程监视器检查音频 API 调用

---

**维护人员**: Claude Sonnet 4.5
**参考**: CLAUDE.md, docs/DESIGN.md, LibVLCSharp 文档
