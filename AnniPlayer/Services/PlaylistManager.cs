using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AnniPlayer.Models;

namespace AnniPlayer.Services
{
    public enum PlaybackMode
    {
        Sequential,
        Shuffle
    }

    public enum RepeatMode
    {
        RepeatAll,    // 列表循环
        RepeatSingle, // 单曲循环
        None          // 播完即止
    }

    public enum PlaylistSortOption
    {
        NameAscending,
        NameDescending,
        DateDescending,
        DateAscending
    }

    public class PlaylistManager
    {
        public static PlaylistManager Instance { get; } = new PlaylistManager();

        public ObservableCollection<PlaylistItem> Items { get; } = new ObservableCollection<PlaylistItem>();
        public Dictionary<string, List<string>> SavedPlaylists { get; } = new();

        private readonly HashSet<string> _itemPathSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _playlistFile;
        private readonly string _libraryFile;
        private int _currentIndex = -1;
        private readonly HashSet<int> _playedShuffleIndices = new();
        private readonly List<int> _shuffleHistory = new();
        private int _shuffleHistoryIndex = -1;
        private CancellationTokenSource? _bgScanCts;

        public int CurrentIndex
        {
            get => _currentIndex;
            private set
            {
                if (_currentIndex >= 0 && _currentIndex < Items.Count)
                {
                    Items[_currentIndex].IsPlaying = false;
                }
                
                _currentIndex = value;
                
                if (_currentIndex >= 0 && _currentIndex < Items.Count)
                {
                    Items[_currentIndex].IsPlaying = true;
                }
            }
        }

        private PlaylistManager()
        {
            string rootAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldAppData = Path.Combine(rootAppData, "AnniPlayer");
            string appData = Path.Combine(rootAppData, "AniPlayer");

            string newPlDir = Path.Combine(appData, "playlists");
            string oldPlDir = Path.Combine(oldAppData, "playlists");
            if (!Directory.Exists(newPlDir) && Directory.Exists(oldPlDir))
            {
                try
                {
                    Directory.CreateDirectory(newPlDir);
                    foreach (var f in Directory.GetFiles(oldPlDir, "*.json"))
                    {
                        string dest = Path.Combine(newPlDir, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Copy(f, dest, true);
                    }
                }
                catch { }
            }

            _playlistFile = Path.Combine(appData, "playlists", "default.json");
            _libraryFile = Path.Combine(appData, "playlists", "library.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_playlistFile)!);
            Load();
            LoadLibrary();
        }

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
            ".ts", ".m2ts", ".mpg", ".mpeg", ".rmvb", ".3gp", ".vob", ".ogv", ".f4v", ".iso"
            // Note: audio-only formats intentionally excluded — use AudioExtensions/IsAudioFile instead
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tiff", ".jfif"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".aac", ".wav", ".m4a", ".ogg", ".opus", ".wma", ".ape", ".alac", ".aiff", ".dsd", ".dff", ".dsf"
        };

        public static bool IsAudioFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path);
            return AudioExtensions.Contains(ext);
        }

