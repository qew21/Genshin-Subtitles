using System;
using System.Collections.Generic;
using GI_Subtitles.Core.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GI_Test
{
    [TestClass]
    public class TestHintScreenSelection
    {
        [TestMethod]
        public void DualEqualWidthScreens_DisplayOnSecondScreen_PicksSecondScreen()
        {
            var screens = new List<OverlayRect>
            {
                new OverlayRect(0, 0, 1920, 1080),
                new OverlayRect(1920, 0, 1920, 1080)
            };
            OverlayRect displayOnB = new OverlayRect(2400, 900, 900, 200);

            OverlayRect target = HintScreenSelection.Select(screens, 0, new[] { displayOnB });

            AssertSameRect(screens[1], target, "The hint must top-center on screen B, not on the virtual-desktop seam.");
        }

        [TestMethod]
        public void VoicePrimaryDisplayInvalid_AnotherPairValid_PicksThatPairScreen()
        {
            var screens = new List<OverlayRect>
            {
                new OverlayRect(0, 0, 1920, 1080),
                new OverlayRect(1920, 0, 1920, 1080)
            };
            OverlayRect invalidPrimary = OverlayRect.Invalid;
            OverlayRect validSecondPair = new OverlayRect(2100, 900, 800, 200);

            OverlayRect target = HintScreenSelection.Select(screens, 0, new[] { invalidPrimary, validSecondPair });

            AssertSameRect(screens[1], target);
        }

        [TestMethod]
        public void NoValidDisplay_FallsBackToPrimaryScreen()
        {
            var screens = new List<OverlayRect>
            {
                new OverlayRect(1920, 0, 1920, 1080),
                new OverlayRect(0, 0, 1920, 1080)
            };

            OverlayRect target = HintScreenSelection.Select(
                screens,
                1,
                new[] { OverlayRect.Invalid, OverlayRect.Invalid });

            AssertSameRect(screens[1], target, "Primary is screens[1] here, proving the fallback is the primary, not screens[0].");
        }

        [TestMethod]
        public void DisplaySpanningTwoScreens_PicksLargerOverlap()
        {
            var screens = new List<OverlayRect>
            {
                new OverlayRect(0, 0, 1920, 1080),
                new OverlayRect(1920, 0, 1920, 1080)
            };

            OverlayRect mostlyOnB = new OverlayRect(1620, 500, 1000, 200);
            Assert.AreEqual(300, OverlapWidth(screens[0], mostlyOnB));
            Assert.AreEqual(700, OverlapWidth(screens[1], mostlyOnB));
            AssertSameRect(screens[1], HintScreenSelection.Select(screens, 0, new[] { mostlyOnB }));

            OverlayRect mostlyOnA = new OverlayRect(1520, 500, 600, 200);
            Assert.AreEqual(400, OverlapWidth(screens[0], mostlyOnA));
            Assert.AreEqual(200, OverlapWidth(screens[1], mostlyOnA));
            AssertSameRect(screens[0], HintScreenSelection.Select(screens, 0, new[] { mostlyOnA }));
        }

        [TestMethod]
        public void EqualOverlap_TakesTheEarlierScreen()
        {
            var screens = new List<OverlayRect>
            {
                new OverlayRect(0, 0, 1920, 1080),
                new OverlayRect(1920, 0, 1920, 1080)
            };
            OverlayRect splitEvenly = new OverlayRect(1420, 500, 1000, 200);

            Assert.AreEqual(500, OverlapWidth(screens[0], splitEvenly));
            Assert.AreEqual(500, OverlapWidth(screens[1], splitEvenly));
            AssertSameRect(screens[0], HintScreenSelection.Select(screens, 1, new[] { splitEvenly }));
        }

        [TestMethod]
        public void ValidDisplayOffEveryScreen_FallsBackToPrimary()
        {
            var screens = new List<OverlayRect>
            {
                new OverlayRect(0, 0, 1920, 1080),
                new OverlayRect(1920, 0, 1920, 1080)
            };
            OverlayRect unplugged = new OverlayRect(5000, 3000, 800, 200);

            OverlayRect target = HintScreenSelection.Select(screens, 1, new[] { unplugged });

            AssertSameRect(screens[1], target, "A display left over from an unplugged monitor must self-heal to the primary screen.");
        }

        [TestMethod]
        public void NoScreens_ReturnsInvalid()
        {
            Assert.IsFalse(HintScreenSelection.Select(
                new OverlayRect[0],
                0,
                new[] { new OverlayRect(10, 10, 100, 100) }).IsValid);
        }

        private static void AssertSameRect(OverlayRect expected, OverlayRect actual, string message = null)
        {
            Assert.IsTrue(
                expected.X == actual.X
                && expected.Y == actual.Y
                && expected.Width == actual.Width
                && expected.Height == actual.Height,
                message ?? $"Expected ({expected.X},{expected.Y},{expected.Width},{expected.Height}) but got ({actual.X},{actual.Y},{actual.Width},{actual.Height}).");
        }

        private static int OverlapWidth(OverlayRect a, OverlayRect b)
        {
            int left = Math.Max(a.X, b.X);
            int right = Math.Min(a.X + a.Width, b.X + b.Width);
            return Math.Max(0, right - left);
        }
    }
}
