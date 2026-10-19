// C# | Navigation/StudioLabFormat.cs | .NET Framework 4.8 x64 | Strict local lab contract, not a replacement for Studio's place loader.
using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Collections.Generic;

namespace BlueLockV2.Navigation
{
    public static class StudioLabFormat
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        public static XmlDocument Validate(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text = new UTF8Encoding(false, true).GetString(bytes);
            Require(text.StartsWith("<roblox", StringComparison.Ordinal), "Place must begin directly with <roblox; BOM/declaration prohibited by generator contract");
            var document = new XmlDocument { XmlResolver = null };
            using (var input = new StringReader(text))
            using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16000000 })) document.Load(reader);
            XmlElement root = document.DocumentElement;
            Require(root != null && root.Name == "roblox" && root.NamespaceURI == "" && root.GetAttribute("version") == "4", "Expected Roblox XML version 4");
            foreach (XmlNode child in root.ChildNodes)
                if (child is XmlElement) Require(child.Name == "Item" || child.Name == "External" || child.Name == "Meta" || child.Name == "SharedStrings", "Invalid Roblox root child");
            Require(root.SelectSingleNode("External[text()='null']") != null && root.SelectSingleNode("External[text()='nil']") != null, "External references missing");
            var referents = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement item in document.SelectNodes("//Item"))
            {
                string referent = item.GetAttribute("referent");
                Require(item.GetAttribute("class").Length > 0 && referent.Length > 0 && referent != "null" && referent != "nil" && referents.Add(referent), "Invalid class or duplicate referent");
                Require(item.SelectNodes("Properties").Count == 1, "Item requires exactly one Properties");
                Require(item.SelectSingleNode("Properties/string[@name='Name']") != null, "Typed Name missing");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (XmlElement property in item.SelectSingleNode("Properties").ChildNodes)
                    Require(property.GetAttribute("name").Length > 0 && names.Add(property.GetAttribute("name")), "Missing or duplicate property name");
            }
            Require(root.SelectNodes("Item[@class='Workspace']").Count == 1, "Workspace service missing or duplicate");
            Require(root.SelectSingleNode("Item[@class='HttpService']/Properties/bool[@name='HttpEnabled' and text()='true']") != null, "Lab requires HttpEnabled boolean");
            Source(document, true); Source(document, false);
            return document;
        }
        public static string Source(XmlDocument document, bool server)
        {
            string hierarchy = server ? "Item[@class='ServerScriptService']/Item[@class='Script']" : "Item[@class='StarterPlayer']/Item[@class='StarterPlayerScripts']/Item[@class='LocalScript']";
            XmlNodeList scripts = document.DocumentElement.SelectNodes(hierarchy + "/Properties/ProtectedString[@name='Source']");
            Require(scripts.Count == 1 && scripts[0].InnerText.Contains("RunService:IsStudio()"), "Studio guarded lab script missing or in incorrect service");
            return scripts[0].InnerText;
        }
    }
}
