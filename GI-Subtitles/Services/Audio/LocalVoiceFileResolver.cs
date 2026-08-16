using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GI_Subtitles.Services.Audio
{
    public sealed class LocalVoiceFileResolver
    {
        private readonly string _applicationDataRoot;
        private readonly string _applicationDataRootPrefix;
        private readonly string _mappingFilePath;
        private readonly object _sync = new object();
        private Dictionary<string, string> _mapping =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _mappingWriteTimeUtc = DateTime.MinValue;
        private long _mappingLength = -1;
        private bool _mappingLoaded;

        public LocalVoiceFileResolver(string applicationDataRoot, string gameDirectoryName)
        {
            if (string.IsNullOrWhiteSpace(applicationDataRoot))
            {
                throw new ArgumentException(
                    "The application data root is required.", nameof(applicationDataRoot));
            }
            if (string.IsNullOrWhiteSpace(gameDirectoryName))
            {
                throw new ArgumentException(
                    "The game directory name is required.", nameof(gameDirectoryName));
            }

            _applicationDataRoot = Path.GetFullPath(applicationDataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _applicationDataRootPrefix = _applicationDataRoot + Path.DirectorySeparatorChar;
            _mappingFilePath = Path.Combine(
                _applicationDataRoot, gameDirectoryName, "md5_mapping.json");
        }

        public bool TryResolve(string md5, out string filePath)
        {
            filePath = null;
            if (!IsMd5(md5)) return false;

            lock (_sync)
            {
                try
                {
                    RefreshMappingIfNeeded();
                    if (!_mapping.TryGetValue(md5, out string relativePath) ||
                        string.IsNullOrWhiteSpace(relativePath) ||
                        Path.IsPathRooted(relativePath))
                    {
                        return false;
                    }

                    string candidate = Path.GetFullPath(Path.Combine(
                        _applicationDataRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!candidate.StartsWith(
                            _applicationDataRootPrefix, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(candidate))
                    {
                        return false;
                    }

                    filePath = candidate;
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
                catch (JsonException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (NotSupportedException)
                {
                    return false;
                }
                catch (System.Security.SecurityException)
                {
                    return false;
                }
            }
        }

        private void RefreshMappingIfNeeded()
        {
            var mappingFile = new FileInfo(_mappingFilePath);
            if (!mappingFile.Exists)
            {
                _mapping.Clear();
                _mappingLoaded = true;
                _mappingWriteTimeUtc = DateTime.MinValue;
                _mappingLength = -1;
                return;
            }

            if (_mappingLoaded &&
                mappingFile.LastWriteTimeUtc == _mappingWriteTimeUtc &&
                mappingFile.Length == _mappingLength)
            {
                return;
            }

            var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                File.ReadAllText(_mappingFilePath));
            _mapping = loaded == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            _mappingLoaded = true;
            _mappingWriteTimeUtc = mappingFile.LastWriteTimeUtc;
            _mappingLength = mappingFile.Length;
        }

        private static bool IsMd5(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            foreach (char character in value)
            {
                bool isHex =
                    (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F');
                if (!isHex) return false;
            }
            return true;
        }
    }
}
