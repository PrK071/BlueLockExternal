// C# | Navigation/StudioLabProject.cs | .NET Framework 4.8 x64 | Creates a dedicated local Studio place from bundled scripts.
using System;
using System.IO;
using System.Xml;
using System.Text;

namespace BlueLockV2.Navigation
{
    public static class StudioLabProject
    {
        public static string Write(string folder, string endpoint)
        {
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || endpoint.IndexOfAny(new[] { '"', '\\', '\r', '\n' }) >= 0) throw new ArgumentException("Localhost only");
            Directory.CreateDirectory(folder);
            string scripts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StudioLab");
            string server = File.ReadAllText(Path.Combine(scripts, "Server.server.luau")).Replace("__ENDPOINT__", endpoint);
            string client = File.ReadAllText(Path.Combine(scripts, "Client.client.luau"));
            string path = Path.Combine(folder, "NavigationLab.rbxlx");
            WriteSources(path, server, client);
            return path;
        }
        public static void WriteSources(string path, string server, string client, string prefix = "NavigationLab")
        {
            XmlConvert.VerifyXmlChars(server); XmlConvert.VerifyXmlChars(client);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
            // Studio's place loader rejected EF BB BF although a generic XML reader accepted it.
            using (var xml = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false, true), OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize }))
            {
                xml.WriteStartElement("roblox");
                xml.WriteAttributeString("xmlns", "xmime", null, "http://www.w3.org/2005/05/xmlmime");
                xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                xml.WriteAttributeString("xsi", "noNamespaceSchemaLocation", "http://www.w3.org/2001/XMLSchema-instance", "http://www.roblox.com/roblox.xsd");
                xml.WriteAttributeString("version", "4");
                xml.WriteElementString("External", "null"); xml.WriteElementString("External", "nil");
                Item(xml, "Workspace", "Workspace"); xml.WriteEndElement();
                Item(xml, "HttpService", "HttpService", "bool", "HttpEnabled", "true"); xml.WriteEndElement();
                Item(xml, "ServerScriptService", "ServerScriptService");
                Script(xml, "Script", prefix + "Server", server); xml.WriteEndElement();
                Item(xml, "StarterPlayer", "StarterPlayer");
                Item(xml, "StarterPlayerScripts", "StarterPlayerScripts");
                Script(xml, "LocalScript", prefix + "Client", client);
                xml.WriteEndElement(); xml.WriteEndElement();
                xml.WriteEndElement();
            }
            StudioLabFormat.Validate(temporary);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static void Item(XmlWriter xml, string type, string name, string propertyType = null, string property = null, string value = null)
        {
            xml.WriteStartElement("Item"); xml.WriteAttributeString("class", type);
            xml.WriteAttributeString("referent", "RBX" + Guid.NewGuid().ToString("N"));
            xml.WriteStartElement("Properties");
            Property(xml, "string", "Name", name);
            if (property != null) Property(xml, propertyType, property, value);
            xml.WriteEndElement();
        }
        static void Property(XmlWriter xml, string type, string name, string value)
        { xml.WriteStartElement(type); xml.WriteAttributeString("name", name); xml.WriteString(value); xml.WriteEndElement(); }
        static void Script(XmlWriter xml, string type, string name, string source)
        {
            xml.WriteStartElement("Item"); xml.WriteAttributeString("class", type); xml.WriteAttributeString("referent", "RBX" + Guid.NewGuid().ToString("N"));
            xml.WriteStartElement("Properties"); Property(xml, "string", "Name", name);
            Property(xml, "ProtectedString", "Source", source);
            xml.WriteEndElement(); xml.WriteEndElement();
        }
    }
}
