using System.Text.Json;

namespace HanziLookup;

/// <summary>
/// A character repository: the list of characters to match against plus the packed table of their
/// sub-strokes. This is what <c>init()</c> loads in the JavaScript implementation (for example
/// <c>mmah.json</c>, derived from Make Me a Hanzi).
/// </summary>
/// <remarks>
/// <para>
/// The JSON format is:
/// <code>
/// {
///   "chars": [["一", 1, 1, 0], ["丁", 2, 3, 75], ...],
///   "substrokes": "&lt;base64 of 3 packed bytes per sub-stroke&gt;"
/// }
/// </code>
/// </para>
/// <para>
/// A repository can be created from JSON text, a file, a stream, or an HTTP URL. Repositories are
/// immutable once loaded and safe to share between threads (and between <see cref="Matcher"/>s).
/// </para>
/// </remarks>
public sealed class HanziData
{
    private readonly byte[] _subStrokes;
    private Dictionary<string, int>? _characterIndex;

    /// <summary>Creates a repository from already parsed parts.</summary>
    /// <param name="characters">The character entries.</param>
    /// <param name="subStrokes">The packed sub-stroke table (3 bytes per sub-stroke).</param>
    public HanziData(IEnumerable<HanziCharacter> characters, byte[] subStrokes)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(subStrokes);

        if (characters is HanziCharacter[] array)
        {
            CharacterArray = array;
        }
        else
        {
            CharacterArray = characters.ToArray();
        }

        _subStrokes = subStrokes;
    }

    /// <summary>The entries of this repository, in the order of the data file.</summary>
    public IReadOnlyList<HanziCharacter> Characters => CharacterArray;

    /// <summary>Number of characters in this repository.</summary>
    public int Count => CharacterArray.Length;

    /// <summary>The packed sub-stroke table (3 bytes per sub-stroke: direction, length, packed centre).</summary>
    public ReadOnlyMemory<byte> SubStrokes => _subStrokes;

    internal HanziCharacter[] CharacterArray { get; }

    internal byte[] SubStrokeArray => _subStrokes;

    /// <summary>Parses a repository from a JSON string.</summary>
    public static HanziData Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return FromJsonDocument(document);
    }

    /// <summary>Parses a repository from UTF-8 encoded JSON.</summary>
    public static HanziData Parse(ReadOnlyMemory<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json);
        return FromJsonDocument(document);
    }

    /// <summary>Loads a repository from a JSON file.</summary>
    public static HanziData Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Loads a repository from a JSON file.</summary>
    public static async Task<HanziData> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(bytes);
    }

    /// <summary>Loads a repository from a stream containing JSON.</summary>
    public static async Task<HanziData> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
        return FromJsonDocument(document);
    }

    /// <summary>
    /// Downloads a repository from a URL, which is the equivalent of the JavaScript <c>init()</c> helper.
    /// </summary>
    /// <param name="url">Location of the JSON data file.</param>
    /// <param name="httpClient">Optional client to use; a temporary one is used when omitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<HanziData> DownloadAsync(
        Uri url,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (httpClient is not null)
        {
            var bytes = await httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            return Parse(bytes);
        }

        using var client = new HttpClient();
        var downloaded = await client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        return Parse(downloaded);
    }

    /// <summary>
    /// Finds the entry of <paramref name="character"/>, or <c>null</c> when the repository does not
    /// contain it. The lookup dictionary is created lazily on first use.
    /// </summary>
    public HanziCharacter? Find(string character)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (_characterIndex is null)
        {
            var index = new Dictionary<string, int>(CharacterArray.Length, StringComparer.Ordinal);
            for (var i = CharacterArray.Length - 1; i >= 0; --i)
            {
                index[CharacterArray[i].Character] = i;
            }

            _characterIndex = index;
        }

        return _characterIndex.TryGetValue(character, out var ix) ? CharacterArray[ix] : null;
    }

    /// <summary>Returns the raw bytes of the sub-strokes of <paramref name="character"/> (3 bytes per sub-stroke).</summary>
    public ReadOnlySpan<byte> GetSubStrokeBytes(HanziCharacter character)
    {
        var offset = character.SubStrokeOffset;
        var length = character.SubStrokeCount * 3;
        if (offset < 0 || length < 0 || offset + length > _subStrokes.Length)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return new ReadOnlySpan<byte>(_subStrokes, offset, length);
    }

    /// <summary>
    /// Merges several repositories into one, so that a single <see cref="Matcher"/> can search all
    /// of them at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes it possible to recognize, for example, Chinese characters and digits with
    /// the same matcher: the packed table of each repository is appended to the next one and the
    /// character rows are re-pointed at their new offsets.  Because the matcher aligns sub-stroke
    /// sequences with skip penalties, searching one combined repository is not the same as searching
    /// the repositories separately and merging the results - a candidate survives the competition of
    /// the whole set rather than only of its own script.
    /// </para>
    /// <para>
    /// Characters are kept in the order they are given; when the same character appears in more than
    /// one repository the first one wins, so put the preferred repository first.
    /// </para>
    /// </remarks>
    /// <param name="repositories">The repositories to merge, in order.</param>
    public static HanziData Concat(params HanziData[] repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        return Concat((IEnumerable<HanziData>)repositories);
    }

    /// <summary>Merges several repositories into one, so that a single <see cref="Matcher"/> can search all of them.</summary>
    /// <param name="repositories">The repositories to merge, in order.</param>
    public static HanziData Concat(IEnumerable<HanziData> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        var characters = new List<HanziCharacter>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var table = new List<byte>();
        var count = 0;

        foreach (var repository in repositories)
        {
            if (repository is null)
            {
                throw new ArgumentException("The repository list contains null.", nameof(repositories));
            }

            foreach (var character in repository.CharacterArray)
            {
                if (!seen.Add(character.Character))
                {
                    continue;
                }

                var bytes = repository.GetSubStrokeBytes(character);
                if (bytes.Length != character.SubStrokeCount * 3)
                {
                    throw new InvalidOperationException(
                        $"Character '{character.Character}' claims {character.SubStrokeCount} sub-strokes " +
                        "but its data is out of range; the repository looks corrupt.");
                }

                characters.Add(new HanziCharacter(
                    character.Character,
                    character.StrokeCount,
                    character.SubStrokeCount,
                    count));
                table.AddRange(bytes.ToArray());
                count += bytes.Length;
            }
        }

        return new HanziData(characters, table.ToArray());
    }

    private static HanziData FromJsonDocument(JsonDocument document)
    {
        var root = document.RootElement;

        if (!root.TryGetProperty("chars", out var charsElement) || charsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Character data is missing the 'chars' array.");
        }

        var characters = new HanziCharacter[charsElement.GetArrayLength()];
        var index = 0;
        foreach (var entry in charsElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 4)
            {
                throw new FormatException($"Character entry #{index} is not a [character, strokeCount, subStrokeCount, subStrokeOffset] array.");
            }

            var character = entry[0].GetString() ?? string.Empty;
            var strokeCount = entry[1].GetInt32();
            var subStrokeCount = entry[2].GetInt32();
            var subStrokeOffset = entry[3].GetInt32();
            characters[index++] = new HanziCharacter(character, strokeCount, subStrokeCount, subStrokeOffset);
        }

        if (!root.TryGetProperty("substrokes", out var subStrokesElement) || subStrokesElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("Character data is missing the 'substrokes' string.");
        }

        var subStrokes = CompactDataDecoder.Decode(subStrokesElement.GetString() ?? string.Empty);
        return new HanziData(characters, subStrokes);
    }
}
