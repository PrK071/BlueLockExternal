// C# | Diagnostics/BallAnnotation.cs | .NET Framework 4.8 x64 | Ball antenna and image-space measurements, independent of input execution.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using BlueLockV2.Core;

namespace BlueLockV2.Diagnostics
{
    public static class BallAnnotation
    {
        public static string Measurements(GameState state, TrackedObject ball)
        {
            var local = state.Best(EntityType.LOCAL_PLAYER);
            string text = String.Format(System.Globalization.CultureInfo.InvariantCulture, "BOLA #{0} | caixa {1:0} x {2:0} px | conf. {3:0%}", ball.Id, ball.Width, ball.Height, ball.Confidence);
            if (local == null)
                return text + "\ndistancia: sem referencia local";
            Vec delta = ball.Position - local.Position;
            return text + String.Format(System.Globalization.CultureInfo.InvariantCulture, "\ndist. tela {0:0} px | dx {1:+0;-0;0} | dy {2:+0;-0;0}\npes: {3} | altura no campo: desconhecida", delta.Length, delta.X, delta.Y, local.Measured ? "detectados" : "referencia calibrada");
        }

        public static void Draw(Graphics graphics, GameState state, Size viewport, Vec origin, double scaleX, double scaleY)
        {
            if (state == null || viewport.Width < 20 || viewport.Height < 20)
                return;
            var ball = state.Best(EntityType.BALL);
            using (Font font = new Font("Consolas", 9, FontStyle.Bold))
            using (Pen marker = new Pen(Color.Cyan, 2))
            using (Pen link = new Pen(Color.Cyan, 1))
            using (Brush label = new SolidBrush(Color.Cyan))
            {
                if (ball == null)
                {
                    graphics.DrawString("BOLA NAO DETECTADA", font, label, 12, 35);
                    return;
                }

                bool uncertain = ball.Ambiguous || ball.Confidence < .7 || ball.Hits < 2;
                Color color = uncertain ? Color.Orange : Color.Cyan;
                marker.Color = color;
                link.Color = color;
                link.DashStyle = DashStyle.Dash;
                float x = (float)((ball.Position.X - origin.X) * scaleX);
                float y = (float)((ball.Position.Y - origin.Y) * scaleY);
                float width = (float)Math.Max(6, ball.Width * scaleX);
                float height = (float)Math.Max(6, ball.Height * scaleY);
                if (x + width < 0 || x - width > viewport.Width || y + height < 0 || y - height > viewport.Height)
                    return;
                // Keep the overlay outside the ball interior. Saturated colors are excluded by the white-ball segmenter.
                graphics.DrawRectangle(marker, x - width / 2 - 3, y - height / 2 - 3, width + 6, height + 6);
                float tip = Math.Max(25, y - height / 2 - 100);
                graphics.DrawLine(marker, x, y - height / 2 - 4, x, tip);
                graphics.DrawLine(marker, x - 5, tip, x + 5, tip);
                var local = state.Best(EntityType.LOCAL_PLAYER);
                if (local != null)
                {
                    float feetX = (float)((local.Position.X - origin.X) * scaleX);
                    float feetY = (float)((local.Position.Y - origin.Y) * scaleY);
                    graphics.DrawLine(link, feetX, feetY, x, y);
                    graphics.DrawLine(marker, feetX - 5, feetY, feetX + 5, feetY);
                    graphics.DrawLine(marker, feetX, feetY - 5, feetX, feetY + 5);
                }

                string text = (uncertain ? "INCERTO\n" : "") + Measurements(state, ball);
                SizeF labelSize = graphics.MeasureString(text, font);
                float left = Math.Max(4, Math.Min(viewport.Width - labelSize.Width - 4, x + 10));
                float top = Math.Max(24, Math.Min(viewport.Height - labelSize.Height - 4, tip - labelSize.Height));
                using (Brush ink = new SolidBrush(color))
                {
                    graphics.DrawString(text, font, Brushes.Black, left + 1, top + 1);
                    graphics.DrawString(text, font, ink, left, top);
                }
            }
        }
    }
}
