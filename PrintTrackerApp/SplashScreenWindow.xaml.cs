using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace PrintTrackerApp
{
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    VersionText.Text = $"VERSION {version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch
            {
                VersionText.Text = "VERSION Unknown";
            }
        }

        public async Task SimulateLoading()
        {
            // Simple animation for the progress bar width over 2 seconds
            double targetWidth = 490; // Approximate width of the bar container
            
            var animation = new DoubleAnimation
            {
                From = 0,
                To = targetWidth,
                Duration = TimeSpan.FromSeconds(2),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            ProgressIndicator.BeginAnimation(FrameworkElement.WidthProperty, animation);
            
            // Update text at intervals
            LoadingText.Text = "Loading configuration...";
            await Task.Delay(500);
            LoadingText.Text = "Initializing UI...";
            await Task.Delay(500);
            LoadingText.Text = "Starting services...";
            await Task.Delay(500);
            LoadingText.Text = "Ready!";
            await Task.Delay(500);
        }
    }
}
