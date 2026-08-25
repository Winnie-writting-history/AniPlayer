using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace AnniPlayer.Services
{
    public class AudioMetadataInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public BitmapImage? CoverImage { get; set; }
    }

    public static class AudioMetadataService
    {
        public static BitmapImage? ExtractCoverImage(string audioPath)
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath)) return null;

            try
            {
                // 1. Check directory for local cover image files
                string dir = Path.GetDirectoryName(audioPath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(audioPath);
                string[] candidates = new[]
                {
                    Path.Combine(dir, baseName + ".jpg"),
                    Path.Combine(dir, baseName + ".png"),
                    Path.Combine(dir, "cover.jpg"),
                    Path.Combine(dir, "cover.png"),
                    Path.Combine(dir, "folder.jpg"),
                    Path.Combine(dir, "folder.png"),
                    Path.Combine(dir, "album.jpg"),
                    Path.Combine(dir, "front.jpg")
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(c);
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }

                // 2. Try extracting embedded ID3v2 APIC / FLAC picture from file bytes
                using var fs = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[Math.Min(fs.Length, 1024 * 1024 * 8)]; // Read up to first 8MB for metadata
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read > 100)
                {
                    // Scan for JPEG header 0xFF, 0xD8, 0xFF, 0xE0 or 0xFF, 0xD8, 0xFF, 0xE1
                    int jpgIndex = FindBytePattern(buffer, new byte[] { 0xFF, 0xD8, 0xFF });
                    if (jpgIndex >= 0)
                    {
                        int jpgEnd = FindBytePattern(buffer, new byte[] { 0xFF, 0xD9 }, jpgIndex);
                        if (jpgEnd > jpgIndex)
                        {
                            int len = (jpgEnd + 2) - jpgIndex;
                            using var ms = new MemoryStream(buffer, jpgIndex, len);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            return bmp;
                        }
                    }

                    // Scan for PNG header 0x89, 0x50, 0x4E, 0x47
                    int pngIndex = FindBytePattern(buffer, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
                    if (pngIndex >= 0)
                    {
                        int pngEnd = FindBytePattern(buffer, new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }, pngIndex);
                        if (pngEnd > pngIndex)
                        {
                            int len = (pngEnd + 8) - pngIndex;
                            using var ms = new MemoryStream(buffer, pngIndex, len);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            return bmp;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static int FindBytePattern(byte[] src, byte[] pattern, int startIndex = 0)
        {
            int max = src.Length - pattern.Length;
            for (int i = startIndex; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (src[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
