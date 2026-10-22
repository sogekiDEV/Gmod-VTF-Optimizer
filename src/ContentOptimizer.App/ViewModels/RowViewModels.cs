using System;
using System.Windows.Media;
using ContentOptimizer.App.Services;
using ContentOptimizer.Core.Optimization;

namespace ContentOptimizer.App.ViewModels
{
    public static class Palette
    {
        public static readonly Brush Success = Make(0x4A, 0xDE, 0x80);
        public static readonly Brush Warn = Make(0xFB, 0xBF, 0x24);
        public static readonly Brush Danger = Make(0xF8, 0x71, 0x71);
        public static readonly Brush Muted = Make(0x8E, 0x97, 0xAB);
        public static readonly Brush Text = Make(0xE8, 0xEB, 0xF2);
        public static readonly Brush Dim = Make(0x4A, 0x52, 0x66);

        private static Brush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public static class Reasons
    {
        public static string Translate(string? note)
        {
            if (string.IsNullOrEmpty(note))
            {
                return string.Empty;
            }

            if (note.StartsWith("quality guard", StringComparison.Ordinal))
            {
                return Loc.T("reason_guard") + note.Substring("quality guard".Length);
            }

            return note switch
            {
                "already optimal" => Loc.T("reason_optimal"),
                "NOLOD flag" => Loc.T("reason_nolod"),
                "excluded by pattern" => Loc.T("reason_excluded"),
                "mip chain too short to reduce" => Loc.T("reason_mips"),
                "no size gain" => Loc.T("reason_nogain"),
                "render target or procedural" => Loc.T("reason_rt"),
                _ => note,
            };
        }
    }

    public sealed class FileRowViewModel
    {
        public FileRowViewModel(FileResult f, bool applied)
        {
            Status = f.Status;
            Saved = f.Saved;
            Name = System.IO.Path.GetFileName(f.RelativePath);
            Directory = System.IO.Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/') ?? string.Empty;
            string dimsBefore = $"{f.WidthBefore}×{f.HeightBefore}";
            string dimsAfter = $"{f.WidthAfter}×{f.HeightAfter}";
            string fmtBefore = (f.FormatBefore?.ToString() ?? string.Empty).ToUpperInvariant();
            string fmtAfter = (f.FormatAfter?.ToString() ?? string.Empty).ToUpperInvariant();

            switch (f.Status)
            {
                case FileStatus.Optimized:
                    StatusBrush = Palette.Success;
                    ChangeTop = Loc.Ltr(dimsBefore == dimsAfter ? dimsBefore : $"{dimsBefore} → {dimsAfter}");
                    ChangeBottom = Loc.Ltr(fmtBefore == fmtAfter ? fmtBefore : $"{fmtBefore} → {fmtAfter}");
                    ChangeBottomBrush = Palette.Muted;
                    SizeTop = Loc.Ltr("−" + ByteFormat.Plain(f.Saved));
                    SizeTopBrush = Palette.Success;
                    string sizes = $"{ByteFormat.Plain(f.BeforeBytes)} → {ByteFormat.Plain(f.AfterBytes)}";
                    if (applied)
                    {
                        sizes += double.IsPositiveInfinity(f.Psnr)
                            ? " · " + Loc.T("row_lossless")
                            : " · " + f.Psnr.ToString("0.0", Loc.Instance.Culture) + " dB";
                    }

                    SizeBottom = Loc.Ltr(sizes);
                    break;
                case FileStatus.Failed:
                    StatusBrush = Palette.Danger;
                    ChangeTop = Loc.T("row_failed");
                    ChangeBottom = Reasons.Translate(f.Note);
                    ChangeBottomBrush = Palette.Danger;
                    SizeTop = ByteFormat.Format(f.BeforeBytes);
                    SizeTopBrush = Palette.Muted;
                    SizeBottom = string.Empty;
                    break;
                default:
                    StatusBrush = Palette.Dim;
                    ChangeTop = dimsBefore;
                    ChangeBottom = Reasons.Translate(f.Note);
                    ChangeBottomBrush = f.Note != null && f.Note.StartsWith("quality guard", StringComparison.Ordinal) ? Palette.Warn : Palette.Muted;
                    SizeTop = ByteFormat.Format(f.BeforeBytes);
                    SizeTopBrush = Palette.Muted;
                    SizeBottom = fmtBefore;
                    break;
            }
        }

        public FileStatus Status { get; }
        public long Saved { get; }
        public string Name { get; }
        public string Directory { get; }
        public Brush StatusBrush { get; }
        public string ChangeTop { get; }
        public string ChangeBottom { get; }
        public Brush ChangeBottomBrush { get; }
        public string SizeTop { get; }
        public Brush SizeTopBrush { get; }
        public string SizeBottom { get; }
    }

    public sealed class PresetCardViewModel : ObservableObject
    {
        private readonly Action<int> select;
        private readonly string nameKey;
        private bool isSelected;
        private double? percent;
        private long afterBytes;

        public PresetCardViewModel(int cap, string nameKey, Action<int> select)
        {
            Cap = cap;
            this.nameKey = nameKey;
            this.select = select;
        }

        public int Cap { get; }
        public string Name => Loc.T(nameKey);
        public string Description => Cap == 0 ? Loc.T("preset_format_d") : Loc.T("preset_upto", Cap);

        public string Prediction => percent == null
            ? "…"
            : Loc.Ltr(Loc.T("preset_prediction", Math.Round(percent.Value), ByteFormat.Plain(afterBytes)));

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (Set(ref isSelected, value) && value)
                {
                    select(Cap);
                }
            }
        }

        public void SetPrediction(double? percentSaved, long after)
        {
            percent = percentSaved;
            afterBytes = after;
            OnPropertyChanged(nameof(Prediction));
        }

        public void SyncSelected(bool value)
        {
            Set(ref isSelected, value, nameof(IsSelected));
        }

        public void RefreshLanguage()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(Prediction));
        }
    }

    public sealed class ChipViewModel
    {
        public ChipViewModel(string text, string detail)
        {
            Text = text;
            Detail = detail;
        }

        public string Text { get; }
        public string Detail { get; }
    }

    public sealed class LanguageItemViewModel : ObservableObject
    {
        private bool isSelected;

        public LanguageItemViewModel(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }

        public string Code { get; }
        public string NativeName { get; }

        public bool IsSelected
        {
            get => isSelected;
            set => Set(ref isSelected, value);
        }
    }

    public enum LogLevel
    {
        Info,
        Success,
        Warn,
        Error,
    }

    public sealed class LogEntry : ObservableObject
    {
        private readonly Func<string> producer;

        public LogEntry(Func<string> producer, LogLevel level)
        {
            this.producer = producer;
            Time = DateTime.Now.ToString("HH:mm:ss");
            Brush = level switch
            {
                LogLevel.Success => Palette.Success,
                LogLevel.Warn => Palette.Warn,
                LogLevel.Error => Palette.Danger,
                _ => Palette.Text,
            };
        }

        public string Time { get; }
        public string Message => producer();
        public Brush Brush { get; }

        public void RefreshLanguage() => OnPropertyChanged(nameof(Message));
    }
}
