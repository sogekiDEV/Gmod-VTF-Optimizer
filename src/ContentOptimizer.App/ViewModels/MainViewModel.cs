using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ContentOptimizer.App.Services;
using ContentOptimizer.Core.Optimization;

namespace ContentOptimizer.App.ViewModels
{
    public enum OutputMode
    {
        Mirror,
        InPlace,
    }

    public enum ResultFilter
    {
        All,
        Optimized,
        Skipped,
        Failed,
    }

    public enum ResultTab
    {
        Files,
        Log,
    }

    public sealed class MainViewModel : ObservableObject
    {
        private enum StatusKind
        {
            Drop,
            Analyzing,
            Ready,
            Nothing,
            Done,
            Optimizing,
            Copying,
            Canceling,
            Canceled,
            Error,
            Failed,
        }

        private const int MaxLogEntries = 1500;

        private readonly DispatcherTimer debounce;
        private CancellationTokenSource? analysisCts;
        private CancellationTokenSource? runCts;
        private Task analysisTask = Task.CompletedTask;
        private RunReport? report;
        private bool loading = true;
        private bool outputEdited;
        private bool backupEdited;
        private string lastOutputPath = string.Empty;

        private string sourcePath = string.Empty;
        private int selectedCap = 1024;
        private int triggerSide;
        private int minConvertSide = 64;
        private double qualityFloorDb = 27;
        private bool convertFormats = true;
        private bool detectOpaque = true;
        private bool convertNormalMaps = true;
        private bool respectNoLod = true;
        private string excludesText = string.Join(Environment.NewLine, OptimizeOptions.DefaultExcludes);
        private bool showAdvanced;
        private OutputMode mode = OutputMode.Mirror;
        private string outputDir = string.Empty;
        private string backupDir = string.Empty;
        private bool copyEverything = true;
        private bool showLanguageMenu;

        private bool isAnalyzing;
        private bool isApplying;
        private double progressValue;
        private StatusKind statusKind = StatusKind.Drop;
        private object[] statusArgs = Array.Empty<object>();
        private bool hasReport;
        private bool reportApplied;
        private long beforeBytes;
        private long afterBytes;
        private int optimizedCount;
        private int skippedCount;
        private int failedCount;
        private int totalCount;
        private IReadOnlyList<FileRowViewModel> rows = Array.Empty<FileRowViewModel>();
        private IReadOnlyList<ChipViewModel> chips = Array.Empty<ChipViewModel>();
        private ResultFilter filter = ResultFilter.All;
        private ResultTab tab = ResultTab.Files;
        private List<FileRowViewModel> allRows = new List<FileRowViewModel>();

        public MainViewModel()
        {
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            debounce.Tick += (_, _) =>
            {
                debounce.Stop();
                analysisTask = AnalyzeAsync();
            };

            Presets = new[]
            {
                new PresetCardViewModel(0, "preset_format", cap => SelectedCap = cap),
                new PresetCardViewModel(1024, "preset_soft", cap => SelectedCap = cap),
                new PresetCardViewModel(512, "preset_balanced", cap => SelectedCap = cap),
                new PresetCardViewModel(256, "preset_strong", cap => SelectedCap = cap),
            };

            Languages = Loc.Instance.Languages
                .Select(l => new LanguageItemViewModel(l.Code, l.NativeName))
                .ToList();

            Log = new ObservableCollection<LogEntry>();
            PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();

            BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
            BrowseTargetCommand = new RelayCommand(BrowseTarget, () => !IsApplying);
            OptimizeCommand = new RelayCommand(() => _ = OptimizeAsync(), CanOptimize);
            CancelCommand = new RelayCommand(CancelRun, () => IsApplying);
            OpenOutputCommand = new RelayCommand(OpenOutput, () => CanOpenOutput);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => report != null && !IsApplying);
            ClearLogCommand = new RelayCommand(() => Log.Clear());
            ToggleLanguageMenuCommand = new RelayCommand(() => ShowLanguageMenu = !ShowLanguageMenu);
            SelectLanguageCommand = new RelayCommand(p =>
            {
                Loc.Instance.SetLanguage(p as string);
                ShowLanguageMenu = false;
            });

