# Vancat.OllamaCloudUsage

在 Visual Studio 中查看 [Ollama Cloud](https://ollama.com) 用量与限额 —— 状态栏实时指示 + 详情工具窗口。

> 设计参考：[ollama-cloud-usage](https://github.com/longnh0411/ollama-cloud-usage)（VS Code 扩展）

## ✨ 功能

- **状态栏用量指示器** — 在 Visual Studio 状态栏实时显示会话（5 小时）与每周窗口的用量百分比：
  - 🖱️ **鼠标悬停** — 弹出详细浮窗（用量条、模型请求列表、重置倒计时）
  - 👆 **点击** — 直接打开用量面板工具窗口
  - ▤ **快捷按钮** — 状态栏右侧的按钮可一键打开详细面板
- **用量面板工具窗口** — 通过「视图 > 其他窗口 > Ollama Cloud 用量面板」打开：
  - 会话与每周用量条，按模型分色显示请求占比
  - 每个窗口的重置倒计时（实时更新）
  - 本窗口 / 本周使用的模型与请求次数
  - 多账户支持：下拉切换、添加、移除账户
- **自动刷新** — 按可配置间隔（默认 60 秒）自动刷新用量。
- **跨实例共享缓存** — 多个 Visual Studio 实例共享同一份用量缓存，不会重复请求 API。
- **UTC 锚定重置窗口** — 会话窗口按 5 小时边界（00/05/10/15/20 UTC）重置；每周窗口在周一 00:00 UTC 重置。
- **安全存储** — API 密钥经 Windows DPAPI（当前用户范围）加密后存储在 `%APPDATA%\Vancat\OllamaCloudUsage\accounts.json`。
- **主题自适应** — 状态栏控件自动适配 VS 浅色/深色/高对比度主题。

> 💡 **状态栏实现说明**：本扩展将 WPF 控件直接注入 VS 主窗口的 WPF 状态栏视觉树（而非使用 `IVsStatusbar` 文本 API），因此支持悬停浮窗、点击交互与图标显示。

## 🚀 使用

1. 安装扩展（见下方构建说明）。
2. 通过菜单「工具 > Ollama Cloud 用量 > 添加账户」添加 API 密钥。
3. 状态栏会立即显示用量；**鼠标悬停**可查看详情，**点击**或按 ▤ 按钮打开完整面板。
4. 用量会按配置间隔自动刷新；面板中可点击 ⟳ 手动刷新，⚙ 修改刷新间隔。

### 找不到菜单或面板？

- **重启 Visual Studio**：首次安装 VSIX 后必须重启才能加载扩展。
- **面板位置**：菜单「视图 (V) > 其他窗口 (E) > Ollama Cloud 用量面板」。
- **命令位置**：菜单「工具 (T) > Ollama Cloud 用量」子菜单。
- **快速入口**：直接点击状态栏的用量文字或 ▤ 按钮。

## 📋 命令

| 命令 | 位置 | 说明 |
|---|---|---|
| 刷新用量 | 工具 > Ollama Cloud 用量 | 立即刷新（绕过缓存） |
| Ollama Cloud 用量面板 | 视图 > 其他窗口 | 打开详情面板 |
| 添加账户 | 工具 > Ollama Cloud 用量 | 添加新的 API 密钥 |
| 移除账户 | 工具 > Ollama Cloud 用量 | 移除已有账户 |
| 设置自动刷新间隔 | 工具 > Ollama Cloud 用量 | 修改刷新间隔（10–86400 秒） |

## 🔨 构建

### 前置要求

- Visual Studio 2022 (17.0+) 或 Visual Studio 2026，需安装 **Visual Studio 扩展开发** 工作负载
- .NET Framework 4.7.2 目标包

### 构建步骤

双击 `构建扩展.bat`，或在开发者命令提示符中执行：

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
    "Vancat.OllamaCloudUsage\Vancat.OllamaCloudUsage.csproj" /t:Rebuild
```

产物位于 `Vancat.OllamaCloudUsage\bin\Debug\net472\Vancat.OllamaCloudUsage.vsix`。

### 调试

在 Visual Studio 中打开 `Vancat.OllamaCloudUsage.slnx`，按 F5 启动实验实例（`/rootsuffix Exp`）进行调试。

## 📁 项目结构

```
Vancat.OllamaCloudUsage/            # 解决方案目录
├── README.md
├── 构建扩展.bat
├── Vancat.OllamaCloudUsage.slnx
└── Vancat.OllamaCloudUsage/        # 扩展项目
    ├── OllamaCloudUsagePackage.cs      # VSPackage 入口（命令注册、生命周期）
    ├── OllamaCloudUsage.vsct           # 命令表（菜单/按钮定义）
    ├── StatusBarManager.cs             # 状态栏控件注入器
    ├── InputDialog.xaml(.cs)           # 通用输入对话框
    ├── AccountPickerDialog.xaml(.cs)   # 账户选择对话框
    ├── Services/
    │   ├── OllamaApiClient.cs          # 用量 API 客户端 + JSON 解析
    │   ├── AccountStore.cs             # 账户存储（DPAPI 加密）
    │   ├── SharedUsageCache.cs         # 跨实例共享缓存
    │   ├── UsageService.cs             # 核心协调服务
    │   ├── ResetTime.cs                # 重置时间计算
    │   └── Config.cs                   # 配置读写
    ├── ToolWindows/
    │   ├── UsageToolWindow.cs          # 工具窗口宿主
    │   └── UsageToolWindowControl.xaml(.cs)  # 用量面板 UI
    ├── Views/
    │   ├── OllamaStatusBarControl.xaml(.cs)  # 状态栏控件（悬停浮窗 + 按钮）
    │   └── UsageBarRenderer.cs         # 用量条共用渲染器
    └── Resources/
        └── Icon.png                    # 扩展图标
```

## 📄 许可与致谢

本项目为独立开发的开源项目。设计上参考了 [ollama-cloud-usage](https://github.com/longnh0411/ollama-cloud-usage)
（Copyright 2026 Nguyễn Hoàng Long，Apache License 2.0），在此致以诚挚感谢。
