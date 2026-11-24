using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    // sandy: Config -> antenna Fusion slider must open for any saved weight, never throw.
    public class FusionSliderTest
    {
        [TestCase(0.01, 5)]    // old "all the way to IMU" value: below range, clamps
        [TestCase(0.06, 6)]    // default
        [TestCase(0.29, 29)]   // rounds, doesn't truncate to 28
        [TestCase(0.30, 30)]
        [TestCase(0.95, 95)]
        [TestCase(5.0, 95)]    // garbage high: clamps
        [TestCase(-1.0, 5)]
        public void SliderPosStaysInRange(double weight, int expected)
        {
            Assert.That(FormConfig.FusionSliderPos(weight, 5, 95), Is.EqualTo(expected));
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
