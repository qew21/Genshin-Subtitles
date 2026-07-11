using System;
using System.IO;
using System.Linq;
using GI_Subtitles.Services.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    /// <summary>
    /// Unit tests for AudioTempFileTracker.
    /// Verifies that temporary audio files are tracked and cleaned up in a batch.
    /// </summary>
    [TestClass]
    public class AudioTempFileTrackerTests
    {
        [TestMethod]
        public void Track_AddsFileToTrackedList()
        {
            var tracker = new AudioTempFileTracker();
            tracker.Track("C:\\temp\\test1.tmp");

            Assert.AreEqual(1, tracker.Count);
            Assert.AreEqual("C:\\temp\\test1.tmp", tracker.TrackedFiles[0]);
        }

        [TestMethod]
        public void Track_IgnoresNullAndEmptyPaths()
        {
            var tracker = new AudioTempFileTracker();
            tracker.Track(null);
            tracker.Track("");
            tracker.Track("   ");

            Assert.AreEqual(0, tracker.Count);
        }

        [TestMethod]
        public void Track_IgnoresDuplicatePaths()
        {
            var tracker = new AudioTempFileTracker();
            tracker.Track("C:\\temp\\test1.tmp");
            tracker.Track("C:\\temp\\test1.tmp");
            tracker.Track("C:\\TEMP\\test1.tmp"); // case insensitive duplicate

            Assert.AreEqual(1, tracker.Count);
        }

        [TestMethod]
        public void CleanupAll_DeletesTrackedFilesAndClearsList()
        {
            var tracker = new AudioTempFileTracker();
            string tempFile1 = Path.GetTempFileName();
            string tempFile2 = Path.GetTempFileName();
            string untrackedFile = Path.GetTempFileName();

            try
            {
                File.WriteAllText(tempFile1, "audio1");
                File.WriteAllText(tempFile2, "audio2");
                File.WriteAllText(untrackedFile, "untracked");

                tracker.Track(tempFile1);
                tracker.Track(tempFile2);

                Assert.AreEqual(2, tracker.Count);

                tracker.CleanupAll();

                Assert.AreEqual(0, tracker.Count);
                Assert.IsFalse(File.Exists(tempFile1), "Tracked temp file 1 should be deleted");
                Assert.IsFalse(File.Exists(tempFile2), "Tracked temp file 2 should be deleted");
                Assert.IsTrue(File.Exists(untrackedFile), "Untracked file should not be deleted");
            }
            finally
            {
                SafeDelete(tempFile1);
                SafeDelete(tempFile2);
                SafeDelete(untrackedFile);
            }
        }

        [TestMethod]
        public void CleanupAll_DoesNotThrowWhenFileAlreadyDeleted()
        {
            var tracker = new AudioTempFileTracker();
            string tempFile = Path.GetTempFileName();

            try
            {
                tracker.Track(tempFile);
                File.Delete(tempFile);

                tracker.CleanupAll();

                Assert.AreEqual(0, tracker.Count);
            }
            finally
            {
                SafeDelete(tempFile);
            }
        }

        [TestMethod]
        public void CleanupAll_DoesNotThrowForMissingFile()
        {
            var tracker = new AudioTempFileTracker();
            tracker.Track("C:\\nonexistent\\path\\file.tmp");

            tracker.CleanupAll();

            Assert.AreEqual(0, tracker.Count);
        }

        [TestMethod]
        public void Dispose_CleansUpTrackedFiles()
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                File.WriteAllText(tempFile, "audio");

                using (var tracker = new AudioTempFileTracker())
                {
                    tracker.Track(tempFile);
                    Assert.AreEqual(1, tracker.Count);
                }

                Assert.IsFalse(File.Exists(tempFile), "Tracked file should be deleted on dispose");
            }
            finally
            {
                SafeDelete(tempFile);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
