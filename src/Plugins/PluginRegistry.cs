using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZhuaQianDesktopApp
{
    public class PluginRegistry
    {
        private readonly Dictionary<string, PluginInfo> _plugins;
        private readonly List<PluginPackage> _packages;
        private readonly PluginCompatibilityValidator _compatibilityValidator;

        public PluginRegistry()
        {
            _plugins = new Dictionary<string, PluginInfo>();
            _packages = new List<PluginPackage>();
            _compatibilityValidator = new PluginCompatibilityValidator();
        }

        public void RegisterPlugin(PluginInfo plugin)
        {
            if (plugin == null)
                throw new ArgumentNullException(nameof(plugin));

            if (_plugins.ContainsKey(plugin.Id))
                throw new InvalidOperationException($"Plugin with ID '{plugin.Id}' is already registered");

            _plugins[plugin.Id] = plugin;
        }

        public PluginInfo GetPlugin(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentNullException(nameof(id));

            return _plugins.ContainsKey(id) ? _plugins[id] : null;
        }

        public List<PluginInfo> GetAllPlugins()
        {
            return new List<PluginInfo>(_plugins.Values);
        }

        public List<PluginInfo> GetPluginsByCategory(string category)
        {
            return _plugins.Values
                .Where(p => p.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        public bool IsPluginAvailable(string id)
        {
            var plugin = GetPlugin(id);
            return plugin?.Status == PluginStatus.Available;
        }

        public PluginCompatibilityResult ValidatePluginCompatibility(string id, string targetEcosystem)
        {
            var plugin = GetPlugin(id);
            if (plugin == null)
                return new PluginCompatibilityResult { IsCompatible = false, Reason = "Plugin not found" };

            return _compatibilityValidator.Validate(plugin, targetEcosystem);
        }

        public Task<PluginInstallResult> InstallPlugin(PluginPackage package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));

            try
            {
                var pluginInfo = new PluginInfo
                {
                    Id = package.Id,
                    Name = package.Name,
                    Version = package.Version,
                    Author = package.Author,
                    Description = package.Description,
                    Category = package.Category,
                    FilePath = package.FilePath,
                    Status = PluginStatus.Installing,
                    Compatibility = new PluginCompatibility
                    {
                        TencentCompatible = package.TencentCompatible,
                        AlibabaCompatible = package.AlibabaCompatible,
                        CrossPlatform = package.CrossPlatform
                    },
                    Metadata = new PluginMetadata
                    {
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Tags = package.Tags,
                        Dependencies = package.Dependencies
                    }
                };

                RegisterPlugin(pluginInfo);
                _packages.Add(package);

                pluginInfo.Status = PluginStatus.Available;

                return Task.FromResult(new PluginInstallResult
                {
                    Success = true,
                    PluginId = pluginInfo.Id,
                    Message = $"Plugin '{pluginInfo.Name}' installed successfully"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new PluginInstallResult
                {
                    Success = false,
                    PluginId = package?.Id,
                    Message = $"Failed to install plugin: {ex.Message}"
                });
            }
        }

        public Task<PluginOperationResult> ExecutePlugin(string pluginId, PluginExecutionContext context)
        {
            var plugin = GetPlugin(pluginId);
            if (plugin == null)
                return Task.FromResult(new PluginOperationResult
                {
                    Success = false,
                    Message = $"Plugin with ID '{pluginId}' not found"
                });

            try
            {
                if (plugin.Status != PluginStatus.Available)
                    throw new InvalidOperationException($"Plugin '{plugin.Name}' is not available");

                var result = new PluginOperationResult
                {
                    Success = true,
                    PluginId = pluginId,
                    ExecutionTime = DateTime.UtcNow
                };

                if (plugin.ExecutionHandler != null)
                {
                    result.Output = plugin.ExecutionHandler(context);
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new PluginOperationResult
                {
                    Success = false,
                    PluginId = pluginId,
                    Error = ex,
                    Message = $"Plugin execution failed: {ex.Message}"
                });
            }
        }

        public async Task<PrioritizedPluginList> GetPluginsForUser(string userId, EcosystemTarget ecosystem)
        {
            var allPlugins = GetAllPlugins();
            var eligiblePlugins = new List<PluginInfo>();

            foreach (var plugin in allPlugins)
            {
                var compatibilityResult = ValidatePluginCompatibility(plugin.Id, ecosystem.ToString());
                if (compatibilityResult.IsCompatible)
                {
                    eligiblePlugins.Add(plugin);
                }
            }

            var prioritizedList = new PrioritizedPluginList
            {
                Ecosystem = ecosystem,
                Plugins = eligiblePlugins.OrderByDescending(p => GetPluginPriority(p, userId)).ToList(),
                TotalCount = eligiblePlugins.Count,
                Version = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            await _storage.SaveAsync($"plugins_{ecosystem}_{userId}", prioritizedList);
            return prioritizedList;
        }

        private int GetPluginPriority(PluginInfo plugin, string userId)
        {
            var basePriority = 0;
            
            if (plugin.Compatibility?.CrossPlatform == true)
                basePriority += 100;
            
            if (plugin.Compatibility?.TencentCompatible == true &&
                plugin.Compatibility?.AlibabaCompatible == true)
                basePriority += 50;
            
            if (plugin.Metadata?.Tags?.Contains("official") == true)
                basePriority += 75;
            
            return basePriority;
        }

        private CrossPlatformStorage _storage = new CrossPlatformStorage();
    }

    // Supporting classes for plugin management
    public class PluginInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public PluginStatus Status { get; set; }
        public string FilePath { get; set; }
        public PluginCompatibility Compatibility { get; set; }
        public PluginMetadata Metadata { get; set; }
        public Func<PluginExecutionContext, string> ExecutionHandler { get; set; }
    }

    public class PluginPackage
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string FilePath { get; set; }
        public bool TencentCompatible { get; set; }
        public bool AlibabaCompatible { get; set; }
        public bool CrossPlatform { get; set; }
        public List<string> Tags { get; set; }
        public List<PluginDependency> Dependencies { get; set; }
    }

    public class PluginExecutionContext
    {
        public string UserId { get; set; }
        public string ExecutionId { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public DateTime StartTime { get; set; }
        public ExecutionMode Mode { get; set; }
        public PluginSandboxSettings Sandbox { get; set; }
    }

    public class PluginCompatibility
    {
        public bool TencentCompatible { get; set; }
        public bool AlibabaCompatible { get; set; }
        public bool CrossPlatform { get; set; }
        public List<string> CompatibleVersions { get; set; }
        public CompatibilityLevel Level { get; set; }
    }

    public class PluginMetadata
    {
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<string> Tags { get; set; }
        public List<PluginDependency> Dependencies { get; set; }
        public Dictionary<string, object> CustomFields { get; set; }
    }

    public class PluginDependency
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public DependencyScope Scope { get; set; }
        public bool Optional { get; set; }
    }

    public class PluginCompatibilityResult
    {
        public bool IsCompatible { get; set; }
        public string Reason { get; set; }
        public CompatibilityLevel CompatibilityLevel { get; set; }
    }

    public class PluginInstallResult
    {
        public bool Success { get; set; }
        public string PluginId { get; set; }
        public string Message { get; set; }
    }

    public class PluginOperationResult
    {
        public bool Success { get; set; }
        public string PluginId { get; set; }
        public DateTime ExecutionTime { get; set; }
        public string Output { get; set; }
        public Exception Error { get; set; }
        public string Message { get; set; }
    }

    public class PrioritizedPluginList
    {
        public EcosystemTarget Ecosystem { get; set; }
        public List<PluginInfo> Plugins { get; set; }
        public int TotalCount { get; set; }
        public string Version { get; set; }
    }

    public class PluginCompatibilityValidator
    {
        public PluginCompatibilityResult Validate(PluginInfo plugin, string targetEcosystem)
        {
            if (plugin == null)
                return new PluginCompatibilityResult { IsCompatible = false, Reason = "Plugin is null" };

            if (string.IsNullOrEmpty(targetEcosystem))
                return new PluginCompatibilityResult { IsCompatible = false, Reason = "Target ecosystem is null" };

            var ecosystem = Enum.TryParse<EcosystemTarget>(targetEcosystem, out var ecoTarget) ? ecoTarget : EcosystemTarget.All;

            if (ecoTarget == EcosystemTarget.All)
                return new PluginCompatibilityResult
                {
                    IsCompatible = true,
                    Reason = "Plugin compatible with all ecosystems",
                    CompatibilityLevel = CompatibilityLevel.High
                };

            var isCompatible = false;
            var level = CompatibilityLevel.None;

            switch (ecoTarget)
            {
                case EcosystemTarget.Tencent:
                    isCompatible = plugin.Compatibility?.TencentCompatible == true;
                    level = isCompatible ? CompatibilityLevel.High : CompatibilityLevel.None;
                    if (!isCompatible)
                        return new PluginCompatibilityResult
                        {
                            IsCompatible = false,
                            Reason = "Plugin not Tencent compatible",
                            CompatibilityLevel = CompatibilityLevel.None
                        };
                    break;

                case EcosystemTarget.Alibaba:
                    isCompatible = plugin.Compatibility?.AlibabaCompatible == true;
                    level = isCompatible ? CompatibilityLevel.High : CompatibilityLevel.None;
                    if (!isCompatible)
                        return new PluginCompatibilityResult
                        {
                            IsCompatible = false,
                            Reason = "Plugin not Alibaba compatible",
                            CompatibilityLevel = CompatibilityLevel.None
                        };
                    break;

                case EcosystemTarget.CrossPlatform:
                    isCompatible = plugin.Compatibility?.CrossPlatform == true;
                    level = isCompatible ? CompatibilityLevel.High : CompatibilityLevel.Medium;
                    if (!isCompatible)
                        return new PluginCompatibilityResult
                        {
                            IsCompatible = false,
                            Reason = "Plugin not cross-platform compatible",
                            CompatibilityLevel = CompatibilityLevel.None
                        };
                    break;
            }

            return new PluginCompatibilityResult
            {
                IsCompatible = true,
                Reason = $"Plugin compatible with {targetEcosystem}",
                CompatibilityLevel = level
            };
        }
    }

    public class CrossPlatformStorage
    {
        private readonly Dictionary<string, object> _store;

        public CrossPlatformStorage()
        {
            _store = new Dictionary<string, object>();
        }

        public async Task SaveAsync(string key, object data)
        {
            _store[key] = data;
            await Task.CompletedTask;
        }

        public async Task<T> GetAsync<T>(string key)
        {
            if (_store.ContainsKey(key))
            {
                return (T)_store[key];
            }

            return default;
        }

        public async Task DeleteAsync(string key)
        {
            _store.Remove(key);
            await Task.CompletedTask;
        }
    }

    public enum PluginStatus
    {
        Unknown,
        Installing,
        Available,
        Running,
        Error,
        Disabled
    }

    public enum EcosystemTarget
    {
        All,
        Tencent,
        Alibaba,
        CrossPlatform
    }

    public enum DependencyScope
    {
        Runtime,
        BuildTime,
        Optional
    }

    public enum CompatibilityLevel
    {
        None,
        Low,
        Medium,
        High
    }

    public enum ExecutionMode
    {
        Interactive,
        Automated,
        Scheduled,
        EventDriven
    }
}