using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SshScreen {
static class Win {
    [StructLayout(LayoutKind.Sequential)]public struct RECT{public int L,T,R,B;}
    [StructLayout(LayoutKind.Sequential)]public struct POINT{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]public struct MSG{public IntPtr h;public uint message;public UIntPtr w;public IntPtr l;public uint time;public POINT point;public uint extra;}
    [StructLayout(LayoutKind.Sequential)]public struct MOUSE{public int x,y;public uint data,flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)]public struct KEY{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Explicit)]public struct UNION{[FieldOffset(0)]public MOUSE mouse;[FieldOffset(0)]public KEY key;}
    [StructLayout(LayoutKind.Sequential)]public struct INPUT{public uint type;public UNION value;}
    [StructLayout(LayoutKind.Sequential)]public struct INFO{public uint size;public int width,height;public ushort planes,bits;public uint compression,image;public int x,y;public uint used,important;}
    [StructLayout(LayoutKind.Sequential)]public struct MOUSEHOOK{public POINT point;public uint data,flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)]public struct KEYHOOK{public uint vk,scan,flags,time;public UIntPtr extra;}
    public delegate IntPtr HookProc(int code,IntPtr w,IntPtr l);
    public static readonly UIntPtr RemoteMouseMarker=new UIntPtr(0x53534832u);
    public static long RemoteMoveCount;
    public delegate bool EnumProc(IntPtr h,IntPtr data);
    [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc proc,IntPtr data);
    [DllImport("user32.dll")]public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
    [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]public static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]public static extern bool ClientToScreen(IntPtr h,ref POINT p);
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]public static extern bool AttachThreadInput(uint a,uint b,bool attach);
    [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]public static extern bool PeekMessage(out MSG msg,IntPtr h,uint a,uint b,uint remove);
    [DllImport("user32.dll")]public static extern int GetMessage(out MSG msg,IntPtr h,uint a,uint b);
    [DllImport("user32.dll")]public static extern bool PostThreadMessage(uint thread,uint message,UIntPtr w,IntPtr l);
    [DllImport("user32.dll")]public static extern IntPtr SetWindowsHookEx(int kind,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")]public static extern uint MapVirtualKey(uint code,uint type);
    [DllImport("user32.dll")]public static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
    [DllImport("winmm.dll")]public static extern uint timeBeginPeriod(uint p);
    [DllImport("winmm.dll")]public static extern uint timeEndPeriod(uint p);
    [DllImport("user32.dll")]public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")]public static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")]public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]public static extern IntPtr CreateDIBSection(IntPtr dc,ref INFO info,uint use,out IntPtr pixels,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]public static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]public static extern bool BitBlt(IntPtr target,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint mode);
    [DllImport("gdi32.dll")]public static extern bool GdiFlush();
    public static void MoveRemote(int x,int y){
        Rectangle area=SystemInformation.VirtualScreen;if(!area.Contains(x,y))throw new IOException("鼠标坐标超出桌面");
        var v=new INPUT();v.value.mouse.x=(int)Math.Min(65535,((long)(x-area.X)*65536+32768)/area.Width);v.value.mouse.y=(int)Math.Min(65535,((long)(y-area.Y)*65536+32768)/area.Height);v.value.mouse.flags=0xc001;v.value.mouse.extra=RemoteMouseMarker;
        if(SendInput(1,new[]{v},Marshal.SizeOf(typeof(INPUT)))!=1)throw new IOException("鼠标定位失败");Interlocked.Increment(ref RemoteMoveCount);
        POINT actual;for(int i=0;i<10;i++){GetCursorPos(out actual);if(actual.X==x&&actual.Y==y)return;Thread.Sleep(1);}throw new IOException("鼠标定位校验失败");
    }
    public static Rectangle Bounds(long window) {
        if(window==0)return Screen.PrimaryScreen.Bounds;
        IntPtr h=new IntPtr(window);if(!IsWindow(h)||!IsWindowVisible(h)||IsIconic(h))throw new IOException("目标窗口已关闭或最小化");RECT r;POINT p=new POINT();
        if(!GetClientRect(h,out r)||!ClientToScreen(h,ref p))throw new IOException("无法取得窗口客户区");var area=new Rectangle(p.X,p.Y,r.R-r.L,r.B-r.T);
        if(area.Width<1||area.Height<1||!SystemInformation.VirtualScreen.Contains(area))throw new IOException("请将目标窗口完整放在屏幕内");return area;
    }
    public static bool Activate(IntPtr h) {
        if(GetForegroundWindow()==h)return true;MSG msg;PeekMessage(out msg,IntPtr.Zero,0,0,0);uint ignored,current=GetCurrentThreadId(),fg=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);bool joined=false;
        try{if(fg!=0&&fg!=current)joined=AttachThreadInput(current,fg,true);SetForegroundWindow(h);}finally{if(joined)AttachThreadInput(current,fg,false);}Thread.Sleep(50);return GetForegroundWindow()==h;
    }
    public static object[] Windows() {
        var list=new List<object>();EnumWindows(delegate(IntPtr h,IntPtr unused){if(!IsWindowVisible(h)||IsIconic(h))return true;var name=new StringBuilder(512);GetWindowText(h,name,name.Capacity);if(name.Length>0){uint pid;GetWindowThreadProcessId(h,out pid);list.Add(Wire.Map("handle",h.ToInt64(),"pid",pid,"title",name.ToString()));}return true;},IntPtr.Zero);return list.ToArray();
    }
}
sealed class LocalMouseMonitor:IDisposable {
    static long generation;public static long Generation{get{return Interlocked.Read(ref generation);}}
    readonly ManualResetEvent ready=new ManualResetEvent(false);Thread thread;uint threadId;IntPtr hook,keyHook;Win.HookProc callback,keyCallback;string error;Win.POINT previous;
    public void Start(){thread=new Thread(Loop){IsBackground=true};thread.Start();if(!ready.WaitOne(3000)||error!=null)throw new IOException(error??"本机鼠标接管监测启动超时");}
    void Loop(){threadId=Win.GetCurrentThreadId();Win.MSG m;Win.PeekMessage(out m,IntPtr.Zero,0,0,0);Win.GetCursorPos(out previous);callback=Observe;keyCallback=ObserveKey;hook=Win.SetWindowsHookEx(14,callback,Win.GetModuleHandle(null),0);keyHook=Win.SetWindowsHookEx(13,keyCallback,Win.GetModuleHandle(null),0);if(hook==IntPtr.Zero||keyHook==IntPtr.Zero)error="本机输入观察不可用";ready.Set();try{if(error==null)while(Win.GetMessage(out m,IntPtr.Zero,0,0)>0){}}finally{KeyboardTrace.Configure(false,0,new int[0]);if(hook!=IntPtr.Zero)Win.UnhookWindowsHookEx(hook);if(keyHook!=IntPtr.Zero)Win.UnhookWindowsHookEx(keyHook);hook=keyHook=IntPtr.Zero;}}
    IntPtr ObserveKey(int code,IntPtr w,IntPtr l){if(code>=0){int kind=w.ToInt32();if(kind==0x100||kind==0x101||kind==0x104||kind==0x105){var key=(Win.KEYHOOK)Marshal.PtrToStructure(l,typeof(Win.KEYHOOK));KeyboardTrace.Observe((int)key.vk,kind==0x100||kind==0x104);}}return Win.CallNextHookEx(keyHook,code,w,l);}
    IntPtr Observe(int code,IntPtr w,IntPtr l){
        if(code>=0){var e=(Win.MOUSEHOOK)Marshal.PtrToStructure(l,typeof(Win.MOUSEHOOK));int kind=w.ToInt32();bool changed=e.point.X!=previous.X||e.point.Y!=previous.Y;previous=e.point;
            // Never block input. Any mouse activity not produced by this bridge has priority.
            if(e.extra!=Win.RemoteMouseMarker&&(kind!=0x200||changed))Interlocked.Increment(ref generation);
        }return Win.CallNextHookEx(hook,code,w,l);
    }
    public void Dispose(){if(threadId!=0)Win.PostThreadMessage(threadId,0x12,UIntPtr.Zero,IntPtr.Zero);if(thread!=null&&thread!=Thread.CurrentThread)thread.Join(2000);}
}
// Opt-in, finite route-key observation. No text, arbitrary keys, or input suppression.
static class KeyboardTrace {
    static readonly object gate=new object();static bool enabled;static long window,epoch,serial;static uint pid;
    static readonly HashSet<int> allowed=new HashSet<int>(),down=new HashSet<int>();static readonly Queue<Dictionary<string,object>> events=new Queue<Dictionary<string,object>>();
    public static long Configure(bool active,long target,int[] keys){lock(gate){enabled=false;allowed.Clear();down.Clear();events.Clear();epoch++;serial=0;if(!active)return epoch;if(target==0||!Win.IsWindow(new IntPtr(target)))throw new IOException("录制请选择具体游戏窗口");if(keys.Length<4||keys.Length>8)throw new IOException("录制键位数量无效");foreach(int key in keys){if(!(key==16||key==17||key==18||key==32||key>=37&&key<=40||key>=48&&key<=90))throw new IOException("录制仅允许方向、字母、数字或修饰键");allowed.Add(key);}window=target;Win.GetWindowThreadProcessId(new IntPtr(window),out pid);enabled=true;foreach(int key in allowed)if((Win.GetAsyncKeyState(key)&0x8000)!=0)down.Add(key);return epoch;}}
    static int Normalize(int vk){return vk==160||vk==161?16:vk==162||vk==163?17:vk==164||vk==165?18:vk;}
    public static void Observe(int raw,bool pressed){lock(gate){int vk=Normalize(raw);if(!enabled||!allowed.Contains(vk))return;if(!pressed)down.Remove(vk);if(Win.GetForegroundWindow()!=new IntPtr(window)){down.Clear();return;}if(pressed&&!down.Add(vk))return;events.Enqueue(Wire.Map("id",++serial,"time",DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond,"vk",vk,"down",pressed));while(events.Count>256)events.Dequeue();}}
    public static Dictionary<string,object> Snapshot(){lock(gate){if(!enabled)return null;uint now;Win.GetWindowThreadProcessId(new IntPtr(window),out now);bool foreground=now==pid&&Win.IsWindow(new IntPtr(window))&&Win.GetForegroundWindow()==new IntPtr(window);if(!foreground)down.Clear();return Wire.Map("enabled",true,"window",window,"epoch",epoch,"last_event",serial,"foreground",foreground,"down",foreground?down.ToArray():new int[0],"events",events.ToArray());}}
    public static long Target{get{lock(gate)return enabled?window:0;}}
}
sealed class CaptureSurface:IDisposable {
    IntPtr screen,memory,bitmap,old,pixels;int width,height,stride;
    public void Read(Rectangle area,byte[] data){if(area.Width!=width||area.Height!=height){Dispose();width=area.Width;height=area.Height;stride=(width*3+3)&~3;screen=Win.GetDC(IntPtr.Zero);memory=Win.CreateCompatibleDC(screen);var info=new Win.INFO{size=40,width=width,height=-height,planes=1,bits=24};bitmap=Win.CreateDIBSection(screen,ref info,0,out pixels,IntPtr.Zero,0);if(screen==IntPtr.Zero||memory==IntPtr.Zero||bitmap==IntPtr.Zero)throw new IOException("桌面采集不可用");old=Win.SelectObject(memory,bitmap);}
        if(!Win.BitBlt(memory,0,0,width,height,screen,area.X,area.Y,0x00CC0020))throw new IOException("桌面采集失败");Win.GdiFlush();if(stride==width*3)Marshal.Copy(pixels,data,0,data.Length);else for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(pixels,y*stride),data,y*width*3,width*3);
    }
    public void Dispose(){if(old!=IntPtr.Zero&&memory!=IntPtr.Zero)Win.SelectObject(memory,old);if(bitmap!=IntPtr.Zero)Win.DeleteObject(bitmap);if(memory!=IntPtr.Zero)Win.DeleteDC(memory);if(screen!=IntPtr.Zero)Win.ReleaseDC(IntPtr.Zero,screen);screen=memory=bitmap=old=pixels=IntPtr.Zero;width=height=0;}
}
sealed class InputState {
    public readonly object Gate=new object();readonly HashSet<int> keys=new HashSet<int>(),buttons=new HashSet<int>();
    public string Owner,Reason="只观看";public long Window;uint pid;public bool Armed;bool requireTargetFocus;long lastBeat,localGeneration;
    static bool Extended(int vk){return(vk>=0x21&&vk<=0x2e)||vk==0x6f||vk==0xa3||vk==0xa5||vk==0x5b||vk==0x5c;}
    static Win.INPUT Key(int vk,bool up){var v=new Win.INPUT();v.type=1;v.value.key.scan=(ushort)Win.MapVirtualKey((uint)vk,0);v.value.key.flags=8u|(up?2u:0u)|(Extended(vk)?1u:0u);if(v.value.key.scan==0){v.value.key.vk=(ushort)vk;v.value.key.flags=up?2u:0u;}return v;}
    static Win.INPUT Mouse(int b,bool up){var v=new Win.INPUT();v.value.mouse.flags=b==1?(up?4u:2u):b==2?(up?16u:8u):(up?64u:32u);v.value.mouse.extra=Win.RemoteMouseMarker;return v;}
    static void Send(Win.INPUT v){if(Win.SendInput(1,new[]{v},Marshal.SizeOf(typeof(Win.INPUT)))!=1)throw new IOException("Windows未接受输入");}
    public void Arm(string owner,long window,bool focusTarget){
        lock(Gate){
            Release("切换目标");
            if(window!=0){
                IntPtr h=new IntPtr(window);
                if(!Win.IsWindow(h)||Win.IsIconic(h))throw new IOException("目标窗口不可用");
                Win.GetWindowThreadProcessId(h,out pid);
                if(focusTarget)Win.Activate(h);
            }
            Owner=owner;Window=window;requireTargetFocus=focusTarget;
            localGeneration=LocalMouseMonitor.Generation;Armed=true;lastBeat=Stopwatch.GetTimestamp();
            Reason="手动控制中，请先点击目标后输入";
        }
    }
    public void Check(string owner,bool foreground=true){
        if(Armed&&LocalMouseMonitor.Generation!=localGeneration)Release("电脑1本机鼠标已接管，远程输入已暂停");
        if(!Armed||Owner!=owner)throw new IOException(Reason);
        if(Window==0)return;
        uint now;IntPtr h=new IntPtr(Window);Win.GetWindowThreadProcessId(h,out now);
        if(!Win.IsWindow(h)||now!=pid){Release("目标窗口不可用，已松键");throw new IOException(Reason);}
        if(foreground&&requireTargetFocus&&Win.GetForegroundWindow()!=h){Release("目标失去前台，已松键");throw new IOException(Reason);}
    }
    public void Beat(string owner){lock(Gate){Check(owner,false);lastBeat=Stopwatch.GetTimestamp();}}
    public void KeyEvent(string owner,int vk,bool down){lock(Gate){Check(owner);if(vk<8||vk>254||vk==0x7b)throw new IOException("该按键不转发");if(down){if(keys.Add(vk))Send(Key(vk,false));}else if(keys.Contains(vk)){Send(Key(vk,true));keys.Remove(vk);}}}
    public void Text(string owner,string text){lock(Gate){Check(owner);if(text.Length>1024)throw new IOException("单次文字过长");foreach(char c in text){var v=new Win.INPUT();v.type=1;v.value.key.scan=c;v.value.key.flags=4;Send(v);v.value.key.flags=6;Send(v);}}}
    public Dictionary<string,object> MouseEvent(string owner,Dictionary<string,object> request){lock(Gate){Check(owner,false);if(Window!=Wire.Long(request,"window"))throw new IOException("画面与控制目标不一致");Rectangle area=Win.Bounds(Window);int x=Wire.Int(request,"x"),y=Wire.Int(request,"y");if(Wire.Int(request,"width")!=area.Width||Wire.Int(request,"height")!=area.Height)throw new IOException("窗口尺寸已改变，请等待新画面");if(x<0||y<0||x>=area.Width||y>=area.Height)throw new IOException("坐标超出客户区");string op=Wire.Str(request,"op");var point=new Win.POINT{X=area.X+x,Y=area.Y+y};
        Win.MoveRemote(point.X,point.Y);Check(owner,false);Win.POINT actual;Win.GetCursorPos(out actual);if(actual.X!=point.X||actual.Y!=point.Y)throw new IOException("鼠标定位校验失败");
        int b=Wire.Int(request,"button");if(op=="mouse_down"){if(b<1||b>3)throw new IOException("鼠标按键无效");buttons.Add(b);Send(Mouse(b,false));}else if(op=="mouse_up"){if(buttons.Contains(b)){Send(Mouse(b,true));buttons.Remove(b);}Runtime.ParkCursor();}else if(op=="wheel"){var v=new Win.INPUT();v.value.mouse.flags=0x800;v.value.mouse.extra=Win.RemoteMouseMarker;v.value.mouse.data=unchecked((uint)Math.Max(-1200,Math.Min(1200,Wire.Int(request,"delta"))));Send(v);Runtime.ParkCursor();}
        return Wire.Map("ok",true,"cursor_x",actual.X,"cursor_y",actual.Y,"client_x",actual.X-area.X,"client_y",actual.Y-area.Y);
    }}
    public void Release(string reason){lock(Gate){foreach(int k in keys.ToArray())try{Send(Key(k,true));keys.Remove(k);}catch{}foreach(int b in buttons.ToArray())try{Send(Mouse(b,true));buttons.Remove(b);}catch{}Armed=false;Owner=null;Reason=reason;}}
    public bool Dragging{get{lock(Gate)return buttons.Count>0;}}
    public int Held{get{lock(Gate)return keys.Count+buttons.Count;}}
    public void Watch(){lock(Gate){if(Armed&&LocalMouseMonitor.Generation!=localGeneration)Release("电脑1本机鼠标已接管，远程输入已暂停");if(Armed&&(Stopwatch.GetTimestamp()-lastBeat)/(double)Stopwatch.Frequency>1.2)Release("控制心跳超时，已松键");if(!Armed&&Held>0)Release(Reason);}}
}
public sealed class Runtime {
    readonly string root,token;readonly int port;readonly InputState input=new InputState();readonly ManualResetEvent done=new ManualResetEvent(false);readonly List<TcpClient> clients=new List<TcpClient>();
    TcpListener listener;volatile bool running=true,everConnected;long lastControl,started,diskBeat;int controlCount,frameCount;long total;static List<Rectangle> games=new List<Rectangle>();static readonly object gamesGate=new object();
    Runtime(string directory){root=directory;var cfg=Wire.Load(Path.Combine(root,"agent.json"));token=Wire.Str(cfg,"token");port=Wire.Int(cfg,"port",47191);started=Stopwatch.GetTimestamp();lastControl=started;}
    static void RefreshGames(){var list=new List<Rectangle>();Win.EnumWindows(delegate(IntPtr h,IntPtr unused){if(Win.IsWindowVisible(h)&&!Win.IsIconic(h)){var name=new StringBuilder(512);Win.GetWindowText(h,name,name.Capacity);if(Regex.IsMatch(name.ToString(),"Maple|Artale|冒险|冒險|新枫|新楓",RegexOptions.IgnoreCase)){Win.RECT r;if(Win.GetWindowRect(h,out r))list.Add(Rectangle.FromLTRB(r.L,r.T,r.R,r.B));}}return true;},IntPtr.Zero);lock(gamesGate)games=list;}
    public static void ParkCursor(){Win.POINT current;Win.GetCursorPos(out current);lock(gamesGate){if(!games.Any(r=>r.Contains(current.X,current.Y)))return;foreach(var screen in Screen.AllScreens){var b=screen.Bounds;foreach(var p in new[]{new Point(b.Left+2,b.Bottom-2),new Point(b.Right-2,b.Bottom-2),new Point(b.Left+2,b.Top+2),new Point(b.Right-2,b.Top+2)})if(!games.Any(r=>r.Contains(p))){Win.MoveRemote(p.X,p.Y);return;}}}throw new IOException("游戏全屏，缺少可靠窗外鼠标位置，请使用窗口模式");}
    static bool CursorInWindow(long window,Win.POINT cursor){
        Win.RECT bounds;
        return window!=0&&Win.GetWindowRect(new IntPtr(window),out bounds)&&
            Rectangle.FromLTRB(bounds.L,bounds.T,bounds.R,bounds.B).Contains(cursor.X,cursor.Y);
    }
    static bool CursorInGame(long window,long watched,Win.POINT cursor){
        if(CursorInWindow(window,cursor)||watched!=window&&CursorInWindow(watched,cursor))return true;
        lock(gamesGate)return games.Any(area=>area.Contains(cursor.X,cursor.Y));
    }
    internal static Point? RecordingParkingPoint(Point current,Rectangle[] screens,Rectangle[] windows){
        if(screens.Any(screen=>screen.Contains(current))&&!windows.Any(window=>window.Contains(current)))return current;
        var blocked=windows.Select(window=>Rectangle.Inflate(window,2,2)).ToArray();
        Point? best=null;double distance=Double.PositiveInfinity;
        foreach(var screen in screens){
            var columns=blocked.SelectMany(window=>new[]{window.Left-1,window.Right})
                .Concat(new[]{screen.Left+2,screen.Right-3,screen.Left+screen.Width/2,current.X});
            var rows=blocked.SelectMany(window=>new[]{window.Top-1,window.Bottom})
                .Concat(new[]{screen.Top+2,screen.Bottom-3,screen.Top+screen.Height/2,current.Y});
            foreach(int x in columns.Where(x=>x>=screen.Left&&x<screen.Right).Distinct())
                foreach(int y in rows.Where(y=>y>=screen.Top&&y<screen.Bottom).Distinct()){
                    var point=new Point(x,y);
                    if(blocked.Any(window=>window.Contains(point)))continue;
                    double dx=(double)x-current.X,dy=(double)y-current.Y,cost=dx*dx+dy*dy;
                    if(cost<distance){distance=cost;best=point;}
                }
        }
        return best;
    }
    static void PrepareRecording(long target){
        IntPtr window=new IntPtr(target);
        Win.RECT rect;
        if(target==0||!Win.IsWindow(window)||!Win.IsWindowVisible(window)||Win.IsIconic(window)||!Win.GetWindowRect(window,out rect))
            throw new IOException("请选择可见的游戏窗口");
        var targetArea=Rectangle.FromLTRB(rect.L,rect.T,rect.R,rect.B);
        RefreshGames();
        Rectangle[] blocked;
        lock(gamesGate)blocked=games.Concat(new[]{targetArea}).Distinct().ToArray();
        Win.POINT cursor;
        if(!Win.GetCursorPos(out cursor))throw new IOException("无法读取电脑1的鼠标位置");
        var current=new Point(cursor.X,cursor.Y);
        var point=RecordingParkingPoint(current,Screen.AllScreens.Select(screen=>screen.Bounds).ToArray(),blocked);
        if(!point.HasValue)throw new IOException("没有可用的窗外位置，请将游戏切换为窗口模式");
        if(point.Value!=current){
            try{Win.MoveRemote(point.Value.X,point.Value.Y);}
            catch(IOException){throw new IOException("无法自动移出电脑1鼠标，请确认游戏未锁定鼠标");}
        }
        if(!Win.GetCursorPos(out cursor)||blocked.Any(area=>area.Contains(cursor.X,cursor.Y)))
            throw new IOException("电脑1鼠标未停在窗外，请确认游戏未锁定鼠标");
    }
    void Stop(){lock(clients){if(!running)return;running=false;KeyboardTrace.Configure(false,0,new int[0]);input.Release("会话结束，已松键");if(listener!=null)listener.Stop();foreach(var c in clients.ToArray())c.Close();done.Set();}}
    void Accept(){while(running)try{var c=listener.AcceptTcpClient();c.NoDelay=true;c.ReceiveTimeout=4000;c.SendTimeout=4000;c.SendBufferSize=65536;lock(clients)clients.Add(c);new Thread(()=>Serve(c)){IsBackground=true}.Start();}catch{if(running)Thread.Sleep(50);}}
    void Serve(TcpClient client){string owner=Guid.NewGuid().ToString("N"),role="";bool registered=false;var surface=new CaptureSurface();byte[] previous=null,raw=null,delta=null;long seq=0,prevSeq=0,lastWindow=-1;Rectangle priorArea=Rectangle.Empty;Dictionary<string,object> streaming=null;
        string priorHash="";try{using(client)using(var stream=client.GetStream()){
            var hello=Wire.Receive(stream).Header;if(Wire.Str(hello,"op")!="hello"||Wire.Int(hello,"version")!=Wire.Version||Wire.Str(hello,"token")!=token)throw new IOException("会话验证失败");role=Wire.Str(hello,"role");if(role!="frame"&&role!="control")throw new IOException("连接类型错误");
            lock(clients){if(role=="control"){if(controlCount>0)throw new IOException("已有控制连接");controlCount++;everConnected=true;Interlocked.Exchange(ref lastControl,Stopwatch.GetTimestamp());}else frameCount++;registered=true;}Wire.Send(stream,Wire.Map("ok",true,"version",Wire.Version));
            while(running){var request=streaming!=null&&!stream.DataAvailable?streaming:Wire.Receive(stream).Header;long begin=Stopwatch.GetTimestamp();string op=Wire.Str(request,"op");var reply=Wire.Map("ok",true);byte[] payload=null;if(role=="control"){Interlocked.Exchange(ref lastControl,begin);if((begin-diskBeat)/(double)Stopwatch.Frequency>0.4){File.WriteAllText(Path.Combine(root,"alive"),"alive");diskBeat=begin;}}
                try{
                    if(op=="ping")reply["utc"]=DateTime.UtcNow.ToString("o");
                    else if(op=="windows")reply["windows"]=Win.Windows();
                    else if(op=="status"){Win.POINT cursor;Win.GetCursorPos(out cursor);reply["cursor_x"]=cursor.X;reply["cursor_y"]=cursor.Y;reply["remote_moves"]=Interlocked.Read(ref Win.RemoteMoveCount);reply["local_generation"]=LocalMouseMonitor.Generation;reply["held"]=input.Held;reply["armed"]=input.Armed;reply["reason"]=input.Reason;reply["frames"]=Interlocked.Read(ref total);reply["record_keys_enabled"]=KeyboardTrace.Target!=0;}
                    else if(role=="control"&&op=="record_keys"){bool enabled=Wire.Bool(request,"enabled");if(enabled)PrepareRecording(Wire.Long(request,"window"));reply["recording_epoch"]=KeyboardTrace.Configure(enabled,Wire.Long(request,"window"),enabled?Wire.IntArray(request["keys"]):new int[0]);reply["recording"]=enabled;}
                    else if(role=="control"&&op=="prepare_vision"){PrepareRecording(Wire.Long(request,"window"));reply["prepared_utc"]=DateTime.UtcNow.ToString("o");}
                    else if(role=="frame"&&(op=="frame"||op=="stream")){
                        if(op=="stream")streaming=request;long window=Wire.Long(request,"window");Rectangle area=Win.Bounds(window);int length=Wire.PixelLength(area.Width,area.Height);bool reset=previous==null||priorArea!=area||lastWindow!=window||Wire.Bool(request,"keyframe");request["keyframe"]=false;
                        if(raw==null||raw.Length!=length){raw=new byte[length];previous=null;delta=new byte[length];reset=true;}
                        var routeInput=KeyboardTrace.Snapshot();
                        long watched=routeInput==null?0:Wire.Long(routeInput,"window");
                        Win.POINT cursorBefore,cursorAfter;
                        bool haveCursorBefore=Win.GetCursorPos(out cursorBefore);
                        bool cursorInGame=!haveCursorBefore||CursorInGame(window,watched,cursorBefore);
                        surface.Read(area,raw);DateTime capturedAt=DateTime.UtcNow;
                        bool haveCursorAfter=Win.GetCursorPos(out cursorAfter);
                        cursorInGame=cursorInGame||!haveCursorAfter||CursorInGame(window,watched,cursorAfter);
                        if(haveCursorBefore&&haveCursorAfter)reply["cursor_samples"]=new[]{cursorBefore.X-area.X,cursorBefore.Y-area.Y,cursorAfter.X-area.X,cursorAfter.Y-area.Y};
                        double capture=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;
                        bool identical=false;long encodeAt=Stopwatch.GetTimestamp();if(reset){payload=Wire.Deflate(raw);reply["encoding"]="bgr_deflate";reply["base_sequence"]=0;}
                        else{int[] tiles=Wire.ChangedTiles(raw,previous,area.Width,area.Height,64);reply["base_sequence"]=prevSeq;reply["changed_tiles"]=tiles.Length;if(tiles.Length==0){identical=true;payload=new byte[0];reply["encoding"]="repeat";}else{payload=Wire.Deflate(Wire.PackTiles(raw,previous,tiles,area.Width,area.Height,64));reply["encoding"]="tile_deflate";reply["tiles"]=tiles;reply["tile_size"]=64;}}
                        reply["encode_ms"]=(Stopwatch.GetTimestamp()-encodeAt)*1000.0/Stopwatch.Frequency;long hashAt=Stopwatch.GetTimestamp();reply["width"]=area.Width;reply["height"]=area.Height;reply["left"]=area.Left;reply["top"]=area.Top;reply["window"]=window;reply["sequence"]=++seq;priorHash=identical?priorHash:Wire.Hash(raw);reply["raw_sha256"]=priorHash;reply["hash_ms"]=(Stopwatch.GetTimestamp()-hashAt)*1000.0/Stopwatch.Frequency;reply["captured_utc"]=capturedAt.ToString("o");reply["route_input"]=routeInput;reply["cursor_in_game"]=cursorInGame;reply["capture_ms"]=capture;reply["processing_ms"]=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;
                        byte[] swap=previous;previous=raw;raw=swap??new byte[length];prevSeq=seq;priorArea=area;lastWindow=window;Interlocked.Increment(ref total);
                    }
                    else if(role=="control"&&op=="test_local_mouse"&&File.Exists(Path.Combine(root,"test-ready.json"))){File.WriteAllText(Path.Combine(root,"local-mouse-probe"),"probe");}
                    else if(role=="control"&&op=="arm")input.Arm(owner,Wire.Long(request,"window"),!Wire.Bool(request,"desktop_input"));
                    else if(role=="control"&&op=="release"){lock(input.Gate)if(input.Owner==owner||!input.Armed)input.Release("只观看，已松键");}
                    else if(role=="control"&&op=="heartbeat")input.Beat(owner);
                    else if(role=="control"&&(op=="key_down"||op=="key_up"))input.KeyEvent(owner,Wire.Int(request,"vk"),op=="key_down");
                    else if(role=="control"&&op=="text")input.Text(owner,Wire.Str(request,"text"));
                    else if(role=="control"&&op.StartsWith("mouse_")||role=="control"&&op=="wheel")reply=input.MouseEvent(owner,request);
                    else if(role=="control"&&op=="close"){Wire.Send(stream,reply);Stop();break;}
                    else throw new IOException("操作不支持");
                }catch(Exception e){if(role=="control")input.Release("输入暂停："+e.Message);reply=Wire.Map("ok",false,"error",e.Message);payload=null;if(streaming!=null)Thread.Sleep(100);}
                Wire.Send(stream,reply,payload);
                if(streaming!=null){double elapsed=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;int delay=(int)(1000.0/60-elapsed);if(delay>0)Thread.Sleep(delay);}
            }
        }}catch{}finally{surface.Dispose();lock(input.Gate)if(input.Owner==owner)input.Release("控制连接断开，已松键");lock(clients){clients.Remove(client);if(registered){if(role=="control"){KeyboardTrace.Configure(false,0,new int[0]);controlCount--;Interlocked.Exchange(ref lastControl,Stopwatch.GetTimestamp());}else frameCount--;}}}
    }
    void RunLoop(){Win.SetProcessDPIAware();Win.timeBeginPeriod(1);try{RefreshGames();using(var monitor=new LocalMouseMonitor()){monitor.Start();listener=new TcpListener(IPAddress.Loopback,port);listener.Start(6);new Thread(Accept){IsBackground=true}.Start();File.WriteAllText(Path.Combine(root,"alive"),"alive");Wire.Save(Path.Combine(root,"ready.json"),Wire.Map("pid",Process.GetCurrentProcess().Id,"session",Process.GetCurrentProcess().SessionId,"port",port,"process",Process.GetCurrentProcess().ProcessName,"build","v2-route-cursor-start-20261119","agent_sha",Wire.Hash(File.ReadAllBytes(Path.Combine(root,"Agent.cs"))),"common_sha",Wire.Hash(File.ReadAllBytes(Path.Combine(root,"Common.cs")))));long tick=0;using(var timer=new System.Threading.Timer(delegate{try{input.Watch();if(++tick%5==0)RefreshGames();double age=(Stopwatch.GetTimestamp()-Interlocked.Read(ref lastControl))/(double)Stopwatch.Frequency;double life=(Stopwatch.GetTimestamp()-started)/(double)Stopwatch.Frequency;if(File.Exists(Path.Combine(root,"stop"))||(!everConnected&&life>20)||(everConnected&&age>4&&controlCount==0))Stop();}catch{Stop();}},null,100,100)){done.WaitOne();}}}finally{Stop();Win.timeEndPeriod(1);Wire.Save(Path.Combine(root,"stopped.json"),Wire.Map("held",input.Held,"frames",Interlocked.Read(ref total)));}}
    public static void Run(string root){new Runtime(root).RunLoop();}
}
}