            LoadSettings();
            SyncPresets();
            SyncLanguageItems();
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            loading = false;
            AddLog(() => Loc.T("log_ready"), LogLevel.Info);
        }

        public PresetCardViewModel[] Presets { get; }
        public IReadOnlyList<LanguageItemViewModel> Languages { get; }
        public ObservableCollection<LogEntry> Log { get; }

        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseTargetCommand { get; }
        public ICommand OptimizeCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenOutputCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ToggleLanguageMenuCommand { get; }
        public ICommand SelectLanguageCommand { get; }

        public string CurrentLanguageName => Loc.Instance.CurrentNativeName;

        public bool ShowLanguageMenu
        {
            get => showLanguageMenu;
            set => Set(ref showLanguageMenu, value);
        }

        public string SourcePath
        {
            get => sourcePath;
            private set
            {
                if (Set(ref sourcePath, value))
                {
                    OnPropertyChanged(nameof(HasSource));
                    OnPropertyChanged(nameof(SourceName));
                    OnPropertyChanged(nameof(TargetHint));
                }
            }
        }

        public bool HasSource => !string.IsNullOrEmpty(sourcePath);
        public string SourceName => HasSource ? (Path.GetFileName(sourcePath.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : sourcePath) : string.Empty;

        public int SelectedCap
        {
            get => selectedCap;
            set
            {
                if (Set(ref selectedCap, Math.Max(0, value)))
                {
                    SyncPresets();
                    OptionChanged();
                }
            }
        }

        public int TriggerSide
        {
            get => triggerSide;
            set
            {
                if (Set(ref triggerSide, Math.Max(0, value)))
                {
                    OptionChanged();
                }
            }
        }

        public int MinConvertSide
        {
            get => minConvertSide;
            set
            {
                if (Set(ref minConvertSide, Math.Max(0, value)))
                {
                    OptionChanged();
                }
            }
        }

        public double QualityFloorDb
        {
            get => qualityFloorDb;
            set
            {
                if (Set(ref qualityFloorDb, Math.Max(0, value)))
                {
                    OptionChanged();
                }
            }
        }

        public bool ConvertFormats
        {
            get => convertFormats;
            set
            {
                if (Set(ref convertFormats, value))
                {
                    OptionChanged();
                }
            }
        }

        public bool DetectOpaque
        {
            get => detectOpaque;
            set
            {
                if (Set(ref detectOpaque, value))
                {
                    OptionChanged();
                }
            }
        }

        public bool ConvertNormalMaps
        {
            get => convertNormalMaps;
            set
            {
                if (Set(ref convertNormalMaps, value))
                {
                    OptionChanged();
                }
            }
        }

        public bool RespectNoLod
        {
            get => respectNoLod;
            set
            {
                if (Set(ref respectNoLod, value))
                {
                    OptionChanged();
                }
            }
        }

        public string ExcludesText
        {
            get => excludesText;
            set
            {
                if (Set(ref excludesText, value ?? string.Empty))
                {
                    OptionChanged();
                }
            }
        }

        public bool ShowAdvanced
        {
            get => showAdvanced;
            set => Set(ref showAdvanced, value);
        }

        public OutputMode Mode
        {
            get => mode;
            set
            {
                if (Set(ref mode, value))
                {
                    OnPropertyChanged(nameof(IsMirror));
                    OnPropertyChanged(nameof(TargetPath));
                    OnPropertyChanged(nameof(TargetLabel));
                    OnPropertyChanged(nameof(TargetHint));
                    OnPropertyChanged(nameof(PrimaryText));
                    if (!loading)
                    {
                        ReportApplied = false;
                    }
                }
            }
        }

        public bool IsMirror => mode == OutputMode.Mirror;

        public string TargetLabel => Loc.T(IsMirror ? "out_target_mirror" : "out_target_backup");

        public string TargetPath
        {
            get => IsMirror ? outputDir : backupDir;
            set
            {
                value ??= string.Empty;
                if (IsMirror)
                {
                    if (Set(ref outputDir, value, nameof(TargetPath)))
                    {
                        outputEdited = true;
                    }
                }
                else if (Set(ref backupDir, value, nameof(TargetPath)))
                {
                    backupEdited = true;
                }

                OnPropertyChanged(nameof(TargetHint));
            }
        }

        public string TargetHint
        {
            get
            {
                if (!HasSource)
                {
                    return string.Empty;
                }

                string target = TargetPath.Trim();
                if (target.Length == 0)
                {
                    return Loc.T("hint_choose");
                }

                if (IsMirror && SamePath(target, sourcePath))
                {
                    return Loc.T("hint_same");
                }

                return string.Empty;
            }
        }

        public bool CopyEverything
        {
            get => copyEverything;
            set => Set(ref copyEverything, value);
        }

        public bool IsAnalyzing
        {
            get => isAnalyzing;
            private set
            {
                if (Set(ref isAnalyzing, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(ShowProgress));
                }
            }
        }

        public bool IsApplying
        {
            get => isApplying;
            private set
            {
                if (Set(ref isApplying, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(IsNotApplying));
                    OnPropertyChanged(nameof(ShowProgress));
                    OnPropertyChanged(nameof(CanOpenOutput));
                }
            }
        }

        public bool IsNotApplying => !isApplying;
        public bool IsBusy => isAnalyzing || isApplying;
        public bool ShowProgress => isAnalyzing || isApplying;

        public double ProgressValue
        {
            get => progressValue;
            private set => Set(ref progressValue, value);
        }

        public string StatusText => statusKind switch
        {
            StatusKind.Analyzing => Loc.T("status_analyzing"),
            StatusKind.Ready => Loc.T("status_ready", optimizedCount, SavedText),
            StatusKind.Nothing => Loc.T("status_nothing"),
            StatusKind.Done => Loc.T("status_done", SavedText, PercentPositive),
            StatusKind.Optimizing => Loc.T("status_optimizing", statusArgs[0], statusArgs[1], statusArgs[2]),
            StatusKind.Copying => Loc.T("status_copying"),
            StatusKind.Canceling => Loc.T("status_canceling"),
            StatusKind.Canceled => Loc.T("status_canceled"),
            StatusKind.Error => Loc.T("status_error", statusArgs[0]),
            StatusKind.Failed => Loc.T("status_failed"),
            _ => Loc.T("status_drop"),
        };

        public bool HasReport
        {
            get => hasReport;
            private set
            {
                if (Set(ref hasReport, value))
                {
                    OnPropertyChanged(nameof(FilesTabLabel));
                }
            }
        }

        public bool ReportApplied
        {
            get => reportApplied;
            private set
            {
                if (Set(ref reportApplied, value))
                {
                    OnPropertyChanged(nameof(CountLabel));
                    OnPropertyChanged(nameof(SummaryTitle));
                    OnPropertyChanged(nameof(CanOpenOutput));
                }
            }
        }

        public string SourceSummary => hasReport ? Loc.T("source_summary", totalCount, ByteFormat.Format(beforeBytes)) : string.Empty;
        public string SummaryTitle => Loc.T(reportApplied ? "sum_real" : "sum_est");
        public string CountLabel => Loc.T(reportApplied ? "sum_opt" : "sum_toopt");
        public string BeforeText => hasReport ? ByteFormat.Format(beforeBytes) : "—";
        public string AfterText => hasReport ? ByteFormat.Format(afterBytes) : "—";
        public string SavedText => hasReport ? ByteFormat.Format(beforeBytes - afterBytes) : "—";
        public string BarRightText => Loc.T("sum_bar_right", BeforeText);

        private double SavedPercent => beforeBytes == 0 ? 0 : 100.0 * (beforeBytes - afterBytes) / beforeBytes;
        public string SavedPercentText => hasReport ? Loc.Ltr(Loc.T("pct_neg", SavedPercent.ToString("0.0", Loc.Instance.Culture))) : string.Empty;
        private string PercentPositive => Loc.Ltr(Loc.T("pct_pos", SavedPercent.ToString("0.0", Loc.Instance.Culture)));
        public double AfterFraction => beforeBytes == 0 ? 1 : (double)afterBytes / beforeBytes;

        public int OptimizedCount => optimizedCount;
        public int SkippedCount => skippedCount;
        public int FailedCount => failedCount;
        public int TotalCount => totalCount;

        public string FilesTabLabel => hasReport ? Loc.T("tab_files_n", totalCount) : Loc.T("tab_files");

        public IReadOnlyList<FileRowViewModel> Rows
        {
            get => rows;
            private set => Set(ref rows, value);
        }

        public IReadOnlyList<ChipViewModel> Chips
        {
            get => chips;
            private set => Set(ref chips, value);
        }

        public ResultFilter Filter
        {
            get => filter;
            set
            {
                if (Set(ref filter, value))
                {
                    RebuildRows();
                }
            }
        }

        public ResultTab Tab
        {
            get => tab;
            set
            {
                if (Set(ref tab, value))
                {
                    OnPropertyChanged(nameof(IsFilesTab));
                    OnPropertyChanged(nameof(IsLogTab));
                }
            }
        }

        public bool IsFilesTab => tab == ResultTab.Files;
        public bool IsLogTab => tab == ResultTab.Log;

        public bool CanOpenOutput => !isApplying && reportApplied && Directory.Exists(lastOutputPath);

        public string PrimaryText => Loc.T(IsMirror ? "btn_optimize" : "btn_inplace");

        public Task WhenIdleAsync() => analysisTask;

        public Task SetSourceAsync(string path)
        {
            string full = Path.GetFullPath(path.Trim().Trim('"'));
            if (!Directory.Exists(full) && !(File.Exists(full) && full.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)))
            {
                AddLog(() => Loc.T("log_invalid", path), LogLevel.Warn);
                return Task.CompletedTask;
            }

            SourcePath = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            ApplyDefaultTargets();
            ClearReport();
            debounce.Stop();
            analysisTask = AnalyzeAsync();
            return analysisTask;
        }

