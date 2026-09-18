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
        private const string LanguagePropertyName = "Language";

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
