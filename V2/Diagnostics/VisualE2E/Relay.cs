// C# | Diagnostics/VisualE2E/Relay.cs | .NET Framework 4.8 x64 | Bounded persistent localhost truth drain; prevents Studio POST retries crossing trial lifetimes.
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
class TruthRelay
{
 static int Main(string[] args)
 {
  string folder=Path.GetFullPath(args[0]);double limit=args.Length>1?Double.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture):900;if(limit<1||limit>1800||Double.IsNaN(limit))throw new ArgumentException("Relay limit 1..1800 seconds");Directory.CreateDirectory(folder);var watch=Stopwatch.StartNew();var listener=new TcpListener(IPAddress.Loopback,60137);listener.Start();
  using(var log=new StreamWriter(Path.Combine(folder,"relay-events.csv"),true))
  {
   log.WriteLine("seconds,bytes,forwarded");log.AutoFlush=true;
   try{while(watch.Elapsed.TotalSeconds<limit&&!File.Exists(Path.Combine(folder,"STOP-RELAY")))
   {
    if(!listener.Pending()){Thread.Sleep(10);continue;}
    using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream())
    {
     client.ReceiveTimeout=client.SendTimeout=300;
     try{
      var h=new StringBuilder();uint tail=0;while(h.Length<4096){int b=stream.ReadByte();if(b<0)throw new IOException();h.Append((char)b);tail=(tail<<8)|(byte)b;if(tail==0x0d0a0d0a)break;}
      if(!h.ToString().StartsWith("POST /truth-visual-e2e HTTP/1.1"))throw new IOException("route");int size=0;
      foreach(string line in h.ToString().Split(new[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))size=int.Parse(line.Substring(15).Trim());if(size<1||size>16384)throw new IOException("size");
      byte[] body=new byte[size];int n=0;while(n<size){int k=stream.Read(body,n,size-n);if(k==0)throw new IOException();n+=k;}
      bool forwarded=false;
      try{using(var target=new TcpClient()){target.ReceiveTimeout=target.SendTimeout=100;target.Connect(IPAddress.Loopback,60138);using(var dest=target.GetStream()){byte[] header=Encoding.ASCII.GetBytes(h.ToString());dest.Write(header,0,header.Length);dest.Write(body,0,body.Length);dest.ReadByte();forwarded=true;}}}catch(SocketException){}catch(IOException){}
      byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");stream.Write(response,0,response.Length);
      log.WriteLine(watch.Elapsed.TotalSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+size+","+forwarded);
     }catch(IOException){}catch(SocketException){}
    }
   }}finally{listener.Stop();}
  }
  return 0;
 }
}
