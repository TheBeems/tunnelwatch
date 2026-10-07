using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TunnelWatch
{
    internal static class SettingsLoader
    {
        internal static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.local.json"); } }
        internal static Settings Load()
        {
            var result = Load(FilePath);
            result.Language = LanguagePreferences.Load(result.Language);
            L.Configure(result.Language);
            return result;
        }
        internal static Settings Load(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException(L.Get("Config.Missing"));
            var result = new JavaScriptSerializer().Deserialize<Settings>(Read(path));
            SettingsRules.Validate(result);
            return result;
        }
        private static string Read(string path)
        {
            if (new FileInfo(path).Length > 65536) throw new InvalidOperationException(L.Get("Config.Size"));
            return File.ReadAllText(path);
        }
        internal static void Save(Settings settings) { Save(FilePath, settings); }
        internal static void Save(string path, Settings settings)
        {
            SettingsRules.Validate(settings);
            var serializer = new JavaScriptSerializer();
            var document = File.Exists(path) ? serializer.Deserialize<Dictionary<string, object>>(Read(path)) : new Dictionary<string, object>();
            if (document == null) throw new InvalidOperationException(L.Get("Config.Empty"));
            foreach (var field in typeof(Settings).GetFields()) document[field.Name] = field.GetValue(settings);
            string json = serializer.Serialize(document);
            if (Encoding.UTF8.GetByteCount(json) > 65536) throw new InvalidOperationException(L.Get("Config.Size"));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

}
