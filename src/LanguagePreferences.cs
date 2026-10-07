using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TunnelWatch
{
    internal static class LanguagePreferences
    {
        private static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TunnelWatch", "language.txt"); } }
        internal static string Load(string fallback) { return Load(FilePath, fallback); }
        internal static void Save(string language) { Save(FilePath, language); }
        internal static string Load(string path, string fallback)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 128) return fallback;
                string language = File.ReadAllText(path).Trim();
                return string.IsNullOrEmpty(language) ? fallback : CultureInfo.GetCultureInfo(language).Name;
            }
            catch (IOException) { return fallback; }
            catch (UnauthorizedAccessException) { return fallback; }
            catch (CultureNotFoundException) { return fallback; }
        }
        internal static void Save(string path, string language)
        {
            string name = CultureInfo.GetCultureInfo(language).Name;
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A language code is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, name, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