        public void SaveSettings()
        {
            SettingsStore.Save(new UserSettings
            {
                Cap = selectedCap,
                TriggerSide = triggerSide,
                MinConvertSide = minConvertSide,
                QualityFloorDb = qualityFloorDb,
                ConvertFormats = convertFormats,
                DetectOpaque = detectOpaque,
                ConvertNormalMaps = convertNormalMaps,
                RespectNoLod = respectNoLod,
                InPlace = mode == OutputMode.InPlace,
                CopyEverything = copyEverything,
                Excludes = excludesText,
                Language = Loc.Instance.CurrentCode,
            });
        }

        private void LoadSettings()
        {
            var s = SettingsStore.Load();
            selectedCap = s.Cap;
            triggerSide = s.TriggerSide;
            minConvertSide = s.MinConvertSide;
            qualityFloorDb = s.QualityFloorDb;
            convertFormats = s.ConvertFormats;
            detectOpaque = s.DetectOpaque;
            convertNormalMaps = s.ConvertNormalMaps;
            respectNoLod = s.RespectNoLod;
            mode = s.InPlace ? OutputMode.InPlace : OutputMode.Mirror;
            copyEverything = s.CopyEverything;
            if (!string.IsNullOrWhiteSpace(s.Excludes))
            {
                excludesText = s.Excludes;
            }

            Loc.Instance.SetLanguage(string.IsNullOrWhiteSpace(s.Language) ? Loc.Detect() : s.Language);
        }

