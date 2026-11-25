using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace SshScreen {
public static class UnifiedUiChecks {
    const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    delegate bool EnumProc(IntPtr h,IntPtr l);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr unused);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    static int TopWindows(){return Application.OpenForms.Cast<Form>().Count(f=>f.TopLevel&&f.Visible);}
    static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    static Button Button(Form form,string text){return All(form).OfType<Button>().First(b=>b.Text==text);}
    static object Field(object target,string name){return target.GetType().GetField(name,F).GetValue(target);}
    static void Invoke(object target,string method){target.GetType().GetMethod(method,F).Invoke(target,null);}
    static void Need(bool test,string message){if(!test)throw new IOException(message);}
    static void Move(Control view,int x,int y){var area=(Rectangle)view.GetType().GetMethod("ImageRect").Invoke(view,null);var size=(Size)view.GetType().GetProperty("SourceSize").GetValue(view,null);Point p=view.PointToScreen(new Point(area.X+(int)((x+0.5)*area.Width/size.Width),area.Y+(int)((y+0.5)*area.Height/size.Height)));SetCursorPos(p.X,p.Y);}
    static void Down(){mouse_event(2,0,0,0,UIntPtr.Zero);}static void Up(){mouse_event(4,0,0,0,UIntPtr.Zero);}
    static void Shot(Form f,string path){Rectangle rect=Rectangle.Intersect(f.Bounds,Screen.PrimaryScreen.Bounds);using(var bitmap=new Bitmap(rect.Width,rect.Height))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(rect.Location,Point.Empty,rect.Size);bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
    static byte[] MakeScene(PlayerProfile p,out Rectangle face,out Point body){int width=1368,height=768;using(var bitmap=new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
        using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(45,60,78));using(var dot=new SolidBrush(Color.FromArgb(255,255,136)))g.FillRectangle(dot,99,99,3,3);using(var name=RouteFiles.Png(p.nametag.png))using(var mask=new Bitmap(name.Width,name.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
            int nx=550,ny=510;for(int y=0;y<name.Height;y++)for(int x=0;x<name.Width;x++){Color c=name.GetPixel(x,y);bitmap.SetPixel(nx+x,ny+y,c.G>=180&&c.R<=80&&c.B<=80?Color.FromArgb(45,60,78):c);}body=new Point(nx+(int)p.nametag.body_x,ny+(int)p.nametag.body_y);}
            using(var image=RouteFiles.Png(p.left.png)){face=new Rectangle(body.X-image.Width/2,body.Y-40-image.Height/2,image.Width,image.Height);g.DrawImageUnscaled(image,face.Location);}}
        return Wire.Pixels(bitmap);
    }}
    [STAThread]public static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);string root=args[0];var report=Wire.Map("ok",false,"game_input",false,"ssh_started",false);ControllerForm form=null;try{
        var profile=PlayerFiles.Load(Path.Combine(root,"players","current.json"));Rectangle face;Point body;byte[] pixels=MakeScene(profile,out face,out body);long seq=0;Func<Dictionary<string,object>> source=()=>Wire.Map("bgr24",(byte[])pixels.Clone(),"width",1368,"height",768,"native_width",1368,"native_height",768,"window",42L,"sequence",++seq,"age_ms",0.0,"cursor_in_game",false,"captured_utc",DateTime.UtcNow.ToString("o"));
        form=new ControllerForm(root,false,false);form.FixtureSource=source;form.GetType().GetField("target",F).SetValue(form,42L);form.GetType().GetField("displayedWindow",F).SetValue(form,42L);var view=(RemoteView)Field(form,"view");view.Frame=Wire.BitmapFrom(pixels,1368,768);view.SourceSize=view.Frame.Size;view.Sequence=1;var person=(PlayerPanel)Field(form,"person");var timer=new Timer{Interval=180};int phase=0;DateTime due=DateTime.MinValue,deadline=DateTime.MinValue;int worker=0;Rectangle selected=Rectangle.Empty;Point picked=Point.Empty;
        form.Shown+=delegate{form.WindowState=FormWindowState.Normal;timer.Start();};timer.Tick+=delegate{if(DateTime.UtcNow<due)return;try{switch(phase){
            case 0:Invoke(form,"OpenRoutes");report["forms"]=Application.OpenForms.Cast<Form>().Select(f=>Wire.Map("title",f.Text,"top_level",f.TopLevel,"visible",f.Visible)).ToArray();Need(TopWindows()==1,"Route editor opened a second window");report["single_main_window"]=true;phase++;break;
            case 1:Invoke(form,"OpenPerson");Button(form,"开始识别").PerformClick();deadline=DateTime.UtcNow.AddSeconds(10);phase++;break;
            case 2:if(person.Current==null||!Wire.Bool(person.Current,"direction_fresh")){if(DateTime.UtcNow<deadline)return;throw new IOException("Character pipeline did not become ready");}Need(Wire.Bool(person.Current,"found")&&Wire.Str(person.Current,"direction")=="left","Wrong character result");int[] xy=Wire.IntArray(person.Current["body"]);Need(xy[0]==body.X&&xy[1]==body.Y,"Body marker offset mismatch");report["name_body_and_left_face"]=true;var runtime=(PlayerRuntime)Field(person,"runtime");worker=runtime.Pid;Shot(form,Path.Combine(root,"unified-person-large.png"));Button(form,"暂停").PerformClick();phase++;break;
            case 3:All(form).OfType<Button>().First(b=>b.Name=="Capture_left").PerformClick();Need(Field(form,"captureFrame")!=null,"Capture did not start in preview");Need(TopWindows()==1,"Capture opened a second window");report["single_window_capture"]=true;phase++;break;
            case 4:Move(view,face.X,face.Y);Down();phase++;break;
            case 5:Move(view,face.Right-1,face.Bottom-1);phase++;break;
            case 6:Up();selected=view.Selections.Single();phase++;break;
            case 7:Move(view,body.X,body.Y);Down();phase++;break;
            case 8:Up();picked=view.Pick;Button(form,"保存模板").PerformClick();Need(Field(form,"captureFrame")==null,"Capture did not finish");using(var stored=RouteFiles.Png(person.Profile.left.png)){var expected=new byte[selected.Width*selected.Height*3];for(int y=0;y<selected.Height;y++)Buffer.BlockCopy(pixels,((y+selected.Y)*1368+selected.X)*3,expected,y*selected.Width*3,selected.Width*3);Need(Wire.Equal(expected,Wire.Pixels(stored)),"Capture altered original pixels");}Need(person.Profile.left.body_x==picked.X-selected.X&&person.Profile.left.body_y==picked.Y-selected.Y,"Body center not saved");report["native_capture_pixel_exact"]=true;report["body_offset_saved"]=true;phase++;break;
            case 9:bool alive=false;try{alive=!Process.GetProcessById(worker).HasExited;}catch{}Need(!alive,"Recognition process stayed after stop");report["recognition_process_cleaned"]=true;Invoke(form,"OpenRoutes");Button(form,"标定小地图").PerformClick();Need(TopWindows()==1,"Minimap calibration opened a new window");phase++;break;
            case 10:Move(view,60,60);Down();phase++;break;
            case 11:Move(view,239,179);phase++;break;
            case 12:Up();phase++;break;
            case 13:Move(view,100,100);Down();phase++;break;
            case 14:Up();Button(form,"保存模板").PerformClick();var route=(RouteStudio)Field(form,"routes");var e=(RouteEngine)Field(route,"engine");Need(e.Document.dotR==255&&e.Document.dotG==255&&e.Document.dotB==136&&e.Document.window==42,"Inline minimap calibration mismatch");report["single_window_minimap_calibration"]=true;phase++;break;
            case 15:Action<Dictionary<string,object>> late=null;using(var cancelled=new PlayerPanel(root,source,(ready,fail)=>late=ready,slot=>{},answer=>{})){cancelled.Start();cancelled.Stop("已暂停");late(source());Need(!cancelled.Active&&Field(cancelled,"runtime")==null,"Cancelled preparation restarted recognition");}report["pending_start_cancelled"]=true;Need(person.Profile.minimap!=null,"Shared minimap calibration not saved");Invoke(form,"OpenPerson");Button(form,"开始识别").PerformClick();deadline=DateTime.UtcNow.AddSeconds(10);phase++;break;
            case 16:if(person.Current==null||!Wire.Bool(person.Current,"minimap_fresh")){if(DateTime.UtcNow<deadline)return;throw new IOException("Shared minimap did not reach character worker");}Need(Wire.Bool(person.Current,"minimap_active")&&Wire.Bool(person.Current,"found"),"Minimap guard not integrated with position result");report["shared_minimap_guard"]=true;Button(form,"暂停").PerformClick();form.WindowState=FormWindowState.Normal;form.Size=new Size(960,640);phase++;break;
            case 17:Need(TopWindows()==1,"Extra window after resize");Shot(form,Path.Combine(root,"unified-small.png"));report["small_window"]=true;report["compact_size"]=new[]{form.Width,form.Height};report["ok"]=true;timer.Stop();form.Close();break;

        }}catch(Exception e){report["error"]=e.GetBaseException().Message;Up();timer.Stop();form.Close();}};form.FormClosed+=delegate{timer.Stop();Wire.Save(Path.Combine(root,"unified-ui-checks.json"),report);};Application.Run(form);timer.Dispose();
    }catch(Exception e){report["error"]=e.ToString();if(form!=null)form.Dispose();Wire.Save(Path.Combine(root,"unified-ui-checks.json"),report);}}
}
}
