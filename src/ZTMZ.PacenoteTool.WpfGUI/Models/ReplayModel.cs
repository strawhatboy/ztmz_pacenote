using System.Globalization;

namespace ZTMZ.PacenoteTool.WpfGUI.Models
{
    public partial class ReplayModel : ObservableObject
    {
        [ObservableProperty]
        private int _id;

        [ObservableProperty]
        private string _track = string.Empty;

        [ObservableProperty]
        private string _car = string.Empty;

        [ObservableProperty]
        private string _car_class = string.Empty;

        [ObservableProperty]
        private float _finish_time;

        [ObservableProperty]
        private DateTime _date;

        [ObservableProperty]
        private string _comment = string.Empty;

        [ObservableProperty]
        private string _video_path = string.Empty;

        [ObservableProperty]
        private bool _locked;

        [ObservableProperty]
        private bool _available = true;

        [ObservableProperty]
        private bool _hasVideo;

        public string FinishTimeDisplay
        {
            get
            {
                var duration = TimeSpan.FromSeconds(Math.Max(0, Finish_time));
                return duration.TotalHours >= 1
                    ? duration.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
                    : duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
            }
        }

        public string RecordedDisplay => Date == DateTime.MinValue
            ? string.Empty
            : Date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        public string SearchText => $"{Track} {Car} {Car_class} {Comment}";

        partial void OnFinish_timeChanged(float value)
        {
            OnPropertyChanged(nameof(FinishTimeDisplay));
        }

        partial void OnDateChanged(DateTime value)
        {
            OnPropertyChanged(nameof(RecordedDisplay));
        }

        partial void OnTrackChanged(string value)
        {
            OnPropertyChanged(nameof(SearchText));
        }

        partial void OnCarChanged(string value)
        {
            OnPropertyChanged(nameof(SearchText));
        }

        partial void OnCar_classChanged(string value)
        {
            OnPropertyChanged(nameof(SearchText));
        }

        partial void OnCommentChanged(string value)
        {
            OnPropertyChanged(nameof(SearchText));
        }
    }
}
