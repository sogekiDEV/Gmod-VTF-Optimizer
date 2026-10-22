using System.IO;

namespace ContentOptimizer.App.Services
{
    public static class FolderPicker
    {
        public static string? Pick(string? initial, string title)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = title,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };

            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            {
                dialog.SelectedPath = initial;
            }

            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }
    }
}
