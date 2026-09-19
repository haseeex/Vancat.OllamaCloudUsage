using System;
using System.Collections.Generic;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>支持的语言。</summary>
    public enum AppLanguage
    {
        Chinese,
        English,
    }

    /// <summary>
    /// 本地化中心：集中管理所有 UI 字符串的中英文对照。
    /// 语言选择持久化在 VS 设置存储中，切换时触发 LanguageChanged 事件，
    /// 各 UI 组件订阅该事件以即时刷新界面。
    /// </summary>
    public static class Loc
    {
        private const string LanguagePropertyName = "Language";

        private static AppLanguage _current = DetectDefault();

        /// <summary>语言切换事件（在调用方线程触发）。</summary>
        public static event EventHandler LanguageChanged;

        /// <summary>当前语言。</summary>
        public static AppLanguage Current
        {
            get => _current;
            set
            {
                if (_current == value)
                {
                    return;
                }

                _current = value;

                try
                {
                    Config.SetLanguageName(value.ToString());
                }
                catch
                {
                    // VS 设置存储不可用时仅保留内存状态（如设计时/测试环境）。
                }

                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>按系统区域检测默认语言（首次运行时使用）。</summary>
        private static AppLanguage DetectDefault()
        {
            try
            {
                var saved = Config.GetLanguageName();
                if (!string.IsNullOrEmpty(saved) &&
                    Enum.TryParse(saved, ignoreCase: true, result: out AppLanguage parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // VS 设置存储不可用（如设计时/测试环境）时回退到系统语言。
            }

            // 跟随系统：中文系统默认中文，其余默认英文。
            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Chinese
                : AppLanguage.English;
        }

        /// <summary>是否为中文界面。</summary>
        public static bool IsChinese => _current == AppLanguage.Chinese;

        // ---- 字符串表 ----

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 状态栏
            ["StatusBar.Text"] = "Ollama  5h: {0}%  Wk: {1}%",
            ["StatusBar.Loading"] = "Ollama …",
            ["StatusBar.NoData"] = "Ollama —",
            ["StatusBar.Tooltip"] = "Open Ollama Cloud usage panel",

            // 浮窗
            ["Hover.Title"] = "☁ Ollama Cloud Usage",
            ["Hover.NoData"] = "No usage data.",
            ["Hover.SessionWindow"] = "5-hour window",
            ["Hover.Remaining"] = "≈{0} left",
            ["Hover.RemainingTip"] = "Estimated from this window's average consumption: about {0} more requests available before reset",
            ["Hover.WeeklyWindow"] = "Weekly window",
            ["Hover.ResetIn"] = "Resets in: ",
            ["Hover.Refresh"] = "Auto-refresh every {0}",
            ["Hover.LastUpdated"] = " · Last {0}",
            ["Hover.ClickToOpen"] = "\nClick to open detail panel",

            // 工具窗口
            ["Panel.Title"] = "☁ Cloud Usage",
            ["Panel.Refresh"] = "Refresh",
            ["Panel.Settings"] = "Set auto-refresh interval",
            ["Panel.RemoveAccount"] = "Remove account",
            ["Panel.AddAccount"] = "Add account",
            ["Panel.AddKey"] = "Add API key",
            ["Panel.SessionWindow"] = "5-hour window usage",
            ["Panel.WeeklyWindow"] = "Weekly window usage",
            ["Panel.SessionModels"] = "Models this window",
            ["Panel.WeeklyModels"] = "Models this week",
            ["Panel.Used"] = "Used {0}%",
            ["Panel.Remaining"] = "≈{0} req. left",
            ["Panel.RemainingTip"] = "Estimated from this window's average consumption: about {0} more requests available before reset",
            ["Panel.Activity"] = "Activity",
            ["Panel.Cost"] = "Cost: ${0}",
            ["Panel.Period"] = "Period: {0}",
            ["Panel.Loading"] = "Loading Ollama usage…",
            ["Panel.NoData"] = "No data.",
            ["Panel.ResetIn"] = "Resets in: ",
            ["Panel.AutoRefresh"] = "Auto-refresh every {0}",
            ["Panel.LastUpdated"] = "Last updated {0}",

            // 模型列表
            ["Models.None"] = "No model requests",
            ["Models.Requests"] = "{0} req.",
            ["Models.WindowShare"] = "{0}%",
            ["Models.WindowShareTip"] = "This model accounts for {0}% of the window's quota (window usage {1}% × this model's share of requests)",
            ["Models.Remaining"] = "· ≈{0} left",
            ["Models.RemainingTip"] = "If this model were used exclusively: window capacity {0} − requests already made by this model = about {1} more requests left",


            // 时间
            ["Time.Days"] = "{0} d",
            ["Time.Hours"] = "{0} h",
            ["Time.HoursMinutes"] = "{0} h {1} min",
            ["Time.Minutes"] = "{0} min",
            ["Time.Seconds"] = "{0} s",
            ["Interval.Hours"] = "{0} h",
            ["Interval.Minutes"] = "{0} min",
            ["Interval.Seconds"] = "{0} s",

            // 命令（菜单）
            ["Cmd.SubMenu"] = "Ollama Cloud Usage",
            ["Cmd.Refresh"] = "Refresh Usage",
            ["Cmd.OpenPanel"] = "Ollama Cloud Usage Panel",
            ["Cmd.AddAccount"] = "Add Account",
            ["Cmd.RemoveAccount"] = "Remove Account",
            ["Cmd.SetInterval"] = "Set Refresh Interval",
            ["Cmd.ToggleLang"] = "Switch Language (中/EN)",
            ["Cmd.SetPrecision"] = "Set Usage Display Precision",

            // 对话框
            ["Dlg.Ok"] = "OK",
            ["Dlg.Cancel"] = "Cancel",
            ["Dlg.AddAccountTitle"] = "Add Account",
            ["Dlg.AccountLabel"] = "Account name (e.g. Work, Personal)",
            ["Dlg.AccountLabelRequired"] = "Name cannot be empty.",
            ["Dlg.ApiKey"] = "Enter Ollama API key",
            ["Dlg.ApiKeyRequired"] = "API key cannot be empty.",
            ["Dlg.RemoveAccountTitle"] = "Select account to remove",
            ["Dlg.PickAccountTitle"] = "Select account",
            ["Dlg.PickAccountPrompt"] = "Please select an account:",
            ["Dlg.IntervalTitle"] = "Set Auto-refresh Interval",
            ["Dlg.IntervalPrompt"] = "Auto-refresh interval (seconds), range {0}–{1}",
            ["Dlg.IntervalInvalid"] = "Please enter a number.",
            ["Dlg.IntervalRange"] = "Interval must be between {0} and {1} seconds.",
            ["Dlg.PrecisionTitle"] = "Set Usage Display Precision",
            ["Dlg.PrecisionPrompt"] = "Decimal places for usage percentage, range {0}–{1} (default 1)",
            ["Dlg.PrecisionInvalid"] = "Please enter a number.",
            ["Dlg.PrecisionRange"] = "Precision must be between {0} and {1}.",

            // 消息
            ["Msg.CannotCreatePanel"] = "Failed to create usage panel window.",
            ["Msg.AccountAdded"] = "Account added.",
            ["Msg.AccountRemoved"] = "Account removed.",
            ["Msg.IntervalSet"] = "Auto-refresh interval set to {0}.",
            ["Msg.LanguageSet"] = "Language switched to English.",
            ["Msg.PrecisionSet"] = "Usage display precision set to {0} decimal place(s).",

            // 错误
            ["Err.NoApiKey"] = "No Ollama API key configured. Please add an account first.",
            ["Err.EmptyKey"] = "Ollama API key is empty.",
            ["Err.Timeout"] = "Ollama request timed out.",
            ["Err.CannotConnect"] = "Cannot connect to Ollama Cloud: {0}",
            ["Err.HttpStatus"] = "Ollama returned HTTP {0}.",
            ["Err.ResponseTooLarge"] = "Ollama response too large.",
            ["Err.InvalidResponse"] = "Invalid Ollama response.",
            ["Err.InvalidField"] = "Invalid {0} in Ollama response.",
            ["Err.RefreshFailed"] = "Refresh failed: {0}",

            // 面板标题
            ["Window.Title"] = "Ollama Cloud Usage",
        };

        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 状态栏
            ["StatusBar.Text"] = "Ollama  5时: {0}%  周: {1}%",
            ["StatusBar.Loading"] = "Ollama …",
            ["StatusBar.NoData"] = "Ollama —",
            ["StatusBar.Tooltip"] = "打开 Ollama Cloud 用量面板",

            // 浮窗
            ["Hover.Title"] = "☁ Ollama Cloud 用量",
            ["Hover.NoData"] = "暂无用量数据。",
            ["Hover.SessionWindow"] = "5 小时窗口",
            ["Hover.Remaining"] = "剩余约 {0} 次",
            ["Hover.RemainingTip"] = "按当前窗口平均消耗估算：重置前还可请求约 {0} 次（总请求数 ÷ 已用比例 − 总请求数）",
            ["Hover.WeeklyWindow"] = "每周窗口",
            ["Hover.ResetIn"] = "重置倒计时：",
            ["Hover.Refresh"] = "每 {0}自动刷新",
            ["Hover.LastUpdated"] = " · 上次 {0}",
            ["Hover.ClickToOpen"] = "\n点击打开详细面板",

            // 工具窗口
            ["Panel.Title"] = "☁ 云端用量",
            ["Panel.Refresh"] = "刷新",
            ["Panel.Settings"] = "设置自动刷新间隔",
            ["Panel.RemoveAccount"] = "移除账户",
            ["Panel.AddAccount"] = "添加账户",
            ["Panel.AddKey"] = "添加 API 密钥",
            ["Panel.SessionWindow"] = "5 小时窗口用量",
            ["Panel.WeeklyWindow"] = "每周窗口用量",
            ["Panel.SessionModels"] = "本窗口使用的模型",
            ["Panel.WeeklyModels"] = "本周使用的模型",
            ["Panel.Used"] = "已用 {0}%",
            ["Panel.Remaining"] = "剩余约 {0} 次",
            ["Panel.RemainingTip"] = "按当前窗口平均消耗估算：重置前还可请求约 {0} 次（总请求数 ÷ 已用比例 − 总请求数）",
            ["Panel.Activity"] = "活动",
            ["Panel.Cost"] = "费用：${0}",
            ["Panel.Period"] = "周期：{0}",
            ["Panel.Loading"] = "正在加载 Ollama 用量…",
            ["Panel.NoData"] = "暂无数据。",
            ["Panel.ResetIn"] = "重置倒计时：",
            ["Panel.AutoRefresh"] = "每 {0}自动刷新",
            ["Panel.LastUpdated"] = "上次更新 {0}",

            // 模型列表
            ["Models.None"] = "无模型请求",
            ["Models.Requests"] = "{0} 次",
            ["Models.WindowShare"] = "{0}%",
            ["Models.WindowShareTip"] = "该模型占用窗口 {0}% 的配额（窗口用量 {1}% × 该模型请求占比）",
            ["Models.Remaining"] = "· 剩余约 {0} 次",
            ["Models.RemainingTip"] = "若仅使用该模型：窗口容量 {0} 次 − 该模型已用次数 = 还可请求约 {1} 次",

            // 时间
            ["Time.Days"] = "{0} 天",
            ["Time.Hours"] = "{0} 小时",
            ["Time.HoursMinutes"] = "{0} 小时 {1} 分钟",
            ["Time.Minutes"] = "{0} 分钟",
            ["Time.Seconds"] = "{0} 秒",
            ["Interval.Hours"] = "{0} 小时",
            ["Interval.Minutes"] = "{0} 分钟",
            ["Interval.Seconds"] = "{0} 秒",

            // 命令（菜单）
            ["Cmd.SubMenu"] = "Ollama Cloud 用量",
            ["Cmd.Refresh"] = "刷新用量",
            ["Cmd.OpenPanel"] = "Ollama Cloud 用量面板",
            ["Cmd.AddAccount"] = "添加账户",
            ["Cmd.RemoveAccount"] = "移除账户",
            ["Cmd.SetInterval"] = "设置自动刷新间隔",
            ["Cmd.ToggleLang"] = "切换语言 (中/EN)",
            ["Cmd.SetPrecision"] = "设置用量显示精度",

            // 对话框
            ["Dlg.Ok"] = "确定",
            ["Dlg.Cancel"] = "取消",
            ["Dlg.AddAccountTitle"] = "添加账户",
            ["Dlg.AccountLabel"] = "账户名称（例如：工作、个人）",
            ["Dlg.AccountLabelRequired"] = "名称不能为空。",
            ["Dlg.ApiKey"] = "输入 Ollama API 密钥",
            ["Dlg.ApiKeyRequired"] = "API 密钥不能为空。",
            ["Dlg.RemoveAccountTitle"] = "选择要移除的账户",
            ["Dlg.PickAccountTitle"] = "选择账户",
            ["Dlg.PickAccountPrompt"] = "请选择账户：",
            ["Dlg.IntervalTitle"] = "设置自动刷新间隔",
            ["Dlg.IntervalPrompt"] = "自动刷新间隔（秒），范围 {0}–{1}",
            ["Dlg.IntervalInvalid"] = "请输入数字。",
            ["Dlg.IntervalRange"] = "间隔需在 {0}–{1} 秒之间。",
            ["Dlg.PrecisionTitle"] = "设置用量显示精度",
            ["Dlg.PrecisionPrompt"] = "用量百分比小数位数，范围 {0}–{1}（默认 1）",
            ["Dlg.PrecisionInvalid"] = "请输入数字。",
            ["Dlg.PrecisionRange"] = "精度需在 {0}–{1} 之间。",

            // 消息
            ["Msg.CannotCreatePanel"] = "无法创建用量面板窗口。",
            ["Msg.AccountAdded"] = "账户已添加。",
            ["Msg.AccountRemoved"] = "账户已移除。",
            ["Msg.IntervalSet"] = "自动刷新间隔已设为 {0}。",
            ["Msg.LanguageSet"] = "界面语言已切换为中文。",
            ["Msg.PrecisionSet"] = "用量显示精度已设为 {0} 位小数。",

            // 错误
            ["Err.NoApiKey"] = "尚未配置 Ollama API 密钥，请先添加账户。",
            ["Err.EmptyKey"] = "Ollama API 密钥为空。",
            ["Err.Timeout"] = "Ollama 请求超时。",
            ["Err.CannotConnect"] = "无法连接 Ollama Cloud：{0}",
            ["Err.HttpStatus"] = "Ollama 返回 HTTP {0}。",
            ["Err.ResponseTooLarge"] = "Ollama 响应过大。",
            ["Err.InvalidResponse"] = "Ollama 响应无效。",
            ["Err.InvalidField"] = "Ollama 响应中的 {0} 无效。",
            ["Err.RefreshFailed"] = "刷新失败：{0}",

            // 面板标题
            ["Window.Title"] = "Ollama Cloud 用量",
        };

        /// <summary>取本地化字符串（无参）。</summary>
        public static string T(string key)
        {
            var table = IsChinese ? Zh : En;
            return table.TryGetValue(key, out var value) ? value : key;
        }

        /// <summary>取本地化字符串并格式化。</summary>
        public static string T(string key, params object[] args)
        {
            var format = T(key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}
