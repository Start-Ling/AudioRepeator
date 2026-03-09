using System.Windows.Input;

namespace AudioRepeator
{
    public class MainWindowViewModel : NotifyPropertyChangedBase
    {
        public ICommand ToggleStartCommand { get; set; }

        public bool IsStarted { get; set; }

        public MainWindowViewModel()
        {
            ToggleStartCommand = new RelayCommand(() =>
            {
                IsStarted = !IsStarted;
            });
        }

    }
}
