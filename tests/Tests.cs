using System;
using System.Collections.Generic;

namespace TunnelWatch
{
    internal static class Tests
    {
        private static int passed;
        private static Settings settings = new Settings { Router = "192.0.2.1", HomeHost = "198.51.100.33", HomeNetwork = "198.51.100.0/24" };
        private static Observation Healthy(bool full)
        {
            return new Observation { Split = full ? ServiceState.Stopped : ServiceState.Running, Full = full ? ServiceState.Running : ServiceState.Missing, ListenerVerified = true, RouterReachable = true, RouterRoute = new Route { Adapter = full ? settings.FullProfile : settings.SplitProfile, Description = "WireGuard Tunnel", Up = true, Source = "192.0.2.2", Index = 7, Luid = 7, NextHop = "0.0.0.0", Prefix = "192.0.2.0", PrefixLength = 24 }, HomeRoute = new Route { Error = "geen route" } };
        }
        private static Route Home() { return new Route { Adapter = "Wi-Fi", Physical = true, Up = true, Source = "198.51.100.7", Prefix = "198.51.100.0", PrefixLength = 24, NextHop = "0.0.0.0" }; }
        private static void Expect(string name, Observation o, Health expected)
        {
            var result = Status.Evaluate(o, settings);
            if (result.Health != expected) throw new Exception(name + ": verwacht " + expected + ", kreeg " + result.Health);
            if (string.IsNullOrEmpty(result.Reason)) throw new Exception(name + ": reden ontbreekt.");
            passed++;
        }
        internal static int Run()
        {
            passed = 0;
            L.Configure("en");
            Expect("gezonde split zonder admin", Healthy(false), Health.Healthy);
            Expect("gezonde full", Healthy(true), Health.Healthy);
            var o = Healthy(false); o.Tcp443 = false; Expect("TCP443 ontbreekt maar tunneltest slaagt", o, Health.Healthy);
            o = Healthy(false); o.ListenerVerified = false; o.Tcp443 = true; Expect("WireGuard actief WSS uit", o, Health.Broken);
            o = Healthy(false); o.ListenerVerified = false; o.WssDetail = "ander proces"; Expect("verkeerde listener-eigenaar", o, Health.Broken);
            o = Healthy(false); o.RouterReachable = false; o.Tcp443 = true; Expect("TCP Established router onbereikbaar", o, Health.Broken);
            o = Healthy(false); o.RouterRoute = Home(); Expect("ping via wifi geen VPN-bewijs", o, Health.Broken);
            o = Healthy(false); o.HomeRoute = Home(); o.HomeReachable = true; o.RouterRoute = Home(); Expect("thuis bereikbaar actief profiel defect", o, Health.Broken);
            o = Healthy(false); o.Split = ServiceState.Stopped; o.RouterReachable = false; Expect("alles uit", o, Health.Off);
            o.HomeRoute = Home(); o.HomeReachable = true; Expect("direct thuis wifi", o, Health.Home);
            o.HomeRoute.Physical = false; Expect("virtuele adapter geen thuisbewijs", o, Health.Off);
            o.HomeRoute = Home(); o.HomeRoute.NextHop = "198.51.100.1"; Expect("gerouteerde bereikbaarheid geen directe thuisclaim", o, Health.Off);
            o.HomeRoute = Home(); o.HomeRoute.Source = "203.0.113.4"; Expect("verkeerd subnet geen thuisclaim", o, Health.Off);
            o.HomeRoute = Home(); o.HomeReachable = false; Expect("onlink zonder antwoord geen thuisclaim", o, Health.Off);
            o = Healthy(false); o.ListenerUnknown = true; Expect("geen rechten eigenaar", o, Health.Checking);
            o = Healthy(false); o.Split = ServiceState.Unknown; Expect("geen rechten dienst", o, Health.Checking);
            o = Healthy(false); o.Full = ServiceState.Pending; Expect("dienst schakelt", o, Health.Checking);
            o = Healthy(false); o.Full = ServiceState.Running; Expect("beide profielen actief", o, Health.Broken);
            o = Healthy(false); o.Changed = true; Expect("routewijziging meetronde", o, Health.Checking);
            o = Healthy(false); o.RouterRoute.Up = false; Expect("adapter down", o, Health.Broken);
            o = Healthy(false); o.RouterRoute.Error = "geen route"; Expect("geen routerroute", o, Health.Broken);
            o = Healthy(true); o.RouterRoute.Adapter = settings.SplitProfile; Expect("full dienst split route", o, Health.Broken);
            o = Healthy(false); o.RouterRoute.Description = "andere tunnel"; Expect("profielnaam zonder WireGuard adapter", o, Health.Broken);
            var a = Home(); var b = Home(); if (!a.Same(b)) throw new Exception("stabiele route"); b.Source = "198.51.100.8"; if (a.Same(b)) throw new Exception("bronwijziging niet herkend"); passed++;
            byte[] port = { 0x98, 0xa3, 0, 0 }; if (PortCodec.Decode(port, 0) != 39075) throw new Exception("poort bytevolgorde"); passed++;
            if (Route.InSubnet("203.0.113.1", "198.51.100.0", 24) || !Route.InSubnet("198.51.100.33", "198.51.100.0", 24)) throw new Exception("subnet"); passed++;
            // Presentation must not promote stale data or a direct home response
            // into proof that the VPN works.
            o = Healthy(false); var status = Status.Evaluate(o, settings);
            var visual = StatusVisual.From(status, o, settings);
            if (visual.Home != Health.Healthy || visual.Mode != "Split") throw new Exception("gezonde splitweergave"); passed++;
            visual = StatusVisual.From(new Status { Health = Health.Checking, Reason = "Meting verouderd" }, o, settings);
            if (visual.Wss != Health.Checking || visual.Vpn != Health.Checking || visual.Home != Health.Checking || visual.Mode != null) throw new Exception("verouderde groene weergave"); passed++;
            o.RouterRoute = Home(); o.HomeRoute = Home(); o.HomeReachable = true;
            visual = StatusVisual.From(Status.Evaluate(o, settings), o, settings);
            if (visual.Home != Health.Home || visual.HomeText != "Direct home") throw new Exception("directe thuisroute als VPN weergegeven"); passed++;
            o = Healthy(true); visual = StatusVisual.From(Status.Evaluate(o, settings), o, settings);
            if (!visual.Summary.Contains("unconfirmed") || visual.Mode != "Full") throw new Exception("full suggereert internetbewijs"); passed++;
            o = Healthy(false); o.ListenerVerified = false;
            visual = StatusVisual.From(Status.Evaluate(o, settings), o, settings);
            if (visual.Wss != Health.Broken || visual.VpnText != "Split") throw new Exception("ontbrekend WSS verstopt"); passed++;
            o = Healthy(false); o.Full = ServiceState.Running;
            visual = StatusVisual.From(Status.Evaluate(o, settings), o, settings);
            if (visual.Mode != null || visual.Vpn != Health.Broken || visual.Home == Health.Healthy) throw new Exception("profielconflict verstopt"); passed++;
            CheckLocalization();
            CheckLanguagePreferences();
            CheckSystemErrors();
            passed += SettingsTests.Run();
#if !CORE_TESTS
            passed += SettingsPersistenceTests.Run();
#endif
            Console.WriteLine(passed + " meaningful checks passed.");
            return passed;
        }
        private static void CheckLocalization()
        {
            var formatting = System.Globalization.CultureInfo.CurrentCulture;
            if (new Settings().Language != "en" || L.Language != "en" || Status.Evaluate(new Observation(), settings).Title != "Tunnel off") throw new Exception("English default"); passed++;
            L.Configure("nl-NL");
            if (L.Get("Ui.Refresh") != "Verversen" || Status.Evaluate(Healthy(false), settings).Title != "Tunnel werkt") throw new Exception("Regional culture fallback"); passed++;
            if (Status.Evaluate(new Observation(), settings).Profile != "off" || StatusVisual.From(Status.Evaluate(Healthy(true), settings), Healthy(true), settings).Mode != "Full") throw new Exception("Language changed machine identifiers"); passed++;
            if (System.Threading.Tasks.Task.Run(delegate { return L.Get("Ui.Refresh"); }).Result != "Verversen") throw new Exception("Background task language"); passed++;
            if (L.Format("Wss.NoListener", 39075).IndexOf("39075", StringComparison.Ordinal) < 0) throw new Exception("Localized placeholders"); passed++;
            L.Configure("fr-FR");
            if (L.Get("Ui.Refresh") != "Refresh" || Status.Evaluate(Healthy(false), settings).Title != "Tunnel working") throw new Exception("English fallback"); passed++;
            // Isolated partial translation proves per-key fallback without leaving
            // intentional gaps in the shipped Dutch translation.
            var directory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "localization-test");
            System.IO.Directory.CreateDirectory(directory);
            using (var writer = new System.Resources.ResourceWriter(System.IO.Path.Combine(directory, "Test.resources"))) { writer.AddResource("Fallback", "English fallback"); writer.Generate(); }
            using (var writer = new System.Resources.ResourceWriter(System.IO.Path.Combine(directory, "Test.fr.resources"))) { writer.AddResource("Present", "Traduction"); writer.Generate(); }
            var partial = System.Resources.ResourceManager.CreateFileBasedResourceManager("Test", directory, null);
            try {
                var french = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                if (partial.GetString("Present", french) != "Traduction" || partial.GetString("Fallback", french) != "English fallback") throw new Exception("Missing key fallback");
            } finally { partial.ReleaseAllResources(); }
            passed++;
            bool rejected = false;
            try { L.Configure("../not-a-culture"); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid culture accepted"); passed++;
            L.Configure(null);
            if (L.Language != "en" || !object.Equals(System.Globalization.CultureInfo.CurrentCulture, formatting)) throw new Exception("UI language changed date/number formatting"); passed++;
        }
        private static void CheckSystemErrors()
        {
            L.Configure("en");
            // Reproduce the mixed-language Details panel without a real VPN.
            var error = new System.ComponentModel.Win32Exception(1231, "De netwerklocatie kan niet worden bereikt.");
            System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
            string english = SystemErrors.Describe(error);
            if (english.IndexOf("network location cannot be reached", StringComparison.OrdinalIgnoreCase) < 0 || !english.Contains("1231") || english.Contains("netwerklocatie")) throw new Exception("Windows error follows OS language instead of English"); passed++;
            var context = System.Threading.ExecutionContext.Capture();
            L.Configure("nl-NL");
            string dutch = SystemErrors.Describe(error);
            // Windows without a Dutch message pack must use English, not an
            // unrelated OS language. The surrounding label follows the app.
            if (!dutch.Contains("Windows-fout 1231") || dutch == english) throw new Exception("Regional Windows error language"); passed++;
            L.Configure("en");
            System.Threading.ExecutionContext.Run(context, delegate(object state) {
                if (SystemErrors.Describe(error) != english) throw new Exception("Old callback culture leaked into Windows error");
            }, null); passed++;
            L.Configure("fr-FR");
            if (SystemErrors.Describe(error) != english) throw new Exception("Untranslated app language must use English Windows errors"); passed++;
            L.Configure("en");
            if (SystemErrors.Win32(int.MaxValue) != "Windows error 2147483647.") throw new Exception("Unknown Windows error must preserve its code"); passed++;
            string socket = SystemErrors.Describe(new System.Net.Sockets.SocketException(10060));
            if (!socket.Contains("10060") || socket.IndexOf("connection", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("Socket error localization"); passed++;
            string io = SystemErrors.Describe(new System.IO.IOException("Nederlandse systeemmelding", unchecked((int)0x80070005)));
            if (io.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) < 0 || !io.Contains("5")) throw new Exception("Windows HRESULT localization"); passed++;
            if (SystemErrors.Describe(new InvalidOperationException("Application diagnostic")) != "Application diagnostic") throw new Exception("Application diagnostic lost"); passed++;
        }
        private static void CheckLanguagePreferences()
        {
            var directory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language-preferences-test", Guid.NewGuid().ToString("N"));
            var path = System.IO.Path.Combine(directory, "language.txt");
            if (LanguagePreferences.Load(path, "en") != "en" || LanguagePreferences.Load(path, "nl") != "nl") throw new Exception("Missing preference overrides config default"); passed++;
            LanguagePreferences.Save(path, "nl");
            if (LanguagePreferences.Load(path, "en") != "nl") throw new Exception("Language choice does not persist"); passed++;
            LanguagePreferences.Save(path, "en");
            if (LanguagePreferences.Load(path, "nl") != "en" || System.IO.Directory.GetFiles(directory).Length != 1) throw new Exception("Preference replacement or temporary file cleanup"); passed++;
            System.IO.File.WriteAllText(path, "../invalid-language");
            if (LanguagePreferences.Load(path, "en") != "en") throw new Exception("Corrupt preference prevents startup"); passed++;
            System.IO.File.WriteAllText(path, new string('x', 200));
            if (LanguagePreferences.Load(path, "nl") != "nl") throw new Exception("Oversized preference prevents startup"); passed++;
            LanguagePreferences.Save(path, "en"); bool rejected = false;
            try { LanguagePreferences.Save(path, "../invalid-language"); } catch (ArgumentException) { rejected = true; }
            if (!rejected || LanguagePreferences.Load(path, "nl") != "en") throw new Exception("Invalid preference damaged saved choice"); passed++;
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Strings.fr.resources"), "synthetic discovery fixture");
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Strings.nl.resources"), "synthetic discovery fixture");
            var available = L.GetLanguages(directory);
            if (available.Count != 3 || available[0].Name != "en" || L.DisplayedLanguage("fr-FR", available) != "fr" || L.DisplayedLanguage("nl-NL", available) != "nl" || L.DisplayedLanguage("de-DE", available) != "en") throw new Exception("Automatic discovery or selected language fallback"); passed++;
            var context = System.Threading.ExecutionContext.Capture();
            L.Configure("nl");
            System.Threading.ExecutionContext.Run(context, delegate(object state) {
                if (L.Get("Ui.Settings") != "Instellingen" || L.Get("Ui.Language") != "Taal") throw new Exception("Queued callback restored the old language");
            }, null);
            passed++;
            L.Configure("en");
        }
    }
}
