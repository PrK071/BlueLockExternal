// C# | Settings.cs | .NET Framework / Windows x64 | Local pilot configuration.
using System;
using System.Drawing;
using System.IO;
using System.Xml.Serialization;

public sealed class PilotSettings
{
    public bool UseArrows=false, Sprint=true, Dribble=true, AutoShoot=true, AutoJump=false, RequestPass=false;
    public int ShotMs=300, SampleDiameter=28, ConfidencePercent=85;
    public int DribbleIntervalMs=1600, ShotIntervalMs=3500;
    public double AnchorX=.5, AnchorY=.73;
    public string ShotKey="MOUSELEFT";
    public int CalibratedWindowWidth=1280, CalibratedWindowHeight=720;
    public string Forward="W", Back="S", Left="A", Right="D";
}

internal static class SettingsStore
{
    internal static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BlueLockExternal","Pilot");
    internal static readonly string TemplatePath=Path.Combine(Folder,"ball.png");
    internal static PilotSettings Load()
    {
        string path=Path.Combine(Folder,"settings.xml");
        if(!File.Exists(path)) return new PilotSettings();
        using(FileStream f=File.OpenRead(path)) return (PilotSettings)new XmlSerializer(typeof(PilotSettings)).Deserialize(f);
    }
    internal static void Save(PilotSettings settings)
    {
        Directory.CreateDirectory(Folder);
        string path=Path.Combine(Folder,"settings.xml"),temporary=path+".tmp";
        using(FileStream f=File.Create(temporary)) new XmlSerializer(typeof(PilotSettings)).Serialize(f,settings);
        if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
    }
}
