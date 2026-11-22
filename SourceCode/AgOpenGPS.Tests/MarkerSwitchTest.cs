using System;
using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    // sandy: the cab's Marker ON/off tile. Tap shows the new state at once, a poll confirms it,
    // and if no poll agrees within 6 s it goes back to what the AiO reports.
    public class MarkerSwitchTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 8, 0, 0);

        [Test]
        public void OfflineShowsNothingAndTapSendsNothing()
        {
            var m = new MarkerSwitch();
            Assert.That(m.Shown(null, T0), Is.Null);
            Assert.That(m.Tap(null, T0), Is.Null);
            Assert.That(m.Pending, Is.False);
        }

        [Test]
        public void TapIsOptimisticUntilPollConfirms()
        {
            var m = new MarkerSwitch();
            Assert.That(m.Tap(false, T0), Is.True);                       // off -> send en=1
            Assert.That(m.Shown(false, T0.AddSeconds(1)), Is.True);       // old poll: still shows ON
            Assert.That(m.Pending, Is.True);
            Assert.That(m.Shown(true, T0.AddSeconds(2)), Is.True);        // confirmed
            Assert.That(m.Pending, Is.False);
            Assert.That(m.Shown(false, T0.AddSeconds(3)), Is.False);      // AiO now says off: follow it
        }

        [Test]
        public void UnconfirmedTapRevertsAfterSixSeconds()
        {
            var m = new MarkerSwitch();
            m.Tap(true, T0);                                              // on -> send en=0
            Assert.That(m.Shown(true, T0.AddSeconds(5.9)), Is.False);
            Assert.That(m.Shown(true, T0.AddSeconds(6.1)), Is.True);
            Assert.That(m.Pending, Is.False);
        }

        [Test]
        public void FailedPostRevertsAndSecondTapFlipsBack()
        {
            var m = new MarkerSwitch();
            m.Tap(false, T0);
            m.Failed();
            Assert.That(m.Shown(false, T0), Is.False);
            m.Tap(false, T0);
            Assert.That(m.Tap(false, T0.AddSeconds(1)), Is.False);        // quick second tap undoes the first
        }
    }
}
