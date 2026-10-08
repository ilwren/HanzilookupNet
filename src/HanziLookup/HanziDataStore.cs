using System.Collections.Concurrent;

namespace HanziLookup;

/// <summary>
/// A process-wide registry of named repositories.
/// </summary>
/// <remarks>
/// <para>
/// This exists for parity with the JavaScript API, where <c>init(name, url, callback)</c> stores the
/// data in a module level object and <c>new Matcher(name)</c> picks it up again. When you prefer
/// explicitness (recommended), construct <see cref="Matcher"/> directly from a
/// <see cref="HanziData"/> instance instead of using the registry.
/// </para>
/// <para>
/// The registry is thread safe.
/// </para>
/// </remarks>
public static class HanziDataStore
{
    private static readonly ConcurrentDictionary<string, HanziData> Entries = new(StringComparer.Ordinal);

    /// <summary>The names of all registered repositories (a snapshot of the current keys).</summary>
    public static IReadOnlyCollection<string> Names => Entries.Keys.ToArray();

    /// <summary>Registers (or replaces) a repository under <paramref name="name"/>.</summary>
    public static void Register(string name, HanziData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(data);
        Entries[name] = data;
    }

    /// <summary>Loads a repository from a file and registers it, returning the loaded data.</summary>
    public static async Task<HanziData> RegisterAsync(
        string name,
        string path,
        CancellationToken cancellationToken = default)
    {
        var data = await HanziData.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        Register(name, data);
        return data;
    }

    /// <summary>Tries to get a repository by name.</summary>
    public static bool TryGet(string name, out HanziData data)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Entries.TryGetValue(name, out var found))
        {
            data = found;
            return true;
        }

        data = null!;
        return false;
    }

    /// <summary>Gets a repository by name, throwing when it is not registered.</summary>
    public static HanziData Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Entries.TryGetValue(name, out var found))
        {
            return found;
        }

        throw new KeyNotFoundException(
            $"No character data is registered under '{name}'. Register it first, e.g. " +
            $"HanziDataStore.Register(\"{name}\", HanziData.Load(\"data/mmah.json\"));");
    }

    /// <summary>Removes a repository from the registry.</summary>
    public static bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Entries.TryRemove(name, out _);
    }

    /// <summary>Removes all repositories from the registry.</summary>
    public static void Clear() => Entries.Clear();
}
