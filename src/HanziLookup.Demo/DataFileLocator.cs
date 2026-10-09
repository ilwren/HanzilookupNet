using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HanziLookup.Demo;

/// <summary>
/// Finds the recognizer data files (<c>data/mmah.json</c> and, optionally, <c>data/alnum.json</c>).
/// </summary>
/// <remarks>
/// A file is looked up next to the executable (the project copies it there), in the current
/// directory, and by walking up the directory tree - the last one makes <c>dotnet run</c> work from
/// the repository without any copying. The data is not part of the packages in this repository; see
/// <c>data/README.md</c> for where to get it.
/// </remarks>
internal static class DataFileLocator
{
    /// <summary>The Chinese character repository (Make Me a Hanzi, 9507 characters).</summary>
    public const string ChineseFileName = "mmah.json";

    /// <summary>The digits / Latin letters / punctuation repository (72 characters).</summary>
    public const string AlphanumericFileName = "alnum.json";

    /// <summary>Resolves one data file by name, or returns <c>null</c> when it cannot be found.</summary>
    public static string? Resolve(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var candidates = new List<string>();

        var baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, "data", fileName));
        candidates.Add(Path.Combine(baseDirectory, fileName));

        var directory = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 8 && directory is not null; ++i, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "data", fileName));
        }

        candidates.Add(Path.Combine(Environment.CurrentDirectory, fileName));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "data", fileName));

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Resolves the Chinese character repository.</summary>
    public static string? Resolve() => Resolve(ChineseFileName);
}