using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgOpenGPS.Controls
{
    public static class NudlessNumericUpDownExtensions
    {
        /// <summary>
        /// sandy: metric boxes show mm now. Designer limits are in cm, so scale them x10 once per form.
        /// </summary>
        public static void CmLimitsToMm(params NudlessNumericUpDown[] nuds)
        {
            foreach (var nud in nuds)
            {
                nud.Maximum *= 10;
                nud.Minimum *= 10;
            }
        }

        public static bool ShowKeypad(this NudlessNumericUpDown nudlessNumericUpDown, Form owner)
        {
            var color = nudlessNumericUpDown.BackColor;
            nudlessNumericUpDown.BackColor = Color.Red;
            nudlessNumericUpDown.Value = Math.Round(nudlessNumericUpDown.Value, nudlessNumericUpDown.DecimalPlaces);

            using (FormNumeric form = new FormNumeric((double)nudlessNumericUpDown.Minimum, (double)nudlessNumericUpDown.Maximum, (double)nudlessNumericUpDown.Value))
            {
                DialogResult result = form.ShowDialog(owner);
                if (result == DialogResult.OK)
                {
                    nudlessNumericUpDown.Value = (decimal)form.ReturnValue;
                    nudlessNumericUpDown.BackColor = color;
                    return true;
                }
                else if (result == DialogResult.Cancel)
                {
                    nudlessNumericUpDown.BackColor = color;
                }
                return false;
            }
        }
    }
}
