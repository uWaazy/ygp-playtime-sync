using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YgpPlaytimeSync
{
    /// <summary>Quadros prontos de um avatar (1 quadro = imagem estática; vários = GIF animado).</summary>
    public sealed class AvatarFrames
    {
        public List<BitmapSource> Frames { get; } = new List<BitmapSource>();
        public List<int> DelaysMs { get; } = new List<int>();
        public bool IsAnimated { get { return Frames.Count > 1; } }
    }

    /// <summary>
    /// Decodifica o avatar. O WPF não anima GIF sozinho, então os quadros do GIF são compostos aqui
    /// (respeitando posição e descarte de cada quadro) em imagens pequenas, e a tela troca de quadro por tempo.
    /// Precisa rodar na thread da interface (usa RenderTargetBitmap).
    /// </summary>
    public static class AvatarDecoder
    {
        private const int MaxFrames = 150;
        private const int MaxBytes = 8 * 1024 * 1024;

        public static bool IsGif(byte[] b)
        {
            return b != null && b.Length > 6 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8';
        }

        /// <summary>Retorna os quadros do avatar, ou null se a imagem não puder ser lida. maxSize = maior lado em pixels.</summary>
        public static AvatarFrames Decode(byte[] data, int maxSize)
        {
            if (data == null || data.Length == 0 || data.Length > MaxBytes) return null;

            try
            {
                if (IsGif(data))
                {
                    var gif = DecodeGif(data, maxSize);
                    if (gif != null) return gif;
                }
            }
            catch
            {
                // cai para a imagem estática abaixo
            }

            try
            {
                return DecodeStatic(data, maxSize);
            }
            catch
            {
                return null;
            }
        }

        private static AvatarFrames DecodeStatic(byte[] data, int maxSize)
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(data))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = maxSize;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();

            var result = new AvatarFrames();
            result.Frames.Add(bmp);
            result.DelaysMs.Add(100);
            return result;
        }

        private static AvatarFrames DecodeGif(byte[] data, int maxSize)
        {
            GifBitmapDecoder decoder;
            using (var ms = new MemoryStream(data))
            {
                decoder = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }

            var frames = decoder.Frames;
            if (frames == null || frames.Count == 0) return null;
            if (frames.Count == 1 || frames.Count > MaxFrames) return DecodeStatic(data, maxSize);

            // Tamanho lógico da animação (fallback: o maior quadro).
            int width = 0, height = 0;
            try
            {
                var meta = decoder.Metadata;
                if (meta != null)
                {
                    width = ReadInt(meta, "/logscrdesc/Width", 0);
                    height = ReadInt(meta, "/logscrdesc/Height", 0);
                }
            }
            catch { }
            if (width <= 0 || height <= 0)
            {
                foreach (var f in frames)
                {
                    width = Math.Max(width, f.PixelWidth);
                    height = Math.Max(height, f.PixelHeight);
                }
            }
            if (width <= 0 || height <= 0) return null;

            double scale = Math.Min(1.0, (double)maxSize / Math.Max(width, height));
            int outW = Math.Max(1, (int)Math.Round(width * scale));
            int outH = Math.Max(1, (int)Math.Round(height * scale));
            var full = new Rect(0, 0, outW, outH);

            var result = new AvatarFrames();
            BitmapSource state = null;      // resultado do quadro anterior já composto
            BitmapSource beforeLast = null; // estado antes de desenhar o quadro anterior (descarte 3)
            Rect lastRect = Rect.Empty;
            int lastDisposal = 0;

            foreach (var frame in frames)
            {
                var fm = frame.Metadata as BitmapMetadata;
                int left = ReadInt(fm, "/imgdesc/Left", 0);
                int top = ReadInt(fm, "/imgdesc/Top", 0);
                int fw = ReadInt(fm, "/imgdesc/Width", frame.PixelWidth);
                int fh = ReadInt(fm, "/imgdesc/Height", frame.PixelHeight);
                int delay = ReadInt(fm, "/grctlext/Delay", 10) * 10;
                int disposal = ReadInt(fm, "/grctlext/Disposal", 0);
                if (delay <= 10) delay = 100; // navegadores tratam atrasos muito curtos como 100 ms
                if (delay < 40) delay = 40;

                var rect = new Rect(left * scale, top * scale, Math.Max(1, fw * scale), Math.Max(1, fh * scale));

                BitmapSource basis;
                if (state == null) basis = null;
                else if (lastDisposal == 2) basis = Render(outW, outH, dc =>
                {
                    var clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(lastRect));
                    dc.PushClip(clip);
                    dc.DrawImage(state, full);
                    dc.Pop();
                });
                else if (lastDisposal == 3) basis = beforeLast;
                else basis = state;

                var composed = Render(outW, outH, dc =>
                {
                    if (basis != null) dc.DrawImage(basis, full);
                    dc.DrawImage(frame, rect);
                });

                beforeLast = basis;
                state = composed;
                lastRect = rect;
                lastDisposal = disposal;

                result.Frames.Add(composed);
                result.DelaysMs.Add(delay);
            }

            return result.Frames.Count > 0 ? result : null;
        }

        private static BitmapSource Render(int w, int h, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                draw(dc);
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static int ReadInt(BitmapMetadata meta, string query, int fallback)
        {
            if (meta == null) return fallback;
            try
            {
                var v = meta.GetQuery(query);
                if (v == null) return fallback;
                return Convert.ToInt32(v);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
