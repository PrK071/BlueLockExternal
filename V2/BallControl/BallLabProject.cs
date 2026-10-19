// C# | BallControl/BallLabProject.cs | .NET Framework 4.8 x64 | Generates a separate controlled physical-ball place; preserves NavigationLab.
using System;
using System.IO;
using BlueLockV2.Navigation;
namespace BlueLockV2.BallControl
{
    public static class BallLabProject
    {
        public static string Write(string folder, string endpoint)
        {
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || endpoint.IndexOfAny(new[]{'"','\\','\r','\n'}) >= 0)
                throw new ArgumentException("Localhost endpoint required");
            Directory.CreateDirectory(folder);
            string scripts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"StudioLab");
            string path = Path.Combine(folder,"BallControlLab.rbxlx");
            StudioLabProject.WriteSources(path,File.ReadAllText(Path.Combine(scripts,"BallServer.server.luau")).Replace("__ENDPOINT__",endpoint),File.ReadAllText(Path.Combine(scripts,"BallClient.client.luau")),"BallControlLab");
            return path;
        }
    }
}
