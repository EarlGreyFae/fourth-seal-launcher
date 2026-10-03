// Small things the launcher writes or draws.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace FourthSealLauncher
{
    // The KubeJS client script put in the game folder: the first time the
    // title screen shows after the game starts, it opens the multiplayer
    // server list (where The Fourth Seal is saved at the top). Plain ES5 for
    // KubeJS's script engine.
    static class ClientScript
    {
        public const string Text =
@"// Added by The Fourth Seal launcher (it rewrites this file on every run).
// Opens the multiplayer server list once, the first time the title screen
// shows after the game starts.
var fourthSealOpenedServerList = false;
ClientEvents.tick(function (event) {
  if (fourthSealOpenedServerList) return;
  var screen = Client.screen;
  if (!screen) return;
  if (String(screen.getClass().getName()).indexOf('TitleScreen') < 0) return;
  fourthSealOpenedServerList = true;
  try {
    var JoinMultiplayerScreen = Java.loadClass('net.minecraft.client.gui.screens.multiplayer.JoinMultiplayerScreen');
    Client.setScreen(new JoinMultiplayerScreen(screen));
  } catch (e) {
    console.warn('[The Fourth Seal] Could not open the server list: ' + e);
  }
});
";
    }

    // The launcher's emblem: a gold seal with "IV" on deep purple. Used for
    // the window and the Minecraft Launcher profile.
    static class ProfileIcon
    {
        public static Bitmap Draw(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(46, 26, 71)))
                    g.FillRectangle(bg, 0, 0, size, size);
                float m = size * 0.1f;
                using (var gold = new SolidBrush(Color.FromArgb(214, 170, 72)))
                    g.FillEllipse(gold, m, m, size - 2 * m, size - 2 * m);
                using (var ring = new Pen(Color.FromArgb(120, 86, 24), Math.Max(1f, size * 0.04f)))
                {
                    float r = size * 0.18f;
                    g.DrawEllipse(ring, r, r, size - 2 * r, size - 2 * r);
                }
                using (var font = new Font("Georgia", size * 0.3f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var ink = new SolidBrush(Color.FromArgb(46, 26, 71)))
                {
                    var format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    g.DrawString("IV", font, ink, new RectangleF(0, size * 0.02f, size, size), format);
                }
            }
            return bmp;
        }

        // An .ico file with the emblem at several sizes (PNG inside, which
        // Windows Vista and later read).
        public static void WriteIco(string path)
        {
            var sizes = new[] { 16, 32, 48, 256 };
            var images = new List<byte[]>();
            foreach (var size in sizes)
            {
                using (var bmp = Draw(size))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var image in images) w.Write(image);
            }
        }

        // The Minecraft Launcher takes a profile icon as a PNG data URL.
        public static string DataUrl
        {
            get
            {
                using (var bmp = Draw(128))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
        }
    }
}
