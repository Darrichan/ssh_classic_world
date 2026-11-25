using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace SshScreen {
public static class RouteUiCheck {
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
    static string root;static Dictionary<string,object> report=Wire.Map("ok",false,"game_input",false,"ssh_started",false);
    static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    static Button Button(Form f,string title){return All(f).OfType<Button>().First(b=>b.Text==title);}
    static void Move(Point p){SetCursorPos(p.X,p.Y);}
    static void Down(){mouse_event(2,0,0,0,UIntPtr.Zero);}static void Up(){mouse_event(4,0,0,0,UIntPtr.Zero);}
    static void Snapshot(Form f,string name){var r=f.Bounds;Move(new Point(Screen.PrimaryScreen.Bounds.Right-3,Screen.PrimaryScreen.Bounds.Bottom-3));using(var bitmap=new Bitmap(r.Width,r.Height))using(var g=Graphics.FromImage(bitmap)){g.CopyFromScreen(r.Location,Point.Empty,r.Size);bitmap.Save(Path.Combine(root,name),System.Drawing.Imaging.ImageFormat.Png);}}
    static Dictionary<string,object> Full(){var pixels=RouteChecks.Background(320,200,7);var map=RouteChecks.Background(96,72,3);RouteChecks.Dot(map,96,20,30);for(int y=0;y<72;y++)Buffer.BlockCopy(map,y*96*3,pixels,((y+10)*320+10)*3,96*3);return Wire.Map("bgr24",pixels,"width",320,"height",200,"window",42L);}
    static Point OnImage(RouteImageSelection view,int x,int y){var r=(Rectangle)view.GetType().GetMethod("Area",F).Invoke(view,null);return view.PointToScreen(new Point(r.X+(int)((x+0.5)*r.Width/view.Image.Width),r.Y+(int)((y+0.5)*r.Height/view.Image.Height)));}
    [STAThread]public static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);root=args[0];Directory.CreateDirectory(root);try{if(args.Contains("--calibrate"))Calibration();else Studio();}catch(Exception e){report["error"]=e.ToString();Wire.Save(Path.Combine(root,args.Contains("--calibrate")?"route-calibration-ui.json":"route-studio-ui.json"),report);}}
    static void Calibration(){using(var f=new RouteCalibration(Full(),RouteChecks.Doc())){var timer=new Timer{Interval=220};int phase=0;f.Shown+=delegate{timer.Start();};timer.Tick+=delegate{try{var view=(RouteImageSelection)typeof(RouteCalibration).GetField("canvas",F).GetValue(f);switch(phase++){case 0:Move(OnImage(view,10,10));Down();break;case 1:Move(OnImage(view,105,81));break;case 2:Up();break;case 3:Button(f,"② 点选人物黄点").PerformClick();break;case 4:Move(OnImage(view,30,40));Down();break;case 5:Up();break;case 6:Snapshot(f,"route-calibration.png");Button(f,"使用此标定").PerformClick();break;case 7:throw new IOException("Calibration did not finish");}}catch(Exception e){report["error"]=e.Message;Up();timer.Stop();f.Close();}};f.FormClosed+=delegate{timer.Stop();if(f.Result!=null){report["roi"]=new[]{f.Result.roiX,f.Result.roiY,f.Result.roiWidth,f.Result.roiHeight};report["color"]=new[]{f.Result.dotR,f.Result.dotG,f.Result.dotB};report["seed"]=new[]{f.Result.dotSeedX,f.Result.dotSeedY};report["ok"]=f.Result.roiX==10&&f.Result.roiY==10&&f.Result.roiWidth==96&&f.Result.roiHeight==72&&f.Result.dotR==255&&f.Result.dotG==255&&f.Result.dotB==136;}Wire.Save(Path.Combine(root,"route-calibration-ui.json"),report);};Application.Run(f);timer.Dispose();}}
    static void Studio(){bool watching=false;long sequence=0;int calls=0;Func<Rectangle,Dictionary<string,object>> source=delegate(Rectangle r){int n=calls++%36,x=n<18?20+n*3:71-(n-18)*3;var p=RouteChecks.Background(96,72,3);RouteChecks.Dot(p,96,x,30);return RouteChecks.Frame(++sequence,p,watching?new[]{n<18?39:37}:new int[0]);};using(var f=new RouteStudio(root,Full,(layout,reference)=>source(layout.CalibratedArea),(active,d)=>watching=active)){
        var engine=new RouteEngine(RouteChecks.Doc());typeof(RouteStudio).GetField("engine",F).SetValue(f,engine);var canvas=All(f).OfType<RouteCanvas>().First();var timer=new Timer{Interval=250};int phase=0;DateTime due=DateTime.MinValue;Point end=Point.Empty;int recordedEdges=0,drawnEdges=0;
        f.Shown+=delegate{timer.Start();};timer.Tick+=delegate{if(DateTime.UtcNow<due)return;try{switch(phase++){
            case 0:Button(f,"开始录制").PerformClick();due=DateTime.UtcNow.AddMilliseconds(2200);break;
            case 1:Button(f,"暂停录制").PerformClick();if(engine.Document.samples.Count<15||engine.Document.edges.Count==0)throw new IOException("Recording/graph generation failed");report["samples"]=engine.Document.samples.Count;report["edges"]=engine.Document.edges.Count;report["bidirectional"]=engine.Document.edges.Any(e=>e.both);recordedEdges=engine.Document.edges.Count;
                if(!f.SelectBrush("walk"))throw new IOException("Walk brush unavailable");
                canvas.GetType().GetMethod("LayoutMap",F).Invoke(canvas,null);
                var project=canvas.GetType().GetMethod("ScreenPoint",F);
                var begin=canvas.PointToScreen(Point.Round((PointF)project.Invoke(canvas,new object[]{25,50})));
                end=canvas.PointToScreen(Point.Round((PointF)project.Invoke(canvas,new object[]{70,50})));
                Move(begin);Down();break;
            case 2:Move(end);break;
            case 3:Up();drawnEdges=engine.Document.edges.Count;if(drawnEdges<=recordedEdges)throw new IOException("Stroke did not create a route");report["drawn_stroke"]=true;break;
            case 4:Button(f,"撤销").PerformClick();if(engine.Document.edges.Count!=recordedEdges)throw new IOException("Undo did not restore recorded route");report["undo"]=true;break;
            case 5:Button(f,"重做").PerformClick();if(engine.Document.edges.Count!=drawnEdges)throw new IOException("Redo did not restore edit");report["redo"]=true;typeof(RouteStudio).GetMethod("SaveDraft",F).Invoke(f,null);var saved=RouteFiles.Load(Path.Combine(root,"routes","draft.route.json"));if(saved.samples.Count!=engine.Document.samples.Count)throw new IOException("Draft data mismatch");report["draft_roundtrip"]=true;break;
            case 6:if(All(f).Any(c=>c.Text=="检查路线"||c.Text=="预览路径"||c.Text=="传送键"))throw new IOException("Unexpected extra route controls");report["focused_movement_ui"]=true;var dropFile=Path.Combine(Path.GetDirectoryName(root),"checks-drop.route.json");if(File.Exists(dropFile)){engine=new RouteEngine(RouteFiles.Load(dropFile));typeof(RouteStudio).GetField("engine",F).SetValue(f,engine);typeof(RouteStudio).GetMethod("RefreshGraph",F).Invoke(f,null);canvas.SelectedEdge=engine.Document.edges.First(edge=>edge.kind=="drop").id;typeof(RouteStudio).GetMethod("RefreshGraph",F).Invoke(f,null);
                bool timing=All(f).OfType<Label>().Any(c=>c.Text.Contains("下键提前 180ms")&&c.Text.Contains("跳键 100ms")&&c.Text.Contains("下键尾随 40ms"));if(!timing)throw new IOException("Drop timing detail missing");report["drop_timing_detail"]=true;}f.Size=new Size(1000,700);break;
            case 7:Snapshot(f,"route-studio.png");report["small_window"]=true;report["ok"]=true;f.Close();break;
        }}catch(Exception e){report["error"]=e.Message;Up();timer.Stop();f.Close();}};f.FormClosed+=delegate{timer.Stop();report["key_watch_disabled"]=!watching;Wire.Save(Path.Combine(root,"route-studio-ui.json"),report);};Application.Run(f);timer.Dispose();
    }}
}
}
