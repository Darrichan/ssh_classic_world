using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SshScreen {
public sealed class TestWindow:Form {
    [StructLayout(LayoutKind.Sequential)]struct RECT{public int L,T,R,B;}
    delegate bool EnumProc(IntPtr h,IntPtr d);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc p,IntPtr d);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    readonly string root;readonly TextBox text=new TextBox();readonly Label state=new Label();readonly Timer timer=new Timer();readonly HashSet<Keys> keys=new HashSet<Keys>();readonly List<string> events=new List<string>();readonly IntPtr original;
    int clicks,ticks,gameCount;bool combo;DateTime end=DateTime.UtcNow.AddMinutes(12);
    public TestWindow(string path){root=path;original=GetForegroundWindow();
        var games=new List<Rectangle>();EnumWindows(delegate(IntPtr h,IntPtr d){if(IsWindowVisible(h)&&!IsIconic(h)){var title=new StringBuilder(512);GetWindowText(h,title,title.Capacity);if(Regex.IsMatch(title.ToString(),"Maple|Artale|冒险|冒險|新枫|新楓",RegexOptions.IgnoreCase)){RECT r;if(GetWindowRect(h,out r))games.Add(Rectangle.FromLTRB(r.L,r.T,r.R,r.B));}}return true;},IntPtr.Zero);gameCount=games.Count;
        var free=Screen.AllScreens.Select(s=>s.WorkingArea).ToList();foreach(var game in games){var next=new List<Rectangle>();foreach(var r in free){var c=Rectangle.Intersect(r,game);if(c.IsEmpty){next.Add(r);continue;}if(c.Top>r.Top)next.Add(Rectangle.FromLTRB(r.Left,r.Top,r.Right,c.Top));if(c.Bottom<r.Bottom)next.Add(Rectangle.FromLTRB(r.Left,c.Bottom,r.Right,r.Bottom));if(c.Left>r.Left)next.Add(Rectangle.FromLTRB(r.Left,c.Top,c.Left,c.Bottom));if(c.Right<r.Right)next.Add(Rectangle.FromLTRB(c.Right,c.Top,r.Right,c.Bottom));}free=next;}
        Rectangle area=free.Where(r=>r.Width>=500&&r.Height>=390).OrderByDescending(r=>(long)r.Width*r.Height).FirstOrDefault();if(area.IsEmpty)throw new IOException("游戏窗外没有足够的测试窗口区域");
        Text=File.Exists(Path.Combine(root,"mouse-park-probe"))?"SSH双机测试窗口 MapleMouseProbe":"SSH双机测试窗口";StartPosition=FormStartPosition.Manual;ClientSize=new Size(480,340);Location=new Point(area.Left+8,area.Top+8);Font=new Font("Microsoft YaHei UI",11);KeyPreview=true;BackColor=Color.White;
        var titleLabel=new Label{Text="电脑1画面 → SSH → 电脑2",Location=new Point(20,20),Size=new Size(440,36),ForeColor=Color.FromArgb(35,88,174),Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold)};
        var button=new Button{Text="点击测试",Location=new Point(20,75),Size=new Size(150,42)};button.Click+=delegate{clicks++;};text.Location=new Point(20,145);text.Size=new Size(430,32);text.PreviewKeyDown+=delegate(object s,PreviewKeyDownEventArgs e){if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Right||e.KeyCode==Keys.Left)e.IsInputKey=true;};
        state.Location=new Point(20,200);state.Size=new Size(440,120);Controls.AddRange(new Control[]{titleLabel,button,text,state});
        KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Right||e.KeyCode==Keys.Left){keys.Add(e.KeyCode);events.Add(e.KeyCode+":down");combo|=keys.Contains(Keys.Up)&&keys.Contains(Keys.Right);e.SuppressKeyPress=true;}};
        KeyUp+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Right||e.KeyCode==Keys.Left){keys.Remove(e.KeyCode);events.Add(e.KeyCode+":up");e.SuppressKeyPress=true;}};
        Shown+=delegate{SetCursorPos(area.Right-4,area.Bottom-4);Wire.Save(Path.Combine(root,"test-ready.json"),Wire.Map("window",Handle.ToInt64(),"pid",Process.GetCurrentProcess().Id,"width",ClientSize.Width,"height",ClientSize.Height,"game_windows",gameCount,"title",Text,"mouseParkProbe",File.Exists(Path.Combine(root,"mouse-park-probe"))));};
        timer.Interval=16;timer.Tick+=delegate{if(File.Exists(Path.Combine(root,"local-mouse-probe"))){File.Delete(Path.Combine(root,"local-mouse-probe"));Point local=PointToScreen(new Point(120,160));SetCursorPos(local.X,local.Y);mouse_event(1,1,0,0,UIntPtr.Zero);}ticks++;BackColor=Color.FromArgb(220+(ticks%30),225+((ticks*3)%25),225+((ticks*7)%25));state.Text="鼠标点击："+clicks+"\n输入文字："+text.Text+"\n上+右组合："+combo+"  |  计数 "+ticks;if(ticks%10==0)Save();if(File.Exists(Path.Combine(root,"test-stop"))||DateTime.UtcNow>end)Close();};timer.Start();FormClosing+=delegate{Save();if(GetForegroundWindow()==Handle)SetForegroundWindow(original);};
    }
    void Save(){Wire.Save(Path.Combine(root,"test-proof.json"),Wire.Map("clicks",clicks,"text",text.Text,"combo",combo,"left_down",(GetAsyncKeyState(0x25)&0x8000)!=0,"up_down",(GetAsyncKeyState(0x26)&0x8000)!=0,"right_down",(GetAsyncKeyState(0x27)&0x8000)!=0,"events",events.ToArray(),"ticks",ticks));}
    [STAThread]public static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();string root=args.Length>0?args[0]:AppDomain.CurrentDomain.BaseDirectory;try{Application.Run(new TestWindow(root));}catch(Exception e){Wire.Save(Path.Combine(root,"test-error.json"),Wire.Map("error",e.Message));}}
}
}
