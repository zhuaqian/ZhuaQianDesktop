using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ZhuaQianDesktopApp.Tools
{
    // Captures the desktop to a PNG so the agent can *see* before it acts and
    // *verify* after it acts -- turning blind coordinate clicks into an
    // observable perceive -> act -> verify loop. Pure GDI (System.Drawing),
    // no external dependency.
    public sealed class DesktopScreenCapture
    {
        // Capture the primary screen (or a region if x/y/w/h are provided).
        // Saves to `path` when given and always returns the PNG bytes.
        public byte[] Capture(string path = null, int x = -1, int y = -1, int w = -1, int h = -1)
        {
            Rectangle bounds;
            if (x >= 0 && y >= 0 && w > 0 && h > 0)
                bounds = new Rectangle(x, y, w, h);
            else
                bounds = Screen.PrimaryScreen.Bounds;

            using (var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }
                if (!string.IsNullOrEmpty(path))
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    bmp.Save(path, ImageFormat.Png);
                }
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }
    }
}
