using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ContentOptimizer.App.ViewModels;

namespace ContentOptimizer.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var window = new MainWindow();
            int render = Array.IndexOf(e.Args, "--render");
            if (render >= 0 && render + 1 < e.Args.Length)
            {
                _ = RenderAsync(window, e.Args, e.Args[render + 1]);
                return;
            }

            window.Show();
        }

        private static string? Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private async Task RenderAsync(MainWindow window, string[] args, string output)
        {
            try
            {
                var vm = (MainViewModel)window.DataContext;
                string? lang = Arg(args, "--lang");
                if (lang != null)
                {
                    Services.Loc.Instance.SetLanguage(lang);
                }

                string? size = Arg(args, "--size");
                if (size != null)
                {
                    var parts = size.Split('x');
                    window.Width = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    window.Height = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }

                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -6000;
                window.Top = 0;
                window.ShowInTaskbar = false;
                window.Show();

                string? source = Arg(args, "--source");
                if (source != null)
                {
                    await vm.SetSourceAsync(source);
                    await vm.WhenIdleAsync();
                }

                string? apply = Arg(args, "--apply");
                if (apply != null)
                {
                    vm.Mode = OutputMode.Mirror;
                    vm.TargetPath = apply;
                    await vm.OptimizeAsync();
                }

                if (Arg(args, "--advanced") == "1")
                {
                    vm.ShowAdvanced = true;
                }

                if (Arg(args, "--menu") == "1")
                {
                    vm.ShowLanguageMenu = true;
                }

                if (Arg(args, "--tab") == "log")
                {
                    vm.Tab = ResultTab.Log;
                }

                string? cap = Arg(args, "--cap");
                if (cap != null)
                {
                    vm.SelectedCap = int.Parse(cap, CultureInfo.InvariantCulture);
                    await Task.Delay(700);
                    await vm.WhenIdleAsync();
                }

                if (Arg(args, "--scroll") == "end")
                {
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    window.ScrollLeftToEnd();
                }

                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await Task.Delay(300);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var element = (FrameworkElement)window.Content;
                element.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(output);
                encoder.Save(stream);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(output + ".error.txt", ex.ToString());
                Shutdown(1);
            }
        }
    }
}
