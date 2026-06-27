using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading.Tasks;

namespace PrintTrackerApp
{
    public partial class App : System.Windows.Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var splash = new SplashScreenWindow();
            this.MainWindow = splash;
            splash.Show();

            // Simulate loading for the splash screen
            await splash.SimulateLoading();

            var main = new MainWindow();
            this.MainWindow = main;
            main.Show();
            splash.Close();
        }
    }
}
