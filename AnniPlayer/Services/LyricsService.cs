using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AnniPlayer.Services
{
    public class LyricLine
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class LyricsService
    {
        private static readonly Lazy<LyricsService> _instance = new(() => new LyricsService());
        public static LyricsService Instance => _instance.Value;

        public List<LyricLine> CurrentLyrics { get; private set; } = new();
        public string CurrentLoadedFile { get; private set; } = string.Empty;

        private LyricsService() { }

        public void LoadLyricsForAudio(string audioPath, string? mpvMetadataLyrics = null)
        {
            CurrentLyrics.Clear();
            CurrentLoadedFile = audioPath;
            if (string.IsNullOrEmpty(audioPath)) return;

            try
            {
                // 1. Check external .lrc file in the same directory (Highest priority)
                if (File.Exists(audioPath))
                {
                    string dir = Path.GetDirectoryName(audioPath) ?? "";
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(audioPath);

                    string[] candidates = new[]
                    {
                        Path.Combine(dir, nameWithoutExt + ".lrc"),
                        Path.Combine(dir, nameWithoutExt + ".chs.lrc"),
                        Path.Combine(dir, nameWithoutExt + ".cht.lrc"),
                        Path.Combine(dir, nameWithoutExt + ".eng.lrc"),
                        Path.Combine(dir, nameWithoutExt + ".txt")
                    };

                    foreach (var candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            ParseLrcContent(File.ReadAllText(candidate, Encoding.UTF8));
                            if (CurrentLyrics.Count > 0) return;
                        }
                    }
                }

                // 2. Check MPV metadata lyrics (from ID3 USLT / Vorbis LYRICS / MP4 lyrics)
                if (!string.IsNullOrWhiteSpace(mpvMetadataLyrics))
                {
                    ParseLrcContent(mpvMetadataLyrics);
                    if (CurrentLyrics.Count > 0) return;
                }

                // 3. Fallback: Parse embedded ID3v2 USLT / FLAC Vorbis LYRICS from local file bytes
                if (File.Exists(audioPath))
                {
                    string embeddedLyrics = ExtractEmbeddedLyricsFromBytes(audioPath);
                    if (!string.IsNullOrWhiteSpace(embeddedLyrics))
                    {
                        ParseLrcContent(embeddedLyrics);
                        if (CurrentLyrics.Count > 0) return;
                    }
                }
            }
            catch { }
        }

        private static readonly Regex LrcRegex = new(@"\[(\d{1,2}):(\d{1,2})(?:[\.:](\d{1,3}))?\](.*)", RegexOptions.Compiled);

        public void ParseLrcContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            try
            {
                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                bool hasTimestamps = false;
                var parsedLines = new List<LyricLine>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var matches = LrcRegex.Matches(line);
                    if (matches.Count > 0)
                    {
                        hasTimestamps = true;
                        string text = matches[matches.Count - 1].Groups[4].Value.Trim();

                        foreach (Match m in matches)
                        {
                            if (int.TryParse(m.Groups[1].Value, out int min) &&
                                int.TryParse(m.Groups[2].Value, out int sec))
                            {
                                int ms = 0;
                                if (m.Groups[3].Success)
                                {
                                    string msStr = m.Groups[3].Value;
                                    if (int.TryParse(msStr, out int parsedMs))
                                    {
                                        if (msStr.Length == 2) ms = parsedMs * 10;
                                        else if (msStr.Length == 1) ms = parsedMs * 100;
                                        else ms = parsedMs;
                                    }
                                }

                                var time = new TimeSpan(0, 0, min, sec, ms);
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    parsedLines.Add(new LyricLine { Time = time, Text = text });
                                }
                            }
                        }
                    }
                }

                if (hasTimestamps && parsedLines.Count > 0)
                {
                    CurrentLyrics = parsedLines.OrderBy(l => l.Time).ToList();
                }
                else
                {
                    // Plain text lyrics without timestamps: distribute evenly or store as single blocks
                    CurrentLyrics = lines
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("["))
                        .Select((l, idx) => new LyricLine { Time = TimeSpan.FromSeconds(idx * 4), Text = l })
                        .ToList();
                }
            }
            catch { }
        }

        private string ExtractEmbeddedLyricsFromBytes(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[Math.Min(fs.Length, 1024 * 1024 * 4)]; // Scan first 4MB
                int read = fs.Read(buffer, 0, buffer.Length);
                if (read < 20) return "";

                // 1. ID3v2 "USLT" frame check
                int usltIdx = FindBytePattern(buffer, new byte[] { (byte)'U', (byte)'S', (byte)'L', (byte)'T' });
                if (usltIdx >= 0 && usltIdx + 10 < read)
                {
                    int frameSize = (buffer[usltIdx + 4] << 21) | (buffer[usltIdx + 5] << 14) | (buffer[usltIdx + 6] << 7) | buffer[usltIdx + 7];
                    if (frameSize <= 0 || frameSize > 200000)
                    {
                        frameSize = (buffer[usltIdx + 4] << 24) | (buffer[usltIdx + 5] << 16) | (buffer[usltIdx + 6] << 8) | buffer[usltIdx + 7];
                    }
                    if (frameSize > 10 && usltIdx + 10 + frameSize <= read)
                    {
                        // Skip header (10 bytes) + encoding (1 byte) + lang (3 bytes)
                        int textStart = usltIdx + 14;
                        // Skip descriptor (null-terminated)
                        while (textStart < usltIdx + 10 + frameSize && buffer[textStart] != 0) textStart++;
                        textStart++;
                        int textLen = (usltIdx + 10 + frameSize) - textStart;
                        if (textLen > 0)
                        {
                            return Encoding.UTF8.GetString(buffer, textStart, textLen).Trim('\0', ' ', '\r', '\n');
                        }
                    }
                }

                // 2. Vorbis Comment "LYRICS=" check (FLAC / OGG)
                int lyrIdx = FindBytePattern(buffer, Encoding.ASCII.GetBytes("LYRICS="));
                if (lyrIdx >= 0)
                {
                    int start = lyrIdx + 7;
                    int end = start;
                    while (end < read && buffer[end] != 0 && end - start < 50000) end++;
                    return Encoding.UTF8.GetString(buffer, start, end - start).Trim('\0', ' ', '\r', '\n');
                }
            }
            catch { }
            return "";
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

        public (LyricLine? current, LyricLine? next) GetLyricsAt(TimeSpan position)
        {
            if (CurrentLyrics.Count == 0) return (null, null);

            for (int i = CurrentLyrics.Count - 1; i >= 0; i--)
            {
                if (position >= CurrentLyrics[i].Time)
                {
                    var current = CurrentLyrics[i];
                    LyricLine? next = (i + 1 < CurrentLyrics.Count) ? CurrentLyrics[i + 1] : null;
                    return (current, next);
                }
            }

            return (null, CurrentLyrics.FirstOrDefault());
        }

        public int GetCurrentLyricIndex(TimeSpan position)
        {
            if (CurrentLyrics.Count == 0) return -1;

            for (int i = CurrentLyrics.Count - 1; i >= 0; i--)
            {
                if (position >= CurrentLyrics[i].Time)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
