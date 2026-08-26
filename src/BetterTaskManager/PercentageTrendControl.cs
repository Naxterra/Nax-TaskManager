using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BetterTaskManager
{
    internal sealed class PercentageTrendControl : Control
    {
        private readonly List<double> samples = new List<double>();
        private readonly int maximumSamples;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Title { get; set; } = "Trend";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color LineColor { get; set; } = Color.FromArgb(77, 151, 207);
        public int SampleCount { get { return samples.Count; } }
        public double LatestSample { get { return samples.Count == 0 ? 0 : samples[samples.Count - 1]; } }

        public PercentageTrendControl(int maximumSamples = 60)
        {
            if (maximumSamples < 2) throw new ArgumentOutOfRangeException(nameof(maximumSamples));
            this.maximumSamples = maximumSamples;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(24, 33, 47);
            ForeColor = Color.FromArgb(235, 242, 250);
            MinimumSize = new Size(240, 120);
            AccessibleRole = AccessibleRole.Chart;
        }

        public void AddSample(double value)
        {
            samples.Add(NormalizePercentage(value));
            if (samples.Count > maximumSamples) samples.RemoveRange(0, samples.Count - maximumSamples);
            Invalidate();
        }

        internal double[] SnapshotSamples()
        {
            return samples.ToArray();
        }

        internal static double NormalizePercentage(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return Math.Max(0, Math.Min(100, value));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            Rectangle plot = new Rectangle(10, 32, Math.Max(1, ClientSize.Width - 20), Math.Max(1, ClientSize.Height - 44));
            using (var borderPen = new Pen(Color.FromArgb(58, 76, 101)))
            using (var gridPen = new Pen(Color.FromArgb(39, 53, 72)))
            using (var linePen = new Pen(LineColor, 2f))
            using (var textBrush = new SolidBrush(ForeColor))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(163, 180, 201)))
            using (var pointBrush = new SolidBrush(LineColor))
            {
                e.Graphics.DrawRectangle(borderPen, plot);
                for (int percent = 25; percent <= 75; percent += 25)
                {
                    float y = plot.Bottom - plot.Height * percent / 100f;
                    e.Graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                }

                string latest = samples.Count == 0 ? "waiting for samples" : LatestSample.ToString("0.0") + "%";
                e.Graphics.DrawString(Title, Font, textBrush, 10, 8);
                SizeF latestSize = e.Graphics.MeasureString(latest, Font);
                e.Graphics.DrawString(latest, Font, mutedBrush, Math.Max(10, ClientSize.Width - latestSize.Width - 10), 8);

                if (samples.Count == 1)
                {
                    float y = plot.Bottom - (float)(plot.Height * samples[0] / 100d);
                    e.Graphics.FillEllipse(pointBrush, plot.Left - 2, y - 2, 5, 5);
                }
                else if (samples.Count > 1)
                {
                    var points = new PointF[samples.Count];
                    for (int index = 0; index < samples.Count; index++)
                    {
                        float x = plot.Left + plot.Width * index / (float)(samples.Count - 1);
                        float y = plot.Bottom - (float)(plot.Height * samples[index] / 100d);
                        points[index] = new PointF(x, y);
                    }
                    e.Graphics.DrawLines(linePen, points);
                }
            }
        }
    }
}
