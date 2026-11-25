using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SshScreen {
// Runs the real ControllerForm without SSH, exercising actual Windows focus events.
public static class PauseRegression {
    [StructLayout(LayoutKind.Sequential)]struct MOUSE{public int x,y;public uint data,flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)]struct KEY{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Explicit)]struct UNION{[FieldOffset(0)]public MOUSE mouse;[FieldOffset(0)]public KEY key;}
    [StructLayout(LayoutKind.Sequential)]struct INPUT{public uint type;public UNION value;}
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern uint SendInput(uint n,INPUT[] values,int size);
    static readonly BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Get(object target,string name){return target.GetType().GetField(name,Fields).GetValue(target);}
    static void Set(object target,string name,object value){var field=target.GetType().GetField(name,Fields);if(field!=null)field.SetValue(target,value);}
    static int Arms(ControllerForm form){int count=0;foreach(var item in (ConcurrentQueue<Dictionary<string,object>>)Get(form,"queue"))if(Wire.Str(item,"op")=="arm")count++;return count;}
    static void Clear(ControllerForm form){Dictionary<string,object> item;while(((ConcurrentQueue<Dictionary<string,object>>)Get(form,"queue")).TryDequeue(out item)){} }
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    static void Mouse(bool up){if(!up){uint owner;GetWindowThreadProcessId(GetForegroundWindow(),out owner);if(owner!=(uint)System.Diagnostics.Process.GetCurrentProcess().Id)throw new IOException("Own QA form lost foreground; no mouse click sent");}var v=new INPUT();v.value.mouse.flags=up?4u:2u;if(SendInput(1,new[]{v},Marshal.SizeOf(typeof(INPUT)))!=1)throw new IOException("Local test mouse failed");}
    [STAThread]public static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var rows=new List<object>();foreach(string kind in new[]{"pause_click","pause_hold","cancel_pending","duplicate_moves"})rows.Add(Run(args[0],kind));bool ok=true;foreach(Dictionary<string,object> row in rows)ok&=Wire.Bool(row,"ok");Wire.Save(Path.Combine(args[0],"pause-regression.json"),Wire.Map("ok",ok,"ssh_started",false,"cases",rows.ToArray()));if(!ok)Environment.ExitCode=1;}
    static object Run(string root,string kind){var result=Wire.Map("case",kind,"ok",false);using(var form=new ControllerForm(root,false,false)){
        form.Text="SSH暂停按钮回归 · 仅电脑2本机测试";var view=(Control)Get(form,"view");var button=(Button)Get(form,"control");var timer=new System.Windows.Forms.Timer{Interval=100};int phase=0;DateTime due=DateTime.MinValue;
        view.GetType().GetField("Frame").SetValue(view,new Bitmap(480,340));view.GetType().GetProperty("SourceSize").SetValue(view,new Size(480,340),null);Set(form,"displayedWindow",0L);
        form.Shown+=delegate{form.Activate();view.Focus();Set(form,"running",true);Set(form,"lastFrame",DateTime.UtcNow);timer.Start();};
        timer.Tick+=delegate{if(DateTime.UtcNow<due)return;try{
            if(phase==0){view.Focus();Clear(form);Set(form,"lastFrame",DateTime.UtcNow);Set(form,"armed",kind!="cancel_pending");Set(form,"armPending",false);var presented=button.GetType().GetField("PresentsPauseAction");if(presented!=null)presented.SetValue(button,kind!="cancel_pending");button.Text=kind=="cancel_pending"?"开启手动控制":"暂停控制（F12）";
                if(kind=="duplicate_moves"){var method=typeof(Control).GetMethod("OnMouseMove",BindingFlags.NonPublic|BindingFlags.Instance);for(int i=0;i<20;i++){method.Invoke(view,new object[]{new MouseEventArgs(MouseButtons.None,0,200,200,0)});Thread.Sleep(15);}int moves=0;foreach(var entry in (ConcurrentQueue<Dictionary<string,object>>)Get(form,"queue"))if(Wire.Str(entry,"op")=="mouse_move")moves++;result["queued_moves"]=moves;result["ok"]=moves==1;timer.Stop();form.Close();return;}
                Point at=button.PointToScreen(new Point(button.Width/2,button.Height/2));SetCursorPos(at.X,at.Y);Mouse(false);phase=1;due=DateTime.UtcNow.AddMilliseconds(kind=="pause_hold"?300:50);
            }else if(phase==1){result["armed_after_mouse_down"]=(bool)Get(form,"armed");Mouse(true);phase=2;due=DateTime.UtcNow.AddMilliseconds(150);
            }else if(phase==2){if(kind=="cancel_pending"){Point at=button.PointToScreen(new Point(button.Width/2,button.Height/2));SetCursorPos(at.X,at.Y);Mouse(false);Mouse(true);phase=3;due=DateTime.UtcNow.AddMilliseconds(150);return;}Finish();}
            else Finish();
        }catch(Exception e){result["error"]=e.Message;timer.Stop();form.Close();}};
        Action finish=delegate{};
        // Local function equivalent for the framework compiler.
        finish=delegate{result["queued_arms"]=Arms(form);result["armed_after_click"]=(bool)Get(form,"armed");var pending=form.GetType().GetField("armPending",Fields);bool isPending=pending!=null&&(bool)pending.GetValue(form);result["ok"]=Arms(form)==0&&!(bool)Get(form,"armed")&&!isPending;timer.Stop();form.Close();};
        finishActions[form]=finish;
        activeForm=form;Application.Run(form);finishActions.Remove(form);timer.Dispose();
    }return result;}
    static ControllerForm activeForm;static readonly Dictionary<ControllerForm,Action> finishActions=new Dictionary<ControllerForm,Action>();
    static void Finish(){finishActions[activeForm]();}
}
}
