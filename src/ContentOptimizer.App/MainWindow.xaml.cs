using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ContentOptimizer.App.Services;
using ContentOptimizer.App.ViewModels;

namespace ContentOptimizer.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;

        public MainWindow()
        {
            InitializeComponent();
            viewModel = new MainViewModel();
            DataContext = viewModel;
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            Closing += (_, _) => viewModel.SaveSettings();
            viewModel.Log.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (LogList.Items.Count > 0)
                    {
                        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                    }
                }),
                DispatcherPriority.Background);
        }

        public void ScrollLeftToEnd() => LeftPanel.ScrollToEnd();

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            bool files = e.Data.GetDataPresent(DataFormats.FileDrop) && viewModel.IsNotApplying;
            e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
            DropZone.Tag = files ? "drag" : null;
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.Tag = null;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.Tag = null;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && viewModel.IsNotApplying)
            {
                _ = viewModel.SetSourceAsync(paths.First());
            }

            e.Handled = true;
        }
    }
}
