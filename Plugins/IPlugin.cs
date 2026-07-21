namespace ShowCuePlayer.Plugins;

/// <summary>
/// Plugin interface for future extensibility.
/// Implement this interface to add new cue types (Video, PDF, MIDI, DMX, etc.)
/// without modifying the core application.
/// </summary>
public interface IPlugin
{
    /// <summary>Unique plugin identifier.</summary>
    string Id { get; }

    /// <summary>Display name shown in menus.</summary>
    string Name { get; }

    /// <summary>Plugin version.</summary>
    Version Version { get; }

    /// <summary>Called once when the plugin is loaded.</summary>
    Task InitializeAsync(IServiceProvider services);

    /// <summary>Called on application shutdown.</summary>
    Task ShutdownAsync();
}