        private void OnLanguageChanged()
        {
            SyncLanguageItems();
            foreach (var card in Presets)
            {
                card.RefreshLanguage();
            }

            foreach (var entry in Log)
            {
                entry.RefreshLanguage();
            }

            if (report != null)
            {
                BuildRowsAndChips(report, reportApplied);
            }

            OnPropertyChanged(string.Empty);
        }

        private void SyncLanguageItems()
        {
            foreach (var item in Languages)
            {
                item.IsSelected = item.Code == Loc.Instance.CurrentCode;
            }
        }

        private void SyncPresets()
        {
            foreach (var card in Presets)
            {
                card.SyncSelected(card.Cap == selectedCap);
            }
        }

        private void OptionChanged()
        {
            if (loading || isApplying)
            {
                return;
            }

            if (reportApplied)
            {
                ReportApplied = false;
            }

            if (HasSource)
            {
                debounce.Stop();
                debounce.Start();
            }
        }

        private OptimizeOptions BuildOptions(int cap) => new OptimizeOptions
        {
            MaxSize = cap,
            TriggerSide = triggerSide,
            MinConvertSide = minConvertSide,
            QualityFloorDb = qualityFloorDb,
            ConvertFormats = convertFormats,
            DetectOpaque = detectOpaque,
            ConvertNormalMaps = convertNormalMaps,
            RespectNoLod = respectNoLod,
            Excludes = excludesText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray(),
        };

