using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Threading;

namespace TunnelWatch
{
    internal static class L
    {
        private static readonly ResourceManager resources = ResourceManager.CreateFileBasedResourceManager(
            "Strings", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales"), null);
        private static CultureInfo uiCulture = CultureInfo.GetCultureInfo("en");
        internal static string Language { get { return uiCulture.Name; } }
        internal static IList<CultureInfo> GetLanguages()
        {
            return GetLanguages(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales"));
        }
        internal static IList<CultureInfo> GetLanguages(string directory)
        {
            var languages = new List<CultureInfo>();
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory, "Strings.*.resources"))
                {
                    string name = Path.GetFileNameWithoutExtension(file).Substring("Strings.".Length);
                    try
                    {
                        var culture = CultureInfo.GetCultureInfo(name);
                        if (culture.Name.Length != 0 && culture.Name != "en" && !languages.Exists(c => c.Name == culture.Name)) languages.Add(culture);
                    }
                    catch (CultureNotFoundException) { } // Ignore unrelated/malformed filenames.
                }
            }
            languages.Sort((a, b) => string.Compare(a.NativeName, b.NativeName, StringComparison.CurrentCulture));
            languages.Insert(0, CultureInfo.GetCultureInfo("en"));
            return languages;
        }
        internal static string DisplayedLanguage(string language, IList<CultureInfo> available)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            while (culture.Name.Length != 0)
            {
                foreach (var candidate in available) if (candidate.Name == culture.Name) return candidate.Name;
                culture = culture.Parent;
            }
            return "en";
        }
        internal static void Configure(string language)
        {
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en" : language); }
            catch (CultureNotFoundException) { throw new ArgumentException(Format("Config.LanguageInvalid", language)); }
            // UI language is independent of the user's date and number formatting.
            uiCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        internal static string Get(string key)
        {
            // Queued WinForms callbacks and tasks may retain an old execution
            // context after a language switch. Use the explicit app preference.
            var value = resources.GetString(key, uiCulture);
            if (value == null) throw new InvalidOperationException("Missing English resource: " + key);
            return value;
        }
        internal static string Format(string key, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
        }
    }
}
