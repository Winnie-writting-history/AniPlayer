using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AnniPlayer.Services
{
    public class StreamDownloadProgress
    {
        public string Url { get; set; } = "";
        public string TargetFilePath { get; set; } = "";
        public long ReceivedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double Percent { get; set; }
        public double SpeedBytesPerSec { get; set; }
        public bool IsHls { get; set; }
        public int CompletedSegments { get; set; }
        public int TotalSegments { get; set; }
    }

    public class StreamDownloadService
    {
        private static readonly Lazy<StreamDownloadService> _instance = new(() => new StreamDownloadService());
        public static StreamDownloadService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _activeCts;
        private readonly object _lock = new();

        public bool IsDownloading { get; private set; } = false;
        public string CurrentUrl { get; private set; } = "";
        public string CurrentSavePath { get; private set; } = "";

        public event Action<StreamDownloadProgress>? ProgressChanged;
        public event Action<string>? Started;
        public event Action<string>? Completed;
        public event Action<string>? Failed;

        private StreamDownloadService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 AniPlayer/1.0");
        }

        public string GetDefaultSaveDirectory()
        {
            string cfgDir = SettingsService.Instance.Config.NetworkStreamSaveDir;
            if (!string.IsNullOrWhiteSpace(cfgDir) && Directory.Exists(cfgDir))
            {
                return cfgDir;
            }

            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "AniPlayer", "Streams");
            try
            {
                Directory.CreateDirectory(defaultDir);
            }
            catch { }
            return defaultDir;
        }

        public static string GenerateSafeFileName(string url, string? title = null)
        {
            string name = "";
            // 1. Check title if provided and not a URL
            if (!string.IsNullOrWhiteSpace(title) && !title.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !title.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                name = title.Trim();
            }

            // 2. Parse from URL query or path
            if (string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    var uri = new Uri(url);
                    // Check URL query parameters: title, name, filename, video_title
                    string query = uri.Query;
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        var match = Regex.Match(query, @"(?:[?&])(?:title|name|filename|video_title)=([^&]+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            name = Uri.UnescapeDataString(match.Groups[1].Value);
                        }
                    }

                    // Extract from URL absolute path
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        string lastSeg = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
                        if (!string.IsNullOrWhiteSpace(lastSeg))
                        {
                            // If it's a generic m3u8 playlist name, check the preceding directory segment
                            if (lastSeg.Equals("master.m3u8", StringComparison.OrdinalIgnoreCase) ||
                                lastSeg.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase) ||
                                lastSeg.Equals("playlist.m3u8", StringComparison.OrdinalIgnoreCase) ||
                                lastSeg.Equals("live.m3u8", StringComparison.OrdinalIgnoreCase))
                            {
                                var segments = uri.Segments.Select(s => Uri.UnescapeDataString(s.Trim('/', '\\'))).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                                if (segments.Length >= 2)
                                {
                                    string prev = segments[segments.Length - 2];
                                    if (!prev.Equals("hls", StringComparison.OrdinalIgnoreCase) && !prev.Equals("live", StringComparison.OrdinalIgnoreCase))
                                    {
                                        name = prev;
                                    }
                                }
                            }
                            else
                            {
                                name = Path.GetFileNameWithoutExtension(lastSeg);
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Clean invalid Windows filename characters
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    name = name.Replace(c, '_');
                }
                name = name.Trim(' ', '.', '_');
            }

            // 4. Strip any existing media extension to avoid double extensions (e.g. .mp4.mp4)
            if (!string.IsNullOrWhiteSpace(name))
            {
                string existingExt = Path.GetExtension(name);
                if (!string.IsNullOrEmpty(existingExt) && existingExt.Length <= 6)
                {
                    name = Path.GetFileNameWithoutExtension(name).Trim(' ', '.', '_');
                }
            }

            // 5. Fallback if empty or invalid
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Stream_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            if (name.Length > 80) name = name.Substring(0, 80);
            return name;
        }

        public void StartDownload(string url, string? saveDirectory = null, string? customTitle = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            lock (_lock)
            {
                StopDownload();

                string saveDir = !string.IsNullOrWhiteSpace(saveDirectory) && Directory.Exists(saveDirectory)
                    ? saveDirectory
                    : GetDefaultSaveDirectory();

                try
                {
                    Directory.CreateDirectory(saveDir);
                }
                catch { }

                string baseName = GenerateSafeFileName(url, customTitle);
                bool isHls = url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
                bool isMpd = url.Contains(".mpd", StringComparison.OrdinalIgnoreCase);
                string ext = (isHls || isMpd) ? ".mp4" : GetExtensionFromUrl(url);

                string targetFile = Path.Combine(saveDir, $"{baseName}{ext}");
                int counter = 1;
                while (File.Exists(targetFile))
                {
                    targetFile = Path.Combine(saveDir, $"{baseName} ({counter++}){ext}");
                }

                CurrentUrl = url;
                CurrentSavePath = targetFile;
                IsDownloading = true;

                var downloadCts = new CancellationTokenSource();
                _activeCts = downloadCts;
                var token = downloadCts.Token;

                Started?.Invoke(CurrentSavePath);

                Task.Run(async () =>
                {
                    try
                    {
                        if (isHls)
                        {
                            await DownloadHlsAsync(url, targetFile, token);
                        }
                        else if (isMpd)
                        {
                            await DownloadMpdAsync(url, targetFile, token);
                        }
                        else
                        {
                            await DownloadDirectStreamAsync(url, targetFile, token);
                        }

                        bool isCurrentDownload = CompleteDownload(downloadCts);
                        if (isCurrentDownload && !token.IsCancellationRequested && File.Exists(targetFile))
                        {
                            Completed?.Invoke(targetFile);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Clean up temporary files on user cancellation if incomplete
                        CleanupPartFile(targetFile);
                        CompleteDownload(downloadCts);
                    }
                    catch (Exception ex)
                    {
                        CleanupPartFile(targetFile);
                        if (CompleteDownload(downloadCts))
                        {
                            Failed?.Invoke(ex.Message);
                        }
                    }
                    finally
                    {
                        downloadCts.Dispose();
                    }
                });
            }
        }

        public void StopDownload()
        {
            lock (_lock)
            {
                if (_activeCts != null)
                {
                    try
                    {
                        _activeCts.Cancel();
                    }
                    catch { }
                    _activeCts = null;
                }
                IsDownloading = false;
            }
        }

        private bool CompleteDownload(CancellationTokenSource downloadCts)
        {
            lock (_lock)
            {
                // A cancelled older worker must not clear the state of a newer download.
                if (!ReferenceEquals(_activeCts, downloadCts)) return false;
                _activeCts = null;
                IsDownloading = false;
                return true;
            }
        }

        private static string GetExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5)
                {
                    return ext;
                }
            }
            catch { }
            return ".mp4";
        }

        private static void CleanupPartFile(string targetFile)
        {
            string partFile = $"{targetFile}.anni_part";
            try
            {
                if (File.Exists(partFile))
                {
                    File.Delete(partFile);
                }
            }
            catch { }
        }

        private async Task DownloadDirectStreamAsync(string url, string targetFile, CancellationToken ct)
        {
            string partFile = $"{targetFile}.anni_part";

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;

            using (var destination = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, useAsync: true))
            {
                if (totalBytes > 0)
                {
                    // Sparse pre-allocation: Set length instantly on NTFS without physical zeroing
                    destination.SetLength(totalBytes);
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[64 * 1024];
                long totalRead = 0;
                int bytesRead;

                var speedWatch = System.Diagnostics.Stopwatch.StartNew();
                long lastBytes = 0;
                double currentSpeed = 0;
                var lastProgressTime = DateTime.UtcNow;

                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    // Calculate speed & trigger progress every 300ms
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressTime).TotalMilliseconds >= 300)
                    {
                        double elapsedSec = speedWatch.Elapsed.TotalSeconds;
                        if (elapsedSec > 0.1)
                        {
                            currentSpeed = (totalRead - lastBytes) / elapsedSec;
                            lastBytes = totalRead;
                            speedWatch.Restart();
                        }

                        lastProgressTime = now;
                        double percent = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;

                        ProgressChanged?.Invoke(new StreamDownloadProgress
                        {
                            Url = url,
                            TargetFilePath = targetFile,
                            ReceivedBytes = totalRead,
                            TotalBytes = totalBytes,
                            Percent = percent,
                            SpeedBytesPerSec = currentSpeed,
                            IsHls = false
                        });
                    }
                }

                await destination.FlushAsync(ct);
            }

            // Atomic rename from .anni_part to final target file
            if (File.Exists(partFile))
            {
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }
                File.Move(partFile, targetFile);
            }
        }

        private async Task DownloadHlsAsync(string m3u8Url, string targetFile, CancellationToken ct)
        {
            string partFile = $"{targetFile}.anni_part";

            // 1. Fetch main m3u8
            string m3u8Content = await _httpClient.GetStringAsync(m3u8Url, ct);
            var uriBase = new Uri(m3u8Url);

            // Handle Master Playlist: parse all variants and select the BEST (highest resolution & bandwidth)
            if (m3u8Content.Contains("#EXT-X-STREAM-INF"))
            {
                string[] lines = m3u8Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string bestSubUrl = "";
                long maxBandwidth = -1;
                long maxPixels = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length)
                    {
                        string subPath = lines[i + 1].Trim();
                        if (string.IsNullOrEmpty(subPath) || subPath.StartsWith("#")) continue;

                        long bw = 0;
                        var bwMatch = Regex.Match(line, @"(?:AVERAGE-BANDWIDTH|BANDWIDTH)=(\d+)", RegexOptions.IgnoreCase);
                        if (bwMatch.Success && long.TryParse(bwMatch.Groups[1].Value, out long parsedBw))
                        {
                            bw = parsedBw;
                        }

                        long pixels = 0;
                        var resMatch = Regex.Match(line, @"RESOLUTION=(\d+)x(\d+)", RegexOptions.IgnoreCase);
                        if (resMatch.Success &&
                            long.TryParse(resMatch.Groups[1].Value, out long w) &&
                            long.TryParse(resMatch.Groups[2].Value, out long h))
                        {
                            pixels = w * h;
                        }

                        // Determine if audio-only stream
                        bool isAudioOnly = line.IndexOf("RESOLUTION=", StringComparison.OrdinalIgnoreCase) < 0 &&
                                           line.IndexOf("avc", StringComparison.OrdinalIgnoreCase) < 0 &&
                                           line.IndexOf("hvc", StringComparison.OrdinalIgnoreCase) < 0 &&
                                           line.IndexOf("hev", StringComparison.OrdinalIgnoreCase) < 0 &&
                                           line.IndexOf("av1", StringComparison.OrdinalIgnoreCase) < 0 &&
                                           line.IndexOf("vp9", StringComparison.OrdinalIgnoreCase) < 0;

                        // Evaluation score: pixels first, then bandwidth
                        bool isBetter = false;
                        if (string.IsNullOrEmpty(bestSubUrl))
                        {
                            isBetter = true;
                        }
                        else if (!isAudioOnly && maxPixels <= 0 && pixels > 0)
                        {
                            isBetter = true;
                        }
                        else if (pixels > maxPixels)
                        {
                            isBetter = true;
                        }
                        else if (pixels == maxPixels && bw > maxBandwidth)
                        {
                            isBetter = true;
                        }
                        else if (maxPixels <= 0 && bw > maxBandwidth && !isAudioOnly)
                        {
                            isBetter = true;
                        }

                        if (isBetter)
                        {
                            maxBandwidth = bw;
                            maxPixels = pixels;
                            bestSubUrl = new Uri(uriBase, subPath).ToString();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(bestSubUrl))
                {
                    m3u8Url = bestSubUrl;
                    uriBase = new Uri(m3u8Url);
                    m3u8Content = await _httpClient.GetStringAsync(m3u8Url, ct);
                }
            }

            // 2. Parse segments from media playlist and optional #EXT-X-MAP initialization segment
            var segmentLines = m3u8Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var segmentUrls = new List<string>();
            string? initUrl = null;

            foreach (var line in segmentLines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("#EXT-X-MAP", StringComparison.OrdinalIgnoreCase))
                {
                    var mapMatch = Regex.Match(trimmed, @"URI=[""']?([^""'\s>]+)[""']?", RegexOptions.IgnoreCase);
                    if (mapMatch.Success)
                    {
                        initUrl = new Uri(uriBase, mapMatch.Groups[1].Value).ToString();
                    }
                    continue;
                }

                if (trimmed.StartsWith("#")) continue;

                var segUri = new Uri(uriBase, trimmed);
                segmentUrls.Add(segUri.ToString());
            }

            if (segmentUrls.Count == 0)
            {
                throw new Exception("HLS Playlist contains no readable media segments.");
            }

            int totalSegments = segmentUrls.Count + (initUrl != null ? 1 : 0);
            long totalBytesReceived = 0;

            using (var destination = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, useAsync: true))
            {
                var speedWatch = System.Diagnostics.Stopwatch.StartNew();
                long lastBytes = 0;
                double currentSpeed = 0;

                // Download initialization segment if present (e.g. fMP4)
                if (!string.IsNullOrEmpty(initUrl))
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] initData = await _httpClient.GetByteArrayAsync(initUrl, ct);
                    await destination.WriteAsync(initData, 0, initData.Length, ct);
                    totalBytesReceived += initData.Length;
                }

                for (int i = 0; i < segmentUrls.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string segUrl = segmentUrls[i];
                    byte[] segData = await _httpClient.GetByteArrayAsync(segUrl, ct);

                    await destination.WriteAsync(segData, 0, segData.Length, ct);
                    totalBytesReceived += segData.Length;

                    double elapsedSec = speedWatch.Elapsed.TotalSeconds;
                    if (elapsedSec > 0.2)
                    {
                        currentSpeed = (totalBytesReceived - lastBytes) / elapsedSec;
                        lastBytes = totalBytesReceived;
                        speedWatch.Restart();
                    }

                    int completed = (initUrl != null ? 1 : 0) + i + 1;
                    double percent = (double)completed / totalSegments * 100.0;
                    ProgressChanged?.Invoke(new StreamDownloadProgress
                    {
                        Url = m3u8Url,
                        TargetFilePath = targetFile,
                        ReceivedBytes = totalBytesReceived,
                        TotalBytes = -1,
                        Percent = percent,
                        SpeedBytesPerSec = currentSpeed,
                        IsHls = true,
                        CompletedSegments = i + 1,
                        TotalSegments = totalSegments
                    });
                }

                await destination.FlushAsync(ct);
            }

            // Atomic rename from .anni_part to final target file
            if (File.Exists(partFile))
            {
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }
                File.Move(partFile, targetFile);
            }
        }

        private static double ParseIso8601Duration(string isoDuration)
        {
            if (string.IsNullOrWhiteSpace(isoDuration)) return 0;
            try
            {
                return System.Xml.XmlConvert.ToTimeSpan(isoDuration).TotalSeconds;
            }
            catch
            {
                double totalSec = 0;
                var mHours = Regex.Match(isoDuration, @"(\d+(?:\.\d+)?)H", RegexOptions.IgnoreCase);
                var mMins = Regex.Match(isoDuration, @"(\d+(?:\.\d+)?)M", RegexOptions.IgnoreCase);
                var mSecs = Regex.Match(isoDuration, @"(\d+(?:\.\d+)?)S", RegexOptions.IgnoreCase);
                if (mHours.Success && double.TryParse(mHours.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double h)) totalSec += h * 3600;
                if (mMins.Success && double.TryParse(mMins.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double m)) totalSec += m * 60;
                if (mSecs.Success && double.TryParse(mSecs.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double s)) totalSec += s;
                return totalSec;
            }
        }

        private async Task DownloadMpdAsync(string mpdUrl, string targetFile, CancellationToken ct)
        {
            string partFile = $"{targetFile}.anni_part";
            string mpdContent = await _httpClient.GetStringAsync(mpdUrl, ct);
            var uriBase = new Uri(mpdUrl);

            var xmlDoc = new System.Xml.XmlDocument();
            xmlDoc.LoadXml(mpdContent);

            // Check BaseURL inside MPD
            var baseNode = xmlDoc.SelectSingleNode("//*[local-name()='BaseURL']");
            if (baseNode != null && !string.IsNullOrWhiteSpace(baseNode.InnerText))
            {
                uriBase = new Uri(uriBase, baseNode.InnerText.Trim());
            }

            double totalDuration = 0;
            var mpdRoot = xmlDoc.DocumentElement;
            if (mpdRoot != null)
            {
                string durAttr = mpdRoot.GetAttribute("mediaPresentationDuration");
                if (!string.IsNullOrEmpty(durAttr))
                {
                    totalDuration = ParseIso8601Duration(durAttr);
                }
            }

            // Find Video AdaptationSet
            var adaptSets = xmlDoc.SelectNodes("//*[local-name()='AdaptationSet']");
            System.Xml.XmlNode? videoAdapt = null;
            if (adaptSets != null)
            {
                foreach (System.Xml.XmlNode adapt in adaptSets)
                {
                    string mime = adapt.Attributes?["mimeType"]?.Value ?? "";
                    string contentType = adapt.Attributes?["contentType"]?.Value ?? "";
                    if (mime.Contains("video", StringComparison.OrdinalIgnoreCase) ||
                        contentType.Contains("video", StringComparison.OrdinalIgnoreCase))
                    {
                        videoAdapt = adapt;
                        break;
                    }
                }
                if (videoAdapt == null && adaptSets.Count > 0)
                {
                    videoAdapt = adaptSets[0];
                }
            }

            if (videoAdapt == null)
            {
                throw new Exception("MPD Manifest contains no video adaptation set.");
            }

            // Pick best Representation (highest bandwidth / resolution)
            var reps = videoAdapt.SelectNodes(".//*[local-name()='Representation']");
            System.Xml.XmlNode? bestRep = null;
            long maxBandwidth = -1;

            if (reps != null)
            {
                foreach (System.Xml.XmlNode rep in reps)
                {
                    string bwStr = rep.Attributes?["bandwidth"]?.Value ?? "0";
                    if (long.TryParse(bwStr, out long bw) && bw > maxBandwidth)
                    {
                        maxBandwidth = bw;
                        bestRep = rep;
                    }
                }
                if (bestRep == null && reps.Count > 0) bestRep = reps[0];
            }

            string repId = bestRep?.Attributes?["id"]?.Value ?? "";

            // Find SegmentTemplate
            var segTemplate = bestRep?.SelectSingleNode(".//*[local-name()='SegmentTemplate']") 
                           ?? videoAdapt.SelectSingleNode(".//*[local-name()='SegmentTemplate']")
                           ?? xmlDoc.SelectSingleNode("//*[local-name()='SegmentTemplate']");

            var segmentUrls = new List<string>();
            string? initUrl = null;

            if (segTemplate != null)
            {
                string initPattern = segTemplate.Attributes?["initialization"]?.Value ?? "";
                string mediaPattern = segTemplate.Attributes?["media"]?.Value ?? "";
                long timescale = long.TryParse(segTemplate.Attributes?["timescale"]?.Value, out long ts) && ts > 0 ? ts : 1;
                double segDuration = double.TryParse(segTemplate.Attributes?["duration"]?.Value, out double d) ? d / timescale : 0;
                long startNumber = long.TryParse(segTemplate.Attributes?["startNumber"]?.Value, out long sn) ? sn : 1;

                if (!string.IsNullOrEmpty(initPattern))
                {
                    string initRel = initPattern.Replace("$RepresentationID$", repId);
                    initUrl = new Uri(uriBase, initRel).ToString();
                }

                // Check if SegmentTimeline exists
                var timeline = segTemplate.SelectSingleNode(".//*[local-name()='SegmentTimeline']");
                if (timeline != null)
                {
                    var sNodes = timeline.SelectNodes(".//*[local-name()='S']");
                    long currentTime = 0;
                    if (sNodes != null)
                    {
                        foreach (System.Xml.XmlNode sNode in sNodes)
                        {
                            string tStr = sNode.Attributes?["t"]?.Value ?? "";
                            string dStr = sNode.Attributes?["d"]?.Value ?? "0";
                            string rStr = sNode.Attributes?["r"]?.Value ?? "0";

                            if (long.TryParse(tStr, out long tVal)) currentTime = tVal;
                            long dVal = long.TryParse(dStr, out long dv) ? dv : 0;
                            long rVal = long.TryParse(rStr, out long rv) ? rv : 0;

                            for (long r = 0; r <= rVal; r++)
                            {
                                string segRel = mediaPattern
                                    .Replace("$RepresentationID$", repId)
                                    .Replace("$Time$", currentTime.ToString())
                                    .Replace("$Number$", startNumber.ToString());
                                segmentUrls.Add(new Uri(uriBase, segRel).ToString());
                                currentTime += dVal;
                                startNumber++;
                            }
                        }
                    }
                }
                else if (segDuration > 0 && totalDuration > 0)
                {
                    int totalSegs = (int)Math.Ceiling(totalDuration / segDuration);
                    for (long i = 0; i < totalSegs; i++)
                    {
                        long segNum = startNumber + i;
                        string segRel = mediaPattern
                            .Replace("$RepresentationID$", repId)
                            .Replace("$Number$", segNum.ToString());
                        segmentUrls.Add(new Uri(uriBase, segRel).ToString());
                    }
                }
            }
            else
            {
                // Check SegmentList
                var segList = bestRep?.SelectSingleNode(".//*[local-name()='SegmentList']")
                           ?? videoAdapt.SelectSingleNode(".//*[local-name()='SegmentList']");
                if (segList != null)
                {
                    var initNode = segList.SelectSingleNode(".//*[local-name()='Initialization']");
                    string initSource = initNode?.Attributes?["sourceURL"]?.Value ?? "";
                    if (!string.IsNullOrEmpty(initSource))
                    {
                        initUrl = new Uri(uriBase, initSource).ToString();
                    }
                    var segUrls = segList.SelectNodes(".//*[local-name()='SegmentURL']");
                    if (segUrls != null)
                    {
                        foreach (System.Xml.XmlNode sUrl in segUrls)
                        {
                            string media = sUrl.Attributes?["media"]?.Value ?? "";
                            if (!string.IsNullOrEmpty(media))
                            {
                                segmentUrls.Add(new Uri(uriBase, media).ToString());
                            }
                        }
                    }
                }
            }

            if (segmentUrls.Count == 0 && string.IsNullOrEmpty(initUrl))
            {
                throw new Exception("MPD Manifest contains no downloadable video segments.");
            }

            int totalItems = (initUrl != null ? 1 : 0) + segmentUrls.Count;
            int completedItems = 0;
            long totalBytesReceived = 0;

            using (var destination = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, useAsync: true))
            {
                var speedWatch = System.Diagnostics.Stopwatch.StartNew();
                long lastBytes = 0;
                double currentSpeed = 0;

                // 1. Download initialization segment if present
                if (!string.IsNullOrEmpty(initUrl))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        byte[] initData = await _httpClient.GetByteArrayAsync(initUrl, ct);
                        await destination.WriteAsync(initData, 0, initData.Length, ct);
                        totalBytesReceived += initData.Length;
                    }
                    catch { }
                    completedItems++;
                }

                // 2. Download all media segments
                for (int i = 0; i < segmentUrls.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string segUrl = segmentUrls[i];
                    byte[] segData = await _httpClient.GetByteArrayAsync(segUrl, ct);
                    await destination.WriteAsync(segData, 0, segData.Length, ct);
                    totalBytesReceived += segData.Length;
                    completedItems++;

                    double elapsedSec = speedWatch.Elapsed.TotalSeconds;
                    if (elapsedSec > 0.2)
                    {
                        currentSpeed = (totalBytesReceived - lastBytes) / elapsedSec;
                        lastBytes = totalBytesReceived;
                        speedWatch.Restart();
                    }

                    double percent = (double)completedItems / totalItems * 100.0;
                    ProgressChanged?.Invoke(new StreamDownloadProgress
                    {
                        Url = mpdUrl,
                        TargetFilePath = targetFile,
                        ReceivedBytes = totalBytesReceived,
                        TotalBytes = -1,
                        Percent = percent,
                        SpeedBytesPerSec = currentSpeed,
                        IsHls = true,
                        CompletedSegments = completedItems,
                        TotalSegments = totalItems
                    });
                }

                await destination.FlushAsync(ct);
            }

            if (File.Exists(partFile))
            {
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }
                File.Move(partFile, targetFile);
            }
        }
    }
}
