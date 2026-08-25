using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AnniPlayer.Models;

namespace AnniPlayer.Services
{
    public class DonationService
    {
        public static DonationService Instance { get; } = new DonationService();

        private const string RemoteJsonUrl = "https://aniplayer.ai.studio/donation.json";
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3.5)
        };

        private DonationConfig? _cachedConfig;
        private DateTime _lastFetchTime = DateTime.MinValue;

        public DonationConfig Config => _cachedConfig ??= GetDefaultConfig();

        static DonationService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AniPlayer/1.0 (Windows; Native)");
            }
            catch { }
        }

        /// <summary>
        /// Asynchronously fetches the latest donation config from remote URL.
        /// If remote fails or is offline, instantly falls back to hardcoded default.
        /// </summary>
        public async Task<DonationConfig> FetchConfigAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedConfig != null && (DateTime.UtcNow - _lastFetchTime).TotalMinutes < 30)
            {
                return _cachedConfig;
            }

            try
            {
                var response = await _httpClient.GetAsync(RemoteJsonUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    var remoteConfig = JsonSerializer.Deserialize<DonationConfig>(json, jsonOptions);
                    if (remoteConfig != null && remoteConfig.Networks != null && remoteConfig.Networks.Count > 0)
                    {
                        _lastFetchTime = DateTime.UtcNow;
                        _cachedConfig = remoteConfig;
                        return remoteConfig;
                    }
                }
            }
            catch
            {
                // Network unavailable or server unreachable - silent fallback
            }

            return _cachedConfig ??= GetDefaultConfig();
        }

        public DonationConfig GetDefaultConfig()
        {
            return new DonationConfig
            {
                Version = 1,
                Enabled = true,
                TitleZh = "💖 捐助与支持安妮播放器",
                TitleEn = "💖 Support & Donation for Ani player",
                DescriptionZh = "本软件完全免费供大家使用，无需支付任何费用。赞助打赏虽非强制，但您的每一份支持都弥足珍贵与深受感激！",
                DescriptionEn = "This software is completely free of charge. You can use it freely without any payment. A donation is always appreciated, but not mandatory.",
                Networks = new List<DonationNetwork>
                {
                    new DonationNetwork
                    {
                        Name = "USDT (TRC20)",
                        BadgeZh = "",
                        BadgeEn = "",
                        Chain = "TRON (TRC20)",
                        Address = "TH94GGYbieeXPmuMHMFDU4F4rQ2oY2VupH",
                        NoteZh = "请务必通过 TRON (TRC20) 网络进行转账，转错网络资产将无法找回。",
                        NoteEn = "Please send via TRON (TRC20) network to avoid asset loss."
                    },
                    new DonationNetwork
                    {
                        Name = "USDT / ETH (ERC20)",
                        BadgeZh = "",
                        BadgeEn = "",
                        Chain = "Ethereum / BSC / Polygon / Arbitrum",
                        Address = "0xB3A1a70DF04f81E89F8cD65fAe6e885B8D18D6fF",
                        NoteZh = "支持 Ethereum (ERC20)、BNB Chain (BEP20)、Polygon 等 EVM 兼容链。",
                        NoteEn = "Supports Ethereum (ERC20), BNB Chain (BEP20), Polygon and other EVM chains."
                    }
                },
                WebsiteUrl = "https://aniplayer.ai.studio/"
            };
        }
    }
}
