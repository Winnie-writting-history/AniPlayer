using System;
using System.Collections.Generic;

namespace AnniPlayer.Models
{
    public class DonationNetwork
    {
        public string Name { get; set; } = "USDT";
        public string BadgeZh { get; set; } = "";
        public string BadgeEn { get; set; } = "";
        public string Chain { get; set; } = "";
        public string Address { get; set; } = "";
        public string NoteZh { get; set; } = "";
        public string NoteEn { get; set; } = "";
    }

    public class DonationConfig
    {
        public int Version { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public string TitleZh { get; set; } = "💖 捐助与支持安妮播放器";
        public string TitleEn { get; set; } = "💖 Support & Donation for Ani player";
        public string DescriptionZh { get; set; } = "本软件完全免费供大家使用，无需支付任何费用。赞助打赏虽非强制，但您的每一份支持都弥足珍贵与深受感激！";
        public string DescriptionEn { get; set; } = "This software is completely free of charge. You can use it freely without any payment. A donation is always appreciated, but not mandatory.";
        public List<DonationNetwork> Networks { get; set; } = new List<DonationNetwork>();
        public string WebsiteUrl { get; set; } = "https://aniplayer.ai.studio/";
    }
}
