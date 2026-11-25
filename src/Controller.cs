using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SshScreen {
sealed class TargetItem {public long Handle;public string Title;public override string ToString(){return Title;}}
// Capture the action before Windows moves focus away from the preview.
sealed class ControlActionButton:AppleButton {
    public Func<bool> IsPauseRequested;
    public Func<int> InputEpoch;
    public Action PauseImmediately;
    public bool PresentsPauseAction;
    bool pointerActivation,pauseAtPointerDown;int epochAfterPointerDown;
    public bool PauseForActivation {get{return pointerActivation?pauseAtPointerDown||(InputEpoch!=null&&InputEpoch()!=epochAfterPointerDown):PresentsPauseAction||(IsPauseRequested!=null&&IsPauseRequested());}}
    protected override void WndProc(ref Message m){
        bool up=m.Msg==0x202,down=m.Msg==0x201||m.Msg==0x203;
        if(down){pointerActivation=true;pauseAtPointerDown=PresentsPauseAction||(IsPauseRequested!=null&&IsPauseRequested());if(pauseAtPointerDown&&PauseImmediately!=null)PauseImmediately();if(InputEpoch!=null)epochAfterPointerDown=InputEpoch();}
        try{base.WndProc(ref m);}finally{if(down&&InputEpoch!=null)epochAfterPointerDown=InputEpoch();if(up){pointerActivation=false;pauseAtPointerDown=false;}}
    }
}
sealed class RemoteView:Control {
    public BattleSettings Guides;public int? CombatAnchor;public Bitmap Frame;public long Sequence;public List<Rectangle> Selections=new List<Rectangle>();public Point Pick;public bool HasPick;public Dictionary<string,object> Person;public DateTime PersonAt;public Point? LieTarget;public float LieConfidence;public DateTime LieTargetAt;public Size SourceSize{get;set;}long lastPaint;public event Action<long> Painted;
    public string EmptyMessage="未连接";
    public bool MapCalibration;
    public RemoteView(){Name="RemotePreview";AccessibleName="自适应远程画面";SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);TabStop=true;BackColor=Color.FromArgb(22,27,37);}
    protected override bool IsInputKey(Keys keys){return true;}
    public Rectangle ImageRect(){if(Frame==null||SourceSize.Width<1||SourceSize.Height<1||ClientSize.Width<1||ClientSize.Height<1)return Rectangle.Empty;double s=Math.Min((double)ClientSize.Width/SourceSize.Width,(double)ClientSize.Height/SourceSize.Height);int w=Math.Max(1,Math.Min(ClientSize.Width,(int)Math.Round(SourceSize.Width*s))),h=Math.Max(1,Math.Min(ClientSize.Height,(int)Math.Round(SourceSize.Height*s)));return new Rectangle((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);}
    public bool Position(Point p,out Point remote){var r=ImageRect();if(Frame==null||r.Width<1||r.Height<1||!r.Contains(p)){remote=Point.Empty;return false;}remote=new Point(Math.Min(SourceSize.Width-1,(p.X-r.X)*SourceSize.Width/r.Width),Math.Min(SourceSize.Height-1,(p.Y-r.Y)*SourceSize.Height/r.Height));return true;}
    void PaintMonsters(Graphics graphics,Rectangle area,double sx,double sy){
        if(Person==null||(DateTime.UtcNow-PersonAt).TotalMilliseconds>=800)return;object value;if(!Person.TryGetValue("monsters",out value)||!(value is System.Collections.IEnumerable))return;
        using(var pen=new Pen(Color.FromArgb(52,199,89),3f))foreach(object item in (System.Collections.IEnumerable)value){var target=item as Dictionary<string,object>;if(target==null)continue;object position,size;if(!target.TryGetValue("position",out position)||!target.TryGetValue("size",out size))continue;int[] p=Wire.IntArray(position),d=Wire.IntArray(size);if(p.Length!=2||d.Length!=2||d[0]<=0||d[1]<=0)continue;object bp;if(Guides!=null&&Person.TryGetValue("body",out bp)&&bp!=null){int[] b=Wire.IntArray(bp);if(!BattleGeometry.Hit(BattleGeometry.Search(new Point(b[0],b[1]),Guides,CombatAnchor),new Rectangle(p[0],p[1],d[1],d[0])))continue;}graphics.DrawRectangle(pen,(float)(area.X+p[0]*sx),(float)(area.Y+p[1]*sy),(float)(d[1]*sx),(float)(d[0]*sy));}
    }
    void PaintGuides(Graphics g,Rectangle area,double sx,double sy){if(Guides==null||Person==null||(DateTime.UtcNow-PersonAt).TotalMilliseconds>800||!Wire.Bool(Person,"found"))return;object value;if(!Person.TryGetValue("body",out value)||value==null)return;var p=Wire.IntArray(value);var body=new Point(p[0],p[1]);var save=g.Save();g.SetClip(area);var boxes=new[]{BattleGeometry.Search(body,Guides,CombatAnchor),BattleGeometry.Attack(body,Guides,true),BattleGeometry.Attack(body,Guides,false)};for(int i=0;i<boxes.Length;i++){var r=boxes[i];if(r.IsEmpty)continue;using(var pen=new Pen(i==0?Color.FromArgb(0,122,255):Color.FromArgb(255,69,58),3f)){if(i==0)pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,(float)(area.X+r.X*sx),(float)(area.Y+r.Y*sy),(float)(r.Width*sx),(float)(r.Height*sy));}}int anchor=CombatAnchor??body.X;int left=Guides.mode=="patrol"?(int)(Guides.patrolRadius*Guides.screenPerMini):Guides.moveLeft,right=Guides.mode=="patrol"?left:Guides.moveRight;if(Guides.mode!="route"&&(left>0||right>0))using(var pen=new Pen(Color.FromArgb(175,82,222),3.5f)){pen.DashStyle=DashStyle.Dash;foreach(int x in new[]{anchor-left,anchor+right})g.DrawLine(pen,(float)(area.X+x*sx),(float)(area.Y+(body.Y-130)*sy),(float)(area.X+x*sx),(float)(area.Y+(body.Y+130)*sy));}g.Restore(save);}
    void PaintMapCalibration(Graphics graphics,Rectangle image,double scaleX,double scaleY){
        if(Selections.Count!=0){
            var selection=Selections[Selections.Count-1];
            var box=new RectangleF((float)(image.X+selection.X*scaleX),(float)(image.Y+selection.Y*scaleY),
                (float)(selection.Width*scaleX),(float)(selection.Height*scaleY));
            using(var outside=new Region(image))
            using(var shade=new SolidBrush(Color.FromArgb(158,13,21,29))){
                outside.Exclude(box);
                graphics.FillRegion(shade,outside);
            }
            using(var outline=new Pen(Color.FromArgb(10,20,23),5))
            using(var border=new Pen(AppUi.Blue,2.8f)){
                graphics.DrawRectangle(outline,box.X,box.Y,box.Width,box.Height);
                graphics.DrawRectangle(border,box.X,box.Y,box.Width,box.Height);
            }
        }
        if(!HasPick)return;
        float x=(float)(image.X+(Pick.X+.5)*scaleX),y=(float)(image.Y+(Pick.Y+.5)*scaleY);
        using(var outline=new Pen(Color.FromArgb(18,21,23),6))
        using(var gold=new Pen(Color.FromArgb(255,218,56),3)){
            graphics.DrawEllipse(outline,x-9,y-9,18,18);
            graphics.DrawEllipse(gold,x-9,y-9,18,18);
        }
    }

    void PaintLieTarget(Graphics graphics,Rectangle area,double sx,double sy){
        if(!LieTarget.HasValue||(DateTime.UtcNow-LieTargetAt).TotalMilliseconds>=600)return;
        float x=(float)(area.X+LieTarget.Value.X*sx),y=(float)(area.Y+LieTarget.Value.Y*sy),half=20f;
        using(var outline=new Pen(Color.FromArgb(25,35,25),7))using(var pen=new Pen(Color.Lime,3)){
            graphics.DrawRectangle(outline,x-half,y-half,half*2,half*2);graphics.DrawRectangle(pen,x-half,y-half,half*2,half*2);
            graphics.DrawLine(pen,x-5,y,x+5,y);graphics.DrawLine(pen,x,y-5,x,y+5);
        }
        string label=String.Format("测谎坐标 ({0}, {1}) 置信度 {2:0.00}",LieTarget.Value.X,LieTarget.Value.Y,LieConfidence);
        using(var brush=new SolidBrush(Color.Lime))graphics.DrawString(label,Font,brush,x+24,y-28);
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Frame==null){using(var b=new SolidBrush(Color.LightSteelBlue))e.Graphics.DrawString(EmptyMessage,Font,b,24,24);return;}e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;var area=ImageRect();if(area.IsEmpty)return;e.Graphics.DrawImage(Frame,area);double sx=(double)area.Width/SourceSize.Width,sy=(double)area.Height/SourceSize.Height;
        if(MapCalibration){PaintMapCalibration(e.Graphics,area,sx,sy);return;}
        foreach(var r in Selections)using(var pen=new Pen(Color.Cyan,2))e.Graphics.DrawRectangle(pen,(float)(area.X+r.X*sx),(float)(area.Y+r.Y*sy),(float)(r.Width*sx),(float)(r.Height*sy));if(HasPick){float x=(float)(area.X+Pick.X*sx),y=(float)(area.Y+Pick.Y*sy);using(var pen=new Pen(Color.OrangeRed,2)){e.Graphics.DrawLine(pen,x-9,y,x+9,y);e.Graphics.DrawLine(pen,x,y-9,x,y+9);}}PaintGuides(e.Graphics,area,sx,sy);PaintMonsters(e.Graphics,area,sx,sy);PaintLieTarget(e.Graphics,area,sx,sy);if(Person!=null&&(DateTime.UtcNow-PersonAt).TotalMilliseconds<800&&Wire.Bool(Person,"found")){object value;if(Person.TryGetValue("body",out value)){int[] body=Wire.IntArray(value);float x=(float)(area.X+body[0]*sx),y=(float)(area.Y+body[1]*sy);using(var pen=new Pen(Color.FromArgb(255,159,10),3)){e.Graphics.DrawEllipse(pen,x-6,y-6,12,12);e.Graphics.DrawLine(pen,x-10,y,x+10,y);e.Graphics.DrawLine(pen,x,y-10,x,y+10);}string direction=Wire.Str(Person,"direction");using(var brush=new SolidBrush(Color.Lime))e.Graphics.DrawString(direction=="left"?"← 朝左":direction=="right"?"朝右 →":"人物",Font,brush,x+9,y-22);}foreach(string key in new[]{"name_box","face_box"}){object box;if(Person.TryGetValue(key,out box)&&box!=null){int[] r=Wire.IntArray(box);using(var pen=new Pen(key=="name_box"?Color.Cyan:Color.Orange,2.5f))e.Graphics.DrawRectangle(pen,(float)(area.X+r[0]*sx),(float)(area.Y+r[1]*sy),(float)(r[2]*sx),(float)(r[3]*sy));}}}if(Sequence!=lastPaint){lastPaint=Sequence;if(Painted!=null)Painted(Sequence);}}
    protected override void Dispose(bool disposing){if(disposing&&Frame!=null){Frame.Dispose();Frame=null;}base.Dispose(disposing);}
}
sealed class SettingsPages:TabControl {
    public SettingsPages(){
        Appearance=TabAppearance.FlatButtons;SizeMode=TabSizeMode.Fixed;ItemSize=new Size(1,1);TabStop=false;
    }
    protected override void WndProc(ref Message message){
        const int adjustTabRectangle=0x1328;
        if(message.Msg==adjustTabRectangle){message.Result=IntPtr.Zero;return;}
        base.WndProc(ref message);
    }
}
public sealed class ControllerForm:Form {
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int mode);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint p);
    [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint p);
    readonly string root;readonly bool testMode;readonly RemoteView view=new RemoteView();readonly Label status=new Label(),metrics=new Label();readonly Button connect=new Button(),refresh=new Button(),typeText=new AppleButton(),lieButton=new AppleButton();readonly ControlActionButton control=new ControlActionButton();readonly ComboBox targets=new WorkspaceComboBox();readonly LocalTargetTrackClient lieVision;readonly PolygraphGameGate polygraphGate;
    readonly ConcurrentQueue<Dictionary<string,object>> queue=new ConcurrentQueue<Dictionary<string,object>>();readonly HashSet<int> pressed=new HashSet<int>();readonly object frameGate=new object();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly List<object> samples=new List<object>();
    readonly FrameDecoder nativeFrames=new FrameDecoder();
    WorkspaceSettings workspace,savedWorkspace;bool settingsDirty,setupRequired;readonly Button saveSettings=new AppleButton();RoleSelector roleSelector;BattlePanel battlePanel;SkillsPanel skillsPanel;readonly Button runButton=new AppleButton();readonly CheckBox showGuides=new CheckBox{Checked=true};readonly RichTextBox activity=new RichTextBox();readonly object observationGate=new object();Dictionary<string,object> observation;double observationAt;volatile bool autoRunning,autoPending,routeTrial;bool autoArmPending;int autoEpoch;long autoWindow;CombatPlanner planner,trialPlanner;string lastAction="";
    static double ClockMs(){return Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;}
    const double VisionHoldMs=240;
    volatile bool trialCombatEnabled;
    Dictionary<string,object> trialCombatVision;double trialCombatVisionAt;
    volatile Dictionary<string,object> lastVisionIssue;
    volatile Dictionary<string,object> lastRouteState;
    readonly object trialTraceGate=new object();
    List<Dictionary<string,object>> trialTrace;
    double trialTraceAt;
    string trialTraceRoute;
    void BeginTrialTrace(string route){
        lock(trialTraceGate){
            trialTraceAt=ClockMs();trialTraceRoute=route;
            trialTrace=new List<Dictionary<string,object>>();
        }
    }
    void TraceTrial(Dictionary<string,object> entry){
        lock(trialTraceGate){
            if(trialTrace==null||trialTrace.Count>=1200)return;
            entry["ms"]=(int)(ClockMs()-trialTraceAt);trialTrace.Add(entry);
        }
    }
    void FinishTrialTrace(string reason){
        lock(trialTraceGate){
            if(trialTrace==null)return;
            try{
                Wire.Save(Path.Combine(root,"route-trial-last.json"),Wire.Map("utc",DateTime.UtcNow.ToString("o"),
                    "route",trialTraceRoute,"result",reason,"events",trialTrace.ToArray()));
            }catch(IOException){}catch(UnauthorizedAccessException){}
            finally{trialTrace=null;}
        }
    }
    readonly Panel previewSurface=new Panel{Dock=DockStyle.Fill};
    readonly Panel minimapPanel=new Panel{Dock=DockStyle.Right,Width=185,BackColor=AppUi.Surface};
    readonly Panel mapCalibrationZoom=new Panel{Name="MapCalibrationZoom",Dock=DockStyle.Right,Width=230,Visible=false,BackColor=AppUi.Surface};
    readonly RemoteView mapCalibrationImage=new RemoteView{Dock=DockStyle.Fill,TabStop=false,MapCalibration=true,Cursor=Cursors.Cross,AccessibleName="放大的小地图黄点"};
    readonly Button mapReselect=new AppleButton{Name="ReselectMapBounds",Text="重新框选",Width=103,Height=32,Visible=false};
    readonly Button navigateButton=new AppleButton{Name="NavigateToPoint",Text="导航",Dock=DockStyle.Bottom,Height=36};
    readonly Label minimapHint=new Label{Text="小地图 · 点击选点",Dock=DockStyle.Top,Height=32,TextAlign=ContentAlignment.MiddleCenter,ForeColor=AppUi.Text};
    volatile bool pointNavigation;
    readonly Panel routeSurface=new Panel{Dock=DockStyle.Fill,Visible=false,Name="RouteMapSurface"};
    readonly SplitContainer workspacePane;
    readonly Button settingsButton=new AppleButton(),routeWorkspaceButton=new AppleButton(),previewButton=new AppleButton();
    readonly FlowLayoutPanel settingsNavigation=new FlowLayoutPanel();
    readonly List<Button> navigationButtons=new List<Button>();
    readonly Label settingsHeading=new Label(),deviceLabel=new Label();
    FlowLayoutPanel selectors;
    ConnectionPanel connectionPanel;
    SshDestination activeDestination;
    bool settingsOpen;
    TabPage routePage;
    RouteStudio routes;PlayerPanel person;readonly TabControl sideTabs=new SettingsPages();readonly FlowLayoutPanel captureBar=new FlowLayoutPanel();readonly Label captureHint=new Label();readonly Button captureNext=new Button(),captureSave=new Button(),captureCancel=new Button(),captureClearRow=new AppleButton();readonly RadioButton captureNameRow=new RadioButton(),captureFamilyRow=new RadioButton(),captureMedalRow=new RadioButton();readonly List<string> captureRoles=new List<string>();string captureRole="name";bool rangeAnchor;
    public Func<Dictionary<string,object>> FixtureSource;
    Dictionary<string,object> captureFrame;string captureSlot;bool captureDragging,capturePick;Point captureAnchor;Color captureDotColor;readonly List<Rectangle> captureAreas=new List<Rectangle>();RouteDocument captureRouteBase;Action<RouteDocument> captureRouteDone;
    int prepareId;Action<Dictionary<string,object>> prepareSuccess;Action<string> prepareFailure;
    readonly object routeGate=new object();int routeRevision;RouteDocument keyboardRecording;bool routeKeyboardReady;Dictionary<string,object> routeRequest=Wire.Map("op","record_keys","enabled",false);
    Dictionary<string,object> CaptureNativeMinimap(MinimapLayout layout,byte[] reference){
        if(FixtureSource==null&&!running)throw new IOException("尚未连接");
        if(layout.SourceHost!=SourceIdentity())throw new IOException("请为当前被控端标定小地图");
        var frame=FixtureSource==null?nativeFrames.NativeRegion(layout.TryArea):null;
        if(frame!=null){
            if(Wire.Long(frame,"window")!=Interlocked.Read(ref target)||Convert.ToDouble(frame["age_ms"])>800)
                throw new FrameUnavailableException("目标原图尚未就绪或已超时");
            return frame;
        }
        var source=CaptureNative();
        frame=layout.Extract(source,reference);
        if(frame==null)throw new FrameUnavailableException("无法确认当前窗口的小地图位置");
        return frame;
    }
    void ConfigureRouteWatch(bool enabled,RouteDocument d){lock(routeGate){routeRequest=Wire.Map("op","record_keys","enabled",enabled,"window",d.window,"keys",new[]{37,38,39,40,d.jumpKey}.Distinct().ToArray());routeRevision++;}}
    bool ControlFrameReady(long window){
        return running&&captureFrame==null&&view.Frame!=null&&displayedWindow==window&&
            (DateTime.UtcNow-lastFrame).TotalMilliseconds<=800;
    }
    void ValidateRouteSource(RouteDocument document){
        if(!running)throw new IOException("预览已断开，请重新连接");
        long window=Interlocked.Read(ref target);
        if(window==0||!ControlFrameReady(window))throw new IOException("请选择画面就绪的游戏窗口");
        document.window=window;document.sourceHost=SourceIdentity();
    }
    void ArmManualControl(long window){
        if(!ControlFrameReady(window)||!view.Focus())throw new IOException("预览未就绪，请连接并选择游戏窗口");
        armPending=true;lastPointerValid=false;
        Enqueue(Wire.Map("op","arm","window",window,"desktop_input",keyboardRecording==null&&!routes.ManualCaptureActive,"local_epoch",inputEpoch));
    }
    void StartRouteKeyboard(RouteDocument document){
        if(document.window==0||document.window!=Interlocked.Read(ref target))
            throw new IOException("请选择标定的游戏窗口");
        keyboardRecording=document;routeKeyboardReady=false;
        try{ArmManualControl(document.window);message="正在准备录制键盘…";}
        catch{keyboardRecording=null;throw;}
    }
    void FinishRouteKeyboard(){
        if(keyboardRecording!=null)Pause();
    }
    bool RouteWatchCurrent(int connection,int revision){
        lock(routeGate)return running&&connection==generation&&revision==routeRevision;
    }
    void FinishRouteWatch(int connection,int revision,long window,long watchEpoch){
        try{
            var clock=Stopwatch.StartNew();
            long baseline=-1;
            if(watchEpoch<=0)baseline=Wire.Long(FixtureSource==null?nativeFrames.Information():FixtureSource(),"sequence");
            while(clock.ElapsedMilliseconds<2000&&RouteWatchCurrent(connection,revision)){
                var frame=FixtureSource==null?nativeFrames.Information():FixtureSource();
                object value;
                var watch=frame.TryGetValue("route_input",out value)?value as Dictionary<string,object>:null;
                if(Wire.Long(frame,"window")==window&&Wire.Long(frame,"sequence")>baseline&&
                   Convert.ToDouble(frame["age_ms"])<800&&!Wire.Bool(frame,"cursor_in_game")&&
                   watch!=null&&Wire.Bool(watch,"enabled")&&Wire.Long(watch,"window")==window&&
                   (watchEpoch<=0||Wire.Long(watch,"epoch")==watchEpoch)){
                    SafeUi(()=>{if(RouteWatchCurrent(connection,revision))routes.WatchReady();});
                    return;
                }
                Thread.Sleep(15);
            }
            SafeUi(()=>{if(RouteWatchCurrent(connection,revision))routes.PauseRecording("未收到窗外的新画面，请重新开始录制");});
        }catch(IOException e){
            string reason=e.Message;
            SafeUi(()=>{if(RouteWatchCurrent(connection,revision))routes.PauseRecording(reason);});
        }
    }
    void RouteRecordingReady(){
        if(keyboardRecording==null||!armed||!view.Focused)return;
        routeKeyboardReady=true;message="录制中 · 本机键盘";
    }
    void UpdateRouteSurface(){
        if(routes==null||routes.IsDisposed||IsDisposed||Disposing)return;
        bool capture=captureFrame!=null;
        bool editing=settingsOpen&&!capture;
        bool routeTools=editing&&sideTabs.SelectedTab==routePage;
        routes.ShowTrialControls(routeTools);
        if(!editing||routeTools)workspacePane.Panel1Collapsed=false;
        if(editing)workspacePane.Panel2Collapsed=false;
        if(editing&&!routeTools)workspacePane.Panel1Collapsed=true;
        if(!editing)workspacePane.Panel2Collapsed=true;
        settingsNavigation.Visible=editing&&!routeTools;
        if(routeTools)FitRouteTools();
        selectors.Visible=!capture&&(!settingsOpen||routeTools);
        bool showMini=!capture&&(!editing||routeTools&&routes.NeedsGamePreview);
        minimapPanel.Visible=showMini;
        minimapHint.Text=editing?"小地图":"小地图 · 点击选点";
        navigateButton.Visible=!editing;
        routes.SetHomeMapVisible(showMini);
        showGuides.Visible=!settingsOpen&&!capture;
        settingsHeading.Visible=capture||settingsOpen&&!routeTools;
        settingsHeading.Text=capture?(captureSlot=="minimap"?"标定小地图":"标定预览"):sideTabs.SelectedTab.Text;
        ShowSelectedSection(previewButton,!settingsOpen||capture);
        ShowSelectedSection(routeWorkspaceButton,routeTools);
        ShowSelectedSection(settingsButton,editing&&!routeTools);
        saveSettings.Visible=editing&&!routeTools;
        for(int i=0;i<navigationButtons.Count;i++){
            var button=navigationButtons[i];
            ShowSelectedSection(button,(int)button.Tag==sideTabs.SelectedIndex);
        }
        bool show=routeTools&&!routes.NeedsGamePreview&&(prepareSuccess==null||routeTrial);
        routeSurface.Visible=show;previewSurface.Visible=!show;routes.SurfaceState(show);
        if(!show&&previewSurface.Visible){
            view.CreateControl();
            view.Invalidate();
        }
    }
    static void ShowSelectedSection(Button button,bool selected){
        button.BackColor=selected?AppUi.Selection:AppUi.Surface;
        button.ForeColor=selected?AppUi.Blue:AppUi.Muted;
    }
    void FitRouteTools(){
        if(workspacePane.Panel1Collapsed||workspacePane.Panel2Collapsed)return;
        int width=workspacePane.ClientSize.Width;
        int toolsWidth=Math.Max(workspacePane.Panel2MinSize,Math.Min(438,width*35/100));
        int split=width-workspacePane.SplitterWidth-toolsWidth;
        if(split>=workspacePane.Panel1MinSize&&workspacePane.SplitterDistance!=split)
            workspacePane.SplitterDistance=split;
    }
    void AddRouteLegend(){
        var palette=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
            Padding=new Padding(7,0,0,5),WrapContents=true,BackColor=Color.FromArgb(29,39,54)};
        var brushes=new[]{new[]{"选择","select"},new[]{"行走","walk"},new[]{"梯绳","climb"},
            new[]{"起跳","jump"},new[]{"下跳","jump_down"},new[]{"跳跃抓绳","rope_mount"},new[]{"下落","drop"},new[]{"擦除","erase"}};
        var buttons=new List<Button>();
        foreach(var brush in brushes){
            string tool=brush[1];
            var button=new AppleButton{Text=brush[0],Width=tool=="rope_mount"?80:56,Height=29,AccessibleName=brush[0],Tag=tool};
            AppUi.Button(button);button.Margin=new Padding(3,5,3,0);
            button.FlatAppearance.MouseOverBackColor=Color.FromArgb(60,76,96);
            button.FlatAppearance.MouseDownBackColor=Color.FromArgb(63,85,115);
            button.Click+=delegate{
                if(!routes.SelectBrush(tool))return;
                foreach(var item in buttons)item.BackColor=item==button?AppUi.Selection:AppUi.Field;
            };
            button.BackColor=tool=="select"?AppUi.Selection:AppUi.Field;
            button.ForeColor=tool=="select"||tool=="erase"?Color.White:RouteCanvas.ActionColor(tool);
            buttons.Add(button);palette.Controls.Add(button);
        }
        var fit=new AppleButton{Text="适合窗口",Width=86,Height=30};
        AppUi.Button(fit);fit.Margin=new Padding(12,5,2,0);fit.Click+=delegate{routes.FitRoute();};palette.Controls.Add(fit);
        palette.SetFlowBreak(fit,true);
        var zoomOut=new AppleButton{Text="缩小 −",Name="RouteZoomOut",Width=70,Height=29};
        var zoomIn=new AppleButton{Text="放大 +",Name="RouteZoomIn",Width=70,Height=29};
        var zoomLabel=new Label{Name="RouteZoomPercent",Text="100%",Width=55,Height=29,TextAlign=ContentAlignment.MiddleCenter,ForeColor=AppUi.Text};
        var clear=new AppleButton{Text="取消选择",Name="RouteClearSelection",Width=90,Height=29};
        foreach(var button in new[]{zoomOut,zoomIn,clear}){AppUi.Button(button);button.Margin=new Padding(3,3,3,0);}
        zoomOut.Click+=delegate{routes.ZoomRoute(.8f);};zoomIn.Click+=delegate{routes.ZoomRoute(1.25f);};
        clear.Click+=delegate{routes.CancelSelection();};
        routes.RouteZoomChanged+=percent=>zoomLabel.Text=percent+"%";
        palette.Controls.AddRange(new Control[]{zoomOut,zoomLabel,zoomIn,clear,
            new Label{Text="滚轮缩放 · 中键平移 · 空白拖框多选 · Esc取消",AutoSize=true,ForeColor=AppUi.Muted,Margin=new Padding(10,9,0,0)}});
        routeSurface.Controls.Add(palette);
    }
    void SetSettingsOpen(bool open){
        if(!open)routes.CancelPointCapture();
        settingsOpen=open;
        saveSettings.Visible=open;
        runButton.Visible=!open;
        UpdateRouteSurface();
    }
    void ReturnToPreview(){if(!settingsOpen||ResolveUnsaved())SetSettingsOpen(false);}
    void OpenSettingsPage(int index){
        if(index!=3)routes.CancelPointCapture();
        sideTabs.SelectedIndex=index;
        SetSettingsOpen(true);
    }
    void OpenRoutes(){Pause();OpenSettingsPage(3);}
    void OpenPerson(){Pause();OpenSettingsPage(0);}
    string SourceIdentity(){return (running&&activeDestination!=null?activeDestination:savedWorkspace.ssh).Identity();}
    public Dictionary<string,object> CaptureNative(){if(FixtureSource!=null)return FixtureSource();if(!running)throw new IOException("尚未连接");var frame=nativeFrames.NativeSnapshot();if(Wire.Long(frame,"window")!=Interlocked.Read(ref target)||Convert.ToDouble(frame["age_ms"])>800)throw new FrameUnavailableException("目标原图尚未就绪或已超时");frame["source_host"]=SourceIdentity();return frame;}
    readonly object connectionGate=new object();
    const int FrameTimeoutSeconds=6;
    volatile bool running,armed,armPending,releaseRequested,previewReady;
    volatile string connectionFault;
    long target,lastDecodedPreviewTick;
    bool changingTargetList,userSelectedTarget;
    int generation,inputEpoch;
    Thread connectionWorker;
    Session session;Link frameLink,controlLink;Bitmap newest;Dictionary<string,object> newestHeader,currentHeader;
    bool lastPointerValid;Point lastPointer;long lastPointerWindow;
    volatile string message="未连接";DateTime lastFrame=DateTime.MinValue,lastMove=DateTime.MinValue;long decoded,painted,bytes,hashChanges,previousDecoded,previousPainted,previousBytes,previousHashChanges,lastPaintTick,maxGapTicks;string oldHash="";long bucketAt=Stopwatch.GetTimestamp(),connectedAt,lieSubmitAt,lastLieSequence,lastLieGateAt,lastLabels;double fps,decodeFps,mbps,decodeMs;long displayedWindow=-1;int disconnecting,metricsWritePending,lieGateHits,lieGateMisses;bool lieRequested,lieGameActive;byte[] lieGatePixels;
    public ControllerForm(string path,bool test,bool autoConnect=true){root=path;testMode=test;lieVision=new LocalTargetTrackClient(root);polygraphGate=new PolygraphGameGate(Path.Combine(root,"lie-runtime"));workspace=WorkspaceFiles.Load(root);savedWorkspace=WorkspaceFiles.Copy(workspace);setupRequired=!File.Exists(Path.Combine(root,"workspace.json"));settingsDirty=false;Text="冒险工作台";BackColor=AppUi.Background;ForeColor=AppUi.Text;var work=Screen.PrimaryScreen.WorkingArea;Size=new Size(Math.Min(1120,work.Width-24),Math.Min(720,work.Height-24));MinimumSize=new Size(960,640);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);KeyPreview=true;
        var navigation=new FlowLayoutPanel{Name="WorkspaceNavigation",Dock=DockStyle.Top,Height=49,WrapContents=false,
            Padding=new Padding(15,6,15,4),BackColor=AppUi.Surface};
        previewButton.Text="预览";routeWorkspaceButton.Text="路线工坊";lieButton.Text="开启测谎";
        routeWorkspaceButton.Name="WorkspaceRoutesButton";lieButton.Name="LieVisionButton";
        foreach(var button in new[]{previewButton,routeWorkspaceButton,lieButton,settingsButton}){
            button.Size=new Size(102,32);AppUi.Button(button);navigation.Controls.Add(button);
        }
        previewButton.Click+=delegate{ReturnToPreview();};
        routeWorkspaceButton.Click+=delegate{if(!settingsOpen||sideTabs.SelectedTab!=routePage)OpenRoutes();};
        lieButton.Click+=delegate{ToggleLieVision();};
        var bar=new Panel{Dock=DockStyle.Top,Height=50,Padding=new Padding(18,7,18,5),BackColor=AppUi.Background};
        selectors=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Name="HomeRouteSelectors",Margin=Padding.Empty};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Right,Width=212,WrapContents=false,
            FlowDirection=FlowDirection.RightToLeft,Margin=Padding.Empty};
        settingsHeading.Dock=DockStyle.Fill;settingsHeading.TextAlign=ContentAlignment.MiddleLeft;
        settingsHeading.Font=new Font(Font.FontFamily,14,FontStyle.Bold);
        bar.Controls.Add(selectors);bar.Controls.Add(settingsHeading);bar.Controls.Add(actions);
        settingsButton.Name="WorkspaceSettingsButton";settingsButton.Text="设置";settingsButton.Size=new Size(94,32);
        AppUi.Button(settingsButton);
        settingsButton.Click+=delegate{
            if(!settingsOpen||sideTabs.SelectedTab==routePage){Pause();OpenSettingsPage(4);}
        };
        connect.Name="ConnectButton";connect.Text="连接";connect.Size=new Size(122,34);connect.Click+=delegate{if(running)Disconnect();else Connect();};
        targets.Name="TargetWindows";targets.DropDownStyle=ComboBoxStyle.DropDownList;targets.Width=280;targets.Items.Add(new TargetItem{Handle=0,Title="整个主屏幕"});targets.SelectedIndex=0;
        targets.SelectedIndexChanged+=delegate{
            if(changingTargetList)return;
            userSelectedTarget=true;var selected=targets.SelectedItem as TargetItem;
            SelectPreviewTarget(selected==null?0:selected.Handle);
        };
        refresh.Text="刷新";refresh.Size=new Size(64,32);refresh.Click+=delegate{Enqueue(Wire.Map("op","windows"));};
        control.Name="ControlButton";control.Text="开启手动控制";control.Size=new Size(145,32);control.Enabled=false;control.IsPauseRequested=()=>armed||armPending;control.InputEpoch=()=>Volatile.Read(ref inputEpoch);control.PauseImmediately=Pause;control.Click+=delegate{if(control.PauseForActivation){Pause();return;}try{routes.ShowGamePreview();ArmManualControl(Interlocked.Read(ref target));}catch(IOException e){message=e.Message;}};
        typeText.Name="TextButton";typeText.Text="输入文字";typeText.Size=new Size(92,32);typeText.Click+=delegate{SendTextDialog();};
        saveSettings.Text="保存设置";saveSettings.Size=new Size(100,32);saveSettings.Click+=delegate{SaveSettings();};
        runButton.Text="开启脚本";runButton.Enabled=false;runButton.Size=new Size(100,32);
        runButton.Click+=delegate{
            if(autoRunning||autoPending){Pause();message="已暂停";Log("运行已暂停");}
            else StartAutomation();
        };
        showGuides.Name="RecognitionPreviewToggle";showGuides.Text="识别预览";showGuides.AutoSize=true;
        showGuides.Anchor=AnchorStyles.Left;showGuides.Margin=new Padding(10,0,0,0);
        showGuides.CheckedChanged+=delegate{
            if(showGuides.Checked){StartPreview();return;}
            view.Guides=null;view.Person=null;view.Invalidate();
            if(!autoPending&&!autoRunning&&!routeTrial&&person!=null)person.Stop("识别预览已关闭");
        };
        AppUi.Button(saveSettings,true);AppUi.Button(runButton,true);
        AppUi.Button(connect,true);AppUi.Button(refresh);AppUi.Button(control);AppUi.Button(typeText);
        lieVision.Target=delegate(LieVisionTarget target){SafeUi(delegate{if(!lieRequested||!lieGameActive||!lieVision.Enabled)return;view.LieTarget=target.Point;view.LieConfidence=target.Confidence;view.LieTargetAt=DateTime.UtcNow;view.Invalidate();});};
        lieVision.StateChanged=delegate(string text){SafeUi(delegate{if(lieRequested&&lieGameActive){message="测谎识别 · "+text;Log(message);}});};
        actions.Controls.AddRange(new Control[]{saveSettings,runButton});
        var bottom=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=43,ColumnCount=5,RowCount=1,
            Padding=new Padding(18,0,18,0),BackColor=AppUi.Surface};
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,164));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,154));
        status.Dock=metrics.Dock=DockStyle.Fill;status.AutoEllipsis=true;
        status.TextAlign=ContentAlignment.MiddleLeft;metrics.TextAlign=ContentAlignment.MiddleRight;
        status.ForeColor=metrics.ForeColor=AppUi.Muted;
        deviceLabel.Dock=DockStyle.Fill;deviceLabel.AutoEllipsis=true;
        deviceLabel.TextAlign=ContentAlignment.MiddleLeft;deviceLabel.ForeColor=AppUi.Text;
        control.Anchor=AnchorStyles.Right;
        bottom.Controls.Add(deviceLabel,0,0);bottom.Controls.Add(status,1,0);
        bottom.Controls.Add(metrics,2,0);bottom.Controls.Add(showGuides,3,0);bottom.Controls.Add(control,4,0);
        workspacePane=new SplitContainer{Dock=DockStyle.Fill,Size=new Size(1080,650),SplitterDistance=730,
            Panel1MinSize=300,Panel2MinSize=350,FixedPanel=FixedPanel.Panel2};
        workspacePane.Name="UnifiedWorkspace";workspacePane.SplitterWidth=6;workspacePane.BackColor=AppUi.Border;
        workspacePane.Resize+=delegate{FitRouteTools();};
        var preview=previewSurface;view.Dock=DockStyle.Fill;captureBar.Dock=DockStyle.Top;captureBar.AutoSize=true;captureBar.Padding=new Padding(4);captureBar.BackColor=AppUi.Surface;captureBar.Visible=false;captureHint.AutoSize=true;captureHint.MaximumSize=new Size(850,0);captureNext.Text="指定身体中心";captureNext.Width=130;captureNext.Height=captureSave.Height=captureCancel.Height=32;captureSave.Text="保存模板";captureSave.Width=95;captureSave.Enabled=false;captureCancel.Text="取消";captureCancel.Width=65;captureNext.Click+=delegate{if(captureAreas.Count>0){capturePick=true;captureHint.Text="点击身体中心";}};captureSave.Click+=delegate{SaveCapture();};captureCancel.Click+=delegate{CancelCapture();if(showGuides.Checked)BeginInvoke(new Action(StartPreview));};AppUi.Button(captureNext);AppUi.Button(captureSave,true);AppUi.Button(captureCancel);SetupCaptureRole(captureNameRow,"姓名","name");SetupCaptureRole(captureFamilyRow,"家族","family");SetupCaptureRole(captureMedalRow,"勋章","medal");captureClearRow.Text="清除";captureClearRow.Width=60;captureClearRow.Height=32;AppUi.Button(captureClearRow);captureClearRow.Click+=delegate{capturePick=false;int at=captureRoles.IndexOf(captureRole);if(at>=0){captureRoles.RemoveAt(at);captureAreas.RemoveAt(at);}UpdateCaptureRows();};captureBar.Controls.AddRange(new Control[]{captureHint,captureNameRow,captureFamilyRow,captureMedalRow,captureClearRow,captureNext,captureSave,captureCancel});captureBar.SetFlowBreak(captureHint,true);preview.Controls.Add(view);preview.Controls.Add(captureBar);workspacePane.Panel1.Controls.Add(preview);workspacePane.Panel1.Controls.Add(routeSurface);
        sideTabs.Dock=DockStyle.Fill;sideTabs.Name="WorkspaceTabs";sideTabs.Padding=new Point(8,8);
        var rolePage=new TabPage("角色");var battlePage=new TabPage("战斗");var skillsPage=new TabPage("技能");
        routePage=new TabPage("路线管理");
        var logPage=new TabPage("记录");var connectionPage=new TabPage("连接");
        sideTabs.TabPages.AddRange(new[]{rolePage,battlePage,skillsPage,routePage,connectionPage,logPage});
        foreach(TabPage page in sideTabs.TabPages)page.BackColor=AppUi.Background;
        workspacePane.Panel2.Controls.Add(sideTabs);
        connectionPanel=new ConnectionPanel(()=>SettingsChanged(false),connect,targets,refresh,typeText);
        connectionPanel.LoadDestination(workspace.ssh);connectionPage.Controls.Add(connectionPanel);
        settingsNavigation.Dock=DockStyle.Left;settingsNavigation.Width=170;settingsNavigation.BackColor=AppUi.Surface;
        settingsNavigation.Padding=new Padding(12,12,12,12);settingsNavigation.WrapContents=false;
        settingsNavigation.FlowDirection=FlowDirection.TopDown;
        foreach(int index in new[]{4,0,1,2,5}){
            var button=new AppleButton{Text=sideTabs.TabPages[index].Text,Tag=index,Width=146,Height=40,
                TextAlign=ContentAlignment.MiddleLeft};
            AppUi.Button(button);button.Margin=new Padding(0,0,0,6);
            button.Click+=delegate{OpenSettingsPage((int)button.Tag);};
            navigationButtons.Add(button);settingsNavigation.Controls.Add(button);
        }
        sideTabs.SelectedIndex=4;
        routes=new RouteStudio(root,CaptureNative,CaptureNativeMinimap,ConfigureRouteWatch,ConfiguredJumpKey);routes.CalibrateInMain=delegate(RouteDocument document,Action<RouteDocument> done){captureRouteBase=document;captureRouteDone=done;BeginTemplateCapture("minimap");};routes.BeforeMutation=Pause;routes.ValidateRecordingSource=ValidateRouteSource;routes.BeginKeyboardRecording=StartRouteKeyboard;routes.RecordingStopped=FinishRouteKeyboard;routes.RecordingReady=RouteRecordingReady;routes.RouteTrialRequested=ToggleRouteTrial;routes.EmergencyStop=()=>StopAll("F12已暂停");routes.EmbedInto(routePage);routes.AttachCanvas(routeSurface);AddRouteLegend();routes.SurfaceChanged=UpdateRouteSurface;routes.PlaceSelectors(selectors);routes.PlaceTrialControls(navigation);
        minimapPanel.Controls.Add(routes.HomeMap);minimapPanel.Controls.Add(minimapHint);minimapPanel.Controls.Add(navigateButton);
        previewSurface.Controls.Add(minimapPanel);
        mapCalibrationZoom.Controls.Add(mapCalibrationImage);
        mapCalibrationZoom.Controls.Add(new Label{Text="放大小地图 · 可点击校正",Dock=DockStyle.Top,Height=34,
            ForeColor=AppUi.Text,TextAlign=ContentAlignment.MiddleCenter});
        previewSurface.Controls.Add(mapCalibrationZoom);
        mapCalibrationImage.MouseDown+=PickMinimapFromZoom;
        AppUi.Button(mapReselect);
        mapReselect.Click+=delegate{ResetMinimapSelection();};
        captureBar.Controls.Add(mapReselect);
        AppUi.Button(navigateButton,true);
        navigateButton.Click+=delegate{
            if(pointNavigation){Pause();message="导航已停止";return;}
            if(!routes.NavigationTarget.HasValue){message="请先点击小地图上的平台或梯绳";return;}
            StartRouteTrial(routes.NavigationTarget);
        };
        routes.TargetChanged+=delegate{message="已选目标，点击导航开始试走";};
        routes.NavigationSelectionHint=reason=>message=reason;
        sideTabs.SelectedIndexChanged+=delegate{UpdateRouteSurface();};UpdateRouteSurface();
        person=new PlayerPanel(root,CaptureNative,PrepareVision,BeginTemplateCapture,OnVision);person.ManagedPreset=true;person.Failed=delegate(string reason){Pause();message=reason;Log(reason);};person.RequestMinimap=delegate{captureRouteBase=workspace.Current.vision.minimap==null?new RouteDocument():workspace.Current.vision.minimap.Route();captureRouteDone=null;BeginTemplateCapture("minimap");};person.UseRouteCalibration=()=>trialCombatEnabled||workspace.Current.battle.mode=="route";person.Calibration=routes.VisionCalibration;person.SetProfile(workspace.Current.vision);person.CurrentRole=()=>workspace.Current;person.ConfigurationChanged=delegate{workspace.Current.vision=person.Profile;SettingsChanged(true);};roleSelector=new RoleSelector();roleSelector.Refresh(workspace);roleSelector.Selected=SelectRole;roleSelector.Operation=RoleOperation;rolePage.Controls.Add(person);rolePage.Controls.Add(roleSelector);
        battlePanel=new BattlePanel(()=>workspace.Current,()=>SettingsChanged(false),BeginTemplateCapture);skillsPanel=new SkillsPanel(()=>workspace.Current,()=>SettingsChanged(false),BeginTemplateCapture);battlePage.Controls.Add(battlePanel);skillsPage.Controls.Add(skillsPanel);activity.Dock=DockStyle.Fill;activity.ReadOnly=true;activity.BorderStyle=BorderStyle.None;activity.BackColor=AppUi.Surface;activity.ForeColor=AppUi.Text;activity.Font=new Font("Microsoft YaHei UI",9);logPage.Controls.Add(activity);
        var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16,0,16,10),BackColor=AppUi.Background};
        body.Controls.Add(workspacePane);body.Controls.Add(settingsNavigation);
        Controls.Add(body);Controls.Add(bottom);Controls.Add(bar);Controls.Add(navigation);AppUi.ApplyInputs(this);
        SetSettingsOpen(false);
        view.Painted+=delegate(long seq){painted++;long now=Stopwatch.GetTimestamp();if(lastPaintTick!=0)maxGapTicks=Math.Max(maxGapTicks,now-lastPaintTick);lastPaintTick=now;};
        view.VisibleChanged+=delegate{lastPaintTick=maxGapTicks=0;};
        view.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.F12){StopAll("F12已暂停");e.SuppressKeyPress=true;return;}if(armed&&(keyboardRecording==null||routeKeyboardReady)){if(pressed.Add(e.KeyValue))Enqueue(Wire.Map("op","key_down","vk",e.KeyValue));e.SuppressKeyPress=true;e.Handled=true;}};
        view.KeyUp+=delegate(object s,KeyEventArgs e){if(armed&&pressed.Remove(e.KeyValue)){Enqueue(Wire.Map("op","key_up","vk",e.KeyValue));e.SuppressKeyPress=true;}};
        view.MouseDown+=delegate(object s,MouseEventArgs e){if(captureFrame!=null){CaptureDown(e);return;}view.Focus();Mouse("mouse_down",e);};view.MouseUp+=delegate(object s,MouseEventArgs e){if(captureFrame!=null){CaptureUp(e);return;}Mouse("mouse_up",e);};view.MouseMove+=delegate(object s,MouseEventArgs e){if(captureFrame!=null){CaptureMove(e);return;}if((DateTime.UtcNow-lastMove).TotalMilliseconds>=12){lastMove=DateTime.UtcNow;Mouse("mouse_move",e);}};view.MouseWheel+=delegate(object s,MouseEventArgs e){Mouse("wheel",e);};view.LostFocus+=delegate{if(!routeTrial&&(armed||armPending))Pause();};Deactivate+=delegate{if(armed||armPending||routeTrial)Pause();};
        FormClosing+=delegate(object sender,FormClosingEventArgs args){ValidateChildren();if(!ResolveUnsaved()){args.Cancel=true;return;}StopAll("窗口已关闭");if(routes!=null&&!routes.IsDisposed)routes.Close();Disconnect(true);timer.Stop();lock(frameGate){if(newest!=null)newest.Dispose();newest=null;}timeEndPeriod(1);};timeBeginPeriod(1);timer.Interval=1;timer.Tick+=Render;timer.Start();Shown+=delegate{UpdateRouteSurface();if(autoConnect)Connect();};
    }
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        if(SystemInformation.HighContrast)return;
        const int darkMode=20,borderColor=34,captionColor=35,captionTextColor=36;
        int enabled=1,background=ColorTranslator.ToWin32(AppUi.Surface),text=ColorTranslator.ToWin32(AppUi.Text);
        int noBorder=unchecked((int)0xfffffffe);
        DwmSetWindowAttribute(Handle,darkMode,ref enabled,sizeof(int));
        DwmSetWindowAttribute(Handle,captionColor,ref background,sizeof(int));
        DwmSetWindowAttribute(Handle,captionTextColor,ref text,sizeof(int));
        DwmSetWindowAttribute(Handle,borderColor,ref noBorder,sizeof(int));
    }
    void Log(string text){if(text==lastAction)return;lastAction=text;if(activity.IsDisposed)return;activity.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);if(activity.Lines.Length>500)activity.Lines=activity.Lines.Skip(150).ToArray();}
    void SettingsChanged(bool vision){Pause();workspace.Current.vision=person.Profile;settingsDirty=true;saveSettings.Enabled=true;runButton.Enabled=false;message="设置未保存";view.Guides=showGuides.Checked?workspace.Current.battle:null;view.CombatAnchor=null;view.Invalidate();if(vision){person.Stop("预设已更新");if(showGuides.Checked&&IsHandleCreated)BeginInvoke(new Action(StartPreview));}}
    bool SaveSettings(){try{ValidateChildren();var device=connectionPanel.ReadDestination();workspace.Current.vision=person.Profile;workspace.ssh=device;WorkspaceFiles.Save(root,workspace);PlayerFiles.Save(Path.Combine(root,"players","current.json"),person.Profile);savedWorkspace=WorkspaceFiles.Copy(workspace);connectionPanel.LoadDestination(device);settingsDirty=setupRequired=false;saveSettings.Enabled=false;message=autoRunning?"运行中 · 设置已保存":"设置已保存 · 尚未开启脚本";Log("设置已保存 · "+workspace.Current.name);return true;}catch(Exception e){message=e.Message;return false;}}
    bool ResolveUnsaved(){if(!settingsDirty)return true;var answer=RoleSelector.ConfirmSave(this,workspace.Current.name);if(answer==DialogResult.Cancel)return false;if(answer==DialogResult.Yes)return SaveSettings();DiscardSettings();return true;}
    void DiscardSettings(){workspace=WorkspaceFiles.Copy(savedWorkspace);connectionPanel.LoadDestination(workspace.ssh);settingsDirty=false;person.SetProfile(workspace.Current.vision);battlePanel.Build();skillsPanel.Build();roleSelector.Refresh(workspace);view.Guides=showGuides.Checked?workspace.Current.battle:null;view.CombatAnchor=null;view.Invalidate();}
    void SelectRole(string id){if(workspace.active==id)return;if(!ResolveUnsaved()){roleSelector.Refresh(workspace);return;}StopAll("切换角色");workspace.Current.vision=person.Profile;workspace.active=id;person.SetProfile(workspace.Current.vision);PlayerFiles.Save(Path.Combine(root,"players","current.json"),person.Profile);battlePanel.Build();skillsPanel.Build();roleSelector.Refresh(workspace);WorkspaceFiles.Save(root,workspace);savedWorkspace=WorkspaceFiles.Copy(workspace);settingsDirty=setupRequired=false;view.Guides=showGuides.Checked?workspace.Current.battle:null;view.CombatAnchor=null;Log("已切换预设 · "+workspace.Current.name);if(showGuides.Checked)BeginInvoke(new Action(StartPreview));}
    void RoleOperation(string action){if(!ResolveUnsaved())return;if(action=="删除"){if(workspace.roles.Count==1){message="至少保留一个角色预设";return;}if(MessageBox.Show(this,"删除当前角色预设？","删除预设",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;string old=workspace.active;SelectRole(workspace.roles.First(r=>r.id!=old).id);workspace.roles.RemoveAll(r=>r.id==old);roleSelector.Refresh(workspace);WorkspaceFiles.Save(root,workspace);savedWorkspace=WorkspaceFiles.Copy(workspace);return;}string initial=action=="新建"?"新角色":action=="复制"?workspace.Current.name+" 副本":workspace.Current.name;string name=RoleSelector.Ask(this,action+"角色预设",initial);if(name==null)return;Pause();if(action=="改名"){workspace.Current.name=name;workspace.Current.vision.name=name;WorkspaceFiles.Save(root,workspace);savedWorkspace=WorkspaceFiles.Copy(workspace);roleSelector.Refresh(workspace);return;}var role=action=="复制"?WorkspaceFiles.Copy(workspace.Current):new RolePreset();role.id=Guid.NewGuid().ToString("N");role.name=name;role.vision.name=name;workspace.roles.Add(role);SelectRole(role.id);}
    void StartPreview(){if(routeTrial||!showGuides.Checked||captureFrame!=null||!running||Interlocked.Read(ref target)==0)return;view.Guides=workspace.Current.battle;if(!person.Active)person.Start();}
    void ToggleLieVision(){
        if(lieRequested){StopLieVision("测谎识别已关闭");return;}
        if(!running||!previewReady||view.Frame==null){message="请先连接并取得实时画面";return;}
        if(!polygraphGate.Available){message="测谎游戏状态模板不可用："+polygraphGate.Error;Log(message);return;}
        lieRequested=true;lieGameActive=false;lieGateHits=lieGateMisses=0;lastLieGateAt=0;view.LieTarget=null;view.LieConfidence=0;lieSubmitAt=lastLieSequence=0;message="测谎已开启 · 等待小游戏开始";Log(message);
    }
    void StopLieVision(string reason){
        lieRequested=false;lieGameActive=false;lieGateHits=lieGateMisses=0;lastLieGateAt=0;lieVision.Stop();view.LieTarget=null;view.LieConfidence=0;lieSubmitAt=lastLieSequence=0;view.Invalidate();if(!String.IsNullOrEmpty(reason)){message=reason;Log(reason);}
    }
    void UpdateLieGameGate(long now,int width,int height){
        if(now-lastLieGateAt<Stopwatch.Frequency/2)return;
        lastLieGateAt=now;
        double sx=width/1366.0,sy=height/768.0;
        int x=(int)Math.Round(550*sx),y=(int)Math.Round(625*sy),w=Math.Max(1,(int)Math.Round(266*sx)),h=Math.Max(1,(int)Math.Round(45*sy));
        if(x<0||y<0||x+w>width||y+h>height||w>1024||h>800){lieGateHits=0;lieGateMisses++;return;}
        int needed=checked(w*h*3);if(lieGatePixels==null||lieGatePixels.Length!=needed)lieGatePixels=new byte[needed];
        double correlation,difference;bool active;
        try{nativeFrames.CopyNativeRegion(new Rectangle(x,y,w,h),lieGatePixels);active=polygraphGate.IsActive(lieGatePixels,w,h,out correlation,out difference);}
        catch(Exception e){lieGateHits=0;lieGateMisses++;if(lieGateMisses==1){message="测谎状态检查暂不可用："+e.Message;Log(message);}return;}
        if(active){lieGateHits++;lieGateMisses=0;}else{lieGateMisses++;lieGateHits=0;}
        if(!lieGameActive&&lieGateHits>=2){
            lieGameActive=true;lieGateMisses=0;lieSubmitAt=lastLieSequence=0;lieVision.Start();message="检测到测谎小游戏 · 正在启动识别";Log(message);
        }else if(lieGameActive&&lieGateMisses>=2){
            lieGameActive=false;lieGateHits=0;lieVision.Stop();view.LieTarget=null;view.LieConfidence=0;view.Invalidate();message="测谎已开启 · 等待小游戏开始";Log(message);
        }
    }
    void FeedLieVision(long now){
        if(!lieRequested||view.Frame==null||currentHeader==null)return;
        if((DateTime.UtcNow-lastFrame).TotalMilliseconds>800){
            lieGateHits=lieGateMisses=0;
            if(lieGameActive){lieGameActive=false;lieVision.Stop();view.LieTarget=null;view.LieConfidence=0;view.Invalidate();}
            return;
        }
        int width=Wire.Int(currentHeader,"width"),height=Wire.Int(currentHeader,"height");if(width<16||height<16)return;
        UpdateLieGameGate(now,width,height);
        if(!lieGameActive||!lieVision.Enabled||view.Sequence==lastLieSequence||now-lieSubmitAt<Stopwatch.Frequency/30)return;
        int x=(int)Math.Round(307.0*width/1366),y=(int)Math.Round(119.0*height/768),w=(int)Math.Round(759.0*width/1366),h=(int)Math.Round(563.0*height/768);
        x=Math.Max(0,Math.Min(width-1,x));y=Math.Max(0,Math.Min(height-1,y));w=Math.Max(1,Math.Min(width-x,w));h=Math.Max(1,Math.Min(height-y,h));
        if(w>1024||h>800)return;
        byte[] pixels=lieVision.Rent(checked(w*h*3));if(pixels==null)return;
        try{nativeFrames.CopyNativeRegion(new Rectangle(x,y,w,h),pixels);lieVision.Submit(pixels,w,h,x,y);lastLieSequence=view.Sequence;lieSubmitAt=now;}
        catch(Exception e){lieVision.Release(pixels);message="测谎画面暂不可用："+e.Message;}
    }
    void StartAutomation(){if(settingsDirty||setupRequired){message="请先保存设置";return;}if(!running||Interlocked.Read(ref target)==0){message="请选择已连接的目标窗口";return;}Pause();routes.PauseRecording("开启脚本前暂停录制");var role=workspace.Current;if(role.battle.mode=="route"&&!routes.HasRuntimeRoute){SetSettingsOpen(false);message="请选择地图和已保存的路线";return;}try{var frame=CaptureNative();int w=Wire.Int(frame,"width"),h=Wire.Int(frame,"height");if(role.Key("hp")!=0&&!role.hp.Valid(w,h)){OpenSettingsPage(2);message="请先框选HP条";return;}if(role.Key("mp")!=0&&!role.mp.Valid(w,h)){OpenSettingsPage(2);message="请先框选MP条";return;}if(role.vision.mode!="yolo_expression"&&role.vision.nametag.png.Length==0){OpenSettingsPage(0);message="请先配置角色名签";return;}if(NeedsMinimap(role)&&person.MinimapForFrame(frame)==null){message="选中的地图还没有小地图标定";return;}if(role.battle.Profile.jumpAttack&&role.Key("jump")!=0&&role.Key("jump")==role.Key("attack")){OpenSettingsPage(2);message="跳跃与攻击需要不同的按键";return;}person.Stop("应用运行预设");routes.ShowGamePreview();if(!person.Active)person.Start();autoWindow=Interlocked.Read(ref target);autoPending=true;autoArmPending=false;message="正在准备人物定位…";Log("准备运行 · "+role.name);}catch(Exception e){message=e.Message;}}
    int ConfiguredJumpKey(){
        var binding=workspace.Current.Binding("jump");
        return binding==null?0:binding.key;
    }
    void ToggleRouteTrial(){
        if(routeTrial){Pause();message="路线试走已停止";routes.ShowRouteMap();return;}
        StartRouteTrial(routes.TrialTarget);
    }
    void StartRouteTrial(Point? destination){
        if(autoPending||autoRunning){message="请先暂停正在运行的脚本";return;}
        if(routes.IsRecording||routes.ManualCaptureActive||!routes.HasRuntimeRoute){message="请先结束录制并保存路线";return;}
        try{
            var route=routes.RuntimeRoute(includeSuggestedJumps:true);
            long window=Interlocked.Read(ref target);
            if(window==0||!ControlFrameReady(window))throw new IOException("请选择画面就绪的游戏窗口");
            var savedCalibration=routes.VisionCalibration();
            var frame=CaptureNative();
            if(destination.HasValue&&savedCalibration!=null&&(savedCalibration.sourceHost??"")!=Wire.Str(frame,"source_host"))
                throw new IOException("请选择当前被控端对应的地图");
            var mini=savedCalibration==null?null:PlayerMinimap.FromRoute(savedCalibration).ForFrame(frame);
            if(mini==null)throw new IOException("选中的地图还没有小地图标定");
            var calibration=mini.Route();
            route.window=calibration.window;route.sourceHost=calibration.sourceHost;
            int jump=ConfiguredJumpKey();
            bool combat=destination.HasValue&&routes.NavigationCombatEnabled;
            if(combat){
                if(settingsDirty||setupRequired)throw new IOException("请先保存角色设置");
                var role=workspace.Current;
                if(role.vision.mode!="yolo_expression"&&role.vision.nametag.png.Length==0)
                    throw new IOException("途中打怪需要先设置当前角色的名签识别");
                int width=Wire.Int(frame,"width"),height=Wire.Int(frame,"height");
                if(role.Key("hp")!=0&&!role.hp.Valid(width,height)||role.Key("mp")!=0&&!role.mp.Valid(width,height))
                    throw new IOException("请先完成当前角色的血蓝条设置");
            }
            bool hasJump=route.edges.Any(e=>e.kind=="jump"||e.kind=="jump_down"||e.kind=="drop"||e.kind=="rope_mount"||e.kind=="rope_exit");
            if(!destination.HasValue&&hasJump&&jump==0)throw new IOException("请先在技能页设置跳跃键");
            Pause();CancelCapture();person.Stop("试走路线");
            var movement=combat?WorkspaceFiles.Copy(workspace.Current):new RolePreset();
            movement.battle.mode="route";
            movement.Binding("jump").key=jump;
            movement.Binding("jump").enabled=true;
            trialPlanner=destination.HasValue?null:new CombatPlanner(movement,ClockMs(),route,true);
            int run=Volatile.Read(ref autoEpoch);
            routeTrial=true;pointNavigation=destination.HasValue;trialCombatEnabled=combat;
            autoPending=true;autoArmPending=false;autoWindow=route.window;
            BeginTrialTrace(route.id);
            lock(observationGate){observation=null;observationAt=0;trialCombatVision=null;trialCombatVisionAt=0;}
            routes.SetTrialActive(true);
            if(!destination.HasValue){OpenSettingsPage(3);routes.ShowRouteMap();}
            message=destination.HasValue?"正在确认导航位置…":"正在确认试走位置…";
            RequestPreparedFrame(prepared=>{
                if(!TrialCurrent(run))return;
                if(combat)person.StartPrepared(prepared);
                if(TrialCurrent(run))new Thread(()=>ObserveTrial(run,calibration,route,destination,movement)){IsBackground=true}.Start();
            },reason=>EndTrialOnError(run,reason));
        }catch(Exception e){if(routeTrial)Pause();message=e.Message;}
    }
    bool TrialCurrent(int run){return routeTrial&&running&&run==Volatile.Read(ref autoEpoch)&&(autoPending||autoRunning);}
    void EndTrialOnError(int run,string reason){
        if(!TrialCurrent(run))return;
        FinishTrialTrace(reason);
        Pause();message=reason;Log(reason);
    }
    internal static Dictionary<string,object> ComposeNavigationSight(Dictionary<string,object> navigation,
        Dictionary<string,object> vision,double elapsed){
        bool ready=vision!=null&&Wire.Long(vision,"window")==Wire.Long(navigation,"window")&&
            Wire.Bool(vision,"found")&&Wire.Bool(vision,"position_fresh")&&
            elapsed+(vision.ContainsKey("capture_age_ms")?Convert.ToDouble(vision["capture_age_ms"]):0)<=CombatObservation.MaxAgeMs;
        var combined=ready?new Dictionary<string,object>(vision):new Dictionary<string,object>();
        foreach(var item in navigation)combined[item.Key]=item.Value;
        combined["navigation_combat_ready"]=ready;
        return combined;
    }
    void ObserveTrial(int run,RouteDocument calibration,RouteDocument route,Point? destination,RolePreset movement){
        try{
            var layout=new MinimapLayout(calibration);var tracker=new MiniTracker();
            tracker.Seed(MinimapLayout.ReferencePixels(calibration),calibration.roiWidth,calibration.roiHeight,
                calibration.lastOffsetX,calibration.lastOffsetY);
            long previous=-1;
            var positionDeadline=Stopwatch.StartNew();
            while(TrialCurrent(run)){
                if(destination.HasValue&&positionDeadline.ElapsedMilliseconds>6000)
                    throw new IOException("未取得可靠的小地图位置，导航已停止");
                Dictionary<string,object> frame;
                try{frame=CaptureNativeMinimap(layout,tracker.LastFrame);}
                catch(FrameUnavailableException){Thread.Sleep(60);continue;}
                if(Wire.Bool(frame,"cursor_in_game"))throw new IOException("鼠标回到游戏窗内，试走已停止");
                long sequence=Wire.Long(frame,"sequence");
                if(sequence!=previous){
                    var dot=tracker.Process((byte[])frame["bgr24"],Wire.Int(frame,"width"),Wire.Int(frame,"height"),calibration);
                    var sight=Wire.Map("sequence",sequence,"window",calibration.window,"found",true,"position_fresh",true,
                        "capture_age_ms",Convert.ToDouble(frame["age_ms"]),
                        "minimap_dot",dot.ok?new[]{dot.x+tracker.offsetX,dot.y+tracker.offsetY}:null);
                    lock(observationGate){
                        if(!TrialCurrent(run))return;
                        if(trialCombatEnabled)sight=ComposeNavigationSight(sight,trialCombatVision,ClockMs()-trialCombatVisionAt);
                        observation=sight;observationAt=ClockMs();
                    }
                    var currentPosition=new Point(dot.x+tracker.offsetX,dot.y+tracker.offsetY);
                    if(dot.ok)positionDeadline.Restart();
                    if(dot.ok&&autoPending)SafeUi(delegate{
                        if(!TrialCurrent(run)||!autoPending||autoArmPending)return;
                        if(destination.HasValue){
                            try{
                                TraceTrial(Wire.Map("position",new[]{currentPosition.X,currentPosition.Y},
                                    "destination",new[]{destination.Value.X,destination.Value.Y}));
                                var targetNode=RouteGraph.NavigationPoint(route,destination.Value,true);
                                if(targetNode==null)throw new IOException("目标点没有落在平台或梯绳上，请重新选点");
                                var start=RouteGraph.NavigationPoint(route,currentPosition,false);
                                if(start==null)throw new IOException("人物黄点离路线太远，请先靠近已画的平台或梯绳");
                                if(route.manualMap)RouteGraph.AddRopeDescents(route);
                                var path=RouteGraph.Plan(route,start.id,targetNode.id);
                                if(path==null)throw new IOException("当前位置到导航点还没有连通，请检查平台和梯绳之间的连接");
                                if(path.Count==0){
                                    bool reached=Math.Abs(currentPosition.X-targetNode.x)<=1&&Math.Abs(currentPosition.Y-targetNode.y)<=1;
                                    EndTrialOnError(run,reached?"已到达导航目标":"请先靠近选中的目标点");return;
                                }
                                if(movement.Key("jump")==0&&path.Any(e=>RouteGraph.IsMapAction(e.kind)||e.kind=="rope_exit"))
                                    throw new IOException("请先在技能页设置跳跃键");
                                TraceTrial(Wire.Map("path",path.Select(edge=>Wire.Map("id",edge.id,"kind",edge.kind,
                                    "from",edge.from,"to",edge.to)).ToArray()));
                                trialPlanner=new CombatPlanner(movement,ClockMs(),route,start.id,targetNode.id,path,trialCombatEnabled);
                            }catch(Exception error){EndTrialOnError(run,error.Message);return;}
                        }
                        autoArmPending=true;
                        Enqueue(Wire.Map("op","arm","window",calibration.window,"auto_epoch",run,"local_epoch",inputEpoch));
                    });
                    previous=sequence;
                }
                Thread.Sleep(45);
            }
        }catch(Exception e){string reason=e.Message;SafeUi(()=>EndTrialOnError(run,reason));}
    }
    void RenderTrialPosition(){
        if(!routeTrial)return;
        int[] dot=null;
        double now=ClockMs();
        lock(observationGate){
            object value;
            if(observation!=null&&now-observationAt+Convert.ToDouble(observation["capture_age_ms"])<=600&&
                observation.TryGetValue("minimap_dot",out value))dot=value as int[];
        }
        routes.SetLivePosition(dot==null?null:(Point?)new Point(dot[0],dot[1]));
    }
    static bool NeedsMinimap(RolePreset role){return role.battle.mode!="stationary"||role.battle.moveLeft>0||role.battle.moveRight>0;}
    void OnVision(Dictionary<string,object> answer){if(routeTrial){if(trialCombatEnabled)lock(observationGate){trialCombatVision=answer;trialCombatVisionAt=ClockMs();}return;}double now=ClockMs();lock(observationGate){observation=answer;observationAt=now;}if(view.IsDisposed)return;view.Person=showGuides.Checked?answer:null;view.PersonAt=DateTime.UtcNow;view.Guides=showGuides.Checked?workspace.Current.battle:null;view.Invalidate();if(answer==null){if(autoRunning||autoArmPending)Pause();return;}if(autoPending&&NeedsMinimap(workspace.Current)){object dot;if(!answer.TryGetValue("minimap_dot",out dot)||dot==null){message="正在等待小地图人物位置…";return;}}if(autoPending&&!autoArmPending&&Wire.Long(answer,"window")==autoWindow&&Wire.Bool(answer,"found")&&Wire.Bool(answer,"position_fresh")&&(!answer.ContainsKey("capture_age_ms")||Convert.ToDouble(answer["capture_age_ms"])<=CombatObservation.MaxAgeMs)){autoArmPending=true;Enqueue(Wire.Map("op","arm","window",autoWindow,"auto_epoch",autoEpoch,"local_epoch",inputEpoch));}}
    static bool AreaSlot(string slot){return slot=="attack_box"||slot=="search_box"||slot=="movement_box"||slot=="hp_bar"||slot=="mp_bar";}
    static bool MeterSlot(string slot){return slot=="hp_bar"||slot=="mp_bar";}
    void StopAll(string reason){Pause();CancelPreparation(reason);CancelCapture();if(routes!=null&&!routes.IsDisposed)routes.PauseRecording(reason);if(person!=null&&!person.IsDisposed)person.Stop(reason);}
    void CancelPreparation(string reason){if(InvokeRequired){SafeUi(()=>CancelPreparation(reason));return;}var failed=prepareFailure;prepareSuccess=null;prepareFailure=null;Interlocked.Increment(ref prepareId);UpdateRouteSurface();if(failed!=null)failed(reason);}
    void PrepareVision(Action<Dictionary<string,object>> success,Action<string> failure){
        Pause();CancelCapture();CancelPreparation("已取消");
        RequestPreparedFrame(success,failure);
    }
    void RequestPreparedFrame(Action<Dictionary<string,object>> success,Action<string> failure){
        try{if(FixtureSource!=null){var sample=CaptureNative();if(Wire.Long(sample,"window")==0)throw new IOException("请选择具体游戏窗口");success(sample);return;}if(!running||Interlocked.Read(ref target)==0)throw new IOException("请连接并选择具体游戏窗口");
            prepareSuccess=success;prepareFailure=failure;UpdateRouteSurface();int id=Interlocked.Increment(ref prepareId);Enqueue(Wire.Map("op","prepare_vision","window",Interlocked.Read(ref target),"vision_id",id));
        }catch(Exception e){prepareSuccess=null;prepareFailure=null;UpdateRouteSurface();failure(e.Message);}
    }
    void FinishPreparation(int id,int epoch,long window,string sourceUtc){
        try{
            DateTime prepared=DateTime.Parse(sourceUtc,null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            var deadline=DateTime.UtcNow.AddMilliseconds(1600);
            while((autoPending||DateTime.UtcNow<deadline)&&id==Volatile.Read(ref prepareId)&&epoch==Volatile.Read(ref inputEpoch)){
                try{
                    var info=nativeFrames.Information();DateTime captured;
                    bool fresh=DateTime.TryParse(Wire.Str(info,"captured_utc"),null,System.Globalization.DateTimeStyles.RoundtripKind,out captured)&&captured.ToUniversalTime()>=prepared;
                    if(fresh&&Wire.Long(info,"window")==window&&Convert.ToDouble(info["age_ms"])<800){
                        var frame=CaptureNative();
                        if(Wire.Bool(frame,"cursor_in_game"))throw new IOException("请把被控端鼠标移到游戏窗外后继续");
                        SafeUi(delegate{
                            if(id!=prepareId||epoch!=inputEpoch)return;
                            var done=prepareSuccess;prepareSuccess=null;prepareFailure=null;Interlocked.Increment(ref prepareId);
                            if(done!=null)done(frame);
                        });
                        return;
                    }
                }catch(FrameUnavailableException){}
                Thread.Sleep(20);
            }
            SafeUi(delegate{if(id==prepareId)CancelPreparation("未取得新原图");});
        }catch(Exception e){string error=e.Message;SafeUi(delegate{if(id==prepareId)CancelPreparation(error);});}
    }
    static string CaptureRoleName(string role){return role=="name"?"姓名":role=="family"?"家族":"勋章";}
    void SetupCaptureRole(RadioButton button,string label,string role){button.Text=label;button.Name="CaptureRow_"+role;button.Appearance=Appearance.Button;button.TextAlign=ContentAlignment.MiddleCenter;button.Size=new Size(58,32);button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=AppUi.Border;button.FlatAppearance.CheckedBackColor=Color.FromArgb(48,107,226);button.Click+=delegate{if(captureSlot=="nametag"){captureRole=role;capturePick=false;UpdateCaptureRows();}};button.CheckedChanged+=delegate{button.BackColor=button.Checked?Color.FromArgb(48,107,226):AppUi.Field;button.ForeColor=button.Checked?Color.White:AppUi.Text;if(button.Checked&&captureSlot=="nametag"){captureRole=role;capturePick=false;UpdateCaptureRows();}};}
    void UpdateCaptureRows(){view.Selections=new List<Rectangle>(captureAreas);captureNext.Enabled=captureAreas.Count>0;captureClearRow.Enabled=captureRoles.Contains(captureRole);captureSave.Enabled=captureAreas.Count>0&&view.HasPick;captureHint.Text="框选"+CaptureRoleName(captureRole);view.Invalidate();}
    void BeginTemplateCapture(string slot){
        Pause();if(routes!=null)routes.PauseRecording("采集模板，录制已暂停");if(person!=null)person.Stop("采集模板，识别已暂停");CancelCapture();
        PrepareVision(delegate(Dictionary<string,object> frame){captureFrame=frame;captureSlot=slot;UpdateRouteSurface();captureAreas.Clear();captureRoles.Clear();captureRole="name";capturePick=false;captureDragging=false;view.Selections.Clear();view.HasPick=false;view.Person=null;
            var old=view.Frame;view.Frame=Wire.BitmapFrom((byte[])frame["bgr24"],Wire.Int(frame,"width"),Wire.Int(frame,"height"));if(old!=null)old.Dispose();view.SourceSize=view.Frame.Size;view.Sequence=Wire.Long(frame,"sequence");captureBar.Visible=true;sideTabs.Enabled=false;captureNext.Visible=slot=="nametag";captureNameRow.Visible=captureFamilyRow.Visible=captureMedalRow.Visible=captureClearRow.Visible=slot=="nametag";captureNameRow.Checked=true;captureClearRow.Enabled=false;captureNext.Enabled=false;captureSave.Enabled=false;
            bool isMap=slot=="minimap";
            view.MapCalibration=isMap;
            view.Cursor=isMap?Cursors.Cross:Cursors.Default;
            captureSave.Text=isMap?"保存标定":"保存模板";
            captureHint.ForeColor=isMap?AppUi.Blue:AppUi.Text;
            captureHint.Text=slot=="nametag"?"冻结画面 · 框选姓名牌":isMap?"框选完整小地图，黄点会自动识别":
                AreaSlot(slot)?(MeterSlot(slot)?"拖选血条范围":"点击本帧角色中心，再拖选范围"):"冻结画面 · 框选"+PlayerPanel.Title(slot);
            rangeAnchor=false;view.Invalidate();
        },delegate(string error){message=error;captureHint.Text=error;});
    }
    void CaptureDown(MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;Point point;if(!view.Position(e.Location,out point))return;view.Focus();if(AreaSlot(captureSlot)&&!MeterSlot(captureSlot)&&!rangeAnchor){view.Pick=point;view.HasPick=true;rangeAnchor=true;captureHint.Text="拖选范围";view.Invalidate();return;}
        if(capturePick){
            if(captureSlot=="minimap"){
                PickMinimapMarker(point,(double)view.ImageRect().Width/view.SourceSize.Width);
                return;
            }
            view.Pick=point;view.HasPick=true;captureSave.Enabled=true;
            captureHint.Text="已标记";view.Invalidate();return;
        }
        captureAnchor=point;captureDragging=true;view.Capture=true;if(captureSlot!="nametag"&&captureSlot!="attack_box"){captureAreas.Clear();captureRoles.Clear();}view.Selections=new List<Rectangle>(captureAreas);view.Selections.Add(new Rectangle(point,new Size(1,1)));view.Invalidate();
    }
    void CaptureMove(MouseEventArgs e){if(!captureDragging)return;Point point;if(!view.Position(e.Location,out point))return;var area=Rectangle.FromLTRB(Math.Min(point.X,captureAnchor.X),Math.Min(point.Y,captureAnchor.Y),Math.Max(point.X,captureAnchor.X)+1,Math.Max(point.Y,captureAnchor.Y)+1);view.Selections=new List<Rectangle>(captureAreas);view.Selections.Add(area);view.Invalidate();}
    void CaptureUp(MouseEventArgs e){if(!captureDragging)return;captureDragging=false;view.Capture=false;var area=view.Selections.Last();
        if(captureSlot=="minimap"){FinishMinimapBounds(area);return;}
        if(AreaSlot(captureSlot)){if(area.Width<(MeterSlot(captureSlot)?8:10)||area.Height<(MeterSlot(captureSlot)?1:4)){captureHint.Text="范围太小，请重新拖选";view.Selections=new List<Rectangle>(captureAreas);return;}if(captureSlot=="attack_box"){Point origin=view.Pick;bool left=area.Left<origin.X,right=area.Right>origin.X;captureAreas.RemoveAll(r=>left&&r.Left<origin.X||right&&r.Right>origin.X);}else captureAreas.Clear();captureAreas.Add(area);view.Selections=new List<Rectangle>(captureAreas);if(MeterSlot(captureSlot)){view.Pick=new Point(area.Left+area.Width/2,area.Top+area.Height/2);view.HasPick=true;}captureSave.Enabled=true;captureHint.Text="范围已选";view.Invalidate();return;}if(area.Width<4||area.Height<3){view.Selections=new List<Rectangle>(captureAreas);captureHint.Text="框选范围太小，请重新拖框";view.Invalidate();return;}if(captureAreas.Count>=8){captureHint.Text="最多框选8行";return;}if(captureSlot=="nametag"){int at=captureRoles.IndexOf(captureRole);if(at>=0)captureAreas[at]=area;else{captureAreas.Add(area);captureRoles.Add(captureRole);}}else captureAreas.Add(area);view.Selections=new List<Rectangle>(captureAreas);captureNext.Enabled=true;captureClearRow.Enabled=captureSlot=="nametag";captureSave.Enabled=view.HasPick&&captureAreas.Count>0;
        if(captureSlot=="nametag")captureHint.Text=CaptureRoleName(captureRole)+"已选";else{capturePick=true;captureHint.Text="点击身体中心";}view.Invalidate();}
    void ClearMinimapZoom(){
        mapCalibrationZoom.Visible=false;
        var old=mapCalibrationImage.Frame;
        mapCalibrationImage.Frame=null;
        mapCalibrationImage.HasPick=false;
        if(old!=null)old.Dispose();
        mapReselect.Visible=false;
    }

    void ResetMinimapSelection(){
        if(captureSlot!="minimap")return;
        capturePick=false;
        captureDragging=false;
        view.Capture=false;
        captureAreas.Clear();
        view.Selections.Clear();
        view.HasPick=false;
        captureSave.Enabled=false;
        ClearMinimapZoom();
        captureHint.Text="框选完整小地图，黄点会自动识别";
        view.Invalidate();
    }

    void FinishMinimapBounds(Rectangle area){
        if(area.Width<24||area.Height<24||area.Width>1024||area.Height>800){
            ResetMinimapSelection();
            captureHint.Text="框选范围无效，请重新圈住完整小地图";
            return;
        }
        captureAreas.Clear();
        captureAreas.Add(area);
        view.Selections=new List<Rectangle>{area};
        capturePick=true;
        var pixels=MinimapLayout.Crop((byte[])captureFrame["bgr24"],view.SourceSize.Width,area);
        mapCalibrationImage.Frame=Wire.BitmapFrom(pixels,area.Width,area.Height);
        mapCalibrationImage.SourceSize=area.Size;
        mapCalibrationZoom.Visible=true;
        mapReselect.Visible=true;
        SetMinimapMarker(MinimapLayout.AutoPick(captureFrame,area),true);
    }

    void PickMinimapFromZoom(object sender,MouseEventArgs mouse){
        if(mouse.Button!=MouseButtons.Left||captureSlot!="minimap"||!capturePick||captureAreas.Count!=1)return;
        Point local;
        if(!mapCalibrationImage.Position(mouse.Location,out local))return;
        var area=captureAreas[0];
        PickMinimapMarker(new Point(local.X+area.X,local.Y+area.Y),
            (double)mapCalibrationImage.ImageRect().Width/area.Width);
    }

    void PickMinimapMarker(Point point,double scale){
        var area=captureAreas[0];
        if(!area.Contains(point)){captureHint.Text="请点框内的人物黄点，或重新框选";return;}
        SetMinimapMarker(MinimapLayout.Pick(captureFrame,area,point,scale),false);
    }

    void SetMinimapMarker(DotResult dot,bool automatic){
        view.HasPick=mapCalibrationImage.HasPick=captureSave.Enabled=dot.ok;
        if(dot.ok){
            view.Pick=new Point(dot.x,dot.y);
            captureDotColor=dot.color;
            var area=captureAreas[0];
            mapCalibrationImage.Pick=new Point(dot.x-area.X,dot.y-area.Y);
            captureHint.Text=automatic?"已自动找到人物黄点 · 核对后点保存标定":"已对准人物黄点 · 点保存标定";
        }else captureHint.Text=dot.reason+"，请点放大图中的人物黄点";
        view.Invalidate();
        mapCalibrationImage.Invalidate();
    }

    void SaveCapture(){if(captureFrame==null||captureAreas.Count==0||!view.HasPick)return;try{Point picked=view.Pick;using(var bitmap=Wire.BitmapFrom((byte[])captureFrame["bgr24"],Wire.Int(captureFrame,"width"),Wire.Int(captureFrame,"height"))){
            if(AreaSlot(captureSlot)){var c=workspace.Current.battle;foreach(var area in captureAreas){if(captureSlot=="attack_box")BattleGeometry.SetAttack(c,picked,area);else if(captureSlot=="search_box")BattleGeometry.SetSearch(c,picked,area);else if(captureSlot=="movement_box")BattleGeometry.SetMovement(c,picked,area);else {var meter=captureSlot=="hp_bar"?workspace.Current.hp:workspace.Current.mp;meter.x=area.X;meter.y=area.Y;meter.width=area.Width;meter.height=area.Height;meter.sourceWidth=bitmap.Width;meter.sourceHeight=bitmap.Height;}}SettingsChanged(false);battlePanel.Build();skillsPanel.Build();}
            else if(captureSlot=="minimap"){
                var d=MinimapLayout.Calibrate(captureFrame,captureAreas[0],picked,captureDotColor,captureRouteBase);
                if(captureRouteDone!=null)captureRouteDone(d);
                person.SetMinimap(d);
            }
            else person.SetTemplate(captureSlot,PlayerTemplate.Capture(bitmap,captureAreas,picked,captureSlot=="nametag"?captureRoles:null));
        }CancelCapture();SaveSettings();if(showGuides.Checked)BeginInvoke(new Action(StartPreview));}catch(Exception e){captureHint.Text=e.Message;}}
    void CancelCapture(){captureFrame=null;captureSlot=null;captureDragging=false;capturePick=false;captureAreas.Clear();captureRoles.Clear();view.Selections.Clear();view.HasPick=false;view.Capture=false;captureBar.Visible=false;sideTabs.Enabled=true;ClearMinimapZoom();view.MapCalibration=false;view.Cursor=Cursors.Default;UpdateRouteSurface();view.Invalidate();}
    protected override bool ProcessCmdKey(ref Message msg,Keys keys){
        if(settingsOpen&&sideTabs.SelectedTab==routePage&&captureFrame==null&&routes.CaptureShortcut(keys))return true;
        if(keys==Keys.Escape&&routes.ManualCaptureActive){Pause();routes.ShowRouteMap();return true;}
        if(routeSurface.Visible&&routeSurface.ContainsFocus){if(keys==(Keys.Control|Keys.Z)){routes.Undo();return true;}if(keys==(Keys.Control|Keys.Y)){routes.Redo();return true;}}if(keys==(Keys.Control|Keys.S)){SaveSettings();return true;}if((keys&Keys.KeyCode)==Keys.F12){StopAll("F12已暂停");return true;}if((keys&Keys.KeyCode)==Keys.Escape&&captureFrame!=null){CancelCapture();if(showGuides.Checked)BeginInvoke(new Action(StartPreview));return true;}return base.ProcessCmdKey(ref msg,keys);}
    void SendTextDialog(){if(!running||view.Frame==null||displayedWindow!=Interlocked.Read(ref target))return;Pause();routes.ShowGamePreview();using(var dialog=new Form{Text="发送文字到"+(activeDestination??savedWorkspace.ssh).Label(),Size=new Size(540,270),StartPosition=FormStartPosition.CenterParent,Font=Font,MinimizeBox=false,MaximizeBox=false}){var hint=new Label{Text="先在预览中点选被控端的输入位置。发送后保持只观看。",Dock=DockStyle.Top,Height=36,Padding=new Padding(8)};var text=new TextBox{Multiline=true,MaxLength=1024,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=44,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6)};var send=new AppleButton{Text="发送",DialogResult=DialogResult.OK,Width=90};var cancel=new AppleButton{Text="取消",DialogResult=DialogResult.Cancel,Width=90};buttons.Controls.AddRange(new Control[]{send,cancel});dialog.Controls.Add(text);dialog.Controls.Add(hint);dialog.Controls.Add(buttons);dialog.AcceptButton=send;dialog.CancelButton=cancel;if(dialog.ShowDialog(this)==DialogResult.OK&&text.Text.Length>0){view.Focus();Enqueue(Wire.Map("op","type_once","window",Interlocked.Read(ref target),"text",text.Text));}}}
    void Pause(){
        bool endedCombat=trialCombatEnabled;trialCombatEnabled=false;
        lock(observationGate){trialCombatVision=null;trialCombatVisionAt=0;}
        if(endedCombat&&person!=null&&!showGuides.Checked)person.Stop("导航已停止");
        pointNavigation=false;
        bool endedKeyboard=keyboardRecording!=null;
        keyboardRecording=null;routeKeyboardReady=false;
        bool endedTrial=routeTrial;routeTrial=false;trialPlanner=null;
        autoRunning=autoPending=autoArmPending=false;Interlocked.Increment(ref autoEpoch);planner=null;
        if(endedTrial&&routes!=null&&!routes.IsDisposed){FinishTrialTrace("试走已停止");routes.SetTrialActive(false);}
        if(prepareFailure!=null)CancelPreparation("操作已暂停");
        armed=false;armPending=false;lastPointerValid=false;Interlocked.Increment(ref inputEpoch);
        pressed.Clear();releaseRequested=true;Dictionary<string,object> ignored;
        while(queue.TryDequeue(out ignored)){}
        if(running)message="只观看";
        if(endedKeyboard&&routes!=null&&!routes.IsDisposed&&routes.IsRecording)
            routes.PauseRecording("键盘已暂停，录制已暂停");
    }
    void Enqueue(Dictionary<string,object> request){if(!running)return;if(queue.Count>=128){Pause();message="输入积压，已暂停";return;}if(!request.ContainsKey("local_epoch"))request["local_epoch"]=inputEpoch;queue.Enqueue(request);}
    void Mouse(string op,MouseEventArgs e){if(!armed||routes!=null&&(routes.IsRecording||routes.ManualCaptureActive))return;if((DateTime.UtcNow-lastFrame).TotalMilliseconds>800){Pause();return;}Point p;if(!view.Position(e.Location,out p)){if(op=="mouse_up")Pause();return;}if(op=="mouse_move"&&lastPointerValid&&lastPointerWindow==displayedWindow&&lastPointer==p)return;int b=e.Button==MouseButtons.Left?1:e.Button==MouseButtons.Right?2:3;Enqueue(Wire.Map("op",op,"window",displayedWindow,"x",p.X,"y",p.Y,"width",view.SourceSize.Width,"height",view.SourceSize.Height,"button",b,"delta",e.Delta));if(op=="mouse_move"){lastPointer=p;lastPointerWindow=displayedWindow;lastPointerValid=true;}}
    void Connect(){
        lock(connectionGate){
            if(running||disconnecting!=0)return;
            if(connectionPanel.Dirty){message="请先保存被控端设置";return;}
            var destination=WorkspaceFiles.Copy(savedWorkspace.ssh);
            activeDestination=destination;
            previewReady=false;connectionFault=null;connectedAt=0;userSelectedTarget=false;
            Interlocked.Exchange(ref lastDecodedPreviewTick,0);
            running=true;armed=false;releaseRequested=false;message="正在连接…";
            view.EmptyMessage=message;view.Invalidate();
            samples.Clear();painted=0;
            Interlocked.Exchange(ref decoded,0);Interlocked.Exchange(ref bytes,0);
            previousDecoded=previousPainted=previousBytes=previousHashChanges=0;
            lastPaintTick=maxGapTicks=0;bucketAt=Stopwatch.GetTimestamp();
            int epoch=++generation;
            connectionWorker=new Thread(()=>ConnectWorker(epoch,destination)){IsBackground=true};
            connectionWorker.Start();
        }
    }
    bool OwnConnection(int epoch){return running&&epoch==Volatile.Read(ref generation);}
    void ConnectWorker(int epoch,SshDestination destination){
        Session own=null;Link video=null,input=null;
        try{
            own=new Session(root,destination);
            lock(connectionGate){
                if(!OwnConnection(epoch))return;
                session=own;
            }
            own.Start(testMode,false,()=>!OwnConnection(epoch));
            if(!OwnConnection(epoch))return;
            video=new Link(own.Port,own.Token,"frame");
            if(!OwnConnection(epoch))return;
            input=new Link(own.Port,own.Token,"control");
            lock(connectionGate){
                if(!OwnConnection(epoch))return;
                frameLink=video;controlLink=input;
                connectedAt=Stopwatch.GetTimestamp();bucketAt=connectedAt;
                Interlocked.Exchange(ref lastDecodedPreviewTick,connectedAt);
                message="已连接 · 等待画面";
            }
            Enqueue(Wire.Map("op","windows"));
            new Thread(()=>ControlWorker(epoch,input)){IsBackground=true}.Start();
            FrameWorker(epoch,video);
        }catch(Exception error){FailConnection(epoch,"startup",error);}
        finally{
            if(input!=null)input.Dispose();if(video!=null)video.Dispose();
            if(own!=null)own.Dispose();
            lock(connectionGate){
                if(epoch==generation){
                    session=null;frameLink=controlLink=null;connectionWorker=null;
                    if(connectionFault==null)message="已断开";
                    Volatile.Write(ref disconnecting,0);
                }
            }
        }
    }
    void FrameWorker(int epoch,Link link){
        var decoder=nativeFrames;
        decoder.Reset();
        long requested=Interlocked.Read(ref target);
        long keyframeAt=0;
        try{
            Wire.Send(link.Stream,Wire.Map("op","stream","window",requested,"keyframe",true));
            while(OwnConnection(epoch)){
                long selected=Interlocked.Read(ref target);
                if(selected!=requested){
                    requested=selected;
                    Wire.Send(link.Stream,Wire.Map("op","stream","window",selected,"keyframe",true));
                }
                var packet=link.Receive();
                var watch=Stopwatch.StartNew();
                Bitmap image;
                try{image=decoder.Decode(packet,960);}
                catch(InvalidDataException){
                    decoder.Reset();
                    long now=Stopwatch.GetTimestamp();
                    if((now-keyframeAt)*4>=Stopwatch.Frequency){
                        Wire.Send(link.Stream,Wire.Map("op","stream","window",requested,"keyframe",true));
                        keyframeAt=now;
                    }
                    continue;
                }
                Interlocked.Increment(ref decoded);Interlocked.Add(ref bytes,packet.Data.Length);
                string hash=Wire.Str(packet.Header,"raw_sha256");
                if(hash!=oldHash){Interlocked.Increment(ref hashChanges);oldHash=hash;}
                if(Wire.Long(packet.Header,"window")!=Interlocked.Read(ref target)){image.Dispose();continue;}
                lock(frameGate){
                    if(!OwnConnection(epoch)){image.Dispose();break;}
                    if(newest!=null)newest.Dispose();
                    newest=image;newestHeader=packet.Header;decodeMs=watch.Elapsed.TotalMilliseconds;
                    Interlocked.Exchange(ref lastDecodedPreviewTick,Stopwatch.GetTimestamp());
                    if(!previewReady){previewReady=true;message="已连接 · 只观看";}
                }
            }
        }catch(Exception error){FailConnection(epoch,"frame",error,link);}
    }
    void CheckFrameProgress(long now){
        long last=Interlocked.Read(ref lastDecodedPreviewTick);
        if(running&&last!=0&&now-last>=FrameTimeoutSeconds*Stopwatch.Frequency)
            FailConnection(generation,"frame",new TimeoutException());
    }
    void FailConnection(int epoch,string channel,Exception error,Link failedLink=null){
        Link video,input;
        string detail;
        var socket=error as System.Net.Sockets.SocketException??error.InnerException as System.Net.Sockets.SocketException;
        bool timeout=error is TimeoutException||socket!=null&&socket.SocketErrorCode==System.Net.Sockets.SocketError.TimedOut;
        if(channel=="startup")detail=error.Message=="临时采集进程启动超时"?"被控端采集启动超时 · 请重新连接":"连接失败 · 请重新连接";
        else if(channel=="control")detail="控制连接已断开 · 请重新连接";
        else if(timeout)detail="画面超时 · 已断开，请重新连接";
        else if(error is RemoteRequestException)detail="采集画面失败 · 已断开";
        else detail="画面连接已断开 · 请重新连接";
        lock(connectionGate){
            if(!OwnConnection(epoch))return;
            video=frameLink;input=controlLink;
            connectionFault=detail;
            Volatile.Write(ref disconnecting,connectionWorker!=null?1:0);
            running=false;previewReady=false;
            PauseFromWorker(detail);
        }
        if(video!=null)video.Dispose();
        if(input!=null)input.Dispose();
        if(failedLink!=null)failedLink.Dispose();
        SafeUi(()=>{
            if(epoch!=generation||running)return;
            StopAll(detail);
            ClearPreview();
            Log(detail);
            UpdateConnectionButton();
        });
        try{
            long last=Interlocked.Read(ref lastDecodedPreviewTick);
            Wire.Save(Path.Combine(root,"last-connection-error.json"),Wire.Map(
                "time_utc",DateTime.UtcNow.ToString("o"),"channel",channel,
                "kind",timeout?"timeout":error is EndOfStreamException?"eof":error.GetType().Name,
                "socket_code",socket==null?null:socket.SocketErrorCode.ToString(),
                "decoded_frames",Interlocked.Read(ref decoded),"frame_age_ms",last==0?-1:
                    (Stopwatch.GetTimestamp()-last)*1000.0/Stopwatch.Frequency));
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void ClearPreview(){
        if(lieRequested||lieVision.Enabled)StopLieVision(null);
        lock(frameGate){if(newest!=null)newest.Dispose();newest=null;newestHeader=null;}
        var old=view.Frame;view.Frame=null;view.Person=null;view.CombatAnchor=null;view.LieTarget=null;view.LieConfidence=0;
        view.EmptyMessage=connectionFault??"已断开";
        if(old!=null)old.Dispose();
        currentHeader=null;displayedWindow=-1;lastFrame=DateTime.MinValue;
        nativeFrames.Reset();view.Invalidate();
    }
    void UpdateConnectionButton(){
        bool cleaning=Volatile.Read(ref disconnecting)!=0;
        connectionPanel.AllowDeviceEdit(!running&&!cleaning);
        connect.Enabled=!cleaning&&(running||!connectionPanel.Dirty);
        connect.Text=cleaning?"清理中…":running?(previewReady?"断开连接":"取消连接"):"连接";
        var shown=running||cleaning?(activeDestination??savedWorkspace.ssh):savedWorkspace.ssh;
        deviceLabel.Text="被控端  "+shown.Label();
    }
    void ControlWorker(int epoch,Link link){DateTime beat=DateTime.MinValue;int appliedRoute=-1,appliedAuto=-1;long plannedSequence=-1;
        Dictionary<string,object> parsedData=null;
        CombatObservation sight=null;
        double originalAge=0,recoverySince=-1,visionWait=0;
        string recoveryIssue=null;
        var scheduler=new ActionScheduler();
        scheduler.Send=delegate(int key,bool down){
            if(!autoRunning||appliedAuto!=Volatile.Read(ref autoEpoch))return;
            try{
                link.Call(Wire.Map("op",down?"key_down":"key_up","vk",key));
                if(routeTrial)TraceTrial(Wire.Map("key",key,"down",down));
            }catch(RemoteRequestException e){if(OwnConnection(epoch))PauseFromWorker(e.Message);}
        };
        try{while(running&&epoch==generation){if(appliedAuto!=Volatile.Read(ref autoEpoch)){scheduler.Cancel();appliedAuto=Volatile.Read(ref autoEpoch);plannedSequence=-1;parsedData=null;sight=null;recoverySince=-1;visionWait=0;recoveryIssue=null;}if(releaseRequested){releaseRequested=false;link.Call(Wire.Map("op","release"));}Dictionary<string,object> routeUpdate=null;int revision;lock(routeGate){revision=routeRevision;if(revision!=appliedRoute)routeUpdate=new Dictionary<string,object>(routeRequest);}if(routeUpdate!=null){try{var reply=link.Call(routeUpdate);appliedRoute=revision;
        if(Wire.Bool(routeUpdate,"enabled")){
            int observed=revision;long window=Wire.Long(routeUpdate,"window"),watchEpoch=Wire.Long(reply.Header,"recording_epoch");
            new Thread(()=>FinishRouteWatch(epoch,observed,window,watchEpoch)){IsBackground=true}.Start();
        }
    }catch(RemoteRequestException e){appliedRoute=revision;int observed=revision;string reason=e.Message;
        SafeUi(()=>{if(RouteWatchCurrent(epoch,observed)&&routes!=null&&!routes.IsDisposed)routes.PauseRecording(reason);});
    }}Dictionary<string,object> request;if(queue.TryDequeue(out request)){string op=Wire.Str(request,"op");if(op!="windows"&&Wire.Int(request,"local_epoch")!=Volatile.Read(ref inputEpoch))continue;try{if(op=="type_once"){try{link.Call(Wire.Map("op","arm","window",Wire.Long(request,"window"),"desktop_input",true));link.Call(Wire.Map("op","text","text",Wire.Str(request,"text")));message="文字已发送 · 只观看";}finally{link.Call(Wire.Map("op","release"));}continue;}Packet result=link.Call(request);if(op=="prepare_vision"){int id=Wire.Int(request,"vision_id"),expected=Wire.Int(request,"local_epoch");long selected=Wire.Long(request,"window");string utc=Wire.Str(result.Header,"prepared_utc");new Thread(()=>FinishPreparation(id,expected,selected,utc)){IsBackground=true}.Start();}else if(op=="arm"){int expected=Wire.Int(request,"local_epoch");if(request.ContainsKey("auto_epoch")){int requested=Wire.Int(request,"auto_epoch");SafeUi(delegate{if(expected!=inputEpoch||requested!=autoEpoch||!autoPending||autoWindow!=Interlocked.Read(ref target)){releaseRequested=true;return;}
            var role=workspace.Current;
            var document=!routeTrial&&role.battle.mode!="stationary"&&routes.HasRuntimeRoute?routes.RuntimeRoute():null;
            planner=routeTrial?trialPlanner:new CombatPlanner(role,ClockMs(),document);
            autoPending=autoArmPending=false;autoRunning=true;message=pointNavigation?"导航中":routeTrial?"试走路线中":"运行中";
            Log(routeTrial?"开始试走路线":"开始运行 · "+workspace.Current.name);});}else SafeUi(delegate{if(expected!=inputEpoch)return;armPending=false;if(running&&view.Focused){
            armed=true;
            if(keyboardRecording!=null)ConfigureRouteWatch(true,keyboardRecording);
            else message="手动控制中";
        }else Pause();});}else if(op=="windows"){object values;if(result.Header.TryGetValue("windows",out values))SafeUi(()=>{if(OwnConnection(epoch))SetTargets(values);});}}catch(RemoteRequestException e){PauseFromWorker(e.Message);}}else Thread.Sleep(3);
        if(autoRunning){
            var ownPlanner=planner;
            bool trial=routeTrial;
            double now=ClockMs(),publishedAt;
            Dictionary<string,object> data;
            lock(observationGate){data=observation;publishedAt=observationAt;}
            if(data!=parsedData){
                parsedData=data;
                sight=data==null?null:CombatObservation.Parse(data,now);
                originalAge=sight==null?0:sight.age;
            }
            if(sight!=null)sight.age=originalAge+now-publishedAt;
            if(sight!=null&&sight.window!=autoWindow)PauseFromWorker("目标窗口已切换，已暂停");
            else if(ownPlanner!=null){
                string issue=ownPlanner.ObservationIssue(sight);
                if(issue!=null){
                    if(recoverySince<0)recoverySince=now;
                    if(recoveryIssue!=issue){
                        recoveryIssue=issue;
                        object workerMs;
                        lastVisionIssue=Wire.Map("utc",DateTime.UtcNow.ToString("o"),"reason",issue,
                            "sequence",sight==null?-1:sight.sequence,"age_ms",sight==null?-1:Math.Round(sight.age),
                            "position_fresh",sight!=null&&sight.fresh,"minimap_found",sight!=null&&sight.dot.HasValue,
                            "worker_ms",data!=null&&data.TryGetValue("processing_ms",out workerMs)?workerMs:null);
                    }
                    if(sight!=null&&sight.age<=CombatObservation.MaxAgeMs&&now-recoverySince<VisionHoldMs)
                        scheduler.ReleaseOnly(now);
                    else if(scheduler.Busy||scheduler.HasKeys)scheduler.Cancel();
                    if(autoRunning&&appliedAuto==Volatile.Read(ref autoEpoch))message="恢复定位中 · "+issue;
                }else{
                    if(recoverySince>=0)visionWait+=now-recoverySince;
                    recoverySince=-1;recoveryIssue=null;
                    sight.now=now-visionWait;
                    if(sight.sequence!=plannedSequence){
                        plannedSequence=sight.sequence;
                        var plan=ownPlanner.Tick(sight);
                        var routeState=ownPlanner.RouteState(sight);
                        routeState["sequence"]=sight.sequence;
                        routeState["utc"]=DateTime.UtcNow.ToString("o");
                        lastRouteState=routeState;
                        if(trial&&sight.dot.HasValue){
                            var dot=sight.dot.Value;
                            TraceTrial(Wire.Map("edge",ownPlanner.CurrentRouteEdge,"sequence",sight.sequence,
                                "x",dot.X,"y",dot.Y,"plan",plan==null?null:plan.name));
                        }
                        if(plan!=null){
                            if(plan.stopReason!=null)PauseFromWorker(plan.stopReason);
                            else{scheduler.Begin(plan,now);string text=plan.name;SafeUi(()=>Log(text));}
                        }
                        if(autoRunning&&appliedAuto==Volatile.Read(ref autoEpoch)){
                            message=(pointNavigation?"导航 · ":trial?"试走路线 · ":"运行中 · ")+ownPlanner.Status;
                            int? anchor=ownPlanner.AnchorX;
                            SafeUi(delegate{if(autoRunning)view.CombatAnchor=anchor;});
                        }
                    }
                    if(autoRunning&&appliedAuto==Volatile.Read(ref autoEpoch))scheduler.Tick(now);
                }
            }
        }
        if((DateTime.UtcNow-beat).TotalMilliseconds>=200){
            bool controlling=armed||autoRunning;
            try{link.Call(Wire.Map("op",controlling?"heartbeat":"ping"));}
            catch(RemoteRequestException e){
                if(!controlling)throw;
                if(OwnConnection(epoch))PauseFromWorker(e.Message);
            }
            beat=DateTime.UtcNow;
        }
    }}catch(Exception e){FailConnection(epoch,"control",e,link);}finally{if(epoch==generation)autoRunning=autoPending=false;try{scheduler.Cancel();link.Call(Wire.Map("op","record_keys","enabled",false));link.Call(Wire.Map("op","release"));}catch{}link.Dispose();}}
    void SafeUi(Action action){if(!IsDisposed)try{BeginInvoke(action);}catch{}}
    void PauseFromWorker(string text){bool endedTrial=routeTrial;if(endedTrial)FinishTrialTrace(text);autoRunning=autoPending=autoArmPending=false;Interlocked.Increment(ref autoEpoch);planner=null;armed=false;armPending=false;Interlocked.Increment(ref inputEpoch);releaseRequested=true;message=text;SafeUi(()=>{Pause();message=text;Log(text);});}
    void SelectPreviewTarget(long next){
        if(next==Interlocked.Read(ref target))return;
        Pause();
        if(routes!=null&&!routes.IsDisposed)routes.PauseRecording("预览目标已更换，录制已暂停");
        if(person!=null)person.Stop("预览目标已更换");
        CancelCapture();CancelPreparation("目标已改变");
        Interlocked.Exchange(ref target,next);
        if(showGuides.Checked&&next!=0&&IsHandleCreated)BeginInvoke(new Action(StartPreview));
    }
    void SetTargets(object values){
        var items=new List<TargetItem>{new TargetItem{Handle=0,Title="整个主屏幕"}};
        var list=values as System.Collections.IEnumerable;
        if(list!=null)foreach(object item in list){
            var descriptor=item as Dictionary<string,object>;
            if(descriptor==null)continue;
            long handle=Wire.Long(descriptor,"handle");
            if(handle!=0)items.Add(new TargetItem{Handle=handle,Title=Wire.Str(descriptor,"title")});
        }
        long selected=Interlocked.Read(ref target);
        var choice=userSelectedTarget?items.FirstOrDefault(item=>item.Handle==selected):null;
        if(choice==null){
            userSelectedTarget=false;
            var preferred=items.Where(item=>String.Equals(item.Title.Trim(),"冒险岛怀旧服",StringComparison.Ordinal)).ToArray();
            choice=preferred.FirstOrDefault(item=>item.Handle==selected)??preferred.FirstOrDefault()??items[0];
        }
        changingTargetList=true;targets.BeginUpdate();
        try{
            targets.Items.Clear();targets.Items.AddRange(items.ToArray());targets.SelectedItem=choice;
        }finally{targets.EndUpdate();changingTargetList=false;}
        SelectPreviewTarget(choice.Handle);
    }
    void QueueMetrics(Dictionary<string,object> snapshot){
        if(Interlocked.Exchange(ref metricsWritePending,1)!=0)return;
        ThreadPool.QueueUserWorkItem(delegate{
            try{Wire.Save(Path.Combine(root,"metrics.json"),snapshot);}
            catch(IOException){}catch(UnauthorizedAccessException){}
            finally{Interlocked.Exchange(ref metricsWritePending,0);}
        });
    }
    void Render(object sender,EventArgs e){Bitmap image=null;Dictionary<string,object> header=null;lock(frameGate){if(newest!=null){image=newest;header=newestHeader;newest=null;}}if(image!=null&&(!running||captureFrame!=null)){image.Dispose();image=null;}if(image!=null){var old=view.Frame;view.Frame=image;view.SourceSize=new Size(Wire.Int(header,"width"),Wire.Int(header,"height"));currentHeader=header;view.Sequence=Wire.Long(header,"sequence");if(view.Visible)view.Invalidate();if(old!=null)old.Dispose();lastFrame=DateTime.UtcNow;displayedWindow=Wire.Long(header,"window");}
        long now=Stopwatch.GetTimestamp();FeedLieVision(now);CheckFrameProgress(now);double seconds=(now-bucketAt)/(double)Stopwatch.Frequency;if(seconds>=1){long got=Interlocked.Read(ref decoded),size=Interlocked.Read(ref bytes),changed=Interlocked.Read(ref hashChanges);fps=(painted-previousPainted)/seconds;decodeFps=(got-previousDecoded)/seconds;mbps=(size-previousBytes)*8.0/seconds/1000000;double live=connectedAt==0?0:(now-connectedAt)/(double)Stopwatch.Frequency;
            if(running&&currentHeader!=null&&live>3){samples.Add(Wire.Map("elapsed_s",live,"painted_fps",fps,"decoded_fps",decodeFps,"max_paint_gap_ms",maxGapTicks*1000.0/Stopwatch.Frequency,"mbps",mbps,"distinct_pixel_frames",changed-previousHashChanges,"width",view.SourceSize.Width,"height",view.SourceSize.Height,"preview_width",view.Frame.Width,"preview_height",view.Frame.Height,"capture_ms",currentHeader["capture_ms"],"encode_ms",currentHeader["encode_ms"],"hash_ms",currentHeader["hash_ms"],"server_ms",currentHeader["processing_ms"],"decode_ms",decodeMs));if(samples.Count>300)samples.RemoveAt(0);QueueMetrics(Wire.Map("definition","unique decoded frames actually drawn by preview OnPaint; first 3 seconds excluded","samples",samples.ToArray(),"decoded",got,"painted",painted,"window",displayedWindow,"lossless",true,"status",message,"vision_issue",lastVisionIssue,"route_state",lastRouteState,"automation_running",autoRunning,"automation_pending",autoPending,"session",session==null?"":session.Id));}
            previousDecoded=got;previousPainted=painted;previousBytes=size;previousHashChanges=changed;maxGapTicks=0;bucketAt=now;
        }
        if(armed&&(DateTime.UtcNow-lastFrame).TotalMilliseconds>800){Pause();message="画面超时，已暂停键鼠";}if((now-lastLabels)/(double)Stopwatch.Frequency<0.1)return;lastLabels=now;
        RenderTrialPosition();
        navigateButton.Text=pointNavigation?"停止导航":"导航";
        navigateButton.Enabled=pointNavigation||running&&!autoPending&&!autoRunning&&!armed&&!armPending&&
            routes.HasRuntimeRoute&&routes.NavigationTarget.HasValue&&captureFrame==null&&prepareSuccess==null;
        settingsButton.Enabled=captureFrame==null&&prepareSuccess==null;
        previewButton.Enabled=routeWorkspaceButton.Enabled=settingsButton.Enabled;
        string gateMetrics=lieRequested?(lieGameActive?lieVision.Metrics():"等待小游戏 · 状态检查0.5秒/次"):"";status.Text="  "+(connectionFault??message);metrics.Text=view.Frame==null||routeSurface.Visible?"":String.Format("{0}×{1}  ·  {2:F1} FPS{3}  ",view.SourceSize.Width,view.SourceSize.Height,fps,String.IsNullOrEmpty(gateMetrics)?"":"  ·  "+gateMetrics);lieButton.Text=lieRequested?"关闭测谎":"开启测谎";lieButton.Enabled=lieRequested||running&&previewReady&&view.Frame!=null&&captureFrame==null;control.PresentsPauseAction=armed||armPending;control.Text=armed?"暂停控制（F12）":armPending?"取消开启":"开启手动控制";runButton.Text=routeTrial?"停止试走":autoPending?"取消准备":autoRunning?"暂停脚本":settingsDirty||setupRequired?"请先保存":"开启脚本";saveSettings.Enabled=(settingsDirty||setupRequired)&&!autoRunning&&!autoPending;runButton.Enabled=routeTrial||autoPending||autoRunning||!settingsDirty&&!setupRequired&&running&&previewReady&&Interlocked.Read(ref target)!=0&&captureFrame==null;control.Visible=!settingsOpen||captureFrame!=null||sideTabs.SelectedTab==routePage&&routes.ManualCaptureActive;control.Enabled=!autoRunning&&!autoPending&&running&&captureFrame==null&&view.Frame!=null&&displayedWindow==Interlocked.Read(ref target);typeText.Enabled=control.Enabled;control.BackColor=armed?AppUi.Selection:control.Enabled?AppUi.Field:AppUi.Surface;UpdateConnectionButton();
    }
    void Disconnect(bool wait=false){
        StopAll("连接已断开，录制已暂停");
        Link video,input;
        Thread worker;
        lock(connectionGate){
            connectionFault=null;running=false;previewReady=false;
            video=frameLink;input=controlLink;worker=connectionWorker;
            Volatile.Write(ref disconnecting,worker!=null?1:0);
            message=worker!=null?"正在断开…":"已断开";
        }
        if(video!=null)video.Dispose();if(input!=null)input.Dispose();
        ClearPreview();
        if(wait&&worker!=null)worker.Join(8500);
    }
    protected override void Dispose(bool disposing){
        if(disposing){
            timer.Stop();
            lieVision.Dispose();
            if(connectionWorker!=null||running)Disconnect(true);
            else if(person!=null&&!person.IsDisposed)person.Stop("窗口已关闭");
            lock(frameGate){if(newest!=null)newest.Dispose();newest=null;}
            timer.Dispose();
        }
        base.Dispose(disposing);
    }
    [STAThread]public static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);string root=args.Length>0?args[0]:AppDomain.CurrentDomain.BaseDirectory;try{bool created;using(var mutex=new Mutex(true,"Local\\SshScreenControl20261119V2",out created)){if(!created){foreach(var p in Process.GetProcessesByName("SSH-Screen-Control"))if(p.Id!=Process.GetCurrentProcess().Id&&p.MainWindowHandle!=IntPtr.Zero){ShowWindow(p.MainWindowHandle,9);SetForegroundWindow(p.MainWindowHandle);break;}return;}try{Application.Run(new ControllerForm(root,args.Contains("--test")));}finally{mutex.ReleaseMutex();}}}catch(Exception e){Wire.Save(Path.Combine(root,"controller-error.json"),Wire.Map("error",e.Message));MessageBox.Show(e.Message,"SSH双机控制");}}

}
}
