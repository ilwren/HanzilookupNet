using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HanziLookup.Demo;

/// <summary>
/// Finds the recognizer data file (<c>data/mmah.json</c>).
/// </summary>
/// <remarks>
/// The file is looked up next to the executable (the project copies it there), in the current
/// directory, and by walking up the directory tree - the last one makes <c>dotnet run</c> work from
/// the repository without any copying. The data is not part of the packages in this repository; see
/// <c>data/README.md</c> for where to get it.
/// </remarks>
internal static class DataFileLocator
{
    private const string RelativePath = "data/mmah.json";

    public static string? Resolve()
    {
        var candidates = new List<string>();

        var baseDirectory = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDirectory, "data", "mmah.json"));
        candidates.Add(Path.Combine(baseDirectory, "mmah.json"));

        var directory = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 8 && directory is not null; ++i, directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "data", "mmah.json"));
        }

        candidates.Add(Path.Combine(Environment.CurrentDirectory, "mmah.json"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        return candidates.FirstOrDefault(File.Exists);
    }
}
