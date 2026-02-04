# Windows 文件关联设置指南

## 功能说明

设置文件关联后，你可以：
1. **右键视频文件** → "打开方式" → 选择 "CornieKit Looper"
2. **双击视频文件** 直接用 CornieKit Looper 打开（设为默认程序后）
3. 视频会自动加载并开始播放，和现在拖拽导入的效果一样

## 支持的视频格式

`.mp4` `.avi` `.mkv` `.mov` `.wmv` `.flv` `.webm` `.m4v` `.mpg` `.mpeg`

## 安装步骤

### 方法 1：使用 PowerShell 脚本（推荐）

1. 找到项目根目录下的 `RegisterFileAssociation.ps1` 文件
2. **右键点击** → 选择 **"以管理员身份运行"** 或 **"Run with PowerShell"**
3. 如果弹出执行策略警告，按 **R** (Run once) 继续
4. 看到 "Registration completed successfully!" 表示成功
5. 按任意键关闭窗口

### 方法 2：手动设置（不推荐）

如果脚本无法运行，可以手动设置：
1. 右键任意视频文件
2. 选择 "打开方式" → "选择其他应用"
3. 点击 "更多应用" → 滚动到底部 → "在这台电脑上查找其他应用"
4. 找到 `CornieKit.Looper.exe` 并选择
5. 勾选 "始终使用此应用打开 .mp4 文件"（可选）

## 设为默认程序

1. 完成上述安装步骤后
2. 打开 **Windows 设置** → **应用** → **默认应用**
3. 搜索 "CornieKit Looper"
4. 点击应用，为想要的视频格式设置默认打开方式

## 卸载文件关联

如果不再需要文件关联：
1. 找到 `UnregisterFileAssociation.ps1` 文件
2. **右键点击** → 选择 **"以管理员身份运行"**
3. 按任意键关闭

或者在 Windows 设置中手动更改默认程序。

## 使用示例

设置完成后：
```
双击 my-video.mp4
    ↓
CornieKit Looper 自动打开
    ↓
视频自动播放（和拖拽导入一样的效果）
```

## 故障排除

### PowerShell 脚本无法运行

**错误**：`无法加载文件，因为在此系统上禁止运行脚本`

**解决**：
1. 以管理员身份打开 PowerShell
2. 运行：`Set-ExecutionPolicy RemoteSigned -Scope CurrentUser`
3. 输入 `Y` 确认
4. 重新运行注册脚本

### 右键菜单中没有显示

- 需要**注销并重新登录** Windows
- 或重启 Windows Explorer（任务管理器 → 找到 "Windows 资源管理器" → 重启）

### 找不到 CornieKit.Looper.exe

确保：
- 脚本文件和 `.exe` 在同一个文件夹
- 已经编译了发布版本（`dotnet publish`）

## 技术细节

脚本会修改以下注册表项：
- `HKEY_CLASSES_ROOT\CornieKit.Looper.VideoFile`
- `HKEY_CURRENT_USER\Software\Classes\Applications\CornieKit.Looper.exe`
- `HKEY_CURRENT_USER\Software\Classes\[扩展名]\OpenWithProgids`

命令行参数处理位置：`App.xaml.cs:OnStartup()`
