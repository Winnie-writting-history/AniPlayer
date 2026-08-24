using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace AnniPlayer.Services
{
    public class LanguageInfo
    {
        public string Code { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public class I18nService : INotifyPropertyChanged
    {
        public static I18nService Instance { get; } = new I18nService();

        // Flat key → value dictionary (auto-flattened from nested JSON)
        private Dictionary<string, string> _strings = new();
        private Dictionary<string, string> _fallbackStrings = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public string this[string key]
        {
            get
            {
                if (_strings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
                    return val;
                if (_fallbackStrings.TryGetValue(key, out var fallbackVal) && !string.IsNullOrEmpty(fallbackVal))
                    return fallbackVal;
                return key;
            }
        }

        public string CurrentLanguage { get; private set; } = "zh-CN";

        private static string GetConfigPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniPlayer");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "config.json");
        }

        private I18nService()
        {
            LoadFallbackDictionary();
            string initialLang = LoadLanguageFromConfig();
            ChangeLanguage(initialLang, saveConfig: false);
        }

        private void LoadFallbackDictionary()
        {
            try
            {
                string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales", "en-US.json");
                if (!File.Exists(fallbackPath))
                {
                    fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales", "zh-CN.json");
                }

                if (File.Exists(fallbackPath))
                {
                    string json = File.ReadAllText(fallbackPath);
                    using var doc = JsonDocument.Parse(json);
                    var flat = new Dictionary<string, string>();
                    FlattenJson(doc.RootElement, flat);
                    _fallbackStrings = flat;
                }
            }
            catch { }
        }

        public List<LanguageInfo> GetAvailableLanguages()
        {
            var list = new List<LanguageInfo>();
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales");
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.json");
                foreach (var f in files)
                {
                    string code = Path.GetFileNameWithoutExtension(f);
                    string displayName = code;
                    try
                    {
                        string json = File.ReadAllText(f);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("LanguageDisplayName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                            {
                                displayName = nameProp.GetString() ?? code;
                            }
                        }
                    }
                    catch { }

                    list.Add(new LanguageInfo { Code = code, DisplayName = displayName });
                }
            }

            if (list.Count == 0)
            {
                list.Add(new LanguageInfo { Code = "zh-CN", DisplayName = "简体中文 (zh-CN)" });
                list.Add(new LanguageInfo { Code = "en-US", DisplayName = "English (en-US)" });
            }

            return list;
        }

        /// <summary>
        /// 获取给定语言代码的泛化匹配关键字（包含 ISO 639-1 / 639-2、常用英文/本地别名）
        /// </summary>
        public static string[] GetLanguageMatchingKeywords(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) return Array.Empty<string>();

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                languageCode.Trim().ToLowerInvariant()
            };

            string normalized = languageCode.Replace("_", "-").ToLowerInvariant();
            string[] parts = normalized.Split('-');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                keywords.Add(parts[0]);
            }

            switch (parts[0])
            {
                case "zh":
                    keywords.UnionWith(new[] { "chi", "zho", "zh-cn", "zh-hans", "zh-hant", "zh-tw", "zh-hk", "chinese", "mandarin", "国语", "普通话", "中文", "汉语", "粤语", "cantonese" });
                    break;
                case "en":
                    keywords.UnionWith(new[] { "eng", "en-us", "en-gb", "english", "英文", "英语" });
                    break;
                case "ja":
                    keywords.UnionWith(new[] { "jpn", "japanese", "日文", "日语", "日本語" });
                    break;
                case "ko":
                    keywords.UnionWith(new[] { "kor", "korean", "韩文", "韩语", "朝鲜语", "한국어" });
                    break;
                case "fr":
                    keywords.UnionWith(new[] { "fre", "fra", "french", "français", "法文", "法语" });
                    break;
                case "de":
                    keywords.UnionWith(new[] { "ger", "deu", "german", "deutsch", "德文", "德语" });
                    break;
                case "es":
                    keywords.UnionWith(new[] { "spa", "spanish", "español", "西班牙文", "西班牙语" });
                    break;
                case "ru":
                    keywords.UnionWith(new[] { "rus", "russian", "русский", "俄文", "俄语" });
                    break;
                case "it":
                    keywords.UnionWith(new[] { "ita", "italian", "italiano", "意大利文", "意大利语" });
                    break;
                case "pt":
                    keywords.UnionWith(new[] { "por", "portuguese", "português", "葡萄牙文", "葡萄牙语" });
                    break;
            }

            var arr = new string[keywords.Count];
            keywords.CopyTo(arr);
            return arr;
        }

        private string LoadLanguageFromConfig()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("language", out var prop))
                    {
                        string lang = prop.GetString() ?? "zh-CN";
                        var available = GetAvailableLanguages();
                        if (available.Exists(l => l.Code.Equals(lang, StringComparison.OrdinalIgnoreCase)))
                        {
                            return lang;
                        }
                    }
                }
            }
            catch { }
            return "zh-CN";
        }

        public void ChangeLanguage(string langCode, bool saveConfig = true)
        {
            CurrentLanguage = langCode;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales", $"{langCode}.json");

            // Fallback to zh-CN if not found
            if (!File.Exists(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales", "zh-CN.json");

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);

                    var flat = new Dictionary<string, string>();
                    FlattenJson(doc.RootElement, flat);

                    _strings = flat;

                    // Notify all WPF bindings to refresh
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(System.Windows.Data.Binding.IndexerName));
                }
                catch (Exception)
                {
                    // Ignore parsing errors
                }
            }

            if (saveConfig) SaveLanguageToConfig(langCode);
        }

        private void SaveLanguageToConfig(string langCode)
        {
            try
            {
                string path = GetConfigPath();
                var data = new Dictionary<string, object>();
                if (File.Exists(path))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (existing != null) data = existing;
                    }
                    catch { }
                }

                data["language"] = langCode;
                string newJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, newJson);
            }
            catch { }
        }

        /// <summary>
        /// Recursively walks a JsonElement and collects all leaf string values
        /// into <paramref name="result"/> using the leaf key (not the full path).
        /// Keys starting with "_" (e.g. "_note", "_info") are treated as
        /// developer-only comments and are skipped.
        /// </summary>
        private static void FlattenJson(JsonElement element, Dictionary<string, string> result)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in element.EnumerateObject())
            {
                // Skip developer comment / metadata keys
                if (prop.Name.StartsWith('_'))
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    // Leaf string value — store with the leaf key name
                    result[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    // Nested category — recurse into it
                    FlattenJson(prop.Value, result);
                }
                // Arrays and other types are ignored
            }
        }
    }
}
