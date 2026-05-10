using System;
using System.Collections.Generic;
using System.IO;

namespace FNS_graphics.Data
{
    internal static class Database_config
    {
        private const string EnvironmentVariable = "FNS_CONNECTION_STRING";
        private const string ConfigFileName = "database.config";
        private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=FNS_rebuild;Username=postgres;Password=postgres";

        internal static string LoadConnectionString()
        {
            string? environmentValue = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
                return environmentValue.Trim();

            foreach (string path in EnumerateConfigPaths())
            {
                string? fileValue = TryReadConnectionString(path);
                if (!string.IsNullOrWhiteSpace(fileValue))
                    return fileValue;
            }

            return DefaultConnectionString;
        }

        private static IEnumerable<string> EnumerateConfigPaths()
        {
            yield return Path.Combine(AppContext.BaseDirectory, ConfigFileName);

            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && directory is not null; i++)
            {
                yield return Path.Combine(directory.FullName, ConfigFileName);
                directory = directory.Parent;
            }
        }

        private static string? TryReadConnectionString(string path)
        {
            if (!File.Exists(path))
                return null;

            foreach (string line in File.ReadLines(path))
            {
                string value = line.Trim();
                if (value.Length == 0 || value.StartsWith("#", StringComparison.Ordinal))
                    continue;

                return value;
            }

            return null;
        }
    }
}
