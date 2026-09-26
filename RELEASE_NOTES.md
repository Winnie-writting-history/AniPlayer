# 🚀 AniPlayer v1.0.0 Release Notes

> **AniPlayer — Minimalist, Ad-Free, Modern Hardware-Accelerated Media Player for Windows**

### 📦 Downloads & Edition Selection

| Asset | Size | Recommended Environment |
| :--- | :---: | :--- |
| **`AniPlayer-v1.0.0-Win11-FrameworkDependent.zip`** | **~49 MB** | **Recommended for Windows 11**. Ultra-lightweight edition utilizing pre-installed .NET 8 desktop runtime. |
| **`AniPlayer-v1.0.0-Windows-x64-Self-Contained.zip`** | **~111 MB** | **Universal for Windows 10 & 11**. Bundled with complete self-contained .NET 8 runtime and Direct3D accelerators. Zero dependencies. |

---

### ✨ Key Highlights & Features

1. **Industrial-Grade Direct3D 11 / D3D11VA Hardware Acceleration**:
   - Powered by `libmpv` and FFmpeg rendering pipeline.
   - Smoothly decodes 4K / 8K / HDR / 10-bit HEVC, AV1, H.264, VP9, and WebM with 0 dropped frames and zero memory bloating.
2. **Comprehensive Network Streaming Protocols**:
   - Native support for **HLS (.m3u8)** with Adaptive Bitrate (ABR) and separated multi-audio track matching.
   - Support for **MPEG-DASH (.mpd)** and **HTTP-FLV** live streams.
   - Support for **RTSP / RTSPS** IPC security surveillance, **RTMP / RTMPS** broadcast streams, **SRT** broadcast-grade transport, and **UDP / RTP** multicast IPTV.
   - Pure RAM ring-buffering with adaptive caching and zero SSD wear.
3. **Modern XAML Dynamic Skinning & Theme Engine**:
   - Built-in themes: Esports Cyan, OLED Pure Black, Emerald Deep Forest, etc.
   - Full support for custom XAML/JSON skin packages with idle background video playback and background audio.
   - Built-in fault tolerance and automatic rollback to built-in themes upon syntax errors.
4. **Dedicated Vinyl Music Mode**:
   - Dedicated player view for MP3, FLAC, APE, WAV, and OGG lossless audio.
   - Dynamic turntable tonearm animation, LRC dynamic synchronized lyrics, and Night Mode dynamic range compression.
5. **Intelligent Black Bar Auto-Cropping & Ambient Blur Fill**:
   - Real-time video black bar detection to automatically crop cinematic and subtitle bars, combined with an ambient blurred background stretch mode (Smart Fill) to seamlessly eliminate black borders.
6. **Full Internationalization (i18n) & Zero Hardcoding**:
   - Out-of-the-box support for English and Simplified Chinese. 100% of UI texts and messages are externalized in JSON configuration files (`locales/`), allowing effortless community localization into any language.
7. **100% Clean, Ad-Free & Privacy-First**:
   - Zero built-in advertisements, no analytics, no background telemetry, and zero network connections during local playback (strictly requests network only when playing online streams or searching subtitles).
8. **Self-Contained & Portable**:
   - User configuration and playlists are safely stored in AppData. No registry modifications, fully green and portable.

---

### 🛡️ SHA-256 Checksums
```text
bb7a926eec9187f49e81f1d47ebb2d65b672fa00833e7bc21f80700515d9f73b  AniPlayer-v1.0.0-Win11-FrameworkDependent.zip (Win11 Framework-Dependent)
bf6f763db2654a2257ed6c4b4d05070547e518a2abbb0dc29f4e92007eb6a373  AniPlayer-v1.0.0-Windows-x64-Self-Contained.zip (Self-Contained Standalone)
```

---

### 💬 Feedback & Community
- Official Website: [https://aniplayer.ai.studio/](https://aniplayer.ai.studio/)
- Issue Tracker: Feel free to submit feedback or bug reports on GitHub Issues!
