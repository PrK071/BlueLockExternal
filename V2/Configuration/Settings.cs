// C# | Configuration/Settings.cs | .NET Framework 4.8 | Versioned XML configuration and performance profiles.
using System;
using System.IO;
using System.Globalization;
using System.Xml;
using System.Xml.Serialization;
using BlueLockV2.Core;

namespace BlueLockV2.Configuration
{
    public sealed class Settings
    {
        public int Version = 2;
        public int PilotHotkey = 0x79;
        public bool PilotDribble = true;
        public bool BallAntenna = true;
        public bool NavigationCalibrated = false;
        public double NavigationRightX, NavigationRightY, NavigationForwardX, NavigationForwardY;
        public double PilotDribbleInterval = 1.6;
        public RunMode Mode = RunMode.ASSISTIDO;
        public PerformanceProfile Profile = PerformanceProfile.ECONOMICO;
        public bool Vision = true, Prediction = true, Overlay = true, CameraCompensation = true, Adaptive = true, UseRoi = true, UseArrows = false, Sprint = true;
        public double Confidence = .83, Horizon = .35, AnchorX = .5, AnchorY = .72, SoftCpuPercent = 15, MaxFrameAge = .6;
        public int ShotA = 180, ShotB = 500, MaxTracks = 24, HistoryLength = 12, OpenCvThreads = 1, CaptureFps = 0, DecisionFps = 0, ProcessingWidth = 0;
        public string Combo = "Q:45; WAIT:120; MOUSELEFT:300", ShotKey = "MOUSELEFT", TemplatePath = "";
        public bool CalibratedLocal = false, ColorsCalibrated = false, GameHudMask = true;
        public double LocalHue = 112, AllyHue = 90, OpponentHue = 0, GoalHue = 150, KeeperHue = 18;
        public double[] ImageCorners = new double[0];
        public double FieldWidth = 100, FieldHeight = 60;
        public void Validate()
        {
            PilotDribbleInterval = Numeric.Clamp(PilotDribbleInterval, .7, 10);
            if (!BlueLockV2.Input.Hotkeys.IsPilotKey(PilotHotkey))
                PilotHotkey = 0x79;
            OpenCvThreads = (int)Numeric.Clamp(OpenCvThreads, 1, 2);
            if (!Enum.IsDefined(typeof(RunMode), Mode))
                Mode = RunMode.ASSISTIDO;
            if (!Enum.IsDefined(typeof(PerformanceProfile), Profile))
                Profile = PerformanceProfile.ECONOMICO;
            if (ImageCorners == null)
                ImageCorners = new double[0];
            Confidence = Numeric.Clamp(Confidence, .5, .99);
            Horizon = Numeric.Clamp(Horizon, .05, 2);
            MaxTracks = (int)Numeric.Clamp(MaxTracks, 1, 64);
            HistoryLength = (int)Numeric.Clamp(HistoryLength, 2, 32);
            AnchorX = Numeric.Clamp(AnchorX, .05, .95);
            AnchorY = Numeric.Clamp(AnchorY, .1, .95);
            SoftCpuPercent = Numeric.Clamp(SoftCpuPercent, 1, 80);
            MaxFrameAge = Numeric.Clamp(MaxFrameAge, .15, 2);
            ShotA = (int)Numeric.Clamp(ShotA, 10, 3000);
            ShotB = (int)Numeric.Clamp(ShotB, 10, 3000);
        }

        public Settings Copy()
        {
            Settings result = (Settings)MemberwiseClone();
            result.ImageCorners = ImageCorners == null ? new double[0] : (double[])ImageCorners.Clone();
            return result;
        }
    }

    public sealed class Profile
    {
        public readonly int Width, PerceptionFps, DecisionFps;
        public Profile(int width, int perception, int decision)
        {
            Width = width;
            PerceptionFps = perception;
            DecisionFps = decision;
        }

        public static Profile For(PerformanceProfile profile)
        {
            return profile == PerformanceProfile.ECONOMICO ? new Profile(480, 5, 15) : profile == PerformanceProfile.EQUILIBRADO ? new Profile(640, 10, 20) : new Profile(960, 15, 30);
        }

        public static Profile For(Settings settings)
        {
            Profile defaults = For(settings.Profile);
            return new Profile(settings.ProcessingWidth > 0 ? (int)Numeric.Clamp(settings.ProcessingWidth, 320, 1280) : defaults.Width, settings.CaptureFps > 0 ? (int)Numeric.Clamp(settings.CaptureFps, 1, 30) : defaults.PerceptionFps, settings.DecisionFps > 0 ? (int)Numeric.Clamp(settings.DecisionFps, 1, 60) : defaults.DecisionFps);
        }
    }

    public static class SettingsStore
    {
        public static bool ImportPilotCalibration(Settings settings)
        {
            if (settings.CalibratedLocal)
                return true;
            string folder = Path.Combine(Directory.GetParent(Folder).FullName, "Pilot");
            string path = Path.Combine(folder, "settings.xml");
            if (!File.Exists(path))
                return false;
            var doc = new XmlDocument
            {
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                doc.Load(reader);
            double x, y;
            int width, height;
            if (!double.TryParse(Value(doc, "AnchorX"), NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !double.TryParse(Value(doc, "AnchorY"), NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !int.TryParse(Value(doc, "CalibratedWindowWidth"), out width) || !int.TryParse(Value(doc, "CalibratedWindowHeight"), out height) || Double.IsNaN(x) || Double.IsNaN(y) || x < .05 || x > .95 || y < .1 || y > .95 || width < 100 || height < 100)
                return false;
            settings.AnchorX = x;
            settings.AnchorY = y;
            settings.CalibratedLocal = true;
            return true;
        }

        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueLockExternal", "V2");
        public static Settings Load()
        {
            string path = Path.Combine(Folder, "settings.xml");
            Settings settings;
            if (File.Exists(path))
            {
                using (var file = File.OpenRead(path))
                    settings = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(file);
            }
            else
            {
                settings = new Settings();
                string legacy = Path.Combine(Directory.GetParent(Folder).FullName, "settings.xml");
                if (File.Exists(legacy))
                {
                    var doc = new XmlDocument();
                    doc.Load(legacy);
                    int n;
                    string v = Value(doc, "ShotA");
                    if (int.TryParse(v, out n))
                        settings.ShotA = n;
                    v = Value(doc, "ShotB");
                    if (int.TryParse(v, out n))
                        settings.ShotB = n;
                    v = Value(doc, "Combo");
                    if (v != null)
                        settings.Combo = v;
                    v = Value(doc, "ShotButton");
                    if (v != null)
                        settings.ShotKey = v;
                }
            }

            settings.Validate();
            return settings;
        }

        static string Value(XmlDocument doc, string name)
        {
            XmlNode node = doc.SelectSingleNode("//" + name);
            return node == null ? null : node.InnerText;
        }

        public static void Save(Settings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "settings.xml"), temporary = path + ".tmp";
            using (var file = File.Create(temporary))
                new XmlSerializer(typeof(Settings)).Serialize(file, settings);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
    }
}
