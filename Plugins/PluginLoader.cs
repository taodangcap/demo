using Microsoft.Extensions.Logging;
using System.Reflection;

namespace ShowCuePlayer.Plugins;

/// <summary>
/// Discovers and loads plugins from the /Plugins directory at runtime.
/// Plugins are .NET assemblies implementing IPlugin placed in the plugins folder.
/// </summary>
public sealed class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly IServiceProvider _services;
    private readonly List<IPlugin> _loaded = new List<IPlugin>();

    public IReadOnlyList<IPlugin> LoadedPlugins => _loaded.AsReadOnly();
    public string PluginsDirectory { get; }

    public PluginLoader(ILogger<PluginLoader> logger, IServiceProvider services)
    {
        _logger = logger;
        _services = services;
        PluginsDirectory = Path.Combine(AppContext.BaseDirectory, "Plugins");
    }

    public async Task LoadAllAsync()
    {
        if (!Directory.Exists(PluginsDirectory))
        {
            _logger.LogInformation("Plugins directory not found. Skipping plugin load.");
            return;
        }

        foreach (var dll in Directory.GetFiles(PluginsDirectory, "*.dll"))
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                var pluginTypes = asm.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        await plugin.InitializeAsync(_services);
                        _loaded.Add(plugin);
                        _logger.LogInformation("Loaded plugin: {Name} v{Version}", plugin.Name, plugin.Version);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {Dll}", dll);
            }
        }

        _logger.LogInformation("Plugin loading complete. {Count} plugin(s) loaded.", _loaded.Count);
    }

    public async Task UnloadAllAsync()
    {
        foreach (var plugin in _loaded)
        {
            try { await plugin.ShutdownAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Error shutting down plugin {Name}", plugin.Name); }
        }
        _loaded.Clear();
    }
}
