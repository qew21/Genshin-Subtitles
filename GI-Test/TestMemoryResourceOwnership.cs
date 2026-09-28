using System;
using System.Drawing;
using GI_Subtitles.Core.Cache;
using GI_Subtitles.Services.Video;
using GI_Subtitles.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
using PaddleOCRSharp;

namespace GI_Test
{
    [TestClass]
    public class TestMemoryResourceOwnership
    {
        [TestMethod]
        public void WithBitmapMat_DisposesTemporaryMatAfterCallback()
        {
            using (var bitmap = new Bitmap(16, 12))
            {
                for (int i = 0; i < 64; i++)
                {
                    Mat observed = null;
                    int result = PaddleOCREngine.WithBitmapMat(
                        bitmap,
                        mat =>
                        {
                            observed = mat;
                            Assert.IsFalse(mat.IsDisposed);
                            return 42;
                        });

                    Assert.AreEqual(42, result);
                    Assert.IsNotNull(observed);
                    Assert.IsTrue(observed.IsDisposed, "Temporary Mat was not disposed on iteration " + i);
                }
            }
        }

        [TestMethod]
        public void WithBitmapMat_DisposesTemporaryMatWhenCallbackThrows()
        {
            using (var bitmap = new Bitmap(16, 12))
            {
                Mat observed = null;
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    PaddleOCREngine.WithBitmapMat<int>(
                        bitmap,
                        mat =>
                        {
                            observed = mat;
                            throw new InvalidOperationException("recognition failed");
                        }));

                Assert.IsNotNull(observed);
                Assert.IsTrue(observed.IsDisposed);
            }
        }

        [TestMethod]
        public void ReplaceRoiFrame_DisposesPreviousFrameBeforeReturningNextFrame()
        {
            using (var source = new Mat(20, 30, MatType.CV_8UC3, Scalar.All(10)))
            {
                Mat previous = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0));
                try
                {
                    for (int i = 0; i < 64; i++)
                    {
                        Mat old = previous;
                        previous = VideoProcessor.ReplaceRoiFrame(
                            old,
                            source,
                            new OpenCvSharp.Rect(4, 5, 8, 6));

                        Assert.IsTrue(old.IsDisposed, "Previous ROI Mat was not disposed on iteration " + i);
                        Assert.AreEqual(8, previous.Width);
                        Assert.AreEqual(6, previous.Height);
                    }
                }
                finally
                {
                    previous.Dispose();
                }
            }
        }

        [TestMethod]
        public void ReplaceStoredBitmap_DisposesPreviousBitmap()
        {
            Bitmap current = new Bitmap(2, 2);
            try
            {
                for (int i = 0; i < 64; i++)
                {
                    Bitmap previous = current;
                    Bitmap replacement = new Bitmap(3, 3);
                    SettingsWindow.ReplaceStoredBitmap(ref current, replacement);

                    Assert.AreSame(replacement, current);
                    Assert.ThrowsExactly<ArgumentException>(() => previous.GetPixel(0, 0));
                }
            }
            finally
            {
                current.Dispose();
            }
        }

        [TestMethod]
        public void AudioHistoryCache_EvictsOldEntriesAtCapacity()
        {
            var history = new LRUCache<string, bool>(2);
            history["first"] = true;
            history["second"] = true;

            Assert.IsTrue(history.ContainsKey("first"));
            history["third"] = true;

            Assert.AreEqual(2, history.Count);
            Assert.IsTrue(history.ContainsKey("first"));
            Assert.IsFalse(history.ContainsKey("second"));
            Assert.IsTrue(history.ContainsKey("third"));
        }
    }
}
