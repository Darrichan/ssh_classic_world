using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SshScreen {
public sealed class LieVisionTarget {
    public readonly Point Point;
    public readonly float Confidence;
    public LieVisionTarget(Point point,float confidence){Point=point;Confidence=confidence;}
}

sealed class LocalTargetFrame {
    public byte[] Pixels;
    public int Width,Height,OriginX,OriginY;
}

public sealed class LocalTargetTrackClient:IDisposable {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string file,IntPtr handle,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Init(int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int SubmitAt(IntPtr bmp,int bytes,long timestampMs,out int x,out int y);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int IntCall();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int TextCall([Out]byte[] output,int capacity);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int TimingCall(out int bmpUs,out int temporalUs,out int modelUs,out int trackerUs);

    readonly string runtime;
    readonly object gate=new object(),rateGate=new object();readonly AutoResetEvent ready=new AutoResetEvent(false);readonly Stack<byte[]> buffers=new Stack<byte[]>();
    LocalTargetFrame latest;Thread worker;volatile bool enabled;volatile string state="已关闭";long nextInitTick;
    IntPtr module;Init initialize;SubmitAt submit;VoidCall shutdown,reset;IntCall backend,version;TextCall error,resultJson,device;TimingCall timings;bool initialized;
    byte[] bmp;long submitted,processed,targets,dropped;long rateAt=Ticks(),lastSubmitted,lastProcessed;double submitFps,processFps;int lastModelUs,lastTrackerUs;string backendText="";
    public Action<LieVisionTarget> Target;public Action<string> StateChanged;
    public bool Enabled{get{return enabled;}}
    public LocalTargetTrackClient(string root){runtime=Path.Combine(root,"lie-runtime");}
    static long Ticks(){return System.Diagnostics.Stopwatch.GetTimestamp();}
    void SetState(string value){if(state==value)return;state=value;var changed=StateChanged;if(changed!=null)changed(value);}

    public void Start(){if(enabled)return;enabled=true;nextInitTick=0;SetState("正在加载本地识别");worker=new Thread(Loop){IsBackground=true,Name="LocalTargetTrack"};worker.Start();}
    public void Stop(){if(!enabled){SetState("已关闭");return;}enabled=false;ready.Set();var active=worker;if(active!=null&&active!=Thread.CurrentThread)active.Join(5000);worker=null;lock(gate){if(latest!=null){ReturnBuffer(latest.Pixels);latest=null;}}SetState("已关闭");}
    public byte[] Rent(int length){if(!enabled||length<3)return null;lock(gate){while(buffers.Count>0){var value=buffers.Pop();if(value.Length==length)return value;}return new byte[length];}}
    public void Release(byte[] pixels){if(pixels!=null)ReturnBuffer(pixels);}
    void ReturnBuffer(byte[] pixels){if(pixels==null)return;lock(gate){if(buffers.Count<3)buffers.Push(pixels);}}
    public void Submit(byte[] pixels,int width,int height,int originX,int originY){
        if(pixels==null)return;if(!enabled){ReturnBuffer(pixels);return;}
        var frame=new LocalTargetFrame{Pixels=pixels,Width=width,Height=height,OriginX=originX,OriginY=originY};
        lock(gate){if(latest!=null){ReturnBuffer(latest.Pixels);Interlocked.Increment(ref dropped);}latest=frame;}Interlocked.Increment(ref submitted);ready.Set();
    }
    void Loop(){
        try{
            while(enabled){
                ready.WaitOne(200);if(!enabled)break;LocalTargetFrame frame=null;lock(gate){frame=latest;latest=null;}if(frame==null)continue;
                try{
                    if(!EnsureInitialized()){ReturnBuffer(frame.Pixels);continue;}
                    BuildBmp(frame);int x,y,status;long timestamp=(long)(Ticks()*1000.0/System.Diagnostics.Stopwatch.Frequency);
                    unsafe{fixed(byte* pointer=bmp)status=submit((IntPtr)pointer,bmp.Length,timestamp,out x,out y);}
                    Interlocked.Increment(ref processed);int bmpUs,temporalUs,modelUs,trackerUs;if(timings(out bmpUs,out temporalUs,out modelUs,out trackerUs)==1){lastModelUs=modelUs;lastTrackerUs=trackerUs;}
                    if(status<0)throw new IOException(ReadText(error,"本地识别失败"));
                    if(status==1){float confidence=ReadConfidence();Interlocked.Increment(ref targets);var notify=Target;if(notify!=null)notify(new LieVisionTarget(new Point(frame.OriginX+x,frame.OriginY+y),confidence));}
                }catch(Exception e){SetState("本地识别失败："+e.Message);ResetNative();Interlocked.Exchange(ref nextInitTick,Ticks()+System.Diagnostics.Stopwatch.Frequency);}
                finally{ReturnBuffer(frame.Pixels);}
            }
        }finally{CloseNative();}
    }
    bool EnsureInitialized(){
        if(initialized)return true;long now=Ticks();if(now<Interlocked.Read(ref nextInitTick))return false;
        Interlocked.Exchange(ref nextInitTick,now+System.Diagnostics.Stopwatch.Frequency);
        try{
            string dll=Path.Combine(runtime,"TargetTrack.dll"),model=Path.Combine(runtime,"model","runicH3.onnx");
            if(!File.Exists(dll)||!File.Exists(model))throw new FileNotFoundException("缺少 lie-runtime/TargetTrack.dll 或 model/runicH3.onnx");
            module=LoadLibraryEx(dll,IntPtr.Zero,8);if(module==IntPtr.Zero)throw new IOException("TargetTrack.dll 加载失败，Win32="+Marshal.GetLastWin32Error());
            initialize=Load<Init>("TT_Init");submit=Load<SubmitAt>("TT_SubmitFrameBmpAt");shutdown=Load<VoidCall>("TT_Shutdown");reset=Load<VoidCall>("TT_ResetDetection");backend=Load<IntCall>("TT_GetBackend");version=Load<IntCall>("TT_GetVersion");error=Load<TextCall>("TT_GetLastError");resultJson=Load<TextCall>("TT_GetLastResultJson");device=Load<TextCall>("TT_GetDeviceName");timings=Load<TimingCall>("TT_GetLastTimings");
            if(version()!=202610025)throw new IOException("TargetTrack DLL版本不匹配");int status=initialize(2);if(status!=1)throw new IOException(ReadText(error,"TargetTrack初始化失败"));
            initialized=true;backendText=(backend()==1?"DirectML":"CPU")+" "+ReadText(device,"");SetState("本地识别已就绪 · "+backendText);return true;
        }catch(Exception e){CloseNative();SetState("本地识别加载失败："+e.Message);return false;}
    }
    T Load<T>(string name)where T:class{IntPtr address=GetProcAddress(module,name);if(address==IntPtr.Zero)throw new MissingMethodException(name);return Marshal.GetDelegateForFunctionPointer(address,typeof(T))as T;}
    string ReadText(TextCall call,string fallback){if(call==null)return fallback;var bytes=new byte[65536];int needed=call(bytes,bytes.Length);if(needed<=1)return fallback;int length=Array.IndexOf(bytes,(byte)0);if(length<0)length=bytes.Length;return Encoding.UTF8.GetString(bytes,0,length);}
    float ReadConfidence(){try{string json=ReadText(resultJson,"{}");var map=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);object value;if(map!=null&&map.TryGetValue("confidence",out value)&&value!=null)return Convert.ToSingle(value);}catch{}return 0;}
    void ResetNative(){if(initialized&&reset!=null)try{reset();}catch{}}
    void CloseNative(){
        if(initialized&&shutdown!=null)try{shutdown();}catch{}initialized=false;
        initialize=null;submit=null;shutdown=null;reset=null;backend=null;version=null;error=null;resultJson=null;device=null;timings=null;backendText="";
        if(module!=IntPtr.Zero){try{FreeLibrary(module);}catch{}module=IntPtr.Zero;}
    }
    void BuildBmp(LocalTargetFrame frame){
        int pitch=(frame.Width*3+3)&~3,pixelBytes=checked(pitch*frame.Height),total=54+pixelBytes;if(bmp==null||bmp.Length!=total)bmp=new byte[total];else Array.Clear(bmp,0,54);
        bmp[0]=(byte)'B';bmp[1]=(byte)'M';W32(bmp,2,total);W32(bmp,10,54);W32(bmp,14,40);W32(bmp,18,frame.Width);W32(bmp,22,-frame.Height);W16(bmp,26,1);W16(bmp,28,24);W32(bmp,34,pixelBytes);
        int row=frame.Width*3;for(int y=0;y<frame.Height;y++){int destination=54+y*pitch;Buffer.BlockCopy(frame.Pixels,y*row,bmp,destination,row);for(int p=row;p<pitch;p++)bmp[destination+p]=0;}
    }
    static void W16(byte[] value,int offset,int number){value[offset]=(byte)number;value[offset+1]=(byte)(number>>8);}
    static void W32(byte[] value,int offset,int number){uint v=unchecked((uint)number);for(int i=0;i<4;i++)value[offset+i]=(byte)(v>>(i*8));}
    public string Metrics(){
        lock(rateGate){long now=Ticks();double seconds=(now-rateAt)/(double)System.Diagnostics.Stopwatch.Frequency;if(seconds>=1){long s=Interlocked.Read(ref submitted),p=Interlocked.Read(ref processed);submitFps=(s-lastSubmitted)/seconds;processFps=(p-lastProcessed)/seconds;lastSubmitted=s;lastProcessed=p;rateAt=now;}return String.Format("{0} · 入{1:F1}/算{2:F1} FPS · 模型{3:F1}ms/跟踪{4:F1}ms · 坐标{5} · 覆盖{6}",state,submitFps,processFps,lastModelUs/1000.0,lastTrackerUs/1000.0,Interlocked.Read(ref targets),Interlocked.Read(ref dropped));}
    }
    public void Dispose(){Stop();ready.Close();}
}
}
