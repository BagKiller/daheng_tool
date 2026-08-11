using System.Windows;

namespace VegaBeamTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            StartMainWindow();
        }
        private void StartMainWindow()
        {
            var window = new MainWindow();
            var windowViewModel = new MainWindowViewModel();
            window.DataContext = windowViewModel;
            window.Show();
        }
    }

}
