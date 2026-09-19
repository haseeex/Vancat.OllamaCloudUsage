# Vancat.OllamaCloudUsage

在 Visual Studio 中查看 [Ollama Cloud](https://ollama.com) 用量与限额 —— 状态栏实时指示 + 详情工具窗口。

> 设计参考：[ollama-cloud-usage](https://github.com/longnh0411/ollama-cloud-usage)（VS Code 扩展）

**中文** | [English](README.en.md)

## 📷 截图

用量面板（左侧）、状态栏悬停浮窗（右下）与状态栏指示器（底部）：

![概览](docs/screenshots/overview.png)

## ✨ 功能

- **状态栏用量指示器** — 在 Visual Studio 状态栏实时显示会话（5 小时）与每周窗口的用量百分比：
  - 🖱️ **鼠标悬停** — 弹出详细浮窗（用量条、模型请求列表、剩余次数预测、重置倒计时）
  - 👆 **点击** — 直接打开用量面板工具窗口
  - ▤ **快捷按钮** — 状态栏右侧的按钮可一键打开详细面板
- **用量面板工具窗口** — 通过「视图 > 其他窗口 > Ollama Cloud 用量面板」打开：
  - 会话与每周用量条，按模型分色显示请求占比
  - 每个窗口的重置倒计时（实时更新）
  - 本窗口 / 本周使用的模型与请求次数
  - **每个模型占窗口比例** — 该模型占窗口总配额的百分比（详见[计算说明](#-计算说明)）
  - **剩余次数预测** — 窗口级与每个模型单独预测的剩余可请求次数（详见[计算说明](#-计算说明)）
  - 多账户支持：下拉切换、添加、移除账户
- **自动刷新** — 按可配置间隔（默认 60 秒）自动刷新用量。
- **用量精度可配置** — 用量百分比的小数位数可调（0–4 位，默认 1 位），状态栏、悬停浮窗与用量面板同步生效。
- **跨实例共享缓存** — 多个 Visual Studio 实例共享同一份用量缓存，不会重复请求 API。
- **UTC 锚定重置窗口** — 会话窗口按 5 小时边界（00/05/10/15/20 UTC）重置；每周窗口在周一 00:00 UTC 重置。
- **安全存储** — API 密钥经 Windows DPAPI（当前用户范围）加密后存储在 `%APPDATA%\Vancat\OllamaCloudUsage\accounts.json`。
- **主题自适应** — 状态栏控件自动适配 VS 浅色/深色/高对比度主题。
- **中英文切换** — 界面支持中英文双语，通过「扩展 > Ollama Cloud 用量 > 切换语言」一键切换；
  首次运行跟随系统语言，选择结果自动保存。状态栏、悬停浮窗、用量面板、对话框与菜单全部实时切换。

> 💡 **状态栏实现说明**：本扩展将 WPF 控件直接注入 VS 主窗口的 WPF 状态栏视觉树（而非使用 `IVsStatusbar` 文本 API），因此支持悬停浮窗、点击交互与图标显示。

## 📐 计算说明

以下计算全部基于 Ollama Cloud 用量 API（`https://ollama.com/api/usage`）的原始返回值：
`limits.session.usage` / `limits.weekly.usage` 是 0–1 的比例（`0.119` = 11.9%），
`models[].request_count` 是各模型在该窗口内的请求次数。

> 💡 计算使用的是 API 返回的**精确** `usage` 值，而非界面上四舍五入后的百分比。
> 例如界面显示「已用 2%」时，实际 `usage` 可能是 `0.023`（2.3%），所有计算均按 2.3% 进行。

### 1. 窗口用量百分比

直接取自 API：

$$P_{\text{窗口}} = usage \times 100\%$$

### 2. 每个模型占窗口比例

按各模型请求数在窗口总请求数中的占比，分摊窗口用量：

$$P_{\text{模型}} = usage \times \frac{n_{\text{模型}}}{N_{\text{窗口}}}, \qquad N_{\text{窗口}} = \sum_i n_i$$

**性质**：所有模型的占比之和恒等于窗口用量（$\sum P_{\text{模型}} = usage$），可用于交叉验证。

示例（每周窗口 `usage = 0.119`，共 2,140 次请求）：

| 模型 | 请求数 | 占窗口比例 |
|---|---:|---:|
| deepseek-v4.1-flash | 1,243 | 11.9% × 1243/2140 = **6.9%** |
| gemma4:31b | 583 | 11.9% × 583/2140 = **3.2%** |
| deepseek-v4-flash:0731 | 311 | 11.9% × 311/2140 = **1.7%** |
| glm-5.3-flash | 3 | 11.9% × 3/2140 = **0.02%** |
| **合计** | **2,140** | **11.9%** ✓ |

### 3. 剩余次数预测

**窗口容量** — 按当前平均消耗水平，重置前总共可请求的次数：

$$C = \frac{N_{\text{窗口}}}{usage}$$

**窗口级剩余** — 整个窗口还能再请求多少次：

$$R_{\text{窗口}} = C - N_{\text{窗口}} = N_{\text{窗口}} \times \frac{1 - usage}{usage}$$

**模型级剩余** — 该模型单独使用时还能请求多少次：

$$R_{\text{模型}} = C - n_{\text{模型}}$$

> ⚠️ 模型级用的是**窗口容量**减去该模型已用次数，而**不是** $\frac{n_{\text{模型}}}{usage} - n_{\text{模型}}$。
> 后者等价于假设「该模型的请求数消耗了整个窗口的用量」，会得出荒谬结果：
> 例如 glm 只用了 3 次（占周请求 0.14%），却会算出「剩余 22 次」。

示例（每周窗口 `usage = 0.119`，容量 $C = 2140 \div 0.119 \approx 17{,}983$）：

| 项目 | 计算 | 结果 |
|---|---|---:|
| 窗口级 | 17,983 − 2,140 | **15,843** |
| deepseek-v4.1-flash | 17,983 − 1,243 | **16,740** |
| gemma4:31b | 17,983 − 583 | **17,400** |
| deepseek-v4-flash:0731 | 17,983 − 311 | **17,672** |
| glm-5.3-flash | 17,983 − 3 | **17,980** |

**边界处理**：

- `usage = 0`（窗口尚无消耗）或该模型无请求记录 → 不显示预测
- `usage ≥ 100%`（额度用尽或超额）→ 显示 `0`
- 预测值上限 999,999，超出显示 `999,999+`

### 4. 显示精度

用量百分比的小数位数通过「扩展 > Ollama Cloud 用量 > 设置用量显示精度」配置（0–4 位，默认 1 位），
状态栏、悬停浮窗与用量面板同步生效。

模型占窗口比例采用**自适应精度**：若按当前精度会显示成 `0.0%`（有效值被抹掉），
则自动提高精度直到可见（不超过 4 位上限）。例如 glm 的 0.0167% 在默认精度 1 下显示为 `0.02%`。

### 5. 重置窗口

会话（5 小时）窗口按 UTC 5 小时边界（00/05/10/15/20 UTC）对齐，每周窗口锚定周一 00:00 UTC——
与服务端的滚动窗口模型一致，不受本地时区与夏令时影响。

## 🚀 使用

1. 安装扩展（见下方构建说明）。
2. 通过菜单「扩展 > Ollama Cloud 用量 > 添加账户」添加 API 密钥。
3. 状态栏会立即显示用量；**鼠标悬停**可查看详情，**点击**或按 ▤ 按钮打开完整面板。
4. 用量会按配置间隔自动刷新；面板中可点击 ⟳ 手动刷新，⚙ 修改刷新间隔。

### 找不到菜单或面板？

- **重启 Visual Studio**：首次安装 VSIX 后必须重启才能加载扩展。
- **面板位置**：菜单「视图 (V) > 其他窗口 (E) > Ollama Cloud 用量面板」。
- **命令位置**：菜单「扩展 (X) > Ollama Cloud 用量」子菜单。
- **快速入口**：直接点击状态栏的用量文字或 ▤ 按钮。

## 📋 命令

| 命令 | 位置 | 说明 |
|---|---|---|
| 刷新用量 | 扩展 > Ollama Cloud 用量 | 立即刷新（绕过缓存） |
| Ollama Cloud 用量面板 | 视图 > 其他窗口 | 打开详情面板 |
| 添加账户 | 扩展 > Ollama Cloud 用量 | 添加新的 API 密钥 |
| 移除账户 | 扩展 > Ollama Cloud 用量 | 移除已有账户 |
| 设置自动刷新间隔 | 扩展 > Ollama Cloud 用量 | 修改刷新间隔（10–86400 秒） |
| 设置用量显示精度 | 扩展 > Ollama Cloud 用量 | 修改用量百分比的小数位数（0–4，默认 1） |
| 切换语言 (中/EN) | 扩展 > Ollama Cloud 用量 | 在中文与英文界面之间切换 |

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
├── README.md                       # 中文文档
├── README.en.md                    # 英文文档
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
    │   ├── QuotaPredictor.cs           # 剩余请求次数预测（窗口级）
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
