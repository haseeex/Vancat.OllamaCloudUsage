using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>扩展配置读取（基于 VS 设置存储）。</summary>
    public static class Config
    {
        public const int MinRefreshIntervalSeconds = 10;
        public const int MaxRefreshIntervalSeconds = 86400;
        public const int DefaultRefreshIntervalSeconds = 60;

        /// <summary>用量百分比的小数位数范围与默认值。</summary>
        public const int MinUsagePrecision = 0;
        public const int MaxUsagePrecision = 4;
        public const int DefaultUsagePrecision = 1;

        private const string CollectionPath = "Vancat\\OllamaCloudUsage";
        private const string PropertyName = "RefreshIntervalSeconds";
        private const string LanguagePropertyName = "Language";
        private const string UsagePrecisionPropertyName = "UsagePrecision";

        private static WritableSettingsStore _store;

        /// <summary>用量精度内存缓存：即使 VS 设置存储不可用（设计时/测试环境），
        /// 修改也能在当前会话内立即生效。</summary>
        private static int? _usagePrecisionCache;

        /// <summary>显示设置（如用量精度）变更事件，界面组件订阅后重新渲染。</summary>
        public static event EventHandler DisplayChanged;

        /// <summary>触发显示设置变更（供设置修改后调用）。</summary>
        public static void RaiseDisplayChanged()
        {
            DisplayChanged?.Invoke(null, EventArgs.Empty);
        }

        private static WritableSettingsStore GetStore()
        {
            if (_store != null)
            {
                return _store;
            }

            var manager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            _store = manager.GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!_store.CollectionExists(CollectionPath))
            {
                _store.CreateCollection(CollectionPath);
            }

            return _store;
        }

        public static int GetRefreshIntervalSeconds()
        {
            try
            {
                var store = GetStore();
                if (store.PropertyExists(CollectionPath, PropertyName))
                {
                    var value = store.GetInt32(CollectionPath, PropertyName);
                    if (value >= MinRefreshIntervalSeconds && value <= MaxRefreshIntervalSeconds)
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // 设置不可用时使用默认值。
            }

            return DefaultRefreshIntervalSeconds;
        }

        public static void SetRefreshIntervalSeconds(int seconds)
        {
            var clamped = Math.Max(MinRefreshIntervalSeconds, Math.Min(MaxRefreshIntervalSeconds, seconds));
            try
            {
                GetStore().SetInt32(CollectionPath, PropertyName, clamped);
            }
            catch
            {
                // 忽略设置写入失败。
            }
        }

        /// <summary>读取已保存的语言名称（未保存时返回 null）。</summary>
        public static string GetLanguageName()
        {
            try
            {
                var store = GetStore();
                if (store.PropertyExists(CollectionPath, LanguagePropertyName))
                {
                    return store.GetString(CollectionPath, LanguagePropertyName);
                }
            }
            catch
            {
                // 设置不可用时视为未保存。
            }

            return null;
        }

        /// <summary>保存语言名称。</summary>
        public static void SetLanguageName(string name)
        {
            try
            {
                GetStore().SetString(CollectionPath, LanguagePropertyName, name);
            }
            catch
            {
                // 忽略设置写入失败。
            }
        }

        /// <summary>读取用量百分比显示精度（小数位数，默认 1）。</summary>
        public static int GetUsagePrecision()
        {
            if (_usagePrecisionCache.HasValue)
            {
                return _usagePrecisionCache.Value;
            }

            var result = DefaultUsagePrecision;
            try
            {
                var store = GetStore();
                if (store.PropertyExists(CollectionPath, UsagePrecisionPropertyName))
                {
                    var value = store.GetInt32(CollectionPath, UsagePrecisionPropertyName);
                    if (value >= MinUsagePrecision && value <= MaxUsagePrecision)
                    {
                        result = value;
                    }
                }
            }
            catch
            {
                // 设置不可用时使用默认值。
            }

            _usagePrecisionCache = result;
            return result;
        }

        /// <summary>保存用量百分比显示精度（超范围时自动限制）。</summary>
        public static void SetUsagePrecision(int precision)
        {
            var clamped = Math.Max(MinUsagePrecision, Math.Min(MaxUsagePrecision, precision));

            // 先写内存，保证设置存储不可用时也能立即生效。
            _usagePrecisionCache = clamped;

            try
            {
                GetStore().SetInt32(CollectionPath, UsagePrecisionPropertyName, clamped);
            }
            catch
            {
                // 忽略设置写入失败。
            }
        }

        /// <summary>
        /// 按当前精度把用量比例（0–1）格式化为百分比数字文本（不含 % 号）。
        /// 例：precision=1 → "2.3"；precision=0 → "2"；precision=2 → "2.30"。
        /// </summary>
        public static string FormatUsagePercent(double usage)
        {
            var precision = GetUsagePrecision();
            var value = usage * 100.0;

            // 负值（异常数据）按 0 显示，避免出现 "-0.0"。
            if (value < 0)
            {
                value = 0;
            }

            return value.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 按当前精度格式化「占比」类百分比（模型占窗口比例）。
        /// 占比数值通常远小于总用量（如 0.0167%），若当前精度下会显示成 0，
        /// 自动提高精度直到可见（不超过上限），避免有效值被显示为 0.0%。
        /// </summary>
        public static string FormatSharePercent(double fraction)
        {
            var precision = GetUsagePrecision();
            var value = fraction * 100.0;

            if (value < 0)
            {
                value = 0;
            }

            var text = value.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

            while (value > 0 && precision < MaxUsagePrecision)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed != 0)
                {
                    break;
                }

                precision++;
                text = value.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }

            return text;
        }

        public static string FormatInterval(int seconds)
        {
            if (seconds % 3600 == 0 && seconds >= 3600)
            {
                return Loc.T("Interval.Hours", seconds / 3600);
            }

            if (seconds % 60 == 0 && seconds >= 60)
            {
                return Loc.T("Interval.Minutes", seconds / 60);
            }

            return Loc.T("Interval.Seconds", seconds);
        }
    }
}
