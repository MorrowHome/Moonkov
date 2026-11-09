using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
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
        private static LocalizedStringTable s_Strings;
        private static FontAsset s_CurrentFont;
        private static FontAsset s_PreparedFont;
        private static bool s_Initialized;
        private static int s_LocaleRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var binding in new List<Binding>(s_Bindings.Values)) binding.Dispose();
            s_Bindings.Clear();
            if (s_Font != null) s_Font.AssetChanged -= FontChanged;
            if (s_Strings != null) s_Strings.TableChanged -= StringTableChanged;
            s_Strings = null;
            s_LocaleRequest++;
            s_Font = null; s_CurrentFont = null; s_PreparedFont = null; s_Initialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (s_Initialized || Application.isBatchMode) return;
            s_Initialized = true;
            s_Font = new LocalizedAsset<FontAsset> { TableReference = Fonts, TableEntryReference = "UI" };
            s_Font.AssetChanged += FontChanged;
            s_Strings = new LocalizedStringTable { TableReference = Strings };
            s_Strings.TableChanged += StringTableChanged;
        }

        private static StringTable ReadyTable
        {
            get
            {
                var operation = s_Strings?.CurrentLoadingOperationHandle ?? default;
                return operation.IsValid() && operation.IsDone && operation.Status == AsyncOperationStatus.Succeeded
                    ? operation.Result : null;
            }
        }

        private static void StringTableChanged(StringTable table)
        {
            // Initial UI renders source text; refresh it once the asynchronous table arrives.
            foreach (var binding in new List<Binding>(s_Bindings.Values)) binding.Refresh();
        }

        public static string CurrentLocaleCode
        {
            get
            {
                var operation = LocalizationSettings.SelectedLocaleAsync;
                return operation.IsDone && operation.Status == AsyncOperationStatus.Succeeded
                    ? operation.Result?.Identifier.Code : PlayerPrefs.GetString("Moonkov.Locale", "zh-Hans");
            }
        }

        private static void FontChanged(FontAsset font)
        {
            s_CurrentFont = font;
            foreach (var binding in s_Bindings.Values) ApplyFont(binding.Element);
        }

        public static async Task PrepareGameplayFontAsync(CancellationToken cancellationToken)
        {
            Initialize();
            if (s_Font == null) return;
            await LocalizationSettings.InitializationOperation.Task;
            cancellationToken.ThrowIfCancellationRequested();
            var font = await s_Font.LoadAssetAsync().Task;
            if (!font || font == s_PreparedFont) return;
            var table = await LocalizationSettings.StringDatabase.GetTableAsync(Strings).Task;
            cancellationToken.ThrowIfCancellationRequested();
            var text = new StringBuilder();
            for (char c = ' '; c <= '~'; c++) text.Append(c);
            if (table != null)
                foreach (var entry in table.Values)
                    text.Append(entry.LocalizedValue);
            // Populate the existing dynamic atlas while the loading screen is
            // visible. Keep the asset's font features; unknown names use fallback.
            font.TryAddCharacters(text.ToString(), out _);
            await Awaitable.NextFrameAsync(cancellationToken);
            s_PreparedFont = font;
        }

        private static void ApplyFont(TextElement element)
        {
            // A surface can own its source-font rendering while keeping string bindings.
            // In particular, LAN uses the source font to avoid shared SDF atlas edge artifacts.
            for (VisualElement scope = element; scope != null; scope = scope.parent)
                if (scope.ClassListContains("moonkov-style-font"))
                {
                    element.style.unityFontDefinition = StyleKeyword.Null;
                    element.style.letterSpacing = StyleKeyword.Null;
                    return;
                }
            if (element.ClassListContains("terminal-home-title") || element.ClassListContains("stash-brand-title") || element.ClassListContains("archive-access-brand"))
            {
                element.style.unityFontDefinition = StyleKeyword.Null;
                element.style.letterSpacing = StyleKeyword.Null;
                return;
            }
            if (s_CurrentFont == null) return;
            element.style.unityFontDefinition = FontDefinition.FromSDFFont(s_CurrentFont);
            if (CurrentLocaleCode == "zh-Hans") element.style.letterSpacing = 0;
            else element.style.letterSpacing = StyleKeyword.Null;
        }

        internal static async Task<bool> SelectAsync(string code)
        {
            Initialize();
            int request = ++s_LocaleRequest;
            try
            {
                var initialization = LocalizationSettings.InitializationOperation;
                await initialization.Task;
                if (request != s_LocaleRequest || initialization.Status != AsyncOperationStatus.Succeeded) return false;
                var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
                if (locale == null) return false;
                LocalizationSettings.SelectedLocale = locale;
                PlayerPrefs.SetString("Moonkov.Locale", locale.Identifier.Code);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        public static string Text(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            Initialize();
            var table = ReadyTable;
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
                if (element.ClassListContains("unity-text-element--inner-input-field") ||
                    element.ClassListContains("unity-text-element--inner-input-field-component")) return;
                // The language dropdown supplies its own option names. Only apply locale fonts to its value.
                if (element.ClassListContains(DropdownField.textUssClassName) &&
                    element.GetFirstAncestorOfType<DropdownField>()?.ClassListContains("terminal-language") == true)
                {
                    if (!s_Bindings.ContainsKey(element)) s_Bindings.Add(element, new Binding(element));
                    return;
                }
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
            private bool m_Disposed;
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
                Refresh();
            }
            public void Refresh()
            {
                if (m_Disposed || m_Arguments == null) return;
                if (m_String != null) m_String.StringChanged -= Updated;
                m_String = null;
                var table = ReadyTable;
                if (table?.GetEntry(m_Source) != null)
                {
                    m_String = new LocalizedString(Strings, m_Source) { Arguments = TranslateArguments() };
                    m_String.StringChanged += Updated;
                }
                else Updated(m_Arguments.Length == 0 ? m_Source : string.Format(CultureInfo.CurrentCulture, m_Source, TranslateArguments()));
            }
            private object[] TranslateArguments()
            {
                var args = new object[m_Arguments.Length];
                for (int i = 0; i < args.Length; i++) args[i] = m_Arguments[i] is string text ? Text(text) : m_Arguments[i];
                return args;
            }
            private void LocaleChanged(Locale locale)
            {
                Refresh();
                ApplyFont(Element);
            }
            private void Updated(string value) { Rendered = value; Element.text = value; }
            private void Detached(DetachFromPanelEvent evt) => Dispose();
            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                if (m_String != null) m_String.StringChanged -= Updated;
                LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
                Element.UnregisterCallback<DetachFromPanelEvent>(Detached);
                s_Bindings.Remove(Element);
            }
        }
    }
}
