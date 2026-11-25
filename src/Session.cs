using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace SshScreen {
public sealed class ProcessJob:IDisposable {
    [StructLayout(LayoutKind.Sequential)]struct BASIC{public long perProcess,perJob;public uint flags;public UIntPtr min,max;public uint limit;public UIntPtr affinity;public uint priority,scheduling;}
    [StructLayout(LayoutKind.Sequential)]struct IO{public ulong a,b,c,d,e,f;}
    [StructLayout(LayoutKind.Sequential)]struct EXTENDED{public BASIC basic;public IO io;public UIntPtr a,b,c,d;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr job,int type,ref EXTENDED data,uint length);
    [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
    IntPtr handle;
    public ProcessJob(Process process){handle=CreateJobObject(IntPtr.Zero,null);var limits=new EXTENDED();limits.basic.flags=0x2000;if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(EXTENDED)))||!AssignProcessToJobObject(handle,process.Handle)){Dispose();try{process.Kill();}catch{}throw new IOException("无法建立SSH进程退出保护");}}
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
public sealed class RemoteRequestException:IOException {public RemoteRequestException(string message):base(message){}}
public sealed class Link:IDisposable {
    public TcpClient Client;public NetworkStream Stream;
    public Link(int port,string token,string role){
        Client=new TcpClient();
        try{
            Client.NoDelay=true;Client.ReceiveBufferSize=65536;Client.ReceiveTimeout=5000;Client.SendTimeout=3000;
            Client.Connect(IPAddress.Loopback,port);
            Stream=Client.GetStream();
            Call(Wire.Map("op","hello","version",Wire.Version,"token",token,"role",role));
        }catch{Dispose();throw;}
    }
    public Packet Call(Dictionary<string,object> request){Wire.Send(Stream,request);return Receive();}
    public Packet Receive(){var p=Wire.Receive(Stream);if(!Wire.Bool(p.Header,"ok"))throw new RemoteRequestException(Wire.Str(p.Header,"error","远端请求失败"));return p;}
    public void Dispose(){var client=Interlocked.Exchange(ref Client,null);if(client!=null)client.Close();}
}
public sealed class SshDestination {
    public bool custom;
    public string host="",user="";
    public int port;
    public string Label(){return custom?(String.IsNullOrWhiteSpace(host)?"自定义主机":host):"电脑1";}
    public string Identity(){return custom?host.ToLowerInvariant()+"|"+port+"|"+user:"";}
    public void Validate(){
        if(!custom)return;
        IPAddress address;
        if(String.IsNullOrWhiteSpace(host)||
            !Regex.IsMatch(host,@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,252}\z")&&
            !(host.Contains(":")&&Regex.IsMatch(host,@"\A[0-9A-Fa-f:.]+\z")&&IPAddress.TryParse(host,out address)))
            throw new IOException("请填写有效的主机名或 IP 地址");
        if(!String.IsNullOrEmpty(user)&&!Regex.IsMatch(user,@"\A[A-Za-z0-9_][A-Za-z0-9._-]{0,63}\z"))
            throw new IOException("SSH 用户名只能使用字母、数字、点、下划线或连字符");
        if(port<0||port>65535)throw new IOException("请填写 1 到 65535 的 SSH 端口，或留空使用默认配置");
    }
    internal string SshOptions(string root){
        Validate();
        if(!custom)return "-F \""+Path.Combine(root,"ssh_config")+"\"";
        return "-o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=6 -o ServerAliveInterval=2 -o ServerAliveCountMax=2"+
            (port>0?" -p "+port:"")+(String.IsNullOrEmpty(user)?"":" -l "+user);
    }
    internal string SshHost(){return custom?host:"maple-screen-computer1";}
}
public sealed class Session:IDisposable {
    readonly string root;Process process;ProcessJob job;int disposeStarted;readonly ManualResetEvent disposeDone=new ManualResetEvent(false);readonly object writer=new object();readonly ManualResetEvent ready=new ManualResetEvent(false),finished=new ManualResetEvent(false);
    public readonly string Id=Guid.NewGuid().ToString("N");public readonly string Token;public int Port;public Dictionary<string,object> Ready,Cleanup;string error;
    readonly SshDestination destination;
    public Session(string directory,SshDestination target=null){root=directory;destination=target??new SshDestination();byte[] secret=new byte[32];using(var rng=System.Security.Cryptography.RandomNumberGenerator.Create())rng.GetBytes(secret);Token=Convert.ToBase64String(secret);}
    public void Start(bool test,bool mouseParkProbe=false,Func<bool> cancelled=null){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        string bootstrap=Convert.ToBase64String(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,"bootstrap.ps1"),Encoding.UTF8)));
        string loader="[Console]::InputEncoding=New-Object Text.UTF8Encoding($false);$code=[Console]::ReadLine();& ([scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($code))))";
        string encodedLoader=Convert.ToBase64String(Encoding.Unicode.GetBytes(loader));
        string ssh=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","OpenSSH","ssh.exe");
        var start=new ProcessStartInfo{FileName=ssh,Arguments=destination.SshOptions(root)+" -T -o ExitOnForwardFailure=yes -L 127.0.0.1:"+Port+":127.0.0.1:47191 "+destination.SshHost()+" powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "+encodedLoader,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        process=Process.Start(start);job=new ProcessJob(process);process.OutputDataReceived+=OnOutput;process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs args){if(!String.IsNullOrWhiteSpace(args.Data)){error="SSH连接或临时进程启动失败，请查看ssh-error.log";File.AppendAllText(Path.Combine(root,"ssh-error.log"),DateTime.UtcNow.ToString("o")+" "+args.Data+Environment.NewLine);}};process.BeginOutputReadLine();process.BeginErrorReadLine();
        var payload=Wire.Map("id",Id,"token",Token,"common",File.ReadAllText(Path.Combine(root,"src","Common.cs"),Encoding.UTF8),"agent",File.ReadAllText(Path.Combine(root,"src","Agent.cs"),Encoding.UTF8),"test",test,"mouseParkProbe",mouseParkProbe);
        if(test)payload["testWindow"]=File.ReadAllText(Path.Combine(root,"src","TestWindow.cs"),Encoding.UTF8);
        process.StandardInput.WriteLine(bootstrap);process.StandardInput.WriteLine(AsciiJson(new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(payload)));process.StandardInput.Flush();
        var deadline=DateTime.UtcNow.AddSeconds(35);while(!ready.WaitOne(100)){if(cancelled!=null&&cancelled())throw new OperationCanceledException();if(process.HasExited)throw new IOException(error??"SSH会话启动失败");if(DateTime.UtcNow>deadline)throw new IOException("临时采集进程启动超时");}
        if(Ready==null||!Wire.Bool(Ready,"ready"))throw new IOException(Ready==null?(error??"临时会话不可用"):Wire.Str(Ready,"error","启动失败"));
    }
    static string AsciiJson(string value){var ascii=new StringBuilder(value.Length);foreach(char c in value){if(c>127)ascii.Append("\\u").Append(((int)c).ToString("x4"));else ascii.Append(c);}return ascii.ToString();}
    void OnOutput(object sender,DataReceivedEventArgs args){if(String.IsNullOrWhiteSpace(args.Data))return;try{var message=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(args.Data);if(message.ContainsKey("ready")){Ready=message;Wire.Save(Path.Combine(root,"last-ready.json"),message);ready.Set();}if(Wire.Bool(message,"cleanup")){Cleanup=message;Wire.Save(Path.Combine(root,"last-cleanup.json"),message);finished.Set();}}catch{}}
    public void Dispose(){if(Interlocked.Exchange(ref disposeStarted,1)!=0){disposeDone.WaitOne(8500);return;}try{if(process!=null){try{lock(writer){process.StandardInput.Close();}finished.WaitOne(6500);if(!process.WaitForExit(1000))process.Kill();}catch{}if(job!=null)job.Dispose();process.Dispose();process=null;}}finally{disposeDone.Set();}}

}
public delegate bool FrameRegionSelector(long window,int width,int height,out Rectangle area);
sealed class FrameUnavailableException:IOException {
    public FrameUnavailableException(string message):base(message){}
}
public sealed class FrameDecoder {
    readonly object gate=new object();byte[] previous,work;long sequence,window=-1,decodedTick;int width,height;string sha,capturedUtc;Dictionary<string,object> routeInput;bool cursorInGame;int[] cursorSamples;
    Dictionary<string,object> SnapshotInfo(){return Wire.Map("width",width,"height",height,"native_width",width,"native_height",height,"sequence",sequence,"window",window,"sha256",sha,"age_ms",(Stopwatch.GetTimestamp()-decodedTick)*1000.0/Stopwatch.Frequency,"captured_utc",capturedUtc,"route_input",routeInput,"cursor_in_game",cursorInGame,"cursor_samples",cursorSamples);}
    public Dictionary<string,object> Information(){lock(gate){if(previous==null)throw new FrameUnavailableException("尚未收到原图");return SnapshotInfo();}}
    public Dictionary<string,object> NativeSnapshot(){lock(gate){if(previous==null)throw new FrameUnavailableException("尚未收到完整原图");var f=SnapshotInfo();f["bgr24"]=(byte[])previous.Clone();return f;}}
    public Dictionary<string,object> NativeRegion(FrameRegionSelector select){
        lock(gate){
            if(previous==null)throw new FrameUnavailableException("尚未收到完整原图");
            Rectangle region;
            return select(window,width,height,out region)?NativeRegion(region):null;
        }
    }
    public Dictionary<string,object> NativeRegion(Rectangle region){lock(gate){if(previous==null)throw new IOException("尚未收到完整原图");if(region.Width<1||region.Height<1||region.Width>1024||region.Height>800||!new Rectangle(0,0,width,height).Contains(region))throw new IOException("小地图区域无效");var pixels=new byte[region.Width*region.Height*3];for(int y=0;y<region.Height;y++)Buffer.BlockCopy(previous,((y+region.Y)*width+region.X)*3,pixels,y*region.Width*3,region.Width*3);var f=SnapshotInfo();f["width"]=region.Width;f["height"]=region.Height;f["minimap_roi"]=new[]{region.X,region.Y,region.Width,region.Height};f["bgr24"]=pixels;return f;}}
    public Bitmap Decode(Packet packet,int previewWidth=0){lock(gate){var h=packet.Header;int w=Wire.Int(h,"width"),he=Wire.Int(h,"height"),length=Wire.PixelLength(w,he);string encoding=Wire.Str(h,"encoding");if(work==null||work.Length!=length)work=new byte[length];
        if(encoding=="bgr_deflate")Wire.InflateInto(packet.Data,work);
        else if(encoding=="xor_deflate"||encoding=="tile_deflate"||encoding=="repeat"){if(previous==null||sequence!=Wire.Long(h,"base_sequence")||window!=Wire.Long(h,"window")||width!=w||height!=he)throw new InvalidDataException("差分基础帧失配");if(encoding=="xor_deflate"){Wire.InflateInto(packet.Data,work);Wire.Xor(work,previous,work);}else{Buffer.BlockCopy(previous,0,work,0,work.Length);if(encoding=="tile_deflate"){int tile=Wire.Int(h,"tile_size");if(tile!=64)throw new InvalidDataException("块尺寸不符");int[] tiles=Wire.IntArray(h["tiles"]);byte[] packed=Wire.Inflate(packet.Data,Wire.TileBytes(tiles,w,he,tile));Wire.ApplyTiles(work,packed,tiles,w,he,tile);}}}
        else throw new InvalidDataException("协议拒绝有损或未知格式");
        if((encoding=="repeat"?sha:Wire.Hash(work))!=Wire.Str(h,"raw_sha256"))throw new InvalidDataException("画面像素校验失败");
        var image=previewWidth>0&&w>previewWidth?PreviewBitmap(work,w,he,previewWidth):Wire.BitmapFrom(work,w,he);
        object samples;
        cursorSamples=h.TryGetValue("cursor_samples",out samples)&&samples!=null?Wire.IntArray(samples):null;
        byte[] swap=previous;previous=work;work=swap;sequence=Wire.Long(h,"sequence");window=Wire.Long(h,"window");width=w;height=he;sha=Wire.Str(h,"raw_sha256");capturedUtc=Wire.Str(h,"captured_utc");routeInput=h.ContainsKey("route_input")?h["route_input"] as Dictionary<string,object>:null;cursorInGame=Wire.Bool(h,"cursor_in_game");decodedTick=Stopwatch.GetTimestamp();return image;
    }}
    static unsafe Bitmap PreviewBitmap(byte[] pixels,int width,int height,int previewWidth){
        int previewHeight=Math.Max(1,(int)Math.Round(height*(double)previewWidth/width));
        var format=System.Drawing.Imaging.PixelFormat.Format24bppRgb;
        var bitmap=new Bitmap(previewWidth,previewHeight,format);
        try{
            var columns=new int[previewWidth];
            for(int x=0;x<columns.Length;x++)columns[x]=(int)((x+.5)*width/previewWidth)*3;
            var area=new Rectangle(0,0,previewWidth,previewHeight);
            var locked=bitmap.LockBits(area,System.Drawing.Imaging.ImageLockMode.WriteOnly,format);
            try{
                fixed(byte* source=pixels){
                    for(int y=0;y<previewHeight;y++){
                        int sourceY=(int)((y+.5)*height/previewHeight);
                        byte* sourceRow=source+sourceY*width*3;
                        byte* target=(byte*)locked.Scan0+y*locked.Stride;
                        foreach(int column in columns){
                            target[0]=sourceRow[column];target[1]=sourceRow[column+1];target[2]=sourceRow[column+2];
                            target+=3;
                        }
                    }
                }
            }finally{bitmap.UnlockBits(locked);}
            return bitmap;
        }catch{bitmap.Dispose();throw;}
    }
    public void Reset(){lock(gate){previous=work=null;sequence=0;window=-1;}}
}
}
