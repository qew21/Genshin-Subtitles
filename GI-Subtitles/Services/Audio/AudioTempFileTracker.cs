using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GI_Subtitles.Services.Audio
{
    /// <summary>
    /// Tracks temporary audio files downloaded during a session and deletes them in a batch.
    /// This avoids deleting files while playback may still need them, and ensures cleanup on exit.
    /// </summary>
    public class AudioTempFileTracker : IDisposable
    {
        private readonly List<string> _trackedFiles = new List<string>();
        private readonly object _lock = new object();

        /// <summary>
        /// Registers a temporary audio file for later cleanup.
        /// Null/empty paths and duplicates are ignored.
        /// </summary>
        public void Track(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            lock (_lock)
            {
                if (!_trackedFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
                {
                    _trackedFiles.Add(filePath);
                }
            }
        }

        /// <summary>
        /// Deletes all tracked temporary audio files and clears the tracking list.
        /// </summary>
        public void CleanupAll()
        {
            lock (_lock)
            {
                foreach (string file in _trackedFiles.ToList())
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to delete audio temp file {file}: {ex.Message}");
                    }
                }
                _trackedFiles.Clear();
            }
        }

        /// <summary>
        /// Returns the currently tracked files (for testing/inspection).
        /// </summary>
        public IReadOnlyList<string> TrackedFiles
        {
            get
            {
                lock (_lock)
                {
                    return _trackedFiles.ToList().AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Number of currently tracked files.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _trackedFiles.Count;
                }
            }
        }

        /// <summary>
        /// Cleans up all tracked files when the tracker is disposed.
        /// </summary>
        public void Dispose()
        {
            CleanupAll();
        }
    }
}