        private void ApplyDefaultTargets()
        {
            string trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(trimmed) ?? trimmed;
            string name = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrEmpty(name))
            {
                name = "content";
            }

            if (!outputEdited || string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(parent, name + "_optimized");
                outputEdited = false;
            }

            if (!backupEdited || string.IsNullOrEmpty(backupDir))
            {
                backupDir = Path.Combine(parent, name + "_backup_vtf");
                backupEdited = false;
            }

            OnPropertyChanged(nameof(TargetPath));
            OnPropertyChanged(nameof(TargetHint));
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void SetStatus(StatusKind kind, params object[] args)
        {
            statusKind = kind;
            statusArgs = args;
            OnPropertyChanged(nameof(StatusText));
        }

        private void BrowseSource()
        {
            string? picked = FolderPicker.Pick(sourcePath, Loc.T("dlg_pick_source"));
            if (picked != null)
            {
                _ = SetSourceAsync(picked);
            }
        }

        private void BrowseTarget()
        {
            string? picked = FolderPicker.Pick(TargetPath, Loc.T(IsMirror ? "dlg_pick_output" : "dlg_pick_backup"));
            if (picked != null)
            {
                TargetPath = picked;
            }
        }

        private void ClearReport()
        {
            report = null;
            allRows = new List<FileRowViewModel>();
            Rows = Array.Empty<FileRowViewModel>();
            Chips = Array.Empty<ChipViewModel>();
            HasReport = false;
            ReportApplied = false;
            totalCount = 0;
            beforeBytes = 0;
            afterBytes = 0;
            foreach (var card in Presets)
            {
                card.SetPrediction(null, 0);
            }

            OnPropertyChanged(string.Empty);
        }

        private void CancelAnalysis()
        {
            analysisCts?.Cancel();
            analysisCts = null;
            IsAnalyzing = false;
        }

