using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FiestaLauncher
{
    public partial class App : Application
    {
        public App()
        {
            // Force software rendering early to avoid the RDP-specific WPF startup crash
            // seen on this host before the main window is even created.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            try
            {
                AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
            }
            catch
            {
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
    }
}
