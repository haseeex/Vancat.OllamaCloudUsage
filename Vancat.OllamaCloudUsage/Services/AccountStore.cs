using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vancat.OllamaCloudUsage.Services
{
    /// <summary>单个账户。</summary>
    public sealed class Account
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Key { get; set; }
    }

    /// <summary>账户集合状态。</summary>
    public sealed class AccountsState
    {
        public List<Account> Accounts { get; set; } = new List<Account>();
        public string ActiveId { get; set; }

        public Account Active => Accounts.FirstOrDefault(a => a.Id == ActiveId);
    }

    /// <summary>
    /// 账户存储：API 密钥经 Windows DPAPI（当前用户范围）加密后落盘，
    /// 文件位于 %APPDATA%\Vancat\OllamaCloudUsage\accounts.json。
    /// </summary>
    public sealed class AccountStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Vancat.OllamaCloudUsage.v1");

        private readonly string _filePath;
        private readonly object _sync = new object();

        public AccountStore(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "vancat", "OllamaCloudUsage", "accounts.json");
        }

        public AccountsState Load()
        {
            lock (_sync)
            {
                if (!File.Exists(_filePath))
                {
                    return new AccountsState();
                }

                try
                {
                    var json = File.ReadAllText(_filePath, Encoding.UTF8);
                    var state = JsonConvert.DeserializeObject<AccountsState>(json) ?? new AccountsState();
                    state.Accounts = state.Accounts ?? new List<Account>();
                    foreach (var account in state.Accounts)
                    {
                        account.Key = Unprotect(account.Key);
                    }

                    return state;
                }
                catch
                {
                    // 文件损坏时返回空状态，避免阻断扩展。
                    return new AccountsState();
                }
            }
        }

        public void Save(AccountsState state)
        {
            lock (_sync)
            {
                var clone = new AccountsState
                {
                    ActiveId = state.ActiveId,
                    Accounts = state.Accounts.Select(a => new Account
                    {
                        Id = a.Id,
                        Label = a.Label,
                        Key = Protect(a.Key),
                    }).ToList(),
                };

                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_filePath, JsonConvert.SerializeObject(clone, Formatting.Indented), Encoding.UTF8);
            }
        }

        public AccountsState Add(string label, string key)
        {
            var state = Load();
            var account = new Account
            {
                Id = NewId(),
                Label = string.IsNullOrWhiteSpace(label) ? "账户" : label.Trim(),
                Key = key.Trim(),
            };
            state.Accounts.Add(account);
            state.ActiveId = account.Id;
            Save(state);
            return state;
        }

        public AccountsState Remove(string id)
        {
            var state = Load();
            state.Accounts = state.Accounts.Where(a => a.Id != id).ToList();
            if (state.ActiveId == id)
            {
                state.ActiveId = state.Accounts.FirstOrDefault()?.Id;
            }

            Save(state);
            return state;
        }

        public AccountsState SetActive(string id)
        {
            var state = Load();
            if (state.Accounts.Any(a => a.Id == id))
            {
                state.ActiveId = id;
                Save(state);
            }

            return state;
        }

        private static string NewId()
        {
            var bytes = new byte[6];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
            {
                return string.Empty;
            }

            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(bytes);
            }
            catch
            {
                // DPAPI 不可用时退化为明文（极少见）。
                return plain;
            }
        }

        private static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                return stored;
            }

            try
            {
                var bytes = Convert.FromBase64String(stored.Substring("dpapi:".Length));
                var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
