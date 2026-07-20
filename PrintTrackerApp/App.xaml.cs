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
            // Always ensure Working Directory is set to application folder on startup
            System.IO.Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // Clean up legacy "-background" parameter in Windows Registry auto-start
            EnsureAutoStartRegistryCleaned();

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

        private void EnsureAutoStartRegistryCleaned()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        var existingValue = key.GetValue("PrintTrackerApp") as string;
                        if (!string.IsNullOrEmpty(existingValue) && existingValue.Contains("-background"))
                        {
                            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName 
                                             ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrintTrackerApp.exe");
                            key.SetValue("PrintTrackerApp", $"\"{exePath}\"");
                        }
                    }
                }
            }
            catch { }
        }
    }
}

