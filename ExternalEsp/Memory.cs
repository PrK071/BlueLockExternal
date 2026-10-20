// C# | ExternalEsp/Memory.cs | .NET Framework 4.8 x64 / Windows | Bounded read-only process transport, exact byte counts and validated strings.
using System;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Text;using Microsoft.Win32.SafeHandles;
namespace BlueLockEsp
{
 public interface IMemory {byte[] Read(long address,int count);}
 public sealed class MemoryFault:IOException {public MemoryFault(string message):base(message){}}
 public sealed class ProcessMemory:IMemory,IDisposable
 {
  readonly SafeProcessHandle handle;public long BytesRead,ReadCalls;public int Pid;
  [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(SafeProcessHandle h,IntPtr address,[Out]byte[] buffer,UIntPtr count,out UIntPtr read);
  public ProcessMemory(int pid){if(IntPtr.Size!=8)throw new PlatformNotSupportedException("Executável x64 necessário");Pid=pid;handle=OpenProcess(0x1010,false,pid);if(handle==null||handle.IsInvalid){int error=Marshal.GetLastWin32Error();if(handle!=null)handle.Dispose();throw new MemoryFault("OpenProcess recusado; Win32="+error);}}
  public byte[] Read(long address,int count){if(address<0x10000||address>0x00007FFFFFFEFFFFL||count<1||count>262144||address>0x00007FFFFFFF0000L-count)throw new MemoryFault("Endereço/tamanho inválido");var data=new byte[count];UIntPtr received;ReadCalls++;bool ok=ReadProcessMemory(handle,new IntPtr(address),data,new UIntPtr((uint)count),out received);int error=ok?0:Marshal.GetLastWin32Error();BytesRead+=(long)received.ToUInt64();if(!ok||received.ToUInt64()!=(ulong)count)throw new MemoryFault("ReadProcessMemory incompleto; Win32="+error+"; bytes="+received.ToUInt64()+"/"+count);return data;}
  public void Dispose(){handle.Dispose();}
 }
 public static class MemoryValues
 {
  public static bool Pointer(long p){return p>=0x10000&&p<0x00007FFFFFFF0000L&&(p&7)==0;}
  public static long PointerAt(this IMemory m,long address){long p=BitConverter.ToInt64(m.Read(address,8),0);if(!Pointer(p))throw new MemoryFault("Ponteiro nulo ou inválido em 0x"+address.ToString("X"));return p;}
  public static string StringAt(this IMemory m,long address){var header=m.Read(address,32);long length=BitConverter.ToInt64(header,16),capacity=BitConverter.ToInt64(header,24);if(length<0||length>256||capacity<length||capacity>1048576)throw new MemoryFault("Layout de string inválido");if(length==0)return "";long start=capacity<16?address:BitConverter.ToInt64(header,0);byte[] bytes=capacity<16?header:m.Read(start,(int)length);string s=new UTF8Encoding(false,true).GetString(bytes,0,(int)length);foreach(char c in s)if(Char.IsControl(c))throw new MemoryFault("String contém controle");return s;}
  public static float FloatAt(this IMemory m,long address){float v=BitConverter.ToSingle(m.Read(address,4),0);if(Single.IsNaN(v)||Single.IsInfinity(v))throw new MemoryFault("Float não finito");return v;}
 }
 public sealed class Target:IDisposable
 {
  public Process Process;public ProcessMemory Memory;public long Base;public string Version,Path;public IntPtr Window;
  public static Target Find(int requestedPid)
  {
   Process selected=null;foreach(var p in System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta")){if(requestedPid>0&&p.Id!=requestedPid){p.Dispose();continue;}if(selected!=null){p.Dispose();selected.Dispose();throw new InvalidOperationException("Há vários clientes. Use --pid ID");}selected=p;}
   if(selected==null)throw new InvalidOperationException("RobloxPlayerBeta não encontrado");
   try{string path=selected.MainModule.FileName;string version=System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));if(!String.Equals(version,Offsets.Info.ClientVersion,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Build incompatível: "+version+"; esperada "+Offsets.Info.ClientVersion);long b=selected.MainModule.BaseAddress.ToInt64();if(selected.MainModule.ModuleMemorySize<=Offsets.FakeDataModel.Pointer+8||selected.MainModule.ModuleMemorySize<=Offsets.VisualEngine.Pointer+8)throw new MemoryFault("RVA fora do módulo");var memory=new ProcessMemory(selected.Id);try{var header=memory.Read(b,2);if(header[0]!=77||header[1]!=90)throw new MemoryFault("Cabeçalho PE inválido");return new Target{Process=selected,Memory=memory,Base=b,Version=version,Path=path,Window=selected.MainWindowHandle};}catch{memory.Dispose();throw;}}
   catch{selected.Dispose();throw;}
  }
  public void Dispose(){Memory.Dispose();Process.Dispose();}
 }
}
