// C# | Tests/Navigation/PlaceFormatTests.cs | .NET Framework 4.8 x64 | Generated place format and actual Studio-export regression checks.
using System;
using System.IO;
using System.Text;
using System.Xml;
using BlueLockV2.Navigation;

namespace BlueLockV2.NavigationAcceptance
{
    static class PlaceFormatTests
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action)
        { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid lab accepted"); }
        public static void Run(Action<string, Action> test, string report)
        {
            string folder = Path.Combine(report, "format-fixtures"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "NavigationLab.rbxlx");
            string guard = "local RunService=game:GetService(\"RunService\")\nif not RunService:IsStudio() then return end\n";
            string special = guard + "-- português 漢字 < > & \" ' ]]>\nprint(\"ok\")\r\n";
            test("place generation uses UTF8 without BOM or declaration", delegate
            {
                StudioLabProject.WriteSources(path, special, special);
                byte[] bytes = File.ReadAllBytes(path);
                Check(bytes[0] == '<' && bytes[1] == 'r' && !File.ReadAllText(path).Contains("<?xml"), "invalid header");
                StudioLabFormat.Validate(path);
            });
            test("place Luau Unicode and XML metacharacters roundtrip", delegate
            {
                XmlDocument document = StudioLabFormat.Validate(path);
                Check(StudioLabFormat.Source(document, true) == special && StudioLabFormat.Source(document, false) == special, "script changed by serialization");
            });
            test("place BOM regression rejected despite generic XML success", delegate
            {
                string bom = Path.Combine(folder, "bom.rbxlx"); File.WriteAllText(bom, File.ReadAllText(path), new UTF8Encoding(true));
                var generic = new XmlDocument(); generic.Load(bom);
                Reject(delegate { StudioLabFormat.Validate(bom); });
            });
            test("native isolation changed only the three BOM bytes", delegate
            {
                byte[] invalid = File.ReadAllBytes(Path.Combine(report, "invalid-original.rbxlx"));
                byte[] opened = File.ReadAllBytes(Path.Combine(report, "bom-only-removed-opened.rbxlx"));
                Check(invalid.Length == opened.Length + 3 && invalid[0] == 239 && invalid[1] == 187 && invalid[2] == 191, "unexpected initial encoding");
                for (int i = 0; i < opened.Length; i++) Check(invalid[i + 3] == opened[i], "isolation changed other content");
            });
            test("place wrong version and typed HTTP flag rejected", delegate
            {
                var document = StudioLabFormat.Validate(path);
                string invalid = Path.Combine(folder, "wrong-contract.rbxlx");
                document.DocumentElement.SetAttribute("version", "3");
                File.WriteAllText(invalid, document.OuterXml, new UTF8Encoding(false));
                Reject(delegate { StudioLabFormat.Validate(invalid); });
                document.DocumentElement.SetAttribute("version", "4");
                document.SelectSingleNode("/roblox/Item[@class='HttpService']/Properties/bool[@name='HttpEnabled']").InnerText = "false";
                File.WriteAllText(invalid, document.OuterXml, new UTF8Encoding(false));
                Reject(delegate { StudioLabFormat.Validate(invalid); });
            });
            test("place truncated tags rejected", delegate
            {
                string invalid = Path.Combine(folder, "truncated.rbxlx");
                File.WriteAllText(invalid, "<roblox version=\"4\"><Item>", new UTF8Encoding(false));
                bool rejected = false; try { StudioLabFormat.Validate(invalid); } catch (XmlException) { rejected = true; }
                Check(rejected, "unclosed tags accepted");
            });
            test("place duplicate referents rejected", delegate
            {
                var document = StudioLabFormat.Validate(path); var items = document.SelectNodes("//Item");
                ((XmlElement)items[1]).SetAttribute("referent", ((XmlElement)items[0]).GetAttribute("referent"));
                string invalid = Path.Combine(folder, "duplicate.rbxlx"); File.WriteAllText(invalid, document.OuterXml, new UTF8Encoding(false));
                Reject(delegate { StudioLabFormat.Validate(invalid); });
            });
            test("place wrong script hierarchy rejected", delegate
            {
                var document = StudioLabFormat.Validate(path);
                var script = document.SelectSingleNode("/roblox/Item[@class='ServerScriptService']/Item");
                document.DocumentElement.SelectSingleNode("Item[@class='Workspace']").AppendChild(script);
                string invalid = Path.Combine(folder, "hierarchy.rbxlx"); File.WriteAllText(invalid, document.OuterXml, new UTF8Encoding(false));
                Reject(delegate { StudioLabFormat.Validate(invalid); });
            });
            test("place failed write preserves existing project", delegate
            {
                byte[] before = File.ReadAllBytes(path); bool rejected = false;
                try { StudioLabProject.WriteSources(path, guard + "\u0001", guard); } catch (XmlException) { rejected = true; }
                Check(rejected && Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "previous place damaged");
            });
            test("place bundled scenario and camera controls preserved", delegate
            {
                string generated = StudioLabProject.Write(folder, "http://127.0.0.1:12345/test");
                var document = StudioLabFormat.Validate(generated);
                string server = StudioLabFormat.Source(document, true), client = StudioLabFormat.Source(document, false);
                Check(server.Contains("LabGround") && server.Contains("PostAsync") && !server.Contains("__ENDPOINT__"), "server missing");
                Check(client.Contains("KeyCode.Z") && client.Contains("KeyCode.T") && client.Contains("KeyCode.Y"), "camera/case/reset missing");
            });
            test("place matches actual Studio exported hierarchy and sources", delegate
            {
                string reference = Path.Combine(report, "studio-exported-reference.rbxlx");
                string opened = Path.Combine(report, "bom-only-removed-opened.rbxlx");
                var exported = StudioLabFormat.Validate(reference);
                var original = new XmlDocument(); original.Load(opened);
                Check(StudioLabFormat.Source(exported, true).Replace("\r", "") == StudioLabFormat.Source(original, true).Replace("\r", ""), "Studio changed server source");
                Check(StudioLabFormat.Source(exported, false).Replace("\r", "") == StudioLabFormat.Source(original, false).Replace("\r", ""), "Studio changed client source");
                var comparison = new StringBuilder("Actual Studio export compared to the BOM-only corrected file.\r\n");
                comparison.AppendLine("Roblox version=4; typed names; unique referents; guarded Script/LocalScript hierarchy; HttpEnabled=true: PASS");
                comparison.AppendLine("Server/client Luau source equality (CRLF normalized): PASS");
                comparison.AppendLine("Studio adds default services/properties, Camera and Terrain; these are not required in the minimal generator.");
                File.WriteAllText(Path.Combine(report, "studio-format-comparison.txt"), comparison.ToString());
            });
        }
    }
}
