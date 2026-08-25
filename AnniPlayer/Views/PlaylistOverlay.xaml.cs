
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AnniPlayer.Services;

namespace AnniPlayer.Views
{
    public class MergeItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string SizeDisplay { get; set; } = string.Empty;
        public string TypeIcon { get; set; } = "\uE8B2";

        public static string GetMediaTypeIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "\uE8B2";
            if (PlaylistManager.IsNetworkUrl(path))
            {
                return "\uE774"; // Globe
            }
            if (PlaylistManager.IsAudioFile(path))
            {
                return "\uEC4F"; // Music Note
            }
            if (PlaylistManager.IsImageFile(path))
            {
                return "\uEB9F"; // Photo/Picture
            }
            // Default: Video
            return "\uE8B2"; // Video
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return string.Empty;
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):0.#} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):0.#} MB";
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):0.##} GB";
        }

        public static string GetSizeString(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (PlaylistManager.IsNetworkUrl(path)) return "URL";
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    return FormatSize(fi.Length);
                }
            }
            catch { }
            return "--";
        }
    }

    public partial class PlaylistOverlay : System.Windows.Controls.UserControl
    {
        public event EventHandler? CloseRequested;
        public event EventHandler<string>? PlayRequested;
        public event Action<string, string>? PlaySpecificFileRequested;

        public bool IsEditingOrMerging => viewEdit.Visibility == Visibility.Visible || viewMerge.Visibility == Visibility.Visible;

        private ObservableCollection<string> _editFiles = new ObservableCollection<string>();
        private string _editingPlaylistName = string.Empty;
        private ObservableCollection<MergeItem> _mergeItems = new ObservableCollection<MergeItem>();

        public PlaylistOverlay()
        {
            InitializeComponent();
            this.Loaded += PlaylistOverlay_Loaded;
            this.KeyDown += PlaylistOverlay_KeyDown;
            lbEditFiles.ItemsSource = _editFiles;
            lbMergePlaylists.ItemsSource = _mergeItems;
        }

        private void PlaylistOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            ResetAndRefresh();
        }

        public void ResetAndRefresh()
        {
            this.Focusable = true;
            this.Focus();
            SwitchToView("Selection");
            RefreshPlaylists();
        }

        private void PlaylistOverlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (viewEdit.Visibility == Visibility.Visible)
                {
                    SwitchToView("Selection");
                    e.Handled = true;
                }
                else if (viewMerge.Visibility == Visibility.Visible)
                {
                    SwitchToView("Selection");
                    e.Handled = true;
                }
                else
                {
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void SwitchToView(string viewName)
        {
            viewSelection.Visibility = Visibility.Collapsed;
            viewEdit.Visibility = Visibility.Collapsed;
            viewMerge.Visibility = Visibility.Collapsed;
            txtTitle.Text = I18nService.Instance["LibraryTitle"];

            if (viewName == "Selection")
            {
                viewSelection.Visibility = Visibility.Visible;
                lbPlaylists.Focus();
            }
            else if (viewName == "Edit")
            {
                viewEdit.Visibility = Visibility.Visible;
                txtTitle.Text = I18nService.Instance["LibraryTitleEdit"];
                txtEditName.Focus();
            }
            else if (viewName == "Merge")
            {
                viewMerge.Visibility = Visibility.Visible;
                txtTitle.Text = I18nService.Instance["LibraryTitleMerge"];
                txtMergeName.Focus();
            }
        }

        private void RefreshPlaylists()
        {
            RefreshPlaylistsPreserveSelection(null);
        }

        private void RefreshPlaylistsPreserveSelection(string? targetSelected = null)
        {
            string? selected = targetSelected ?? (lbPlaylists.SelectedValue as string);
            var playlistItems = new List<object>();
            foreach (var key in PlaylistManager.Instance.SavedPlaylists.Keys.OrderBy(k => k))
            {
                var count = PlaylistManager.Instance.SavedPlaylists[key].Count;
                string countFmt = I18nService.Instance["LibraryVideosCount"];
                if (string.IsNullOrEmpty(countFmt) || countFmt.StartsWith("[")) countFmt = "({0} 个媒体文件)";
                playlistItems.Add(new { Name = key, Display = $"{key} ({string.Format(countFmt, count)})" });
            }
            lbPlaylists.DisplayMemberPath = "Display";
            lbPlaylists.SelectedValuePath = "Name";
            lbPlaylists.ItemsSource = playlistItems;
            
            if (!string.IsNullOrEmpty(selected) && PlaylistManager.Instance.SavedPlaylists.ContainsKey(selected))
            {
                lbPlaylists.SelectedValue = selected;
            }
            else if (playlistItems.Count > 0)
            {
                lbPlaylists.SelectedIndex = 0;
            }
            else
            {
                lbFiles.ItemsSource = null;
                txtSelectedPlaylist.Text = I18nService.Instance["LibraryNoSelection"];
            }
        }

        private void LbPlaylists_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lbPlaylists.SelectedValue is string name)
            {
                txtSelectedPlaylist.Text = name;
                if (PlaylistManager.Instance.SavedPlaylists.TryGetValue(name, out var files))
                {
                    lbFiles.ItemsSource = files.Select(f => new FileItem 
                    { 
                        Name = Path.GetFileName(f), 
                        Path = f,
                        SizeDisplay = FileItem.GetSizeString(f),
                        TypeIcon = FileItem.GetMediaTypeIcon(f)
                    }).ToList();
                }
                else
                {
                    lbFiles.ItemsSource = null;
                }
            }
        }

        private void BtnRemoveFileFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.Button btn && btn.Tag is FileItem fileItem && lbPlaylists.SelectedValue is string playlistName)
            {
                RemoveFileFromCurrentPlaylist(playlistName, fileItem);
            }
        }

        private void LbFiles_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete && lbFiles.SelectedItem is FileItem fileItem && lbPlaylists.SelectedValue is string playlistName)
            {
                RemoveFileFromCurrentPlaylist(playlistName, fileItem);
                e.Handled = true;
            }
        }

        private void RemoveFileFromCurrentPlaylist(string playlistName, FileItem fileItem)
        {
            if (PlaylistManager.Instance.SavedPlaylists.TryGetValue(playlistName, out var files))
            {
                if (files.Remove(fileItem.Path))
                {
                    PlaylistManager.Instance.SaveLibrary();

                    if (lbFiles.ItemsSource is List<FileItem> currentList)
                    {
                        int oldIdx = lbFiles.SelectedIndex;
                        currentList.Remove(fileItem);
                        lbFiles.ItemsSource = null;
                        lbFiles.ItemsSource = currentList;
                        if (currentList.Count > 0)
                        {
                            lbFiles.SelectedIndex = Math.Clamp(oldIdx, 0, currentList.Count - 1);
                        }
                    }

                    RefreshPlaylistsPreserveSelection(playlistName);
                }
            }
        }

        private void LbPlaylists_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lbPlaylists.SelectedValue is string name)
            {
                PlayRequested?.Invoke(this, name);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void LbFiles_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lbPlaylists.SelectedValue is string playlistName && lbFiles.SelectedItem is FileItem item)
            {
                PlaySpecificFileRequested?.Invoke(playlistName, item.Path);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (lbPlaylists.SelectedValue is string name)
            {
                PlayRequested?.Invoke(this, name);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            string name = "Playlist_" + DateTime.Now.ToString("MMdd_HHmmss");
            if (PlaylistManager.Instance.Items.Count > 0)
            {
                PlaylistManager.Instance.SaveAsPlaylist(name);
            }
            else
            {
                PlaylistManager.Instance.SavedPlaylists[name] = new List<string>();
                PlaylistManager.Instance.SaveLibrary();
            }
            RefreshPlaylists();
            lbPlaylists.SelectedValue = name;

            _editingPlaylistName = name;
            txtEditName.Text = name;
            _editFiles.Clear();
            if (PlaylistManager.Instance.SavedPlaylists.TryGetValue(name, out var files))
            {
                foreach (var f in files) _editFiles.Add(f);
            }
            SwitchToView("Edit");
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = I18nService.Instance["LibraryBtnImport"],
                    Filter = I18nService.Instance["LibraryImportFilter"],
                    Multiselect = true
                };

                if (ofd.ShowDialog() == true && ofd.FileNames.Length > 0)
                {
                    int totalPlaylists = 0;
                    int totalItems = 0;

                    foreach (var file in ofd.FileNames)
                    {
                        var (playlistsAdded, itemsAdded) = PlaylistManager.Instance.ImportPlaylists(file);
                        totalPlaylists += playlistsAdded;
                        totalItems += itemsAdded;
                    }

                    if (totalPlaylists > 0)
                    {
                        RefreshPlaylists();
                        string msg = string.Format(I18nService.Instance["LibraryImportSuccess"], totalPlaylists, totalItems);
                        System.Windows.MessageBox.Show(msg, I18nService.Instance["LibraryTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(string.Format(I18nService.Instance["LibraryImportFailed"], "No valid playlist items found."), I18nService.Instance["LibraryTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(string.Format(I18nService.Instance["LibraryImportFailed"], ex.Message), I18nService.Instance["LibraryTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PlaylistManager.Instance.SavedPlaylists.Count == 0)
                {
                    System.Windows.MessageBox.Show(I18nService.Instance["LibraryExportNoPlaylists"], I18nService.Instance["LibraryTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string? selectedPlaylist = lbPlaylists.SelectedValue as string;

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = I18nService.Instance["LibraryBtnExport"],
                    Filter = I18nService.Instance["LibraryExportFilter"]
                };

                if (!string.IsNullOrEmpty(selectedPlaylist))
                {
                    sfd.FileName = $"{selectedPlaylist}.m3u8";
                }
                else
                {
                    sfd.FileName = $"AniPlayer_Library_Backup_{DateTime.Now:yyyyMMdd}.json";
                }

                if (sfd.ShowDialog() == true && !string.IsNullOrEmpty(sfd.FileName))
                {
                    PlaylistManager.Instance.ExportPlaylists(sfd.FileName, selectedPlaylist);
                    string msg = string.Format(I18nService.Instance["LibraryExportSuccess"], sfd.FileName);
                    System.Windows.MessageBox.Show(msg, I18nService.Instance["LibraryTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(string.Format(I18nService.Instance["LibraryExportFailed"], ex.Message), I18nService.Instance["LibraryTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lbPlaylists.SelectedValue is string name)
            {
                PlaylistManager.Instance.DeletePlaylist(name);
                RefreshPlaylists();
            }
        }

        // --- EDIT MODE ---

        private void BtnEditMode_Click(object sender, RoutedEventArgs e)
        {
            if (lbPlaylists.SelectedValue is string name)
            {
                _editingPlaylistName = name;
                txtEditName.Text = name;
                _editFiles.Clear();
                if (PlaylistManager.Instance.SavedPlaylists.TryGetValue(name, out var files))
                {
                    foreach (var f in files) _editFiles.Add(f);
                }
                SwitchToView("Edit");
            }
        }

        private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("Selection");
        }

        private void BtnSaveEdit_Click(object sender, RoutedEventArgs e)
        {
            string newName = txtEditName.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            // Remove old
            if (_editingPlaylistName != newName)
            {
                PlaylistManager.Instance.DeletePlaylist(_editingPlaylistName);
            }

            // Save new
            PlaylistManager.Instance.SavedPlaylists[newName] = new List<string>(_editFiles);
            PlaylistManager.Instance.SaveLibrary();

            SwitchToView("Selection");
            RefreshPlaylists();
            lbPlaylists.SelectedValue = newName;
        }

        private void BtnRemoveVideo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string file)
            {
                _editFiles.Remove(file);
            }
        }

        private void BtnAddVideo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.rmvb;*.webm;*.ts|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                {
                    if (!_editFiles.Contains(f))
                        _editFiles.Add(f);
                }
            }
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择要添加的文件夹",
                Multiselect = true
            };
            if (folderDlg.ShowDialog() == true && folderDlg.FolderNames.Length > 0)
            {
                foreach (var folder in folderDlg.FolderNames)
                {
                    AddFolderToEditFiles(folder);
                }
            }
        }

        private System.Windows.Point _dragEditStartPoint;
        private string? _draggedEditFile;
        private bool _isDraggingEditFile = false;

        private void LbEditFiles_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FindVisualParent<System.Windows.Controls.Button>((DependencyObject)e.OriginalSource) != null) return;

            _dragEditStartPoint = e.GetPosition(null);
            var item = FindVisualParent<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null && item.DataContext is string filePath)
            {
                _draggedEditFile = filePath;
            }
            else
            {
                _draggedEditFile = null;
            }
        }

        private void LbEditFiles_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _draggedEditFile != null && !_isDraggingEditFile)
            {
                System.Windows.Point currentPos = e.GetPosition(null);
                System.Windows.Vector diff = _dragEditStartPoint - currentPos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDraggingEditFile = true;
                    try
                    {
                        var data = new System.Windows.DataObject("AnniEditFileItem", _draggedEditFile);
                        System.Windows.DragDrop.DoDragDrop(lbEditFiles, data, System.Windows.DragDropEffects.Move);
                    }
                    finally
                    {
                        _isDraggingEditFile = false;
                        _draggedEditFile = null;
                    }
                }
            }
        }

        private void LbEditFiles_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("AnniEditFileItem"))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void EditFiles_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("AnniEditFileItem"))
            {
                if (e.Data.GetData("AnniEditFileItem") is string dragged)
                {
                    int oldIndex = _editFiles.IndexOf(dragged);
                    if (oldIndex >= 0)
                    {
                        var targetItem = FindVisualParent<ListBoxItem>((DependencyObject)e.OriginalSource);
                        int newIndex;
                        if (targetItem != null && targetItem.DataContext is string target)
                        {
                            newIndex = _editFiles.IndexOf(target);
                            if (newIndex < 0) newIndex = _editFiles.Count - 1;
                        }
                        else
                        {
                            newIndex = _editFiles.Count - 1;
                        }

                        if (oldIndex != newIndex && newIndex >= 0 && newIndex < _editFiles.Count)
                        {
                            _editFiles.Move(oldIndex, newIndex);
                            lbEditFiles.SelectedIndex = newIndex;
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    foreach (var path in paths)
                    {
                        if (Directory.Exists(path))
                        {
                            AddFolderToEditFiles(path);
                        }
                        else if (File.Exists(path) && PlaylistManager.IsSupportedFile(path))
                        {
                            if (!_editFiles.Contains(path))
                                _editFiles.Add(path);
                        }
                    }
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void AddFolderToEditFiles(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;
            try
            {
                var videoFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                                          .Where(PlaylistManager.IsVideoFile)
                                          .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                          .ToList();

                if (videoFiles.Count > 0)
                {
                    foreach (var f in videoFiles)
                    {
                        if (!_editFiles.Contains(f))
                            _editFiles.Add(f);
                    }
                }
                else
                {
                    var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                                              .Where(PlaylistManager.IsImageFile)
                                              .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                              .ToList();
                    var audioFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                                              .Where(PlaylistManager.IsAudioFile)
                                              .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                              .ToList();

                    if (imageFiles.Count > 0)
                    {
                        foreach (var f in imageFiles)
                        {
                            if (!_editFiles.Contains(f))
                                _editFiles.Add(f);
                        }
                    }
                    else if (audioFiles.Count > 0)
                    {
                        foreach (var f in audioFiles)
                        {
                            if (!_editFiles.Contains(f))
                                _editFiles.Add(f);
                        }
                    }
                }
            }
            catch { }
        }

        // --- MERGE MODE ---

        private void BtnMergeMode_Click(object sender, RoutedEventArgs e)
        {
            txtMergeName.Text = "Merged_" + DateTime.Now.ToString("MMdd_HHmmss");
            _mergeItems.Clear();
            foreach (var key in PlaylistManager.Instance.SavedPlaylists.Keys.OrderBy(k => k))
            {
                _mergeItems.Add(new MergeItem { Name = key, IsSelected = false });
            }
            SwitchToView("Merge");
        }

        private void BtnCancelMerge_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("Selection");
        }

        private void BtnConfirmMerge_Click(object sender, RoutedEventArgs e)
        {
            string newName = txtMergeName.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            var selectedPlaylists = _mergeItems.Where(x => x.IsSelected).Select(x => x.Name).ToList();
            if (selectedPlaylists.Count == 0) return;

            HashSet<string> combinedFiles = new HashSet<string>();
            foreach (var pName in selectedPlaylists)
            {
                if (PlaylistManager.Instance.SavedPlaylists.TryGetValue(pName, out var files))
                {
                    foreach (var f in files) combinedFiles.Add(f);
                }
            }

            if (combinedFiles.Count > 0)
            {
                PlaylistManager.Instance.SavedPlaylists[newName] = combinedFiles.ToList();
                PlaylistManager.Instance.SaveLibrary();
                SwitchToView("Selection");
                RefreshPlaylists();
                lbPlaylists.SelectedValue = newName;
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            bool isEn = Services.I18nService.Instance.CurrentLanguage == "en-US";
            string message = isEn 
                ? "Are you sure you want to clear all saved playlists and library records? This action cannot be undone."
                : "是否确认清空所有播放列表和媒体库记录？该操作无法撤销。";
            string title = isEn ? "Confirm Clear All" : "确认清空";

            var result = System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                PlaylistManager.Instance.ClearAllLibrary();
                RefreshPlaylists();
                lbFiles.Items.Clear();
                txtSelectedPlaylist.Text = Services.I18nService.Instance["LibraryNoSelection"];
            }
        }
    }
}

