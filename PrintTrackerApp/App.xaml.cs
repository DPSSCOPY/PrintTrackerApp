using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading.Tasks;

namespace PrintTrackerApp
{
    public partial class App : System.Windows.Application
    {
        private static System.Threading.Mutex _mutex;
        private bool _isNewInstance;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new System.Threading.Mutex(true, "PrintTrackerApp_SingleInstance_Mutex", out _isNewInstance);

            if (!_isNewInstance)
            {
                // App is already running. Send a message to the first instance to show its window.
                try
                {
                    using (var client = new System.IO.Pipes.NamedPipeClientStream(".", "PrintTrackerAppPipe", System.IO.Pipes.PipeDirection.Out))
                    {
                        client.Connect(1000);
                    }
                }
                catch { } // Ignore if connection fails

                Environment.Exit(0);
                return;
            }

            // Start listening for messages from other instances
            StartPipeServer();

            bool startBackground = e.Args != null && e.Args.Length > 0 && e.Args[0].ToLower() == "-background";

            if (startBackground)
            {
                // Start silently in the background
                var main = new MainWindow();
                this.MainWindow = main;
                // DO NOT call main.Show() here so it stays hidden
            }
            else
            {
                // Start normally with Splash Screen
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

        private void StartPipeServer()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new System.IO.Pipes.NamedPipeServerStream("PrintTrackerAppPipe", System.IO.Pipes.PipeDirection.In))
                        {
                            await server.WaitForConnectionAsync();
                            
                            // Ask the UI thread to show the window
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (System.Windows.Application.Current.MainWindow != null)
                                {
                                    System.Windows.Application.Current.MainWindow.Show();
                                    if (System.Windows.Application.Current.MainWindow.WindowState == System.Windows.WindowState.Minimized)
                                        System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Normal;
                                    System.Windows.Application.Current.MainWindow.Activate();
                                }
                            });
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Pipe Server Error: {ex.Message}");
                    }
                }
            });
        }
    }
}
