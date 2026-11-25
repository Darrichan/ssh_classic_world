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
public static class NametagFieldsChecks {
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
    static void Shot(Form f,string path){f.PerformLayout();using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(Point.Empty,f.Size));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}

    static byte[] MakeScene(PlayerProfile p,out Rectangle face,out Point body){int width=1368,height=768;using(var bitmap=new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
        using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(45,60,78));using(var dot=new SolidBrush(Color.FromArgb(255,255,136)))g.FillRectangle(dot,99,99,3,3);using(var name=RouteFiles.Png(p.nametag.png))using(var mask=new Bitmap(name.Width,name.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
            int nx=550,ny=510;for(int y=0;y<name.Height;y++)for(int x=0;x<name.Width;x++){Color c=name.GetPixel(x,y);bitmap.SetPixel(nx+x,ny+y,c.G>=180&&c.R<=80&&c.B<=80?Color.FromArgb(45,60,78):c);}body=new Point(nx+(int)p.nametag.body_x,ny+(int)p.nametag.body_y);}
            using(var image=RouteFiles.Png(p.left.png)){face=new Rectangle(body.X-image.Width/2,body.Y-40-image.Height/2,image.Width,image.Height);g.DrawImageUnscaled(image,face.Location);}}
        return Wire.Pixels(bitmap);
    }}
    [STAThread]public static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);string root=args[0];var report=Wire.Map("ok",false,"game_input",false,"ssh_started",false);ControllerForm form=null;try{
        var original=PlayerFiles.Load(Path.Combine(root,"players","current.json"));Rectangle face;Point body;byte[] pixels=MakeScene(original,out face,out body);long sequence=0;Func<Dictionary<string,object>> source=()=>Wire.Map("bgr24",(byte[])pixels.Clone(),"width",1368,"height",768,"native_width",1368,"native_height",768,"window",42L,"sequence",++sequence,"age_ms",0.0,"cursor_in_game",false,"captured_utc",DateTime.UtcNow.ToString("o"));
        form=new ControllerForm(root,false,false);form.FixtureSource=source;form.GetType().GetField("target",F).SetValue(form,42L);form.GetType().GetField("displayedWindow",F).SetValue(form,42L);var view=(RemoteView)Field(form,"view");view.Frame=Wire.BitmapFrom(pixels,1368,768);view.SourceSize=view.Frame.Size;view.Sequence=1;var person=(PlayerPanel)Field(form,"person");var timer=new Timer{Interval=150};int phase=0;Rectangle[] areas=null;Point picked=Point.Empty;string savedHash=null;
        form.Shown+=delegate{form.Activate();timer.Start();};timer.Tick+=delegate{try{switch(phase){
            case 0:Invoke(form,"OpenPerson");Need(All(form).OfType<Label>().Any(l=>l.Text=="姓名 · 家族 · 勋章"),"Named fields absent");Need(All(form).OfType<Label>().Any(l=>l.Text=="身体中心 · 已设置"),"Body center state absent");report["named_fields_and_body_visible"]=true;Shot(form,Path.Combine(root,"nametag-fields-default.png"));All(form).OfType<Button>().First(b=>b.Name=="Capture_nametag").PerformClick();Need(TopWindows()==1,"Capture opened another window");Need(Button(form,"指定身体中心").Visible,"Original body center entry absent");Need(All(form).OfType<RadioButton>().Count(b=>b.Visible&&b.Name.StartsWith("CaptureRow_"))==3,"Three named row entries absent");phase++;break;
            case 1:Move(view,562,510);Down();phase++;break;
            case 2:Move(view,613,525);phase++;break;
            case 3:Up();Application.DoEvents();All(form).OfType<RadioButton>().First(b=>b.Name=="CaptureRow_family").Checked=true;phase++;break;
            case 4:Move(view,556,547);Down();phase++;break;
            case 5:Move(view,620,555);phase++;break;
            case 6:Up();Application.DoEvents();Button(form,"清除").PerformClick();Need(view.Selections.Count==1,"Clearing family deleted another row");report["clear_one_row_preserves_other"]=true;All(form).OfType<RadioButton>().First(b=>b.Name=="CaptureRow_medal").Checked=true;phase++;break;
            case 7:Move(view,550,532);Down();phase++;break;
            case 8:Move(view,632,542);phase++;break;
            case 9:Up();Application.DoEvents();Need(view.Selections.Count==2,"Medal row not retained with name");areas=view.Selections.ToArray();Shot(form,Path.Combine(root,"nametag-fields-capture.png"));Button(form,"指定身体中心").PerformClick();phase++;break;
            case 10:Move(view,body.X,body.Y);Down();phase++;break;
            case 11:Up();Application.DoEvents();picked=view.Pick;Need(Button(form,"保存模板").Enabled,"Body point did not enable save");Button(form,"保存模板").PerformClick();Need(Field(form,"captureFrame")==null,"Capture did not finish");var profile=PlayerFiles.Load(Path.Combine(root,"players","current.json"));Need(profile.nametag.rows.Count==2&&profile.nametag.rows.Any(r=>r.role=="name")&&profile.nametag.rows.Any(r=>r.role=="medal")&&!profile.nametag.rows.Any(r=>r.role=="family"),"Named rows not persisted");Rectangle box=areas.Aggregate(Rectangle.Union);Need(profile.nametag.body_x==picked.X-box.X&&profile.nametag.body_y==picked.Y-box.Y,"Body center offset incorrect");using(var stored=RouteFiles.Png(profile.nametag.png)){for(int y=0;y<stored.Height;y++)for(int x=0;x<stored.Width;x++){int sx=box.X+x,sy=box.Y+y;Color actual=stored.GetPixel(x,y);if(areas.Any(r=>r.Contains(sx,sy))){int at=(sy*1368+sx)*3;Need(actual.R==pixels[at+2]&&actual.G==pixels[at+1]&&actual.B==pixels[at],"Selected row pixels changed");}else Need(actual.ToArgb()==Color.Lime.ToArgb(),"Gap not green-masked");}}report["native_name_medal_pixels_exact"]=true;report["body_center_offset_saved"]=true;using(var runtime=new PlayerRuntime(root,profile)){var located=runtime.Locate(source());report["native_result"]=located;Need(Wire.Bool(located,"found")&&located["body"]!=null,"Native original did not locate captured name");int[] xy=Wire.IntArray(located["body"]);Need(Wire.Bool(located,"found")&&xy[0]==picked.X&&xy[1]==picked.Y,"Named rows/body point not used by recognizer");}report["recognizer_uses_named_template_and_body"]=true;savedHash=Wire.Hash(File.ReadAllBytes(Path.Combine(root,"players","current.json")));All(form).OfType<Button>().First(b=>b.Name=="Capture_nametag").PerformClick();Button(form,"取消").PerformClick();Need(savedHash==Wire.Hash(File.ReadAllBytes(Path.Combine(root,"players","current.json"))),"Cancel modified saved template");report["cancel_preserves_saved_template"]=true;form.Size=new Size(960,640);phase++;break;
            case 12:Need(TopWindows()==1,"Extra window after compact resize");Shot(form,Path.Combine(root,"nametag-fields-small.png"));report["compact_size"]=new[]{form.Width,form.Height};report["ok"]=true;timer.Stop();form.Close();break;
        }}catch(Exception e){report["error"]=e.GetBaseException().Message;report["stack"]=e.GetBaseException().StackTrace;Up();timer.Stop();form.Close();}};form.FormClosed+=delegate{timer.Stop();Wire.Save(Path.Combine(root,"nametag-fields-ui.json"),report);};Application.Run(form);timer.Dispose();
    }catch(Exception e){report["error"]=e.GetBaseException().Message;report["stack"]=e.GetBaseException().StackTrace;if(form!=null)form.Dispose();Wire.Save(Path.Combine(root,"nametag-fields-ui.json"),report);}}
}
}
