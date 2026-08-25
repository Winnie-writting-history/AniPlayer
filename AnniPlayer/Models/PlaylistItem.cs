using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AnniPlayer.Models
{
    public class PlaylistItem : INotifyPropertyChanged
    {
        private string _filePath = "";
        private string _title = "";
        private bool _isPlaying = false;
        private DateTime _addedTime = DateTime.Now;

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public DateTime AddedTime
        {
            get => _addedTime;
            set { _addedTime = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        public PlaylistItem() { }

        public PlaylistItem(string path)
        {
            FilePath = path;
            Title = Path.GetFileName(path);
            AddedTime = DateTime.Now;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
