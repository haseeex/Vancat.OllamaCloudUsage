using System;
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

        private const string CollectionPath = "Vancat\\OllamaCloudUsage";
        private const string PropertyName = "RefreshIntervalSeconds";

        private static WritableSettingsStore _store;

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

        public static string FormatInterval(int seconds)
        {
            if (seconds % 3600 == 0 && seconds >= 3600)
            {
                return $"{seconds / 3600} 小时";
            }

            if (seconds % 60 == 0 && seconds >= 60)
            {
                return $"{seconds / 60} 分钟";
            }

            return $"{seconds} 秒";
        }
    }
}
