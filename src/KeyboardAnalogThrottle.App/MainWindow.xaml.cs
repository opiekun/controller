using System.Windows;
using KeyboardAnalogThrottle.App.ViewModels;

namespace KeyboardAnalogThrottle.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
