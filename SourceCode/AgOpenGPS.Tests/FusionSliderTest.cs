using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    // sandy: Config -> antenna Fusion slider must open for any saved weight, never throw.
    public class FusionSliderTest
    {
        [TestCase(0.0, 5)]     // zero: clamps to min
        [TestCase(0.01, 5)]    // old "all the way to IMU" value: below range, clamps
        [TestCase(0.04, 5)]    // just below min round: clamps
        [TestCase(0.049, 5)]   // rounds to 5, at min
        [TestCase(0.051, 5)]   // rounds to 5
        [TestCase(0.06, 6)]    // default
        [TestCase(0.29, 29)]   // rounds, doesn't truncate to 28
        [TestCase(0.30, 30)]
        [TestCase(0.70, 70)]   // the target we send Hardeep to
        [TestCase(0.945, 95)]  // rounds up, at max
        [TestCase(0.95, 95)]
        [TestCase(0.955, 95)]  // just above max: clamps
        [TestCase(1.0, 95)]    // one: clamps
        [TestCase(5.0, 95)]    // garbage high: clamps
        [TestCase(-1.0, 5)]
        [TestCase(-999.0, 5)]  // extreme negative: clamps
        public void SliderPosStaysInRange(double weight, int expected)
        {
            Assert.That(FormConfig.FusionSliderPos(weight, 5, 95), Is.EqualTo(expected));
        }

        // round-trip stability: a value saved, then loaded, then saved again, stays stable
        [TestCase(0.05)]
        [TestCase(0.20)]
        [TestCase(0.50)]
        [TestCase(0.70)]
        [TestCase(0.95)]
        public void SaveLoadRoundTripIsStable(double initial)
        {
            int slider = FormConfig.FusionSliderPos(initial, 5, 95);
            double saved = slider * 0.01;
            int sliderAgain = FormConfig.FusionSliderPos(saved, 5, 95);
            Assert.That(sliderAgain, Is.EqualTo(slider), "second load must match first for in-range values");
        }

        [Test]
        public void RealScrollBarAcceptsLowSavedWeight()
        {
            var bar = new System.Windows.Forms.HScrollBar { Minimum = 5, Maximum = 95 };
            // what last night's build did: throws
            Assert.Throws<System.ArgumentOutOfRangeException>(() => bar.Value = (int)(0.01 * 100));
            // what the page does now
            Assert.DoesNotThrow(() => bar.Value = FormConfig.FusionSliderPos(0.01, bar.Minimum, bar.Maximum));
            Assert.That(bar.Value * 0.01, Is.EqualTo(0.05).Within(1e-9));   // saves back as 0.05, in range
        }
    }
}