        public static bool IsVideoFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path);
            return VideoExtensions.Contains(ext);
        }

        public static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path);
            return ImageExtensions.Contains(ext);
        }

        public static bool IsNetworkUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("srt://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("rtp://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("mms://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("mmsh://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSupportedFile(string path)
        {
            return IsNetworkUrl(path) || IsVideoFile(path) || IsImageFile(path) || IsAudioFile(path);
        }

        public void AddFile(string path, bool playImmediately = false)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (_itemPathSet.Contains(path))
            {
                var existing = Items.FirstOrDefault(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null && playImmediately)
                {
                    CurrentIndex = Items.IndexOf(existing);
                }
                return;
            }

            var item = new PlaylistItem(path);
            _itemPathSet.Add(path);
            Items.Add(item);
            Save();

            if (playImmediately || Items.Count == 1)
            {
                CurrentIndex = Items.Count - 1;
            }
        }

        public void AddFilesBatch(IEnumerable<string> paths, bool playImmediatelyFirst = false, bool deferSave = false)
        {
            bool isFirst = true;
            bool anyAdded = false;
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (_itemPathSet.Contains(path)) continue;

                var item = new PlaylistItem(path);
                _itemPathSet.Add(path);
                Items.Add(item);
                anyAdded = true;

                if (isFirst && (playImmediatelyFirst || Items.Count == 1))
                {
                    CurrentIndex = Items.Count - 1;
                    isFirst = false;
                }
            }

            if (anyAdded && !deferSave)
            {
                Save();
            }
        }

        public async Task<List<string>> ScanPathsAsync(IEnumerable<string> paths, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var resultList = new List<string>();
                var comparer = new NaturalStringComparer();

                foreach (var path in paths)
                {
                    if (ct.IsCancellationRequested) break;
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            // Priority 1: Scan for video files in shallow/top-level directory only (no subdirectories)
                            var videoFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                                      .Where(IsVideoFile)
                                                      .OrderBy(f => f, comparer)
                                                      .ToList();

                            if (videoFiles.Count > 0)
                            {
                                resultList.AddRange(videoFiles);
                            }
                            else
                            {
                                // Priority 2: When video files do not exist, scan image and audio files
                                var imageFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                                          .Where(IsImageFile)
                                                          .OrderBy(f => f, comparer)
                                                          .ToList();
                                var audioFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                                          .Where(IsAudioFile)
                                                          .OrderBy(f => f, comparer)
                                                          .ToList();

                                if (audioFiles.Count > imageFiles.Count && audioFiles.Count > 0)
                                {
                                    resultList.AddRange(audioFiles);
                                }
                                else if (imageFiles.Count > 0)
                                {
                                    resultList.AddRange(imageFiles);
                                }
                            }
                        }
                        catch { }
                    }
                    else if (IsNetworkUrl(path) || (File.Exists(path) && IsSupportedFile(path)))
                    {
                        resultList.Add(path);
                    }
                }
                return resultList;
            }, ct);
        }

        public async Task LoadDirectoryAsync(
            IEnumerable<string> paths,
            string? targetOpenFile,
            Action<string>? onPlayTarget,
            Action<int>? onLoadedCountUpdated = null)
        {
            _bgScanCts?.Cancel();
            var myCts = new CancellationTokenSource();
            _bgScanCts = myCts;
            var ct = myCts.Token;

            var allFiles = await ScanPathsAsync(paths, ct);
            if (ct.IsCancellationRequested || allFiles.Count == 0) return;

            int sortMode = SettingsService.Instance.Config.PlaylistSortMode;
            if (sortMode != -1 && Enum.IsDefined(typeof(PlaylistSortOption), sortMode))
            {
                var option = (PlaylistSortOption)sortMode;
                allFiles = option switch
                {
                    PlaylistSortOption.NameAscending => allFiles.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList(),
                    PlaylistSortOption.NameDescending => allFiles.OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList(),
                    PlaylistSortOption.DateDescending => allFiles.OrderByDescending(f => GetFileModificationDate(f)).ToList(),
                    PlaylistSortOption.DateAscending => allFiles.OrderBy(f => GetFileModificationDate(f)).ToList(),
                    _ => allFiles
                };
            }

            // Determine target file to play
            string fileToPlay = "";
            if (!string.IsNullOrEmpty(targetOpenFile) && allFiles.Contains(targetOpenFile, StringComparer.OrdinalIgnoreCase))
            {
                fileToPlay = targetOpenFile;
            }
            else
            {
                fileToPlay = allFiles[0];
            }

            // Clear playlist items without canceling the newly created myCts token and defer disk write
            ClearInternal(cancelScan: false, deferSave: true);

            // Populate all items in exact order immediately (takes < 5ms for thousands of items)
            AddFilesBatch(allFiles, playImmediatelyFirst: false, deferSave: true);

            // Immediately set target playing item without inserting it out of order
            var currentItem = Items.FirstOrDefault(i => string.Equals(i.FilePath, fileToPlay, StringComparison.OrdinalIgnoreCase));
            if (currentItem != null)
            {
                SetCurrent(currentItem);
                onPlayTarget?.Invoke(currentItem.FilePath);
            }
            else
            {
                onPlayTarget?.Invoke(fileToPlay);
            }
            onLoadedCountUpdated?.Invoke(Items.Count);

            // After ALL items are loaded in memory and displayed in the playlist, write to disk once
            if (!ct.IsCancellationRequested)
            {
                Save();
            }
        }

        public async Task AppendDirectoryAsync(
            IEnumerable<string> paths,
            string? targetOpenFile,
            Action<string>? onPlayTarget,
            Action<int>? onLoadedCountUpdated = null)
        {
            _bgScanCts?.Cancel();
            var myCts = new CancellationTokenSource();
            _bgScanCts = myCts;
            var ct = myCts.Token;

            var allFiles = await ScanPathsAsync(paths, ct);
            if (ct.IsCancellationRequested || allFiles.Count == 0) return;

            // Determine target file to play
            string fileToPlay = "";
            if (!string.IsNullOrEmpty(targetOpenFile) && allFiles.Contains(targetOpenFile, StringComparer.OrdinalIgnoreCase))
            {
                fileToPlay = targetOpenFile;
            }
            else
            {
                fileToPlay = allFiles[0];
            }

            // Append all files in batch in-memory without clearing existing items and without saving to disk
            AddFilesBatch(allFiles, playImmediatelyFirst: false, deferSave: true);

            // Immediately set target playing item
            var currentItem = Items.FirstOrDefault(i => string.Equals(i.FilePath, fileToPlay, StringComparison.OrdinalIgnoreCase));
            if (currentItem != null)
            {
                SetCurrent(currentItem);
                onPlayTarget?.Invoke(currentItem.FilePath);
            }
            else
            {
                onPlayTarget?.Invoke(fileToPlay);
            }
            onLoadedCountUpdated?.Invoke(Items.Count);

            // Note: In-memory dynamic append only, do NOT call Save() to persist to disk.
        }

        public void MoveItem(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Items.Count || newIndex < 0 || newIndex >= Items.Count) return;
            if (oldIndex == newIndex) return;

            PlaylistItem? currentPlaying = (CurrentIndex >= 0 && CurrentIndex < Items.Count) ? Items[CurrentIndex] : null;

            Items.Move(oldIndex, newIndex);

            if (currentPlaying != null)
            {
                _currentIndex = Items.IndexOf(currentPlaying);
            }

            Save();
        }

        public void AddPaths(IEnumerable<string> paths, bool playFirst = false)
        {
            var comparer = new NaturalStringComparer();
            bool isFirst = true;
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        // Priority 1: Scan for video files in shallow/top-level directory only (no subdirectories)
                        var videoFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                                  .Where(IsVideoFile)
                                                  .OrderBy(f => f, comparer)
                                                  .ToList();

                        if (videoFiles.Count > 0)
                        {
                            foreach (var f in videoFiles)
                            {
                                AddFile(f, playImmediately: isFirst && playFirst);
                                if (isFirst && playFirst) isFirst = false;
                            }
                        }
                        else
                        {
                            // Priority 2: When video files do not exist, scan image and audio files
                            var imageFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                                      .Where(IsImageFile)
                                                      .OrderBy(f => f, comparer)
                                                      .ToList();
                            var audioFiles = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                                                      .Where(IsAudioFile)
                                                      .OrderBy(f => f, comparer)
                                                      .ToList();
                            
                            // Determine dominant file type: if audio files outnumber images, treat as pure audio folder; otherwise treat as image folder (images dominate).
                            if (imageFiles.Count > 0 || audioFiles.Count > 0)
                            {
                                if (audioFiles.Count > imageFiles.Count)
                                {
                                    // Audio dominant: load only audio files as music album
                                    foreach (var f in audioFiles)
                                    {
                                        AddFile(f, playImmediately: isFirst && playFirst);
                                        if (isFirst && playFirst) isFirst = false;
                                    }
                                }
                                else
                                {
                                    // Image dominant (or equal): load image files (audio will be treated as BGM elsewhere)
                                    foreach (var f in imageFiles)
                                    {
                                        AddFile(f, playImmediately: isFirst && playFirst);
                                        if (isFirst && playFirst) isFirst = false;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                else if (IsNetworkUrl(path) || (File.Exists(path) && IsSupportedFile(path)))
                {
                    AddFile(path, playImmediately: isFirst && playFirst);
                    if (isFirst && playFirst) isFirst = false;
                }
            }
        }

        public PlaybackMode Mode { get; set; } = PlaybackMode.Sequential;
        public RepeatMode Repeat { get; set; } = RepeatMode.RepeatAll;

        public PlaylistItem? GetCurrent()
        {
            if (CurrentIndex >= 0 && CurrentIndex < Items.Count)
                return Items[CurrentIndex];
            return null;
        }

        public void SetCurrent(PlaylistItem item)
        {
            int idx = Items.IndexOf(item);
            if (idx >= 0)
            {
                CurrentIndex = idx;
                _playedShuffleIndices.Add(idx);

                // Sync with shuffle history
                if (_shuffleHistoryIndex >= 0 && _shuffleHistoryIndex < _shuffleHistory.Count && _shuffleHistory[_shuffleHistoryIndex] == idx)
                {
                    // Already matching current history position
                }
                else
                {
                    if (_shuffleHistoryIndex >= 0 && _shuffleHistoryIndex < _shuffleHistory.Count - 1)
                    {
                        _shuffleHistory.RemoveRange(_shuffleHistoryIndex + 1, _shuffleHistory.Count - (_shuffleHistoryIndex + 1));
                    }
                    _shuffleHistory.Add(idx);
                    _shuffleHistoryIndex = _shuffleHistory.Count - 1;
                }
            }
        }

        public PlaylistItem? GetNext()
        {
            if (Items.Count == 0) return null;

            // Single item repeat takes priority
            if (Repeat == RepeatMode.RepeatSingle && CurrentIndex >= 0 && CurrentIndex < Items.Count)
            {
                return Items[CurrentIndex];
            }

            if (Mode == PlaybackMode.Shuffle && Items.Count > 1)
            {
                // Ensure current track is in history
                if (CurrentIndex >= 0)
                {
                    _playedShuffleIndices.Add(CurrentIndex);
                    if (_shuffleHistory.Count == 0)
                    {
                        _shuffleHistory.Add(CurrentIndex);
                        _shuffleHistoryIndex = 0;
                    }
                }

                // If user pressed Prev and is now advancing forward through existing history
                if (_shuffleHistoryIndex >= 0 && _shuffleHistoryIndex < _shuffleHistory.Count - 1)
                {
                    _shuffleHistoryIndex++;
                    int histIdx = _shuffleHistory[_shuffleHistoryIndex];
                    if (histIdx >= 0 && histIdx < Items.Count)
                    {
                        CurrentIndex = histIdx;
                        return Items[CurrentIndex];
                    }
                }

                // If all items played in shuffle cycle
                if (_playedShuffleIndices.Count >= Items.Count)
                {
                    if (Repeat == RepeatMode.None)
                    {
                        return null; // Stop playback!
                    }
                    _playedShuffleIndices.Clear();
                    if (CurrentIndex >= 0) _playedShuffleIndices.Add(CurrentIndex);
                }

                var unplayed = System.Linq.Enumerable.Range(0, Items.Count)
                                                     .Where(i => !_playedShuffleIndices.Contains(i) && i != CurrentIndex)
                                                     .ToList();

                if (unplayed.Count == 0)
                {
                    if (Repeat == RepeatMode.None) return null;
                    _playedShuffleIndices.Clear();
                    unplayed = System.Linq.Enumerable.Range(0, Items.Count)
                                                         .Where(i => i != CurrentIndex)
                                                         .ToList();
                    if (unplayed.Count == 0) unplayed = System.Linq.Enumerable.Range(0, Items.Count).ToList();
                }

                int nextIndex = unplayed[Random.Shared.Next(0, unplayed.Count)];
                _playedShuffleIndices.Add(nextIndex);
                _shuffleHistory.Add(nextIndex);
                _shuffleHistoryIndex = _shuffleHistory.Count - 1;
                CurrentIndex = nextIndex;
                return Items[CurrentIndex];
            }
            else
            {
                int nextIdx = CurrentIndex + 1;
                if (nextIdx >= Items.Count)
                {
                    if (Repeat == RepeatMode.None)
                    {
                        return null; // Stop at end of list!
                    }
                    nextIdx = 0;
                }
                CurrentIndex = nextIdx;
                return Items[CurrentIndex];
            }
        }

        public PlaylistItem? GetPrev()
        {
            if (Items.Count == 0) return null;

            if (Repeat == RepeatMode.RepeatSingle && CurrentIndex >= 0 && CurrentIndex < Items.Count)
            {
                return Items[CurrentIndex];
            }

            if (Mode == PlaybackMode.Shuffle && Items.Count > 1)
            {
                // Ensure current track is in history
                if (CurrentIndex >= 0 && _shuffleHistory.Count == 0)
                {
                    _shuffleHistory.Add(CurrentIndex);
                    _shuffleHistoryIndex = 0;
                }

                // Strictly navigate backward in recorded chronological history
                if (_shuffleHistoryIndex > 0)
                {
                    _shuffleHistoryIndex--;
                    int prevIdx = _shuffleHistory[_shuffleHistoryIndex];
                    if (prevIdx >= 0 && prevIdx < Items.Count)
                    {
                        CurrentIndex = prevIdx;
                        return Items[CurrentIndex];
                    }
                }

                // If already at the earliest point in shuffle history, keep current
                if (CurrentIndex >= 0 && CurrentIndex < Items.Count)
                {
                    return Items[CurrentIndex];
                }
                return null;
            }
            else
            {
                int prevIdx = CurrentIndex - 1;
                if (prevIdx < 0)
                {
                    if (Repeat == RepeatMode.None)
                    {
                        prevIdx = 0;
                    }
                    else
                    {
                        prevIdx = Items.Count - 1;
                    }
                }
                CurrentIndex = prevIdx;
                return Items[CurrentIndex];
            }
        }

        public void Sort(PlaylistSortOption option)
        {
            SettingsService.Instance.Config.PlaylistSortMode = (int)option;
            SettingsService.Instance.Save();

            if (Items.Count <= 1) return;

            var currentItem = GetCurrent();

            List<PlaylistItem> sortedList = option switch
            {
                PlaylistSortOption.NameAscending => Items.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList(),
                PlaylistSortOption.NameDescending => Items.OrderByDescending(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList(),
                PlaylistSortOption.DateDescending => Items.OrderByDescending(i => GetFileModificationDate(i.FilePath)).ToList(),
                PlaylistSortOption.DateAscending => Items.OrderBy(i => GetFileModificationDate(i.FilePath)).ToList(),
                _ => Items.ToList()
            };

            Items.Clear();
            _itemPathSet.Clear();
            foreach (var item in sortedList)
            {
                _itemPathSet.Add(item.FilePath);
                Items.Add(item);
            }

            if (currentItem != null)
            {
                int newIdx = Items.IndexOf(currentItem);
                if (newIdx >= 0)
                {
                    CurrentIndex = newIdx;
                }
            }
            Save();
        }

        private DateTime GetFileModificationDate(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.GetLastWriteTime(path);
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        public void Clear()
        {
            ClearInternal(cancelScan: true, deferSave: false);
        }

        public void ClearInternal(bool cancelScan = true, bool deferSave = false)
        {
            if (cancelScan)
            {
                _bgScanCts?.Cancel();
            }
            _itemPathSet.Clear();
            Items.Clear();
            CurrentIndex = -1;
            _playedShuffleIndices.Clear();
            _shuffleHistory.Clear();
            _shuffleHistoryIndex = -1;
            if (!deferSave)
            {
                Save();
            }
        }

        public void Remove(PlaylistItem item)
        {
            int idx = Items.IndexOf(item);
            if (idx < 0) return;

            _itemPathSet.Remove(item.FilePath);
            Items.RemoveAt(idx);
            
            // Adjust shuffle history
            for (int i = _shuffleHistory.Count - 1; i >= 0; i--)
            {
                if (_shuffleHistory[i] == idx)
                {
                    _shuffleHistory.RemoveAt(i);
                    if (_shuffleHistoryIndex >= i) _shuffleHistoryIndex--;
                }
                else if (_shuffleHistory[i] > idx)
                {
                    _shuffleHistory[i]--;
                }
            }
            if (_shuffleHistoryIndex < 0 && _shuffleHistory.Count > 0) _shuffleHistoryIndex = 0;

            if (CurrentIndex == idx)
            {
                CurrentIndex = -1; // stop playing or auto-next handled by caller
            }
            else if (CurrentIndex > idx)
            {
                _currentIndex--; // adjust internal index without firing property change events unnecessarily, but we should just use property
            }
            Save();
        }

        private void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_playlistFile, json);
            }
            catch (Exception) { }
        }

        private void Load()
        {
            if (File.Exists(_playlistFile))
            {
                try
                {
                    string json = File.ReadAllText(_playlistFile);
                    var list = JsonSerializer.Deserialize<PlaylistItem[]>(json);
                    if (list != null)
                    {
                        _itemPathSet.Clear();
                        foreach (var item in list)
                        {
                            item.IsPlaying = false;
                            _itemPathSet.Add(item.FilePath);
                            Items.Add(item);
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        public void SaveAsPlaylist(string name)
        {
            SavedPlaylists[name] = Items.Select(i => i.FilePath).ToList();
            SaveLibrary();
        }

        public void LoadPlaylist(string name, string? targetFile = null, Action<string>? onPlayTarget = null)
        {
            if (SavedPlaylists.TryGetValue(name, out var files) && files.Count > 0)
            {
                _bgScanCts?.Cancel();
                ClearInternal(cancelScan: false, deferSave: true);

                AddFilesBatch(files, playImmediatelyFirst: false, deferSave: true);

                string fileToPlay = (!string.IsNullOrEmpty(targetFile) && files.Contains(targetFile, StringComparer.OrdinalIgnoreCase))
                    ? targetFile
                    : files[0];

                var currentItem = Items.FirstOrDefault(i => string.Equals(i.FilePath, fileToPlay, StringComparison.OrdinalIgnoreCase));
                if (currentItem != null)
                {
                    SetCurrent(currentItem);
                    onPlayTarget?.Invoke(currentItem.FilePath);
                }
                else
                {
                    onPlayTarget?.Invoke(fileToPlay);
                }

                Save();
            }
        }

        public async Task LoadPlaylistAsync(string name, string? targetFile = null, Action<string>? onPlayTarget = null)
        {
            if (SavedPlaylists.TryGetValue(name, out var files) && files.Count > 0)
            {
                _bgScanCts?.Cancel();
                var myCts = new CancellationTokenSource();
                _bgScanCts = myCts;
                var ct = myCts.Token;

                ClearInternal(cancelScan: false, deferSave: true);

                AddFilesBatch(files, playImmediatelyFirst: false, deferSave: true);

                string fileToPlay = (!string.IsNullOrEmpty(targetFile) && files.Contains(targetFile, StringComparer.OrdinalIgnoreCase))
                    ? targetFile
                    : files[0];

                var currentItem = Items.FirstOrDefault(i => string.Equals(i.FilePath, fileToPlay, StringComparison.OrdinalIgnoreCase));
                if (currentItem != null)
                {
                    SetCurrent(currentItem);
                    onPlayTarget?.Invoke(currentItem.FilePath);
                }
                else
                {
                    onPlayTarget?.Invoke(fileToPlay);
                }

                if (!ct.IsCancellationRequested)
                {
                    await Task.Run(() => Save());
                }
            }
        }

        public void DeletePlaylist(string name)
        {
            if (SavedPlaylists.Remove(name))
            {
                SaveLibrary();
            }
        }

        public void ClearAllLibrary()
        {
            SavedPlaylists.Clear();
            Clear();
            SaveLibrary();
        }

        public void SaveLibrary()
        {
            try
            {
                string json = JsonSerializer.Serialize(SavedPlaylists, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_libraryFile, json);
            }
            catch (Exception) { }
        }

        public void ExportPlaylists(string targetFilePath, string? specificPlaylistName = null)
        {
            string ext = Path.GetExtension(targetFilePath).ToLowerInvariant();

            if (ext == ".m3u8" || ext == ".m3u")
            {
                var lines = new List<string> { "#EXTM3U" };
                List<string>? files = null;
                if (!string.IsNullOrEmpty(specificPlaylistName) && SavedPlaylists.TryGetValue(specificPlaylistName, out var pf))
                {
                    files = pf;
                }
                else
                {
                    files = SavedPlaylists.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }

                foreach (var file in files)
                {
                    string title = Path.GetFileNameWithoutExtension(file);
                    lines.Add($"#EXTINF:-1,{title}");
                    lines.Add(file);
                }
                File.WriteAllLines(targetFilePath, lines, System.Text.Encoding.UTF8);
            }
            else if (ext == ".txt")
            {
                List<string>? files = null;
                if (!string.IsNullOrEmpty(specificPlaylistName) && SavedPlaylists.TryGetValue(specificPlaylistName, out var pf))
                {
                    files = pf;
                }
                else
                {
                    files = SavedPlaylists.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                File.WriteAllLines(targetFilePath, files, System.Text.Encoding.UTF8);
            }
            else
            {
                // Default to JSON
                if (!string.IsNullOrEmpty(specificPlaylistName) && SavedPlaylists.TryGetValue(specificPlaylistName, out var pf))
                {
                    var singleDict = new Dictionary<string, List<string>> { { specificPlaylistName, pf } };
                    string json = JsonSerializer.Serialize(singleDict, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(targetFilePath, json, System.Text.Encoding.UTF8);
                }
                else
                {
                    string json = JsonSerializer.Serialize(SavedPlaylists, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(targetFilePath, json, System.Text.Encoding.UTF8);
                }
            }
        }

        public (int playlistsAdded, int itemsAdded) ImportPlaylists(string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath)) return (0, 0);

            string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            int playlistsCount = 0;
            int itemsCount = 0;

            if (ext == ".json")
            {
                string json = File.ReadAllText(sourceFilePath, System.Text.Encoding.UTF8);
                try
                {
                    // Case 1: Dictionary<string, List<string>> (Full library backup)
                    var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    if (dict != null && dict.Count > 0)
                    {
                        foreach (var kvp in dict)
                        {
                            string playlistName = GetUniquePlaylistName(kvp.Key);
                            var validFiles = kvp.Value.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
                            SavedPlaylists[playlistName] = validFiles;
                            playlistsCount++;
                            itemsCount += validFiles.Count;
                        }
                    }
                }
                catch
                {
                    try
                    {
                        // Case 2: List<string> (single playlist array)
                        var list = JsonSerializer.Deserialize<List<string>>(json);
                        if (list != null && list.Count > 0)
                        {
                            string name = GetUniquePlaylistName(Path.GetFileNameWithoutExtension(sourceFilePath));
                            SavedPlaylists[name] = list;
                            playlistsCount++;
                            itemsCount += list.Count;
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new FormatException($"Invalid JSON playlist format: {ex.Message}");
                    }
                }
            }
            else if (ext == ".m3u8" || ext == ".m3u" || ext == ".txt")
            {
                string baseDir = Path.GetDirectoryName(sourceFilePath) ?? "";
                var lines = File.ReadAllLines(sourceFilePath);
                var files = new List<string>();

                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    // Resolve relative paths if needed
                    string resolvedPath = line;
                    if (!Path.IsPathRooted(line) && !line.Contains("://") && !string.IsNullOrEmpty(baseDir))
                    {
                        string candidate = Path.GetFullPath(Path.Combine(baseDir, line));
                        if (File.Exists(candidate)) resolvedPath = candidate;
                    }

                    files.Add(resolvedPath);
                }

                if (files.Count > 0)
                {
                    string name = GetUniquePlaylistName(Path.GetFileNameWithoutExtension(sourceFilePath));
                    SavedPlaylists[name] = files;
                    playlistsCount++;
                    itemsCount += files.Count;
                }
            }

            if (playlistsCount > 0)
            {
                SaveLibrary();
            }

            return (playlistsCount, itemsCount);
        }

        private string GetUniquePlaylistName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Imported_Playlist";
            string name = baseName;
            int counter = 1;
            while (SavedPlaylists.ContainsKey(name))
            {
                name = $"{baseName}_{counter++}";
            }
            return name;
        }

        private void LoadLibrary()
        {
            if (File.Exists(_libraryFile))
            {
                try
                {
                    string json = File.ReadAllText(_libraryFile);
                    var lib = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    if (lib != null)
                    {
                        foreach (var kvp in lib)
                        {
                            SavedPlaylists[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
