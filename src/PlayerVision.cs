using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SshScreen {
public sealed class PlayerRect {public int x,y,width,height;public string role="";}
public sealed class PlayerTemplate {
    public string png="";public double body_x,body_y;public List<PlayerRect> rows=new List<PlayerRect>();
    public static PlayerTemplate Capture(Bitmap image,IList<Rectangle> areas,Point body,IList<string> roles=null){
        if(areas.Count<1)throw new IOException("请先框选模板");Rectangle box=areas[0];foreach(var r in areas.Skip(1))box=Rectangle.Union(box,r);
        if(box.Width<4||box.Height<3||box.Width>512||box.Height>512||(long)box.Width*box.Height>131072)throw new IOException("请缩小模板范围，只框选文字或脸部");
        var result=new PlayerTemplate{body_x=body.X-box.X,body_y=body.Y-box.Y};using(var bitmap=new Bitmap(box.Width,box.Height,PixelFormat.Format24bppRgb)){
            using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Lime);int index=0;foreach(var r in areas){var dest=new Rectangle(r.X-box.X,r.Y-box.Y,r.Width,r.Height);g.DrawImage(image,dest,r,GraphicsUnit.Pixel);result.rows.Add(new PlayerRect{x=dest.X,y=dest.Y,width=dest.Width,height=dest.Height,role=roles!=null&&index<roles.Count?roles[index]:""});index++;}}
            using(var ms=new MemoryStream()){bitmap.Save(ms,ImageFormat.Png);result.png=Convert.ToBase64String(ms.ToArray());}
        }return result;
    }
}
public sealed class PlayerProfile {
    public int version=1;public string name="当前角色",mode="nametag";public PlayerTemplate nametag=new PlayerTemplate(),left=new PlayerTemplate(),right=new PlayerTemplate(),left_hit=new PlayerTemplate(),right_hit=new PlayerTemplate();
    public PlayerMinimap minimap;
    public double global_threshold=0.2,audit_threshold=0.12,audit_score_margin=0.05,audit_unique_margin=0.03,reacquire_threshold=0.5,jump_reject_dist=150,parts_single_radius=60;
    public double guard_margin=100,guard_motion_scale=32,guard_vertical_scale=80,guard_expand_step=50,guard_expand_max=300,blink_threshold=0.12,direction_hold_seconds=0.9;
    public int audit_confirm_frames=3,reacquire_frames=40,jump_confirm_frames=3,guard_expand_after=40,confirm_samples=2;
    public double expression_x=0,expression_y=25,yolo_conf=.7,yolo_iou=.8;
    public double name_threshold=0.2,parts_threshold=0.16,face_threshold=0.15,direction_margin=0.03,head_x=0,head_y=-40;
    public int local_radius=120,audit_frames=20,face_half_width=80,face_half_height=70,search_y_end;
}
public sealed class PlayerMinimap {
    public int offsetX,offsetY;public string backgroundPng="",sourceHost="";public long window;public int sourceWidth,sourceHeight,roiX,roiY,roiWidth,roiHeight,dotR=255,dotG=255,dotB=136,tolerance=28,dotSeedX=-1,dotSeedY=-1;
    public static PlayerMinimap FromRoute(RouteDocument d){return new PlayerMinimap{window=d.window,sourceHost=d.sourceHost,sourceWidth=d.sourceWidth,sourceHeight=d.sourceHeight,roiX=d.roiX,roiY=d.roiY,roiWidth=d.roiWidth,roiHeight=d.roiHeight,dotR=d.dotR,dotG=d.dotG,dotB=d.dotB,tolerance=d.tolerance,dotSeedX=d.samples.Count>0?d.samples[d.samples.Count-1].x-d.lastOffsetX:d.dotSeedX,dotSeedY=d.samples.Count>0?d.samples[d.samples.Count-1].y-d.lastOffsetY:d.dotSeedY,offsetX=d.lastOffsetX,offsetY=d.lastOffsetY,backgroundPng=d.lastMiniPng??""};}
    public RouteDocument Route(){return new RouteDocument{window=window,sourceHost=sourceHost,sourceWidth=sourceWidth,sourceHeight=sourceHeight,roiX=roiX,roiY=roiY,roiWidth=roiWidth,roiHeight=roiHeight,dotR=dotR,dotG=dotG,dotB=dotB,tolerance=tolerance,dotSeedX=dotSeedX,dotSeedY=dotSeedY,lastOffsetX=offsetX,lastOffsetY=offsetY,lastMiniPng=backgroundPng};}
    public bool Fits(Dictionary<string,object> frame){
        if(window!=Wire.Long(frame,"window")||(sourceHost??"")!=Wire.Str(frame,"source_host"))return false;
        var calibration=Route();
        var layout=new MinimapLayout(calibration);
        Rectangle area;
        if(layout.TryArea(window,Wire.Int(frame,"width"),Wire.Int(frame,"height"),out area))return true;
        return !layout.Resolve(frame,MinimapLayout.ReferencePixels(calibration)).IsEmpty;
    }
    internal PlayerMinimap ForFrame(Dictionary<string,object> frame){
        long current=Wire.Long(frame,"window");
        if(current==0||roiWidth<24||roiHeight<24)return null;
        var calibration=Route();calibration.window=current;calibration.sourceHost=Wire.Str(frame,"source_host");
        return FromRoute(calibration);
    }
    public Dictionary<string,object> Observe(Dictionary<string,object> frame,MiniTracker tracker,RouteDocument calibration,MinimapLayout layout){
        var region=layout.Extract(frame,tracker.LastFrame);
        if(region==null)return Wire.Map("enabled",false);
        int w=Wire.Int(region,"width"),h=Wire.Int(region,"height");
        var dot=tracker.Process((byte[])region["bgr24"],w,h,calibration);
        return Wire.Map("enabled",true,"dot",dot.ok?(object)new[]{dot.x+tracker.offsetX,dot.y+tracker.offsetY}:null,
            "shape",new[]{h,w},"roi",region["minimap_roi"]);
    }
}
public static class PlayerFiles {
    public static PlayerProfile Load(string path){if(!File.Exists(path))return new PlayerProfile();if(new FileInfo(path).Length>4*1024*1024)throw new IOException("人物模板文件过大");var p=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<PlayerProfile>(File.ReadAllText(path));Validate(p);return p;}
    public static void Validate(PlayerProfile p){if(p==null||p.version!=1||p.nametag==null||p.left==null||p.right==null)throw new IOException("人物模板格式无效");foreach(var t in new[]{p.nametag,p.left,p.right,p.left_hit,p.right_hit}){if(t==null)throw new IOException("模板格式无效");if(t.png.Length>800000||Math.Abs(t.body_x)>4096||Math.Abs(t.body_y)>4096)throw new IOException("模板参数无效");if(t.png.Length>0)using(var image=RouteFiles.Png(t.png)){if(image.Width>512||image.Height>512||(long)image.Width*image.Height>131072)throw new IOException("模板范围过大");}}}
    public static void Save(string path,PlayerProfile p){Validate(p);Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(p),new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,path+".bak",true);else File.Move(tmp,path);}
}
sealed class FrameRecognitionException:IOException {
    public FrameRecognitionException(string message):base(message){}
}
public sealed class PlayerRuntime:IDisposable {
    Process process;ProcessJob job;readonly object gate=new object();int disposed;public int Pid{get{return process==null?0:process.Id;}}
    public PlayerRuntime(string root){
        string native=Path.Combine(root,"native-vision");string executable=Path.Combine(native,"RecognitionHost.exe");
        if(!File.Exists(executable))throw new IOException("原版识别运行库未准备");
        var start=new ProcessStartInfo{FileName=executable,WorkingDirectory=native,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.EnvironmentVariables["TEMP"]=start.EnvironmentVariables["TMP"]=Path.Combine(native,"temp");start.EnvironmentVariables["APPDATA"]=start.EnvironmentVariables["LOCALAPPDATA"]=Path.Combine(native,"profile");start.EnvironmentVariables["QT_QPA_PLATFORM"]="offscreen";start.EnvironmentVariables["SSH_VISION_WORKER"]="1";start.EnvironmentVariables.Remove("SSH_VISION_CONTRACT");start.EnvironmentVariables["OPENBLAS_NUM_THREADS"]=start.EnvironmentVariables["OMP_NUM_THREADS"]="1";
        process=Process.Start(start);job=new ProcessJob(process);process.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e){if(!String.IsNullOrEmpty(e.Data))try{File.WriteAllText(Path.Combine(root,"player-worker-error.log"),e.Data.Substring(0,Math.Min(16000,e.Data.Length)));}catch{}};process.BeginErrorReadLine();
    }
    public PlayerRuntime(string root,PlayerProfile profile):this(root){try{Initialize(profile);}catch{Dispose();throw;}}
    public void Initialize(PlayerProfile profile){var serializer=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};Call(Wire.Map("op","profile","profile",serializer.DeserializeObject(serializer.Serialize(profile))),null);}
    Dictionary<string,object> Call(Dictionary<string,object> header,byte[] data){
        lock(gate){
            if(Volatile.Read(ref disposed)!=0)throw new IOException("识别已停止");
            var p=process;
            var task=Task.Factory.StartNew(()=>{Wire.Send(p.StandardInput.BaseStream,header,data);return Wire.Receive(p.StandardOutput.BaseStream).Header;});
            bool frame=Wire.Str(header,"op")=="frame";
            if(!task.Wait(frame?5000:20000)){Dispose();throw new TimeoutException("人物识别响应超时");}
            var result=task.Result;
            if(!Wire.Bool(result,"ok")){
                string error=Wire.Str(result,"error","人物识别失败");
                if(frame)throw new FrameRecognitionException(error);
                throw new IOException(error);
            }
            return result;
        }
    }
    public Dictionary<string,object> Locate(Dictionary<string,object> frame){object minimap;frame.TryGetValue("vision_minimap",out minimap);return Call(Wire.Map("op","frame","width",Wire.Int(frame,"width"),"height",Wire.Int(frame,"height"),"sequence",Wire.Long(frame,"sequence"),"window",Wire.Long(frame,"window"),"minimap",minimap),(byte[])frame["bgr24"]);}
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;var p=process;process=null;if(job!=null){job.Dispose();job=null;}if(p!=null){try{if(!p.HasExited)p.Kill();}catch{}try{p.Dispose();}catch{}}}
}
public sealed class PlayerPanel:UserControl {
    readonly string root,path;readonly Func<Dictionary<string,object>> capture;readonly Action<Action<Dictionary<string,object>>,Action<string>> prepare;readonly Action<string> requestCapture;readonly Action<Dictionary<string,object>> publish;
    readonly Label state=new Label(),result=new Label(),bodyState=new Label();readonly ComboBox mode=new WorkspaceComboBox{Width=224};readonly Button begin=new Button(),stop=new AppleButton();readonly Dictionary<string,PictureBox> pictures=new Dictionary<string,PictureBox>();readonly Dictionary<string,Label> labels=new Dictionary<string,Label>();
    readonly Dictionary<string,ComboBox> variants=new Dictionary<string,ComboBox>();public Func<RouteDocument> Calibration;public Func<bool> UseRouteCalibration;
    public bool ManagedPreset;public Action RequestMinimap;public Action<string> Failed;public Action ConfigurationChanged;public Func<RolePreset> CurrentRole;bool loadingProfile,preparing;readonly Label minimapState=new Label();PlayerRuntime runtime;volatile bool active;int generation;public PlayerProfile Profile;public Dictionary<string,object> Current;public bool Active{get{return active;}}
    public PlayerPanel(string directory,Func<Dictionary<string,object>> snapshot,Action<Action<Dictionary<string,object>>,Action<string>> ready,Action<string> selection,Action<Dictionary<string,object>> output){
        root=directory;path=Path.Combine(root,"players","current.json");capture=snapshot;prepare=ready;requestCapture=selection;publish=output;Dock=DockStyle.Fill;AutoScroll=true;BackColor=AppUi.Background;ForeColor=AppUi.Text;Font=new Font("Microsoft YaHei UI",10);
        try{Profile=PlayerFiles.Load(path);}catch(Exception e){Profile=new PlayerProfile();state.Text=e.Message;}
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(18,4,18,18),MaximumSize=new Size(690,0)};
        foreach(string slot in new[]{"nametag","left","right"}){
            var card=new AppleFlowCard{AutoSize=true,WrapContents=true,Dock=DockStyle.Top,Padding=new Padding(15,11,15,11),Margin=new Padding(0,0,0,12),BackColor=AppUi.Surface};
            string title=Title(slot);var label=new Label{Text=title,Width=154,Height=56,TextAlign=ContentAlignment.MiddleLeft,ForeColor=AppUi.Text};labels[slot]=label;var pic=new PictureBox{Width=124,Height=56,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(33,39,49)};pictures[slot]=pic;if(slot=="nametag")card.Controls.Add(label);else{var variant=new WorkspaceComboBox{Width=154,Margin=new Padding(3,15,3,0)};variant.Items.AddRange(new object[]{Title(slot),slot=="left"?"受击左脸":"受击右脸"});variant.SelectedIndex=0;variants[slot]=variant;variant.SelectedIndexChanged+=delegate{Stop("模板已更新");RefreshTemplates();};card.Controls.Add(variant);}card.Controls.Add(pic);AppUi.Card(card);
            var shot=new AppleButton{Name="Capture_"+slot,Text="采集",Width=56,Height=30,Margin=new Padding(6,8,3,0)};shot.Click+=delegate{Stop("采集模板，识别已暂停");requestCapture(SelectedSlot(slot));};var import=new AppleButton{Name="Import_"+slot,Text="导入",Width=56,Height=30,Margin=new Padding(3,8,3,0)};import.Click+=delegate{Import(SelectedSlot(slot));};AppUi.Button(shot);AppUi.Button(import);shot.Margin=new Padding(6,8,3,0);import.Margin=new Padding(3,8,3,0);card.Controls.Add(shot);card.Controls.Add(import);layout.Controls.Add(card);
            if(slot=="nametag"){var fields=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,0,0,8)};fields.Controls.Add(new Label{AutoSize=true,Text="姓名 · 家族 · 勋章",ForeColor=AppUi.Muted,Margin=new Padding(3,0,12,0)});bodyState.AutoSize=true;bodyState.ForeColor=AppUi.Muted;bodyState.Margin=new Padding(3,0,3,0);fields.Controls.Add(bodyState);layout.Controls.Add(fields);}
        }
        var miniRow=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,2,0,8)};var miniButton=new AppleButton{Text="标定小地图",Width=126,Height=32};AppUi.Button(miniButton);miniButton.Click+=delegate{if(RequestMinimap!=null)RequestMinimap();};minimapState.AutoSize=true;minimapState.ForeColor=AppUi.Muted;minimapState.Margin=new Padding(8,10,0,0);miniRow.Controls.AddRange(new Control[]{miniButton,minimapState});layout.Controls.Add(miniRow);
        var modeRow=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,0,0,8)};mode.Items.AddRange(new object[]{"姓名牌 · 头像","姓名牌 · 表情 · 头像","表情 · 头像"});mode.SelectedIndex=Profile.mode=="nametag_expression"||Profile.mode=="hybrid"?1:Profile.mode=="yolo_expression"?2:0;modeRow.Controls.Add(new Label{Text="识别方式",Width=136,Height=29,TextAlign=ContentAlignment.MiddleLeft,ForeColor=AppUi.Text});modeRow.Controls.Add(mode);layout.Controls.Add(modeRow);mode.SelectedIndexChanged+=delegate{if(loadingProfile)return;Stop("识别方式已更新");Profile.mode=mode.SelectedIndex==1?"nametag_expression":mode.SelectedIndex==2?"yolo_expression":"nametag";PersistProfile();if(ConfigurationChanged!=null)ConfigurationChanged();};
        state.AutoSize=true;state.ForeColor=AppUi.Muted;state.Margin=new Padding(3,8,3,8);layout.Controls.Add(state);
        var parameters=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Visible=false};var disclosure=new AppleButton{Text="›  定位参数",Width=200,Height=32,TextAlign=ContentAlignment.MiddleLeft,BackColor=AppUi.Surface,ForeColor=AppUi.Muted};AppUi.Button(disclosure);disclosure.Click+=delegate{parameters.Visible=!parameters.Visible;disclosure.Text=parameters.Visible?"⌄  定位参数":"›  定位参数";};layout.Controls.Add(disclosure);layout.Controls.Add(parameters);
        AddNumber(parameters,"姓名匹配阈值",()=>Profile.name_threshold,v=>Profile.name_threshold=v,0,1);
        AddNumber(parameters,"全图匹配阈值",()=>Profile.global_threshold,v=>Profile.global_threshold=v,0,1);
        AddNumber(parameters,"头像匹配阈值",()=>Profile.face_threshold,v=>Profile.face_threshold=v,0,1);
        AddNumber(parameters,"模型置信度",()=>Profile.yolo_conf,v=>Profile.yolo_conf=v,.01,1);
        AddNumber(parameters,"模型去重 IoU",()=>Profile.yolo_iou,v=>Profile.yolo_iou=v,.01,1);
        AddNumber(parameters,"表情身体偏移 Y",()=>Profile.expression_y,v=>Profile.expression_y=v,-4096,4096);
        Controls.Add(layout);RefreshTemplates();
        if(String.IsNullOrEmpty(state.Text))state.Text=Profile.nametag.png.Length>0?"模板就绪":"未采集";
    }
    void PersistProfile(){if(!ManagedPreset)PlayerFiles.Save(path,Profile);}
    void AddNumber(TableLayoutPanel rows,string text,Func<double> get,Action<double> set,double min,double max){var row=new FlowLayoutPanel{Width=334,Height=34,WrapContents=false};row.Controls.Add(new Label{Text=text,Width=196,Height=28,TextAlign=ContentAlignment.MiddleLeft,ForeColor=AppUi.Muted});var box=new TextBox{Text=get().ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),Width=100};box.Tag=get;box.Validated+=delegate{double n;if(Double.TryParse(box.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out n)&&n>=min&&n<=max){if(Math.Abs(get()-n)<0.0000001)return;Stop("预设已更新");set(n);PersistProfile();if(ConfigurationChanged!=null)ConfigurationChanged();}else box.Text=get().ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);};row.Controls.Add(box);rows.Controls.Add(row);}
    public void SetProfile(PlayerProfile value){Stop("预设已切换");loadingProfile=true;Profile=value;mode.SelectedIndex=value.mode=="yolo_expression"?2:value.mode=="nametag_expression"||value.mode=="hybrid"?1:0;foreach(Control parent in Controls)RefreshNumberFields(parent);RefreshTemplates();loadingProfile=false;}
    void RefreshNumberFields(Control parent){foreach(Control c in parent.Controls){var get=c.Tag as Func<double>;if(get!=null)c.Text=get().ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);RefreshNumberFields(c);}}
    public static string Title(string slot){return slot=="nametag"?"姓名牌":slot=="left"?"左脸":slot=="left_hit"?"受击左脸":slot=="right_hit"?"受击右脸":"右脸";}
    string SelectedSlot(string slot){return variants.ContainsKey(slot)&&variants[slot].SelectedIndex==1?slot+"_hit":slot;}
    public void SetMinimap(RouteDocument d){Stop("小地图已标定");Profile.minimap=PlayerMinimap.FromRoute(d);PersistProfile();state.Text="小地图已标定";if(ConfigurationChanged!=null)ConfigurationChanged();}
    PlayerTemplate Get(string slot){return slot=="nametag"?Profile.nametag:slot=="left"?Profile.left:slot=="left_hit"?Profile.left_hit:slot=="right_hit"?Profile.right_hit:Profile.right;}
    public void SetTemplate(string slot,PlayerTemplate template){Stop("模板已更新，保持暂停");if(slot=="nametag"){Profile.nametag=template;Profile.search_y_end=0;}else if(slot=="left")Profile.left=template;else if(slot=="left_hit")Profile.left_hit=template;else if(slot=="right_hit")Profile.right_hit=template;else Profile.right=template;PersistProfile();RefreshTemplates();state.Text="模板已保存";if(ConfigurationChanged!=null)ConfigurationChanged();}
    void RefreshTemplates(){minimapState.Text=Profile.minimap==null?"未标定":"已标定";bodyState.Text="身体中心 · "+(Profile.nametag.png.Length>0?"已设置":"未设置");foreach(string slot in pictures.Keys){var template=Get(SelectedSlot(slot));var old=pictures[slot].Image;pictures[slot].Image=String.IsNullOrEmpty(template.png)?null:RouteFiles.Png(template.png);if(old!=null)old.Dispose();labels[slot].Text=Title(slot);}}
    void Import(string slot){using(var dialog=new OpenFileDialog{Title="导入已裁好的"+Title(slot),Filter="PNG模板 (*.png)|*.png"})if(dialog.ShowDialog(this)==DialogResult.OK)try{using(var image=new Bitmap(dialog.FileName)){
        var previous=Get(slot);int oldHeight=0,oldWidth=0;if(previous.png.Length>0)using(var old=RouteFiles.Png(previous.png)){oldHeight=old.Height;oldWidth=old.Width;}
        var template=PlayerTemplate.Capture(image,new[]{new Rectangle(0,0,image.Width,image.Height)},new Point(image.Width/2+(slot=="nametag"?(int)previous.body_x-oldWidth/2:0),slot=="nametag"?image.Height+(oldHeight>0?(int)previous.body_y-oldHeight:-40):image.Height/2+40));
        string metadata=Path.ChangeExtension(dialog.FileName,".rows.json");if(slot=="nametag"&&File.Exists(metadata)){var rows=Wire.Load(metadata);if(Wire.Str(rows,"sha256").ToLowerInvariant()!=Wire.Hash(File.ReadAllBytes(dialog.FileName)))throw new IOException("分行文件与PNG不匹配");var regions=new List<PlayerRect>();foreach(object entry in (System.Collections.IEnumerable)rows["regions"]){var r=Wire.IntArray(entry);if(r.Length!=4||r[0]<0||r[1]<0||r[2]<=0||r[3]<=0||r[0]+r[2]>image.Width||r[1]+r[3]>image.Height)throw new IOException("分行区域无效");regions.Add(new PlayerRect{x=r[0],y=r[1],width=r[2],height=r[3]});}template.rows=regions;var offset=Wire.IntArray(rows["offset"]);template.body_x=image.Width/2+offset[0];template.body_y=image.Height+offset[1];}
        SetTemplate(slot,template);}}catch(Exception e){state.Text=e.Message;}}
    public void Start(){if(active||preparing)return;if(Profile.nametag.png.Length==0&&Profile.mode!="yolo_expression"){state.Text="请先采集姓名牌";return;}preparing=true;int request=++generation;state.Text="预设就绪";begin.Enabled=false;stop.Enabled=true;prepare(delegate(Dictionary<string,object> frame){if(request==generation)StartReady(frame);},delegate(string error){if(request==generation){preparing=false;state.Text="预设就绪";begin.Enabled=true;stop.Enabled=false;if(Failed!=null)Failed(error);}});}
    internal PlayerMinimap MinimapForFrame(Dictionary<string,object> frame){
        var calibration=Calibration==null?null:Calibration();
        var selected=calibration==null?null:PlayerMinimap.FromRoute(calibration).ForFrame(frame);
        if(selected!=null||(UseRouteCalibration!=null&&UseRouteCalibration()))return selected;
        return Profile.minimap==null?null:Profile.minimap.ForFrame(frame);
    }
    internal void StartPrepared(Dictionary<string,object> frame){if(!active&&!preparing)StartReady(frame);}
    void StartReady(Dictionary<string,object> frame){if(IsDisposed)return;preparing=false;try{var profile=WorkspaceFiles.Copy(Profile);profile.minimap=MinimapForFrame(frame);active=true;int epoch=++generation;long window=Wire.Long(frame,"window");begin.Enabled=false;stop.Enabled=true;state.Text="预设已应用";new Thread(()=>Run(epoch,window,profile)){IsBackground=true}.Start();}catch{Stop("模板不可用");state.Text="模板不可用";if(Failed!=null)Failed("角色素材不可用");}}
    void Run(int epoch,long window,PlayerProfile profile){
        PlayerRuntime own=null;
        try{
            var tracker=new MiniTracker();
            var calibration=profile.minimap==null?null:profile.minimap.Route();
            var layout=calibration==null?null:new MinimapLayout(calibration);
            if(calibration!=null){
                tracker.Seed(MinimapLayout.ReferencePixels(calibration),calibration.roiWidth,calibration.roiHeight,
                    calibration.lastOffsetX,calibration.lastOffsetY);
            }
            long previous=-1;
            while(active&&epoch==generation){
                var watch=Stopwatch.StartNew();
                try{
                    if(own==null){
                        own=new PlayerRuntime(root);
                        if(!active||epoch!=generation)return;
                        runtime=own;own.Initialize(profile);previous=-1;
                        if(!active||epoch!=generation)return;
                        watch.Restart();
                    }
                    var frame=capture();
                    double readAt=watch.Elapsed.TotalMilliseconds;
                    if(Wire.Long(frame,"window")!=window)throw new IOException("目标已改变，识别已暂停");
                    if(Wire.Bool(frame,"cursor_in_game"))throw new IOException("请把电脑1鼠标移到游戏窗外后继续");
                    long sequence=Wire.Long(frame,"sequence");
                    if(sequence!=previous){
                        previous=sequence;
                        if(profile.minimap!=null)frame["vision_minimap"]=profile.minimap.Observe(frame,tracker,calibration,layout);
                        var answer=own.Locate(frame);
                        var role=CurrentRole==null?null:CurrentRole();
                        if(role!=null){
                            int w=Wire.Int(frame,"width"),h=Wire.Int(frame,"height");
                            answer["hp"]=BattleGeometry.Meter((byte[])frame["bgr24"],w,h,role.hp,true);
                            answer["mp"]=BattleGeometry.Meter((byte[])frame["bgr24"],w,h,role.mp,false);
                        }
                        object mini;
                        if(frame.TryGetValue("vision_minimap",out mini)&&mini is Dictionary<string,object>){
                            var m=(Dictionary<string,object>)mini;object dot;m.TryGetValue("dot",out dot);
                            answer["minimap_dot"]=dot;answer["minimap_active"]=Wire.Bool(m,"enabled");answer["minimap_fresh"]=dot!=null;
                        }
                        double inputAge=Convert.ToDouble(frame["age_ms"]);
                        SafeUi(delegate{
                            if(active&&epoch==generation){
                                answer["capture_age_ms"]=inputAge+watch.Elapsed.TotalMilliseconds-readAt;
                                Current=answer;publish(answer);
                            }
                        });
                    }
                }catch(FrameUnavailableException){}
                catch(FrameRecognitionException){}
                catch(Exception failure){
                    var cause=failure.GetBaseException();
                    if(!(cause is TimeoutException)&&!(failure is AggregateException&&cause is IOException))throw;
                    if(own!=null)own.Dispose();
                    if(runtime==own)runtime=null;
                    own=null;
                    Thread.Sleep(350);
                }
                Thread.Sleep(Math.Max(1,100-(int)watch.ElapsedMilliseconds));
            }
        }catch(Exception e){
            string message=e.GetBaseException().Message;
            SafeUi(delegate{
                if(epoch!=generation)return;
                try{File.WriteAllText(Path.Combine(root,"player-worker-error.log"),message);}catch{}
                Stop("识别不可用");if(Failed!=null)Failed(message);
            });
        }finally{
            if(own!=null)own.Dispose();
            if(runtime==own)runtime=null;
        }
    }
    void Display(Dictionary<string,object> answer){string direction=Wire.Str(answer,"direction");string facing=direction=="left"?"朝左":direction=="right"?"朝右":"朝向未确认";if(direction!="unknown"&&!Wire.Bool(answer,"direction_fresh"))facing+="（上次确认）";object body;if(Wire.Bool(answer,"found")&&answer.TryGetValue("body",out body)){int[] xy=Wire.IntArray(body);result.Text="位置 "+xy[0]+", "+xy[1]+"  ·  "+facing;}else result.Text="未定位  ·  "+facing;object targets;int count=0;if(answer.TryGetValue("monsters",out targets)&&targets is System.Collections.IEnumerable)foreach(object unused in (System.Collections.IEnumerable)targets)count++;result.Text+="  ·  怪物 "+count;}
    public void Stop(string reason){active=false;preparing=false;Interlocked.Increment(ref generation);var p=runtime;runtime=null;if(p!=null)p.Dispose();Current=null;publish(null);if(!IsDisposed){state.Text=reason=="识别不可用"?reason:reason.Contains("采集")?"采集中":reason.Contains("模板")?"模板已更新":"预设就绪";result.Text="";begin.Enabled=true;stop.Enabled=false;}}
    void SafeUi(Action action){
        if(IsDisposed)return;
        try{BeginInvoke(action);}catch(InvalidOperationException){}
    }
    protected override void Dispose(bool disposing){if(disposing){Stop("已关闭");foreach(var picture in pictures.Values)if(picture.Image!=null)picture.Image.Dispose();}base.Dispose(disposing);}
}
}
