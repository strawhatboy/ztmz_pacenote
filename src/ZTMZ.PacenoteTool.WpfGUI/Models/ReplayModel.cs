namespace ZTMZ.PacenoteTool.WpfGUI.Models
{
    public partial class ReplayModel : ObservableObject
    {
        [ObservableProperty]
        private int _id;

        [ObservableProperty]
        private string _track;

        [ObservableProperty]
        private string _car;

        [ObservableProperty]
        private string _car_class;

        [ObservableProperty]
        private float _finish_time;

        [ObservableProperty]
        private DateTime _date;

        [ObservableProperty]
        private string _comment;

        [ObservableProperty]
        private string _video_path;

        [ObservableProperty]
        private bool _locked;
    }
}

