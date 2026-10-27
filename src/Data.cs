using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace KlembordTool
{
    public class Clip
    {
        [XmlIgnore] public string Text = "";

        // Stored as base64 so control characters from the clipboard never break the XML file.
        [XmlElement("Data")]
        public string Encoded
        {
            get { return Convert.ToBase64String(Encoding.UTF8.GetBytes(Text ?? "")); }
            set
            {
                try { Text = Encoding.UTF8.GetString(Convert.FromBase64String(value ?? "")); }
                catch (FormatException) { Text = ""; }
            }
        }

        public DateTime Time;
        public uint Mods;
        public uint Key;

        public static string HotkeyText(uint mods, uint key)
        {
            if (key == 0) return "";
            var parts = new List<string>();
            if ((mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & Native.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName((Keys)key));
            return string.Join("+", parts);
        }

        static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (k - Keys.NumPad0);
            switch (k)
            {
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                case Keys.Space: return "Spatie";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                default: return k.ToString();
            }
        }
    }

    public class AppData
    {
        public List<Clip> History = new List<Clip>();
        public List<Clip> Favorites = new List<Clip>();
        public List<Clip> Sequence = new List<Clip>();

        public uint MainMods = Native.MOD_CONTROL | Native.MOD_ALT;
        public uint MainKey = (uint)Keys.V;
        public bool AlwaysOnTop = false;
        public bool HideAfterPaste = false;
        public bool LoopSequence = false;
        public bool SaveHistory = true;
        public int MaxHistory = 200;
        public int SettingsVersion = 0;
        public bool AutoUpdateCheck = true;
        public DateTime LastUpdateCheck;
        public int WinX = int.MinValue, WinY = int.MinValue, WinW = 0, WinH = 0;

        // KLEMBORDTOOL_DATA lets a test copy run with its own data folder.
        public static readonly string Folder = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KLEMBORDTOOL_DATA"))
            ? Environment.GetEnvironmentVariable("KLEMBORDTOOL_DATA")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KlembordTool");

        static string FilePath { get { return Path.Combine(Folder, "data.xml"); } }

        public static AppData Load()
        {
            AppData d = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var s = File.OpenRead(FilePath))
                        d = (AppData)new XmlSerializer(typeof(AppData)).Deserialize(s);
                }
            }
            catch (Exception)
            {
                try { File.Copy(FilePath, Path.Combine(Folder, "data.beschadigd.xml"), true); } catch (Exception) { }
            }
            if (d == null) d = new AppData();
            if (d.SettingsVersion < 2)
            {
                // Version 1 hid the window after every paste; the window now stays open by default.
                d.HideAfterPaste = false;
                d.SettingsVersion = 2;
            }
            return d;
        }

        public void Save()
        {
            List<Clip> history = History;
            try
            {
                if (!SaveHistory) History = new List<Clip>();
                Directory.CreateDirectory(Folder);
                string tmp = FilePath + ".tmp";
                using (var s = File.Create(tmp))
                    new XmlSerializer(typeof(AppData)).Serialize(s, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception) { }
            finally { History = history; }
        }
    }
}
