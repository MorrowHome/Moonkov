using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace Unity.MP_FPS
{
    // UI Toolkit adapter for Unity Localization. Locale selection, persistence and
    // string/font loading belong to the package; bindings only own their UI lifetime.
    public static class MoonkovLocalization
    {
        public const string Strings = "MoonkovUI", Fonts = "MoonkovFonts";
        private static readonly Dictionary<TextElement, Binding> s_Bindings = new Dictionary<TextElement, Binding>();
        private static LocalizedAsset<FontAsset> s_Font;
        private static FontAsset s_CurrentFont;
        private static bool s_Initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var binding in new List<Binding>(s_Bindings.Values)) binding.Dispose();
            s_Bindings.Clear();
            if (s_Font != null) s_Font.AssetChanged -= FontChanged;
            s_Font = null; s_CurrentFont = null; s_Initialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (s_Initialized || Application.isBatchMode) return;
            s_Initialized = true;
            s_Font = new LocalizedAsset<FontAsset> { TableReference = Fonts, TableEntryReference = "UI" };
            s_Font.AssetChanged += FontChanged;
        }

        private static void FontChanged(FontAsset font)
        {
            s_CurrentFont = font;
            foreach (var binding in s_Bindings.Values) ApplyFont(binding.Element);
        }

        private static void ApplyFont(TextElement element)
        {
            if (s_CurrentFont == null) return;
            element.style.unityFontDefinition = FontDefinition.FromSDFFont(s_CurrentFont);
            if (LocalizationSettings.SelectedLocale?.Identifier.Code == "zh-Hans") element.style.letterSpacing = 0;
            else element.style.letterSpacing = StyleKeyword.Null;
        }

        public static void Select(string code)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale != null)
            {
                LocalizationSettings.SelectedLocale = locale;
                PlayerPrefs.SetString("Moonkov.Locale", locale.Identifier.Code);
            }
        }

        public static string Text(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            var table = LocalizationSettings.StringDatabase.GetTable(Strings);
            return table?.GetEntry(source)?.LocalizedValue ?? source;
        }

        public static string Format(FormattableString source)
        {
            var args = source.GetArguments();
            for (int i = 0; i < args.Length; i++) if (args[i] is string text) args[i] = Text(text);
            return string.Format(CultureInfo.CurrentCulture, Text(source.Format), args);
        }

        public static void Set(TextElement element, string source, params object[] arguments)
        {
            if (element == null) return;
            Initialize();
            if (!s_Bindings.TryGetValue(element, out var binding))
            {
                binding = new Binding(element); s_Bindings.Add(element, binding);
            }
            binding.Set(source ?? "", arguments);
            ApplyFont(element);
        }

        public static void Bind(VisualElement root)
        {
            Initialize();
            root.Query<TextElement>().ForEach(element =>
            {
                ApplyFont(element);
                // Input values are player content. Localize field labels and buttons,
                // never usernames, passwords, chat or numeric editing buffers.
                if (element.ClassListContains("unity-text-element--inner-input-field")) return;
                if (!s_Bindings.TryGetValue(element, out var binding) || element.text != binding.Rendered)
                    Set(element, element.text);
            });
        }

        private sealed class Binding : IDisposable
        {
            public readonly TextElement Element;
            public string Rendered { get; private set; }
            private string m_Source;
            private object[] m_Arguments;
            private LocalizedString m_String;
            public Binding(TextElement element)
            {
                Element = element;
                element.RegisterCallback<DetachFromPanelEvent>(Detached);
                LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            }
            public void Set(string source, object[] arguments)
            {
                bool same = m_Source == source && m_Arguments != null && m_Arguments.Length == arguments.Length;
                if (same) for (int i = 0; i < arguments.Length; i++) if (!Equals(arguments[i], m_Arguments[i])) { same = false; break; }
                if (same) return;
                m_Source = source; m_Arguments = arguments;
                if (m_String != null) m_String.StringChanged -= Updated;
                m_String = null;
                var table = LocalizationSettings.StringDatabase.GetTable(Strings);
                if (table?.GetEntry(source) != null)
                {
                    m_String = new LocalizedString(Strings, source) { Arguments = TranslateArguments() };
                    m_String.StringChanged += Updated;
                }
                else Updated(arguments.Length == 0 ? source : string.Format(CultureInfo.CurrentCulture, source, arguments));
            }
            private object[] TranslateArguments()
            {
                var args = new object[m_Arguments.Length];
                for (int i = 0; i < args.Length; i++) args[i] = m_Arguments[i] is string text ? Text(text) : m_Arguments[i];
                return args;
            }
            private void LocaleChanged(Locale locale)
            {
                if (m_String != null) { m_String.Arguments = TranslateArguments(); m_String.RefreshString(); }
                ApplyFont(Element);
            }
            private void Updated(string value) { Rendered = value; Element.text = value; }
            private void Detached(DetachFromPanelEvent evt) => Dispose();
            public void Dispose()
            {
                if (m_String != null) m_String.StringChanged -= Updated;
                LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
                Element.UnregisterCallback<DetachFromPanelEvent>(Detached);
                s_Bindings.Remove(Element);
            }
        }
    }
}
