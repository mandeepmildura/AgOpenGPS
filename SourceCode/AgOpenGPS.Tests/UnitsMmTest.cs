using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    // sandy: settings boxes show mm in metric; the XML stays in metres. Value shown, then saved back, must not drift.
    public class UnitsMmTest
    {
        [TestCase(0.29, 290)]   // was shown as 28 cm by (int) truncation, and re-saved as 0.28
        [TestCase(0.35, 350)]
        [TestCase(1.234, 1234)]
        [TestCase(-0.057, -57)]
        [TestCase(3.0005, 3001)] // half a mm rounds away from zero
        public void MetresShowAsWholeMm(double metres, int mm)
        {
            Assert.That(glm.ToNud(metres, 1000.0, 0), Is.EqualTo((decimal)mm));
        }

        [TestCase(0.29)]
        [TestCase(1.234)]
        [TestCase(2.718)]
        public void MmSaveReloadKeepsMetres(double metres)
        {
            decimal shown = glm.ToNud(metres, 1000.0, 0);
            double saved = (double)shown * 0.001;          // what the click handlers store (inchOrCm2m)
            Assert.That(saved, Is.EqualTo(metres).Within(1e-9));
            Assert.That(glm.ToNud(saved, 1000.0, 0), Is.EqualTo(shown)); // and reloads the same
        }

        [Test]
        public void MetresBoxToOneMm()
        {
            Assert.That(glm.ToNud(6.2549, 1.0, 3), Is.EqualTo(6.255m)); // U-turn radius / headland, 3 decimals
        }

        [Test]
        public void SnapDistanceCmShowsMmAndImperialStillInches()
        {
            Assert.That(glm.ToNud(0.5, 10.0, 0), Is.EqualTo(5m));      // 0.5 cm nudge step = 5 mm (was 0 or 1 cm)
            Assert.That(glm.ToNud(20.0, 0.394, 1), Is.EqualTo(7.9m));  // imperial unchanged: 20 cm = 7.9 in
            Assert.That(glm.ToNud(0.29, glm.m2in, 0), Is.EqualTo(11m)); // imperial inches still whole
        }

        [Test]
        public void SummaryPageShowsMm()
        {
            Assert.That(AgOpenGPS.Core.Models.Distance.SmallDistanceString(true, 0.287), Is.EqualTo("287 mm"));
            Assert.That(AgOpenGPS.Core.Models.Distance.VerySmallDistanceString(true, 0.025), Is.EqualTo("25 mm")); // nudge 2.5 cm
            Assert.That(AgOpenGPS.Core.Models.Distance.SmallDistanceString(false, 0.287), Is.EqualTo("11 in"));
        }
    }
}
