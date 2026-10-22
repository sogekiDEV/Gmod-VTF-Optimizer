using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using ContentOptimizer.App.Languages;

namespace ContentOptimizer.App.Services
{
    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string nativeName, string culture, bool rtl, IReadOnlyDictionary<string, string> strings)
        {
            Code = code;
            NativeName = nativeName;
            Culture = CultureInfo.GetCultureInfo(culture);
            IsRtl = rtl;
            Strings = strings;
        }

        public string Code { get; }
        public string NativeName { get; }
        public CultureInfo Culture { get; }
        public bool IsRtl { get; }
        public IReadOnlyDictionary<string, string> Strings { get; }
    }

    public sealed class Loc : INotifyPropertyChanged
    {
        private LanguageInfo current;

        private Loc()
        {
            Languages = new[]
            {
                new LanguageInfo("en", "English", "en-US", false, En.Strings),
                new LanguageInfo("zh", "中文", "zh-CN", false, Zh.Strings),
                new LanguageInfo("hi", "हिन्दी", "hi-IN", false, Hi.Strings),
                new LanguageInfo("es", "Español", "es-ES", false, Es.Strings),
                new LanguageInfo("ar", "العربية", "en-US", true, Ar.Strings),
                new LanguageInfo("fr", "Français", "fr-FR", false, Fr.Strings),
            };
            current = Languages[0];
        }

        public static Loc Instance { get; } = new Loc();

        public IReadOnlyList<LanguageInfo> Languages { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? LanguageChanged;

        public string this[string key] => Get(key);

        public string CurrentCode => current.Code;
        public string CurrentNativeName => current.NativeName;
        public CultureInfo Culture => current.Culture;
        public FlowDirection Flow => current.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public string Get(string key)
        {
            if (current.Strings.TryGetValue(key, out string? value))
            {
                return value;
            }

            return En.Strings.TryGetValue(key, out value) ? value : key;
        }

        public static string T(string key, params object[] args)
        {
            string text = Instance.Get(key);
            return args.Length == 0 ? text : string.Format(Instance.Culture, text, args);
        }

        public static string Ltr(string text) =>
            Instance.current.IsRtl ? "‎" + text + "‎" : text;

        public static string Detect()
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Instance.Languages.Any(l => l.Code == two) ? two : "en";
        }

        public void SetLanguage(string? code)
        {
            var next = Languages.FirstOrDefault(l => l.Code == code) ?? Languages[0];
            if (ReferenceEquals(next, current))
            {
                return;
            }

            current = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flow)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentNativeName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCode)));
            LanguageChanged?.Invoke();
        }
    }

    [MarkupExtensionReturnType(typeof(object))]
    public sealed class TExtension : MarkupExtension
    {
        public TExtension(string key)
        {
            Key = key;
        }

        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay,
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
