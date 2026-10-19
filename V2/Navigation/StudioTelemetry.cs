// C# | Navigation/StudioTelemetry.cs | .NET Framework 4.8 x64 | Bounded localhost telemetry from an operator-owned Studio scene.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BlueLockV2.Core;
using BlueLockV2.BallControl;

namespace BlueLockV2.Navigation
{
    public interface INavigationSource { NavigationSample Latest { get; } }
    public sealed class StudioTelemetry : INavigationSource, IBallLabSource, IDisposable
    {
        readonly TcpListener listener;
        readonly Thread thread;
        readonly IClock clock;
        readonly string token;
        NavigationSample latest;
        BallFrame latestBall;
        LabCommand ballCommand;
        string ballSession;
        volatile bool stopped;
        long lastSequence = -1;
        public string Endpoint { get; private set; }
        public string LastError { get; private set; }
        public int Accepted { get; private set; }
        public bool ControlValidated;
        public IntPtr WindowHandle;
        public NavigationSample Latest { get { return Volatile.Read(ref latest); } }
        public BallFrame LatestBall { get { return Volatile.Read(ref latestBall); } }
        public void SetBallCommand(LabCommand command) { Volatile.Write(ref ballCommand, command); }
        public StudioTelemetry(IClock time) : this(time, null) { }
        public StudioTelemetry(IClock time, string endpoint)
        {
            clock = time;
            Uri uri = null;
            if (endpoint != null && (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" ||
                uri.Port < 1024 || uri.Port > 65535 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !Regex.IsMatch(uri.AbsolutePath,"^/[a-fA-F0-9]{32}$")))
                throw new ArgumentException("Endpoint persistente inválido; exige localhost e token de 32 dígitos hexadecimais.");
            token = uri == null ? Guid.NewGuid().ToString("N") : uri.AbsolutePath.Substring(1);
            listener = new TcpListener(IPAddress.Loopback, uri == null ? 0 : uri.Port);
            listener.Start(2);
            Endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/" + token;
            thread = new Thread(Loop) { IsBackground = true, Name = "Owned Studio telemetry" };
            thread.Start();
        }
        public static StudioTelemetry OpenPersistent(IClock time, string file)
        {
            string endpoint = null;
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 256) throw new ArgumentException("Configuração de endpoint excede o limite.");
                endpoint = File.ReadAllText(file).Trim();
            }
            var source = new StudioTelemetry(time, endpoint);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
                File.WriteAllText(file, source.Endpoint, new UTF8Encoding(false));
                return source;
            }
            catch { source.Dispose(); throw; }
        }
        public static NavigationSample Parse(string json, double received)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 4 }.Deserialize<Dictionary<string, object>>(json);
            if (!data.ContainsKey("studio") || !(data["studio"] is bool) || !(bool)data["studio"])
                throw new ArgumentException("Telemetria precisa declarar o cenário de Studio.");
            Func<string, double> number = delegate(string key)
            {
                object input = data[key];
                if (!(input is int) && !(input is long) && !(input is double) && !(input is decimal) && !(input is float))
                    throw new ArgumentException("Tipo numérico inválido: " + key);
                double value = Convert.ToDouble(data[key], System.Globalization.CultureInfo.InvariantCulture);
                if (!MotionBasis.Finite(value) || Math.Abs(value) > 1000000) throw new ArgumentException("Valor inválido: " + key);
                return value;
            };
            var sample = new NavigationSample { Time = received, Sequence = (long)number("seq"),
                Space = NavigationSpace.WorldXZ, Player = new Vec(number("px"), number("pz")),
                Target = new Vec(number("tx"), number("tz")), Confidence = 1, CameraConfidence = 1,
                TargetVelocity = new Vec(number("vx"), number("vz")),
                Grounded = data.ContainsKey("grounded") && data["grounded"] is bool && (bool)data["grounded"],
                TargetObserved = !data.ContainsKey("visible") || (data["visible"] is bool && (bool)data["visible"]),
                PlayerObserved = !data.ContainsKey("active") || (data["active"] is bool && (bool)data["active"]),
                TargetId = data.ContainsKey("caseId") ? (int)number("caseId") : 1 };
            sample.Basis = MotionBasis.CameraXZ(new Vec(number("rx"), number("rz")), new Vec(number("fx"), number("fz")), number("speed"), 1);
            return sample;
        }
        void Loop()
        {
            while (!stopped)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = client.SendTimeout = 800;
                        using (NetworkStream stream = client.GetStream())
                        {
                            var header = new StringBuilder();
                            uint tail = 0;
                            bool completeHeader = false;
                            while (header.Length < 4096)
                            {
                                int value = stream.ReadByte();
                                if (value < 0) throw new IOException("Cabeçalho incompleto");
                                header.Append((char)value);
                                tail = (tail << 8) | (byte)value;
                                if (tail == 0x0D0A0D0A) { completeHeader = true; break; }
                            }
                            if (!completeHeader) throw new IOException("Cabeçalho fora do limite");
                            string[] lines = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
                            if (lines[0] != "POST /" + token + " HTTP/1.1") throw new IOException("Rota não autorizada");
                            int length = 0;
                            foreach (string line in lines)
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                    int.TryParse(line.Substring(15).Trim(), out length);
                            if (length <= 0 || length > 16384) throw new IOException("Payload fora do limite");
                            byte[] body = new byte[length];
                            int count = 0;
                            while (count < length)
                            {
                                int read = stream.Read(body, count, length - count);
                                if (read == 0) throw new IOException("Corpo incompleto");
                                count += read;
                            }
                            string json = Encoding.UTF8.GetString(body);
                            bool ballLab = json.IndexOf("ball-lab-v22", StringComparison.Ordinal) >= 0;
                            BallFrame ball = ballLab ? BallProtocol.Parse(json, clock.Now) : null;
                            NavigationSample sample = ball == null ? Parse(json, clock.Now) : ball.Motion;
                            if (ball != null && ballSession != ball.Session)
                            {
                                ballSession = ball.Session; lastSequence = -1; ControlValidated = false;
                                Volatile.Write(ref ballCommand, null);
                            }
                            if (sample.Sequence <= lastSequence) throw new IOException("Sequência repetida");
                            lastSequence = sample.Sequence;
                            Volatile.Write(ref latest, sample);
                            if (ball != null) Volatile.Write(ref latestBall, ball);
                            Accepted++;
                            LastError = "";
                            LabCommand command = Volatile.Read(ref ballCommand);
                            if (ball == null || command == null || command.session != ball.Session || command.trial != ball.Trial) command = null;
                            string responseJson = command == null ? "{}" : new JavaScriptSerializer().Serialize(new { command = command });
                            byte[] payload = Encoding.UTF8.GetBytes(responseJson);
                            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
                            stream.Write(response, 0, response.Length); stream.Write(payload, 0, payload.Length);
                        }
                    }
                }
                catch (Exception error) { if (!stopped) LastError = error.GetType().Name + ": " + error.Message; }
            }
        }
        public void Dispose() { stopped = true; listener.Stop(); if (Thread.CurrentThread != thread) thread.Join(2000); }
    }
}
