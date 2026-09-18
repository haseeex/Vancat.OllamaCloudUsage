using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>用量数据更新事件参数。</summary>
    public sealed class UsageUpdatedEventArgs : EventArgs
    {
        public UsageResponse Usage { get; set; }
        public string Error { get; set; }
        public bool Loading { get; set; }
        public AccountsState Accounts { get; set; }
        public long LastUpdatedMs { get; set; }
        public int IntervalSeconds { get; set; }
    }

    /// <summary>
    /// 用量服务：统一负责账户读取、缓存复用、API 拉取与状态广播。
    /// 工具窗口、状态栏与命令均订阅此服务。
    /// </summary>
    public sealed class UsageService : IDisposable
    {
        private readonly AccountStore _accountStore;
        private readonly SharedUsageCache _cache;
        private readonly OllamaApiClient _api;
        private readonly object _sync = new object();

        private Task _inFlight;

        public UsageService(AccountStore accountStore, SharedUsageCache cache, OllamaApiClient api)
        {
            _accountStore = accountStore;
            _cache = cache;
            _api = api;
        }

        public event EventHandler<UsageUpdatedEventArgs> Updated;

        public UsageResponse Usage { get; private set; }
        public string Error { get; private set; }
        public bool Loading { get; private set; }
        public AccountsState Accounts { get; private set; } = new AccountsState();
        public long LastUpdatedMs { get; private set; }

        public int IntervalSeconds => Config.GetRefreshIntervalSeconds();

        public UsageUpdatedEventArgs Snapshot()
        {
            lock (_sync)
            {
                return new UsageUpdatedEventArgs
                {
                    Usage = Usage,
                    Error = Error,
                    Loading = Loading,
                    Accounts = Accounts,
                    LastUpdatedMs = LastUpdatedMs,
                    IntervalSeconds = IntervalSeconds,
                };
            }
        }

        public Task RefreshAsync(bool force)
        {
            lock (_sync)
            {
                if (_inFlight != null)
                {
                    return _inFlight;
                }

                _inFlight = DoRefreshAsync(force);
                return _inFlight;
            }
        }

        private async Task DoRefreshAsync(bool force)
        {
            try
            {
                Accounts = _accountStore.Load();
                var active = Accounts.Active;

                if (active == null || string.IsNullOrWhiteSpace(active.Key))
                {
                    lock (_sync)
                    {
                        Usage = null;
                        Error = "尚未配置 Ollama API 密钥，请先添加账户。";
                        Loading = false;
                    }

                    RaiseUpdated();
                    return;
                }

                // 非强制刷新时优先复用其他实例写入的新鲜缓存。
                if (!force)
                {
                    var snapshot = _cache.Read();
                    if (SharedUsageCache.IsFresh(snapshot, TimeSpan.FromSeconds(IntervalSeconds), active.Id))
                    {
                        var cached = OllamaApiClient.ParseUsage(snapshot.Usage);
                        lock (_sync)
                        {
                            Usage = cached;
                            Error = null;
                            Loading = false;
                            LastUpdatedMs = snapshot.FetchedAtMs;
                        }

                        RaiseUpdated();
                        return;
                    }
                }

                lock (_sync)
                {
                    Loading = true;
                    Error = null;
                }

                RaiseUpdated();

                try
                {
                    // 拉取原始 JSON：解析给界面，原样写入缓存（保留服务端字段名）。
                    var raw = await _api.FetchUsageJsonAsync(active.Key, CancellationToken.None).ConfigureAwait(false);
                    var usage = OllamaApiClient.ParseUsage(raw);
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    lock (_sync)
                    {
                        Usage = usage;
                        Error = null;
                        Loading = false;
                        LastUpdatedMs = nowMs;
                    }

                    _cache.Write(raw, active.Id);
                }
                catch (UsageApiException ex)
                {
                    lock (_sync)
                    {
                        Loading = false;
                        Error = ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        Loading = false;
                        Error = "刷新失败：" + ex.Message;
                    }
                }

                RaiseUpdated();
            }
            finally
            {
                lock (_sync)
                {
                    _inFlight = null;
                }
            }
        }

        private void RaiseUpdated()
        {
            Updated?.Invoke(this, Snapshot());
        }

        public void Dispose()
        {
            _api.Dispose();
        }
    }
}
