using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace PrintTrackerApp
{
    public partial class ShutdownAlertWindow : Window
    {
        private DispatcherTimer _timer;
        private int _countdown = 30;
        private bool _isPaused = false;
        private bool _isCancelled = false;

        public ShutdownAlertWindow(string message)
        {
            InitializeComponent();
            txtMessage.Text = message;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            _countdown--;
            
            if (_countdown > 0)
            {
                txtCountdown.Text = $"Shutting down in {_countdown} seconds...";
            }
            else
            {
                _timer.Stop();
                txtCountdown.Text = "Shutting down now...";
                ExecuteShutdown();
                this.Close();
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            if (_isPaused)
            {
                btnPause.Content = "Resume (បន្ត)";
                btnPause.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")); // Blue
                txtCountdown.Text = $"Paused at {_countdown} seconds.";
            }
            else
            {
                btnPause.Content = "Pause (ផ្អាក)";
                btnPause.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber
                txtCountdown.Text = $"Shutting down in {_countdown} seconds...";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _isCancelled = true;
            _timer.Stop();
            
            // Just in case a shutdown was already queued by the system, try to abort it
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "-a") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }

            this.Close();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void ExecuteShutdown()
        {
            if (_isCancelled) return;
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "-s -f -t 0") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to trigger shutdown: {ex.Message}");
            }
        }

        // Expose a method to externally close/abort it
        public void AbortExternally()
        {
            _isCancelled = true;
            _timer.Stop();
            this.Dispatcher.Invoke(() => this.Close());
        }
    }
}
