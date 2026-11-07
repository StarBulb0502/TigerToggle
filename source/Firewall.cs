using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AppNetworkSwitch
{
    public sealed class RuleRecord
    {
        public string Name;
        public string Group;
        public string Description;
        public string Program;
        public bool Enabled;
        public int Direction;
        public int Action;
        public int Profiles;
        public int Protocol;
        public string LocalAddresses = "*";
        public string RemoteAddresses = "*";
        public string InterfaceTypes = "All";
        public int InterfaceCount;
        public string ServiceName;
    }

    public interface IRuleStore
    {
        IList<RuleRecord> Read();
        void Add(RuleRecord rule);
        void Remove(string name);
    }

    public static class RuleManager
    {
        public const string Group = "App Network Switch v1";
        public const string Description = "Managed by App Network Switch v1.";
        public const string Prefix = "AppNetworkSwitch.v1.";
        public const int AllProfiles = 2147483647;

        public static string NormalizePath(string path, bool requireFile)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || Path.GetPathRoot(path).Length < 3)
                throw new ArgumentException("请选择程序的完整路径。");
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                throw new ArgumentException("请使用本机磁盘中的程序。");
            if (!String.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择 .exe 程序文件。");
            if (requireFile && !File.Exists(fullPath))
                throw new FileNotFoundException("程序文件已移动或不存在，请重新选择。", fullPath);
            return fullPath;
        }

        public static string GetRuleName(string path)
        {
            string normalized = NormalizePath(path, false).ToUpperInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                return Prefix + BitConverter.ToString(hash).Replace("-", "");
            }
        }

        public static bool Owns(RuleRecord rule, string path)
        {
            return String.Equals(rule.Name, GetRuleName(path), StringComparison.OrdinalIgnoreCase) && rule.Group == Group &&
                rule.Description == Description &&
                String.Equals(rule.Program, NormalizePath(path, false), StringComparison.OrdinalIgnoreCase);
        }

        public static RuleRecord FindOwned(IRuleStore store, string path)
        {
            string name = GetRuleName(path);
            RuleRecord found = null;
            foreach (RuleRecord rule in store.Read())
            {
                if (!String.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null || !Owns(rule, path))
                    throw new InvalidOperationException("发现同名或归属不明确的规则。为保护现有设置，操作已停止。");
                found = rule;
            }
            return found;
        }

        public static bool IsBlocked(RuleRecord rule)
        {
            return rule != null && rule.Enabled && rule.Direction == 2 && rule.Action == 0 &&
                rule.Profiles == AllProfiles && rule.Protocol == 256 &&
                rule.LocalAddresses == "*" && rule.RemoteAddresses == "*" &&
                String.Equals(rule.InterfaceTypes, "All", StringComparison.OrdinalIgnoreCase) &&
                rule.InterfaceCount == 0 && String.IsNullOrEmpty(rule.ServiceName);
        }

        public static void Block(IRuleStore store, string path)
        {
            path = NormalizePath(path, true);
            RuleRecord existing = FindOwned(store, path);
            if (IsBlocked(existing)) return;
            if (existing != null)
                throw new InvalidOperationException("本工具的已有规则设置发生变化。请先撤销，再重新启用。");
            store.Add(new RuleRecord {
                Name = GetRuleName(path), Group = Group, Description = Description,
                Program = path, Enabled = true, Direction = 2, Action = 0,
                Profiles = AllProfiles, Protocol = 256
            });
            if (!IsBlocked(FindOwned(store, path)))
                throw new InvalidOperationException("规则写入后未通过核验，请刷新状态检查。");
        }

        public static void Unblock(IRuleStore store, string path)
        {
            path = NormalizePath(path, false);
            if (FindOwned(store, path) == null) return;
            store.Remove(GetRuleName(path));
            if (FindOwned(store, path) != null)
                throw new InvalidOperationException("规则撤销后仍存在，请刷新状态检查。");
        }
    }

    public sealed class WindowsRuleStore : IRuleStore, IDisposable
    {
        private object policy;

        public WindowsRuleStore()
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true);
            policy = Activator.CreateInstance(type);
        }

        public IList<RuleRecord> Read()
        {
            var records = new List<RuleRecord>();
            dynamic currentPolicy = policy;
            object collection = currentPolicy.Rules;
            try
            {
                foreach (object item in (System.Collections.IEnumerable)collection)
                {
                    try
                    {
                        dynamic rule = item;
                        object interfaces = rule.Interfaces;
                        Array interfaceArray = interfaces as Array;
                        records.Add(new RuleRecord {
                            Name = rule.Name, Group = rule.Grouping, Description = rule.Description,
                            Program = rule.ApplicationName, Enabled = rule.Enabled,
                            Direction = rule.Direction, Action = rule.Action,
                            Profiles = rule.Profiles, Protocol = rule.Protocol,
                            LocalAddresses = rule.LocalAddresses, RemoteAddresses = rule.RemoteAddresses,
                            InterfaceTypes = rule.InterfaceTypes, ServiceName = rule.ServiceName,
                            InterfaceCount = interfaces == null ? 0 : interfaceArray != null ? interfaceArray.Length : 1
                        });
                    }
                    finally { Marshal.FinalReleaseComObject(item); }
                }
            }
            finally { Marshal.FinalReleaseComObject(collection); }
            return records;
        }

        public void Add(RuleRecord record)
        {
            object item = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
            object collection = null;
            try
            {
                dynamic rule = item;
                rule.Name = record.Name;
                rule.Description = record.Description;
                rule.Grouping = record.Group;
                rule.ApplicationName = record.Program;
                rule.Protocol = record.Protocol;
                rule.Direction = record.Direction;
                rule.Profiles = record.Profiles;
                rule.LocalAddresses = "*";
                rule.RemoteAddresses = "*";
                rule.InterfaceTypes = "All";
                rule.Action = record.Action;
                rule.Enabled = record.Enabled;
                dynamic currentPolicy = policy;
                collection = currentPolicy.Rules;
                ((dynamic)collection).Add(rule);
            }
            finally
            {
                if (collection != null) Marshal.FinalReleaseComObject(collection);
                Marshal.FinalReleaseComObject(item);
            }
        }

        public void Remove(string name)
        {
            dynamic currentPolicy = policy;
            object collection = currentPolicy.Rules;
            try { ((dynamic)collection).Remove(name); }
            finally { Marshal.FinalReleaseComObject(collection); }
        }

        public string GetHealth()
        {
            int active = (int)policy.GetType().InvokeMember("CurrentProfileTypes",
                BindingFlags.GetProperty, null, policy, null);
            foreach (int profile in new int[] { 1, 2, 4 })
            {
                if ((active & profile) == 0) continue;
                bool enabled = (bool)policy.GetType().InvokeMember("FirewallEnabled",
                    BindingFlags.GetProperty, null, policy, new object[] { profile });
                if (!enabled) return "当前网络的防火墙已关闭，阻止规则可能无法生效。";
            }
            int modifyState = (int)policy.GetType().InvokeMember("LocalPolicyModifyState",
                BindingFlags.GetProperty, null, policy, null);
            if (modifyState != 0) return "此设备受策略限制，本地规则可能无法生效。";
            return "Windows 防火墙已开启";
        }

        public void Dispose()
        {
            if (policy != null) { Marshal.FinalReleaseComObject(policy); policy = null; }
        }
    }
}
