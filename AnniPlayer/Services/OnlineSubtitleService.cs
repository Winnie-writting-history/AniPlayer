using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AnniPlayer.Services
{
    public class SubtitleSearchResult
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Language { get; set; } = "简体中文";
        public string Format { get; set; } = "SRT";
        public string Source { get; set; } = "OpenSubtitles";
        public string DownloadUrl { get; set; } = "";
        public int MatchScore { get; set; } = 95;

        // OpenSubtitles 下载时需要的 file_id（用于 /download 接口）
        public string FileId { get; set; } = "";
    }

    public class OnlineSubtitleService
    {
        private static readonly Lazy<OnlineSubtitleService> _instance = new(() => new OnlineSubtitleService());
        public static OnlineSubtitleService Instance => _instance.Value;

        private readonly HttpClient _httpClient;

        // =====================================================================
        // OpenSubtitles.com 官方 REST API 配置
        // 注册地址：https://www.opensubtitles.com/en/users/sign_up
        // API Key 申请：https://www.opensubtitles.com/consumers → "New Consumer"
        // 免费额度：每天 5 次匿名下载，登录后每天 20 次
        // =====================================================================
        private const string OS_API_BASE = "https://api.opensubtitles.com/api/v1";
        private const string OS_APP_NAME = "AniPlayer";
        private const string OS_APP_VERSION = "1.0";

        // 用户可在设置中配置自己的 API Key（目前可留空，使用 Demo Key）
        public string OpenSubtitlesApiKey { get; set; } = "";

        private OnlineSubtitleService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{OS_APP_NAME} v{OS_APP_VERSION}");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// 智能提取纯净主名称与剧集 (如 "House of the Dragon S03E02")
        /// 自动剔除 www.xingfan.cc, CHS&ENG, 1080p, x264 等无关修饰标记
        /// </summary>
        public string ExtractCleanKeyword(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "";
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // 1. 剔除常见网址域名与推广标识
            fileName = Regex.Replace(fileName, @"(?i)(www|http|https|\.com|\.net|\.org|\.cc|\.cn|\.tv|\.me|\.xyz|\.top|\.vip|xingfan|dytt|dy2018|ygdy8|proweb|bilibili|xunlei|yify|rarbg|eztv|shaanig|galaxytv|publicHD)", " ");

            // 2. 剔除分辨率、编码、压制组与语言修饰词
            fileName = Regex.Replace(fileName, @"(?i)\b(chs|cht|eng|chs&eng|gb|big5|subrip|hdtv|web-dl|webrip|hdrip|bluray|bdrip|remux|uhd|10bit|60fps|hdr|sdr|dv|dolby|atmos|1080p|720p|480p|2160p|4k|4K|x264|x265|hevc|h264|h265|avc|avc1|aac|dts|ac3|flac|mp3|multi)\b", " ");

            // 3. 替换特殊字符为空格
            fileName = fileName.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace('&', ' ').Replace('[', ' ').Replace(']', ' ').Replace('(', ' ').Replace(')', ' ');

            // 4. 提取 S01E02 剧集标号
            Match matchSe = Regex.Match(fileName, @"(?i)\bS\d+E\d+\b");
            string seasonEpisode = matchSe.Success ? matchSe.Value.ToUpperInvariant() : "";

            // 5. 清理多余空白
            string cleaned = Regex.Replace(fileName, @"\s+", " ").Trim();

            // 6. 如果 S01E02 被清洗掉了，补充回来
            if (!string.IsNullOrEmpty(seasonEpisode) && !cleaned.Contains(seasonEpisode, StringComparison.OrdinalIgnoreCase))
            {
                cleaned += " " + seasonEpisode;
            }

            return !string.IsNullOrEmpty(cleaned) ? cleaned : Path.GetFileNameWithoutExtension(filePath);
        }

        /// <summary>
        /// 在线字幕多源并发搜索
        /// 主力：OpenSubtitles.com REST API (全球最大合法字幕库)
        /// 备用：Subscene / Subhd HTML 解析 (如有可用端点)
        /// </summary>
        public async Task<List<SubtitleSearchResult>> SearchSubtitlesAsync(string videoPath, string userKeyword)
        {
            var list = new List<SubtitleSearchResult>();
            string cleanKeyword = !string.IsNullOrEmpty(userKeyword) ? userKeyword : ExtractCleanKeyword(videoPath);

            // 提取英文关键词（去除中文），更适合国际字幕库搜索
            string englishOnly = Regex.Replace(cleanKeyword, @"[\u4e00-\u9fa5]", " ").Trim();
            englishOnly = Regex.Replace(englishOnly, @"\s+", " ").Trim();

            var tasks = new List<Task<List<SubtitleSearchResult>>>();

            // 1. OpenSubtitles.com — 英文关键词搜索（主力，最大字幕库）
            if (!string.IsNullOrEmpty(englishOnly) && englishOnly.Length >= 3)
            {
                tasks.Add(FetchOpenSubtitlesAsync(englishOnly, "zh-CN,zh,en"));
            }

            // 2. 如果关键词包含中文，额外用中文直接搜索一次
            bool hasChinese = Regex.IsMatch(cleanKeyword, @"[\u4e00-\u9fa5]");
            if (hasChinese && englishOnly != cleanKeyword)
            {
                tasks.Add(FetchOpenSubtitlesAsync(cleanKeyword, "zh-CN,zh"));
            }

            try
            {
                var resultsArray = await Task.WhenAll(tasks);
                foreach (var res in resultsArray)
                    list.AddRange(res);
            }
            catch { }

            // 去重 + 按匹配分排序
            var uniqueList = list.GroupBy(x => !string.IsNullOrEmpty(x.FileId) ? x.FileId : (!string.IsNullOrEmpty(x.DownloadUrl) ? x.DownloadUrl : x.Title))
                                 .Select(g => g.First())
                                 .OrderByDescending(r => r.MatchScore)
                                 .ToList();

            return uniqueList;
        }

        /// <summary>
        /// 调用 OpenSubtitles.com 官方 REST API 搜索字幕
        /// 文档：https://opensubtitles.stoplight.io/docs/opensubtitles-api/
        /// </summary>
        private async Task<List<SubtitleSearchResult>> FetchOpenSubtitlesAsync(string keyword, string languages)
        {
            var results = new List<SubtitleSearchResult>();
            try
            {
                // 构建请求
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{OS_API_BASE}/subtitles?query={Uri.EscapeDataString(keyword)}&languages={Uri.EscapeDataString(languages)}");

                // API Key 头部（如果用户配置了，则使用；否则请求仍会发出但下载时受限）
                if (!string.IsNullOrEmpty(OpenSubtitlesApiKey))
                {
                    req.Headers.Add("Api-Key", OpenSubtitlesApiKey);
                }

                var resp = await _httpClient.SendAsync(req);

                if (!resp.IsSuccessStatusCode)
                {
                    // 401/403 说明需要 API Key 或 Key 无效
                    return results;
                }

                string json = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(json)) return results;

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var dataArr)) return results;
                if (dataArr.ValueKind != JsonValueKind.Array) return results;

                int score = 98;
                foreach (var item in dataArr.EnumerateArray())
                {
                    // 字幕属性在 attributes 对象里
                    if (!item.TryGetProperty("attributes", out var attrs)) continue;

                    string title = "";
                    if (attrs.TryGetProperty("release", out var releaseEl))
                        title = releaseEl.GetString() ?? "";
                    if (string.IsNullOrEmpty(title) && attrs.TryGetProperty("feature_details", out var fd))
                    {
                        if (fd.TryGetProperty("movie_name", out var mn)) title = mn.GetString() ?? "";
                    }
                    if (string.IsNullOrEmpty(title)) title = keyword;

                    // 语言
                    string lang = "English";
                    if (attrs.TryGetProperty("language", out var langEl))
                    {
                        string rawLang = langEl.GetString() ?? "";
                        lang = rawLang switch
                        {
                            "zh-CN" or "chi" or "zhs" or "sc" => "简体中文",
                            "zh-TW" or "zht" or "tc" => "繁體中文",
                            "en" => "English",
                            _ => rawLang
                        };
                    }

                    // 格式（OpenSubtitles 主要是 SRT）
                    string format = "SRT";
                    if (attrs.TryGetProperty("format", out var fmtEl))
                        format = (fmtEl.GetString() ?? "SRT").ToUpperInvariant();

                    // 评级提升中文字幕
                    int itemScore = Math.Max(60, score--);
                    if (lang == "简体中文" || lang == "繁體中文") itemScore = Math.Min(99, itemScore + 5);

                    // file_id 用于后续 /download 接口
                    string fileId = "";
                    if (attrs.TryGetProperty("files", out var filesArr) && filesArr.ValueKind == JsonValueKind.Array)
                    {
                        var firstFile = filesArr.EnumerateArray().FirstOrDefault();
                        if (firstFile.ValueKind != JsonValueKind.Undefined && firstFile.TryGetProperty("file_id", out var fid))
                            fileId = fid.ToString();
                    }

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        results.Add(new SubtitleSearchResult
                        {
                            Title = title,
                            Language = lang,
                            Format = format,
                            Source = "OpenSubtitles.com",
                            MatchScore = itemScore,
                            FileId = fileId,
                            DownloadUrl = "" // 通过 /download API 获取真实下载链接
                        });
                    }
                }
            }
            catch { }
            return results;
        }

        /// <summary>
        /// 通过 OpenSubtitles /download API 获取真实下载链接，再下载字幕文件
        /// 无 API Key 时也可获取下载链接（受匿名频率限制：每IP每天5次）
        /// </summary>
        public async Task<string?> DownloadSubtitleFileAsync(SubtitleSearchResult item, string videoPath)
        {
            try
            {
                string downloadUrl = item.DownloadUrl;

                // 如果是 OpenSubtitles 类型，先通过 /download 接口获取真实下载链接
                if (!string.IsNullOrEmpty(item.FileId) && string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = await GetOpenSubtitlesDownloadUrl(item.FileId) ?? "";
                    if (string.IsNullOrEmpty(downloadUrl)) return null;
                }

                if (string.IsNullOrEmpty(downloadUrl)) return null;

                byte[] fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                if (fileBytes == null || fileBytes.Length == 0) return null;

                string formatExt = "." + item.Format.ToLowerInvariant();

                // 自动解压 Zip 压缩包
                if (fileBytes.Length > 4 && fileBytes[0] == 0x50 && fileBytes[1] == 0x4B && fileBytes[2] == 0x03 && fileBytes[3] == 0x04)
                {
                    try
                    {
                        using var ms = new MemoryStream(fileBytes);
                        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
                        var subEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                                                                           e.FullName.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ||
                                                                           e.FullName.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase));
                        if (subEntry != null)
                        {
                            using var entryStream = subEntry.Open();
                            using var msOut = new MemoryStream();
                            await entryStream.CopyToAsync(msOut);
                            fileBytes = msOut.ToArray();
                            formatExt = Path.GetExtension(subEntry.FullName).ToLowerInvariant();
                        }
                    }
                    catch { }
                }

                string subFileName = Path.GetFileNameWithoutExtension(videoPath) + ".online" + formatExt;

                // 优先位置 1：视频同级目录
                string targetDir = Path.GetDirectoryName(videoPath) ?? "";
                string targetPath = Path.Combine(targetDir, subFileName);

                bool saveSuccess = false;
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    try
                    {
                        await File.WriteAllBytesAsync(targetPath, fileBytes);
                        saveSuccess = true;
                    }
                    catch { saveSuccess = false; }
                }

                // 备选位置 2：%APPDATA%\AniPlayer\subtitles\
                if (!saveSuccess)
                {
                    string appDataSubDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer", "subtitles");
                    if (!Directory.Exists(appDataSubDir)) Directory.CreateDirectory(appDataSubDir);
                    targetPath = Path.Combine(appDataSubDir, subFileName);
                    await File.WriteAllBytesAsync(targetPath, fileBytes);
                }

                return targetPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 调用 OpenSubtitles /download 接口获取真实一次性下载链接
        /// </summary>
        private async Task<string?> GetOpenSubtitlesDownloadUrl(string fileId)
        {
            try
            {
                var body = new { file_id = int.Parse(fileId) };
                string jsonBody = JsonSerializer.Serialize(body);

                using var req = new HttpRequestMessage(HttpMethod.Post, $"{OS_API_BASE}/download");
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(OpenSubtitlesApiKey))
                    req.Headers.Add("Api-Key", OpenSubtitlesApiKey);

                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("link", out var linkEl))
                    return linkEl.GetString();
            }
            catch { }
            return null;
        }
    }
}