        private async Task AnalyzeAsync()
        {
            CancelAnalysis();
            if (!HasSource || isApplying)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            analysisCts = cts;
            IsAnalyzing = true;
            ProgressValue = 0;
            SetStatus(StatusKind.Analyzing);
            string source = sourcePath;
            var options = BuildOptions(selectedCap);
            var progress = new Progress<RunProgress>(p =>
            {
                if (!cts.IsCancellationRequested && p.Total > 0)
                {
                    ProgressValue = 100.0 * p.Done / p.Total;
                }
            });

            try
            {
                var result = await Task.Run(() => OptimizerRunner.Run(source, options, new RunSettings(), progress, cts.Token), cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                ShowReport(result, false);
                LogAnalysis(result);
                await EstimatePresetsAsync(source, cts);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                AddLog(() => Loc.T("log_analysis_fail", message), LogLevel.Error);
                SetStatus(StatusKind.Failed);
            }
            finally
            {
                if (ReferenceEquals(analysisCts, cts))
                {
                    IsAnalyzing = false;
                    UpdateIdleStatus();
                }
            }
        }

        private async Task EstimatePresetsAsync(string source, CancellationTokenSource cts)
        {
            var variants = Presets.Select(p => BuildOptions(p.Cap)).ToList();
            var estimate = await Task.Run(() => OptimizerRunner.Estimate(source, variants, Environment.ProcessorCount, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            for (int i = 0; i < Presets.Length; i++)
            {
                long after = estimate.AfterBytes[i];
                double pct = estimate.BeforeBytes == 0 ? 0 : 100.0 * (estimate.BeforeBytes - after) / estimate.BeforeBytes;
                Presets[i].SetPrediction(pct, after);
            }
        }

        private void ShowReport(RunReport r, bool applied)
        {
            report = r;
            hasReport = true;
            reportApplied = applied;
            optimizedCount = r.Count(FileStatus.Optimized);
            skippedCount = r.Count(FileStatus.Skipped);
            failedCount = r.Count(FileStatus.Failed);
            totalCount = r.Files.Count;
            beforeBytes = r.TotalBefore;
            afterBytes = r.TotalAfter;
            BuildRowsAndChips(r, applied);
            OnPropertyChanged(string.Empty);
        }

        private void BuildRowsAndChips(RunReport r, bool applied)
        {
            var optimized = r.Files.Where(f => f.Status == FileStatus.Optimized).ToList();
            static bool ResChanged(FileResult f) => f.WidthBefore != f.WidthAfter || f.HeightBefore != f.HeightAfter;
            static bool FmtChanged(FileResult f) => f.FormatBefore != f.FormatAfter;
            var groups = new List<(string Key, List<FileResult> Items)>
            {
                ("chip_both", optimized.Where(f => ResChanged(f) && FmtChanged(f)).ToList()),
                ("chip_res", optimized.Where(f => ResChanged(f) && !FmtChanged(f)).ToList()),
                ("chip_fmt", optimized.Where(f => !ResChanged(f) && FmtChanged(f)).ToList()),
            };

            Chips = groups
                .Where(g => g.Items.Count > 0)
                .OrderByDescending(g => g.Items.Sum(f => f.Saved))
                .Select(g => new ChipViewModel(Loc.T(g.Key), Loc.T("chip_detail", g.Items.Count, Loc.Ltr("−" + ByteFormat.Plain(g.Items.Sum(f => f.Saved))))))
                .ToList();

            allRows = r.Files
                .OrderBy(f => f.Status == FileStatus.Optimized ? 0 : f.Status == FileStatus.Failed ? 1 : 2)
                .ThenByDescending(f => f.Saved)
                .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FileRowViewModel(f, applied))
                .ToList();
            RebuildRows();
        }

        private void RebuildRows()
        {
            Rows = filter switch
            {
                ResultFilter.Optimized => allRows.Where(r => r.Status == FileStatus.Optimized).ToList(),
                ResultFilter.Skipped => allRows.Where(r => r.Status == FileStatus.Skipped).ToList(),
                ResultFilter.Failed => allRows.Where(r => r.Status == FileStatus.Failed).ToList(),
                _ => allRows,
            };
        }

        private void LogAnalysis(RunReport r)
        {
            int total = r.Files.Count;
            int opt = optimizedCount;
            int skipped = skippedCount;
            int failed = failedCount;
            double seconds = r.Elapsed.TotalSeconds;
            AddLog(() => Loc.T("log_analysis", total, opt, skipped, failed, seconds), LogLevel.Info);

            var groups = r.Files
                .Where(f => f.Status == FileStatus.Skipped)
                .Select(f => f.Note != null && f.Note.StartsWith("quality guard", StringComparison.Ordinal) ? "quality guard" : f.Note ?? string.Empty)
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => (Note: g.Key, Count: g.Count()))
                .ToList();
            if (groups.Count > 0)
            {
                AddLog(() => Loc.T("log_skipped", string.Join(" · ", groups.Select(g => $"{g.Count.ToString("N0", Loc.Instance.Culture)} {Reasons.Translate(g.Note)}"))), LogLevel.Info);
            }

            foreach (var f in r.Files.Where(f => f.Status == FileStatus.Failed).Take(30))
            {
                string path = f.RelativePath;
                string note = f.Note ?? string.Empty;
                AddLog(() => Loc.T("log_unreadable", path, note), LogLevel.Error);
            }
        }

        private void UpdateIdleStatus()
        {
            if (!HasSource)
            {
                SetStatus(StatusKind.Drop);
            }
            else if (!hasReport)
            {
                SetStatus(StatusKind.Analyzing);
            }
            else if (reportApplied)
            {
                SetStatus(StatusKind.Done);
            }
            else if (optimizedCount == 0)
            {
                SetStatus(StatusKind.Nothing);
            }
            else
            {
                SetStatus(StatusKind.Ready);
            }
        }

        private bool CanOptimize() =>
            HasSource && hasReport && !IsBusy && !reportApplied && optimizedCount > 0 && string.IsNullOrEmpty(TargetHint);

        public async Task OptimizeAsync()
        {
            if (!CanOptimize())
            {
                return;
            }

            if (mode == OutputMode.InPlace)
            {
                var answer = MessageBox.Show(
                    Loc.T("dlg_inplace_text", sourcePath, backupDir),
                    Loc.T("dlg_inplace_title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            debounce.Stop();
            CancelAnalysis();
            var cts = new CancellationTokenSource();
            runCts = cts;
            IsApplying = true;
            ProgressValue = 0;
            string source = sourcePath;
            var options = BuildOptions(selectedCap);
            var settings = new RunSettings
            {
                Mode = mode == OutputMode.Mirror ? RunMode.Mirror : RunMode.InPlace,
                OutputDir = mode == OutputMode.Mirror ? outputDir : null,
                BackupDir = mode == OutputMode.InPlace ? backupDir : null,
                CopyUnchanged = mode == OutputMode.Mirror && copyEverything,
                Threads = Environment.ProcessorCount,
            };
            lastOutputPath = mode == OutputMode.Mirror ? outputDir : source;
            string target = mode == OutputMode.Mirror ? outputDir : backupDir;
            bool mirror = mode == OutputMode.Mirror;
            AddLog(() => Loc.T(mirror ? "log_run_mirror" : "log_run_inplace", target), LogLevel.Info);
            SetStatus(StatusKind.Analyzing);

            var progress = new Progress<RunProgress>(p =>
            {
                if (p.Total == 0)
                {
                    return;
                }

                ProgressValue = 100.0 * p.Done / p.Total;
                if (p.CurrentFile.StartsWith("copie", StringComparison.Ordinal))
                {
                    SetStatus(StatusKind.Copying);
                }
                else
                {
                    SetStatus(StatusKind.Optimizing, p.Done, p.Total, Path.GetFileName(p.CurrentFile));
                }
            });

            try
            {
                var result = await Task.Run(() => OptimizerRunner.Run(source, options, settings, progress, cts.Token));
                ShowReport(result, true);
                LogApplied(result);
            }
            catch (OperationCanceledException)
            {
                AddLog(() => Loc.T("log_canceled"), LogLevel.Warn);
                SetStatus(StatusKind.Canceled);
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                AddLog(() => Loc.T("log_error", message), LogLevel.Error);
                SetStatus(StatusKind.Error, message);
            }
            finally
            {
                runCts = null;
                IsApplying = false;
                if (hasReport && statusKind != StatusKind.Canceled && statusKind != StatusKind.Error)
                {
                    UpdateIdleStatus();
                }
            }
        }

        private void LogApplied(RunReport r)
        {
            int count = optimizedCount;
            double seconds = r.Elapsed.TotalSeconds;
            AddLog(() => Loc.T("log_done", count, SavedText, PercentPositive, seconds), LogLevel.Success);
            foreach (var f in r.Files.Where(f => f.Status == FileStatus.Skipped && f.Note != null && f.Note.StartsWith("quality guard", StringComparison.Ordinal)).Take(30))
            {
                string path = f.RelativePath;
                string detail = f.Note!.Substring("quality guard".Length).Trim();
                AddLog(() => Loc.T("log_guard", path, detail), LogLevel.Warn);
            }

            foreach (var f in r.Files.Where(f => f.Status == FileStatus.Failed).Take(30))
            {
                string path = f.RelativePath;
                string note = f.Note ?? string.Empty;
                AddLog(() => Loc.T("log_failed", path, note), LogLevel.Error);
            }
        }

        private void CancelRun()
        {
            runCts?.Cancel();
            SetStatus(StatusKind.Canceling);
        }

        private void OpenOutput()
        {
            if (Directory.Exists(lastOutputPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{lastOutputPath}\"") { UseShellExecute = true });
            }
        }

        private void ExportCsv()
        {
            if (report == null)
            {
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "vtf-report.csv",
            };
            if (dialog.ShowDialog() == true)
            {
                string file = dialog.FileName;
                ReportCsv.Write(file, report);
                AddLog(() => Loc.T("log_csv", file), LogLevel.Info);
            }
        }

        public void AddLog(Func<string> producer, LogLevel level)
        {
            Log.Add(new LogEntry(producer, level));
            while (Log.Count > MaxLogEntries)
            {
                Log.RemoveAt(0);
            }
        }
    }
}
