using System.ComponentModel;
using System.Runtime.CompilerServices;
using WikiViewer.Core.Models;

namespace _1507_UWP.ViewModels
{
    public class WikiNavItemViewModel : INotifyPropertyChanged
    {
        public WikiInstance Wiki { get; }

        public string IconUrl =>
            !string.IsNullOrEmpty(Wiki.IconUrl) ? Wiki.IconUrl : "ms-appx:///Assets/Square150x150Logo.png";

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public WikiNavItemViewModel(WikiInstance wiki)
        {
            Wiki = wiki;
            IsExpanded = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}