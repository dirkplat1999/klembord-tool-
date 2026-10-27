using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KlembordTool
{
    internal class UpdateInfo
    {
        public Version Version;
        public string Tag, Notes, DownloadUrl, PageUrl;
    }

    /// <summary>Checks GitHub releases for a newer KlembordTool.exe and swaps it in.</summary>
    internal static class Updater
    {
        public const string Repo = "dirkplat1999/klembord-tool-";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        public static Version Current
        {
            get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        public static string Text(Version v)
        {
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        static WebClient NewClient()
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "KlembordTool/" + Text(Current);
            return wc;
        }

        /// <summary>Fetches the latest release; <paramref name="done"/> runs on the UI thread.</summary>
        public static void Check(Action<UpdateInfo, Exception> done)
        {
            var wc = NewClient();
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            wc.DownloadStringCompleted += (s, e) =>
            {
                wc.Dispose();
                if (e.Error != null) { done(null, e.Error); return; }
                UpdateInfo info;
                try { info = Parse(e.Result); }
                catch (Exception ex) { done(null, ex); return; }
                done(info, null);
            };
            wc.DownloadStringAsync(new Uri("https://api.github.com/repos/" + Repo + "/releases/latest"));
        }

        static UpdateInfo Parse(string json)
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var info = new UpdateInfo
            {
                Tag = root["tag_name"] as string ?? "",
                Notes = root.ContainsKey("body") ? root["body"] as string ?? "" : "",
                PageUrl = root.ContainsKey("html_url") ? root["html_url"] as string : ReleasesPage
            };
            Version v;
            info.Version = Version.TryParse(info.Tag.TrimStart('v', 'V'), out v) ? Normalize(v) : new Version(0, 0, 0);
            object assets;
            if (root.TryGetValue("assets", out assets) && assets is object[])
            {
                foreach (object a in (object[])assets)
                {
                    var asset = a as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = asset["name"] as string;
                    if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.DownloadUrl = asset["browser_download_url"] as string;
                        break;
                    }
                }
            }
            return info;
        }

        static string ExePath { get { return Application.ExecutablePath; } }
        public static string NewPath { get { return ExePath + ".new"; } }
        static string OldPath { get { return ExePath + ".oud"; } }

        /// <summary>Downloads the new exe next to the current one; callbacks run on the UI thread.</summary>
        public static void Download(string url, Action<int> progress, Action<Exception> done)
        {
            var wc = NewClient();
            wc.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
            wc.DownloadFileCompleted += (s, e) => { wc.Dispose(); done(e.Error); };
            try
            {
                if (File.Exists(NewPath)) File.Delete(NewPath);
                wc.DownloadFileAsync(new Uri(url), NewPath);
            }
            catch (Exception ex)
            {
                wc.Dispose();
                done(ex);
            }
        }

        /// <summary>
        /// Puts the downloaded exe in place of the running one. A running exe may be renamed but not
        /// overwritten, so the current file moves aside and is deleted on the next start.
        /// </summary>
        public static void Install()
        {
            var fi = new FileInfo(NewPath);
            if (!fi.Exists || fi.Length < 10 * 1024) throw new InvalidDataException("Het gedownloade bestand is onvolledig.");
            if (AssemblyName.GetAssemblyName(NewPath).Name != Assembly.GetExecutingAssembly().GetName().Name)
                throw new InvalidDataException("Het gedownloade bestand is geen Klembord Tool.");

            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            try { File.Move(NewPath, ExePath); }
            catch
            {
                File.Move(OldPath, ExePath);
                throw;
            }
        }

        /// <summary>Removes leftovers of a previous update.</summary>
        public static void CleanUp()
        {
            foreach (string path in new[] { OldPath, NewPath })
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception) { }
            }
        }
    }
}
