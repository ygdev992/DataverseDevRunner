using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataverseDevRunner.ConsoleApp
{
    internal sealed class ReplayMenuEntry
    {
        private const string Suffix = ".replay.json";

        private ReplayMenuEntry(string path, string label)
        {
            Path = path;
            Label = label;
        }

        public string Path { get; }
        public string Label { get; }

        public static IReadOnlyList<ReplayMenuEntry> Discover(string root)
        {
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "local", "Replays"));
            if (!Directory.Exists(directory)) return Array.Empty<ReplayMenuEntry>();
            return Directory.EnumerateFiles(directory, "*" + Suffix, SearchOption.AllDirectories)
                .Select(path => new ReplayMenuEntry(path,
                    path.Substring(directory.Length + 1, path.Length - directory.Length - 1 - Suffix.Length)))
                .OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
