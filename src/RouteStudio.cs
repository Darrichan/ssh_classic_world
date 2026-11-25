using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SshScreen {
static class AppUi {
    public static readonly Color Background=Color.FromArgb(16,24,33),Surface=Color.FromArgb(23,34,46),
        Field=Color.FromArgb(29,44,59),Border=Color.FromArgb(46,64,82),Text=Color.FromArgb(235,242,249),
        Muted=Color.FromArgb(163,181,199),Blue=Color.FromArgb(91,222,185),Selection=Color.FromArgb(32,65,64);
    public static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r,int radius){var p=new System.Drawing.Drawing2D.GraphicsPath();int d=Math.Max(2,Math.Min(radius*2,Math.Min(r.Width,r.Height)));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static void Round(Control c,int radius){Action fit=delegate{if(c.Width<2||c.Height<2)return;using(var path=Rounded(new Rectangle(0,0,c.Width,c.Height),radius)){var old=c.Region;c.Region=new Region(path);if(old!=null)old.Dispose();}};c.Resize+=delegate{fit();};fit();}
    public static void Card(Control c){c.BackColor=Surface;Round(c,8);}
    public static void Button(Button button,bool primary=false){
        button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;
        button.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(121,232,202):Border;
        button.FlatAppearance.MouseDownBackColor=primary?Color.FromArgb(70,191,160):Selection;
        Action style=()=>{button.BackColor=button.Enabled?(primary?Blue:Field):Surface;button.ForeColor=button.Enabled?(primary?Background:Text):Muted;};
        style();button.EnabledChanged+=delegate{style();};button.Cursor=Cursors.Hand;button.Margin=new Padding(4);Round(button,7);
    }
    public static void ApplyInputs(Control root){
        foreach(Control control in root.Controls){
            var text=control as TextBox;
            if(text!=null){text.BackColor=Field;text.ForeColor=Text;}
            ApplyInputs(control);
        }
    }
    public static void Tabs(TabControl tabs){tabs.DrawMode=TabDrawMode.OwnerDrawFixed;tabs.SizeMode=TabSizeMode.Fixed;tabs.ItemSize=new Size(62,34);tabs.DrawItem+=delegate(object sender,DrawItemEventArgs e){e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var brush=new SolidBrush(Background))e.Graphics.FillRectangle(brush,e.Bounds);var rect=Rectangle.Inflate(e.Bounds,-3,-3);if(e.Index==tabs.SelectedIndex)using(var path=Rounded(rect,7))using(var brush=new SolidBrush(Field))e.Graphics.FillPath(brush,path);TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,tabs.Font,rect,e.Index==tabs.SelectedIndex?Text:Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};foreach(TabPage page in tabs.TabPages)page.BackColor=Background;}
}

sealed class RouteImageSelection:Control {
    public Bitmap Image;public Rectangle Selection;public Point Dot;public bool HasDot,PickDot;Point anchor;bool dragging;
    public event Action Changed;
    public RouteImageSelection(){DoubleBuffered=true;BackColor=Color.FromArgb(24,30,40);Cursor=Cursors.Cross;}
    Rectangle Area(){if(Image==null)return Rectangle.Empty;double s=Math.Min((double)ClientSize.Width/Image.Width,(double)ClientSize.Height/Image.Height);int w=(int)(Image.Width*s),h=(int)(Image.Height*s);return new Rectangle((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);}
    public double PreviewScale{get{return Image==null?1:(double)Area().Width/Image.Width;}}
    Point Native(Point p){var r=Area();return new Point(Math.Max(0,Math.Min(Image.Width-1,(p.X-r.X)*Image.Width/Math.Max(1,r.Width))),Math.Max(0,Math.Min(Image.Height-1,(p.Y-r.Y)*Image.Height/Math.Max(1,r.Height))));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Image==null||e.Button!=MouseButtons.Left||!Area().Contains(e.Location))return;if(PickDot){Dot=Native(e.Location);HasDot=Selection.Contains(Dot);if(Changed!=null)Changed();Invalidate();return;}anchor=Native(e.Location);dragging=true;Capture=true;HasDot=false;}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;var p=Native(e.Location);Selection=Rectangle.FromLTRB(Math.Min(anchor.X,p.X),Math.Min(anchor.Y,p.Y),Math.Max(anchor.X,p.X)+1,Math.Max(anchor.Y,p.Y)+1);Invalidate();if(Changed!=null)Changed();}
    protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;base.OnMouseUp(e);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Image==null)return;var r=Area();e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;e.Graphics.DrawImage(Image,r);double sx=(double)r.Width/Image.Width,sy=(double)r.Height/Image.Height;using(var pen=new Pen(Color.Cyan,2))e.Graphics.DrawRectangle(pen,(float)(r.X+Selection.X*sx),(float)(r.Y+Selection.Y*sy),(float)(Selection.Width*sx),(float)(Selection.Height*sy));if(HasDot)using(var pen=new Pen(Color.Orange,2))e.Graphics.DrawEllipse(pen,(float)(r.X+Dot.X*sx-7),(float)(r.Y+Dot.Y*sy-7),14,14);}
}
sealed class RouteCalibration:Form {
    readonly RouteImageSelection canvas=new RouteImageSelection();readonly Label hint=new Label();readonly Button ok=new AppleButton();
    readonly Dictionary<string,object> frame;Color dotColor;public RouteDocument Result;
    public RouteCalibration(Dictionary<string,object> snapshot,RouteDocument old){frame=snapshot;Text="标定小地图 · 使用完整原图";Size=new Size(1100,780);MinimumSize=new Size(760,560);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=76,Padding=new Padding(8)};var roi=new AppleButton{Text="① 拖框小地图",Width=150,Height=32};var dot=new AppleButton{Text="② 点选人物黄点",Width=160,Height=32};ok.Text="使用此标定";ok.Width=130;ok.Height=32;ok.Enabled=false;hint.AutoSize=true;hint.Text="拖框小地图，再点一下人物黄点附近。";
        roi.Click+=delegate{canvas.PickDot=false;};dot.Click+=delegate{canvas.PickDot=true;};top.Controls.AddRange(new Control[]{roi,dot,ok,hint});canvas.Dock=DockStyle.Fill;canvas.Image=Wire.BitmapFrom((byte[])snapshot["bgr24"],Wire.Int(snapshot,"width"),Wire.Int(snapshot,"height"));canvas.Changed+=delegate{
            ok.Enabled=false;
            if(!canvas.HasDot){hint.Text="点一下人物黄点附近";return;}
            var found=MinimapLayout.Pick(frame,canvas.Selection,canvas.Dot,canvas.PreviewScale);
            canvas.HasDot=found.ok;
            if(found.ok){canvas.Dot=new Point(found.x,found.y);dotColor=found.color;ok.Enabled=true;}
            hint.Text=found.ok?"已对准黄点":found.reason;
        };
        ok.Click+=delegate{
            if(!canvas.HasDot)return;
            Result=MinimapLayout.Calibrate(frame,canvas.Selection,canvas.Dot,dotColor,old);
            DialogResult=DialogResult.OK;Close();
        };Controls.Add(canvas);Controls.Add(top);FormClosed+=delegate{canvas.Image.Dispose();};
    }
}
public sealed class RouteCanvas:Control {
    public RouteDocument Document;public Bitmap Map;public readonly object Gate;
    bool allowEdit=true;
    public bool AllowEdit{
        get{return allowEdit;}
        set{if(allowEdit==value)return;allowEdit=value;if(!value)CancelConnection();}
    }
    public Point? LivePosition{get;private set;}
    public string SelectedEdge;
    readonly HashSet<string> selectedNodes=new HashSet<string>(),selectedEdges=new HashSet<string>();
    public string[] SelectedNodeIds{get{return selectedNodes.Concat(selectedNode==null?new string[0]:new[]{selectedNode.id}).Distinct().ToArray();}}
    public string[] SelectedEdgeIds{get{return selectedEdges.Concat(SelectedEdge==null?new string[0]:new[]{SelectedEdge}).Distinct().ToArray();}}
    public bool HasSelection{get{return selectedNode!=null||SelectedEdge!=null||selectedNodes.Count>0||selectedEdges.Count>0;}}
    public int ZoomPercent{get{return (int)Math.Round(zoom*100);}}
    public event Action SelectionChanged;
    public event Action<int> ZoomChanged;
    public event Action<string[],string[]> DeleteSelectionRequested;
    public bool FitWholeMap;
    public Point? MapOrigin;
    public Point? TargetPoint;
    public Action<Point> MapPointPicked;
    public string Brush{get;private set;}
    public List<RouteEdge> Path=new List<RouteEdge>();
    public event Action<string> EdgeSelected,EraseEdge;
    public event Action<string,List<RoutePoint>,RouteNode,RouteNode> StrokeDrawn;
    public event Func<RouteNode,Point,RouteEdge,RouteNode> NodeMoved;
    public event Action<RouteNode> EraseNode,DeleteNode;
    public event Action<RouteEdge,Point> SplitWalk;
    public event Action<RouteEdge,bool> ConvertWalkToJump;
    public event Action<string> EditHint;
    readonly List<RoutePoint> stroke=new List<RoutePoint>();
    Point mouseStart;
    Point? dragTarget;
    RouteNode dragNode,hoverNode,strokeStart,connectionStart,selectedNode;
    Point? pendingActionStart;
    RouteNode pendingActionNode;
    RouteEdge dragEdge;
    bool chooseSegmentFirst;
    readonly ContextMenuStrip nodeMenu=new ContextMenuStrip{ShowImageMargin=false};
    readonly ContextMenuStrip edgeMenu=new ContextMenuStrip{ShowImageMargin=false};
    PointF pan,panStart;
    bool drawing,panning,selecting,boxSelecting;
    Point boxEnd;
    MouseButtons gestureButton;
    float scale=1,ox,oy,zoom=1;
    Size layoutSize;

    public RouteCanvas(object gate){
        Gate=gate;DoubleBuffered=true;BackColor=Color.FromArgb(22,29,41);
        Brush="select";
        SetStyle(ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        TabStop=true;AccessibleName="路线图";
        nodeMenu.Items.Add("删除节点",null,delegate{DeleteSelectedNode();});
        Disposed+=delegate{nodeMenu.Dispose();edgeMenu.Dispose();};
    }
    public void Fit(){Capture=false;CancelConnection();zoom=1;pan=PointF.Empty;Invalidate();if(ZoomChanged!=null)ZoomChanged(ZoomPercent);}
    public void ZoomBy(float factor){ZoomAt(factor,new Point(ClientSize.Width/2,ClientSize.Height/2));}
    public void ZoomAt(float factor,Point at){lock(Gate){
        if(Document==null||drawing||panning||selecting||boxSelecting)return;
        LayoutMap();float x=(at.X-ox)/scale,y=(at.Y-oy)/scale;
        zoom=Math.Max(.25f,Math.Min(12f,zoom*factor));LayoutMap();
        pan=new PointF(pan.X+at.X-(ox+x*scale),pan.Y+at.Y-(oy+y*scale));LayoutMap();
        Invalidate();if(ZoomChanged!=null)ZoomChanged(ZoomPercent);
    }}
    public void ClearSelection(){
        Capture=false;ResetGesture();CancelConnection();selectedNodes.Clear();selectedEdges.Clear();SelectedEdge=null;
        MapPointPicked=null;Cursor=Brush=="select"?Cursors.Default:Cursors.Cross;
        Invalidate();if(EdgeSelected!=null)EdgeSelected(null);if(SelectionChanged!=null)SelectionChanged();
    }
    void SelectBox(RectangleF bounds){
        bounds.Inflate(1,1);selectedNodes.Clear();selectedEdges.Clear();selectedNode=null;SelectedEdge=null;
        foreach(var node in Document.nodes)if(bounds.Contains(ScreenPoint(node.x,node.y)))selectedNodes.Add(node.id);
        foreach(var edge in Document.edges){var points=EdgePoints(edge);if(points.Length>=2&&points.All(point=>bounds.Contains(point)))selectedEdges.Add(edge.id);}
        if(SelectionChanged!=null)SelectionChanged();if(EdgeSelected!=null)EdgeSelected(null);
    }
    public void SetBrush(string brush){
        // A toolbar action always starts a new gesture, even when reselected.
        // Endpoint dragging belongs exclusively to the selection tool.
        ClearSelection();
        Brush=brush;Cursor=brush=="select"?Cursors.Default:Cursors.Cross;Invalidate();
    }
    internal Action PreserveView(){
        LayoutMap();
        var document=Document;float beforeScale=scale;
        var center=new PointF((ClientSize.Width/2f-ox)/scale,(ClientSize.Height/2f-oy)/scale);
        return delegate{
            if(Document!=document)return;
            LayoutMap();zoom=Math.Max(.25f,Math.Min(12f,zoom*beforeScale/scale));LayoutMap();
            pan=new PointF(pan.X+ClientSize.Width/2f-(ox+center.X*scale),
                pan.Y+ClientSize.Height/2f-(oy+center.Y*scale));LayoutMap();Invalidate();
            if(ZoomChanged!=null)ZoomChanged(ZoomPercent);
        };
    }
    public void CancelConnection(){
        connectionStart=hoverNode=selectedNode=pendingActionNode=null;pendingActionStart=null;Invalidate();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys key){
        if(key==Keys.Escape&&(HasSelection||connectionStart!=null||pendingActionStart.HasValue||boxSelecting)){ClearSelection();return true;}
        if((key&Keys.Control)!=0&&((key&Keys.KeyCode)==Keys.Add||(key&Keys.KeyCode)==Keys.Oemplus)){ZoomBy(1.25f);return true;}
        if((key&Keys.Control)!=0&&((key&Keys.KeyCode)==Keys.Subtract||(key&Keys.KeyCode)==Keys.OemMinus)){ZoomBy(.8f);return true;}
        if(key==Keys.Delete&&DeleteSelectedNode())return true;
        return base.ProcessCmdKey(ref msg,key);
    }
    bool DeleteSelectedNode(){
        if(!AllowEdit)return false;
        if((selectedNodes.Count>0||selectedEdges.Count>0)&&DeleteSelectionRequested!=null){
            DeleteSelectionRequested(SelectedNodeIds,SelectedEdgeIds);return true;
        }
        if(selectedNode==null||DeleteNode==null)return false;
        var node=selectedNode;selectedNode=null;DeleteNode(node);Invalidate();return true;
    }
    public void SetLivePosition(Point? position){
        if(LivePosition==position)return;
        LivePosition=position;Invalidate();
    }
    public static Color ActionColor(string action){
        switch(action){
            case "walk":return Color.FromArgb(94,214,248);
            case "climb":return Color.FromArgb(202,164,255);
            case "drop":return Color.FromArgb(255,153,150);
            default:return Color.FromArgb(255,203,116);
        }
    }
    static bool IsJump(string kind){
        return kind=="jump"||kind=="jump_down"||kind=="rope_mount"||kind=="rope_exit";
    }
    void LayoutMap(){
        if(drawing||dragNode!=null||Document==null)return;
        var d=Document;
        var points=d.nodes.Select(n=>new Point(n.x,n.y))
            .Concat(d.edges.Where(e=>!IsJump(e.kind)).SelectMany(e=>e.points.Select(p=>new Point(p.x,p.y))));
        if(d.edges.Count==0&&!d.graphEdited)points=points.Concat(d.samples.Select(p=>new Point(p.x,p.y)));
        var route=points.ToArray();
        RectangleF bounds;
        if(route.Length>0&&!FitWholeMap){
            float left=route.Min(p=>p.X),right=route.Max(p=>p.X),top=route.Min(p=>p.Y),bottom=route.Max(p=>p.Y);
            float w=Math.Max(56,right-left+24),h=Math.Max(44,bottom-top+20);
            bounds=new RectangleF((left+right-w)/2,(top+bottom-h)/2,w,h);
        }else{
            var origin=MapOrigin??new Point(d.mapX,d.mapY);
            bounds=new RectangleF(origin.X,origin.Y,Map==null?Math.Max(120,d.roiWidth):Map.Width,
                Map==null?Math.Max(100,d.roiHeight):Map.Height);
        }
        scale=Math.Max(.01f,Math.Min(Math.Max(1,ClientSize.Width-(FitWholeMap?12:48))/bounds.Width,Math.Max(1,ClientSize.Height-(FitWholeMap?12:48))/bounds.Height))*zoom;
        ox=ClientSize.Width/2f-(bounds.Left+bounds.Width/2)*scale+pan.X;
        oy=ClientSize.Height/2f-(bounds.Top+bounds.Height/2)*scale+pan.Y;
        layoutSize=ClientSize;
    }
    protected override void OnResize(EventArgs e){
        bool preserve=Document!=null&&layoutSize.Width>0&&layoutSize.Height>0&&scale>0&&
            (Math.Abs(zoom-1)>.001f||pan!=PointF.Empty);
        var center=preserve?new PointF((layoutSize.Width/2f-ox)/scale,(layoutSize.Height/2f-oy)/scale):PointF.Empty;
        base.OnResize(e);
        if(preserve)lock(Gate){
            LayoutMap();pan=new PointF(pan.X+ClientSize.Width/2f-(ox+center.X*scale),
                pan.Y+ClientSize.Height/2f-(oy+center.Y*scale));LayoutMap();
        }
    }
    PointF ScreenPoint(int x,int y){return new PointF(ox+x*scale,oy+y*scale);}
    Point WorldPoint(PointF p){
        int x=(int)Math.Round((p.X-ox)/scale),y=(int)Math.Round((p.Y-oy)/scale);
        var origin=MapOrigin??new Point(Document.mapX,Document.mapY);
        int left=origin.X,top=origin.Y;
        return new Point(Math.Max(left,Math.Min(left+(Map==null?Document.roiWidth:Map.Width)-1,x)),
            Math.Max(top,Math.Min(top+(Map==null?Document.roiHeight:Map.Height)-1,y)));
    }
    Point NodePosition(RouteNode node,RouteEdge edge=null){
        return node==dragNode&&dragTarget.HasValue&&(dragEdge==null||dragEdge==edge)?dragTarget.Value:new Point(node.x,node.y);
    }
    PointF[] EdgePoints(RouteEdge edge){
        var a=Document.nodes.FirstOrDefault(n=>n.id==edge.from);
        var b=Document.nodes.FirstOrDefault(n=>n.id==edge.to);
        if(a==null||b==null)return new PointF[0];
        var start=NodePosition(a,edge);var end=NodePosition(b,edge);
        var first=ScreenPoint(start.X,start.Y);var last=ScreenPoint(end.X,end.Y);
        if(IsJump(edge.kind))return JumpArc(first,last);
        if(edge.kind=="drop")return new[]{first,last};
        var shape=dragTarget.HasValue&&(dragEdge==null||dragEdge==edge)&&(a==dragNode||b==dragNode)?
            RouteGraph.ResizeEdge(edge,a,b,start,end):edge.points;
        var points=shape.Count>=2?shape.Select(p=>ScreenPoint(p.x,p.y)).ToArray():new PointF[2];
        points[0]=first;points[points.Length-1]=last;
        return points;
    }
    static PointF[] JumpArc(PointF from,PointF to){
        float dx=to.X-from.X,dy=to.Y-from.Y;
        float length=(float)Math.Sqrt(dx*dx+dy*dy);
        float bend=Math.Max(8,Math.Min(36,length*.24f));
        float cx=(from.X+to.X)/2,cy=(from.Y+to.Y)/2;
        if(Math.Abs(dx)<8)cx+=bend;else cy-=bend;
        var curve=new PointF[17];
        for(int i=0;i<curve.Length;i++){
            float t=(float)i/(curve.Length-1),u=1-t;
            curve[i]=new PointF(u*u*from.X+2*u*t*cx+t*t*to.X,u*u*from.Y+2*u*t*cy+t*t*to.Y);
        }
        return curve;
    }
    static void DashJump(Pen pen){
        pen.DashPattern=new[]{7f/pen.Width,5f/pen.Width};
    }
    static void DrawArrow(Graphics g,Pen pen,PointF a,PointF b){
        float dx=b.X-a.X,dy=b.Y-a.Y,length=(float)Math.Sqrt(dx*dx+dy*dy);
        if(length<17)return;
        float x=a.X+dx*.62f,y=a.Y+dy*.62f;
        dx=dx/length*7;dy=dy/length*7;
        g.DrawLines(pen,new[]{new PointF(x-dx-dy*.55f,y-dy+dx*.55f),new PointF(x,y),new PointF(x-dx+dy*.55f,y-dy-dx*.55f)});
    }
    void DrawEdge(Graphics g,RouteEdge edge,bool selected){
        var points=EdgePoints(edge);if(points.Length<2)return;
        bool jump=IsJump(edge.kind);
        using(var halo=new Pen(Color.FromArgb(232,16,23,32),selected?8:5.5f))
        using(var ink=new Pen(selected?Color.White:edge.suggestedJump?Color.FromArgb(242,154,94):ActionColor(edge.kind),selected?3.5f:2.5f)){
            halo.LineJoin=ink.LineJoin=LineJoin.Round;
            halo.StartCap=halo.EndCap=ink.StartCap=ink.EndCap=LineCap.Round;
            if(edge.suggestedJump){halo.DashStyle=ink.DashStyle=DashStyle.Dot;}
            else if(jump||edge.kind=="drop"||edge.kind=="walk_off"){DashJump(halo);DashJump(ink);}
            g.DrawLines(halo,points);g.DrawLines(ink,points);
            if(selected&&!jump){
                int longest=1;double length=-1;
                for(int i=1;i<points.Length;i++){
                    double next=Distance(points[i-1].X,points[i-1].Y,points[i].X,points[i].Y);
                    if(next>length){longest=i;length=next;}
                }
                var a=points[longest-1];var b=points[longest];
                DrawArrow(g,ink,a,b);
                if(edge.both&&length>=26)DrawArrow(g,ink,b,a);
            }
        }
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        lock(Gate){
            if(Document==null)return;
            LayoutMap();var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Map!=null){
                var origin=MapOrigin??new Point(Document.mapX,Document.mapY);
                var rect=new RectangleF(ox+origin.X*scale,oy+origin.Y*scale,Map.Width*scale,Map.Height*scale);
                g.InterpolationMode=InterpolationMode.NearestNeighbor;g.DrawImage(Map,rect);
                using(var shade=new SolidBrush(Color.FromArgb(FitWholeMap?80:138,18,25,38)))g.FillRectangle(shade,rect);
            }
            if(!AllowEdit&&!Document.graphEdited&&Document.edges.Count==0&&Document.samples.Count>1){
                using(var trail=new Pen(AppUi.Muted,2){DashStyle=DashStyle.Dot}){
                    int begin=Math.Max(1,Document.samples.Count-6000);
                    for(int i=begin;i<Document.samples.Count;i++){
                        var a=Document.samples[i-1];var b=Document.samples[i];
                        if(a.segment==b.segment)g.DrawLine(trail,ScreenPoint(a.x,a.y),ScreenPoint(b.x,b.y));
                    }
                }
            }
            var active=Document.edges.FirstOrDefault(edge=>edge.id==SelectedEdge);
            foreach(var edge in Document.edges)if(edge!=active)DrawEdge(g,edge,Path.Contains(edge)||selectedEdges.Contains(edge.id));
            if(active!=null)DrawEdge(g,active,true);
            if(AllowEdit){
                var gripNodes=HiddenRopeGrips();
                using(var fill=new SolidBrush(BackColor))
                using(var rim=new Pen(Color.FromArgb(218,236,240,247),1.2f))
                foreach(var node in Document.nodes){
                    if(gripNodes.Contains(node.id))continue;
                    var point=NodePosition(node);var center=ScreenPoint(point.X,point.Y);
                    bool highlighted=node==hoverNode||node==selectedNode||node==dragNode&&dragEdge==null||node==connectionStart||selectedNodes.Contains(node.id);
                    bool selected=active!=null&&(node.id==active.from||node.id==active.to);
                    float radius=highlighted?5f:selected?4f:Brush=="select"?1.7f:3f;
                    if(node.marker!=null){
                        radius=highlighted?6:4;
                        var color=ActionColor(node.marker=="rope"||node.marker=="grip"?"climb":
                            node.marker=="drop"?"drop":node.marker=="launch"?"rope_mount":"walk");
                        using(var marker=new SolidBrush(color)){
                            if(node.marker=="launch"||node.marker=="grip")
                                g.FillPolygon(marker,new[]{new PointF(center.X,center.Y-radius),
                                    new PointF(center.X+radius,center.Y),new PointF(center.X,center.Y+radius),
                                    new PointF(center.X-radius,center.Y)});
                            else g.FillEllipse(marker,center.X-radius,center.Y-radius,2*radius,2*radius);
                        }
                    }else g.FillEllipse(highlighted?Brushes.White:fill,center.X-radius,center.Y-radius,2*radius,2*radius);
                    g.DrawEllipse(rim,center.X-radius,center.Y-radius,2*radius,2*radius);
                }
                if(dragEdge!=null&&dragTarget.HasValue){
                    var point=dragTarget.Value;var center=ScreenPoint(point.X,point.Y);
                    g.FillEllipse(Brushes.White,center.X-4,center.Y-4,8,8);
                }
            }
            if(stroke.Count>1){
                var shape=Brush=="walk"?RouteGraph.NormalizeWalkingSweep(stroke):stroke;
                var path=shape.Select(pt=>ScreenPoint(pt.x,pt.y)).ToArray();
                if(Brush=="walk")path=new[]{path[0],path[path.Length-1]};
                else if(IsJump(Brush))path=JumpArc(path[0],path[path.Length-1]);
                else if(Brush=="climb"){
                    float axis=(path[0].X+path[path.Length-1].X)/2;
                    path=new[]{new PointF(axis,path[0].Y),new PointF(axis,path[path.Length-1].Y)};
                }
                using(var ink=new Pen(ActionColor(Brush),3)){DashJump(ink);g.DrawLines(ink,path);}
            }
            if(boxSelecting){
                var box=Rectangle.FromLTRB(Math.Min(mouseStart.X,boxEnd.X),Math.Min(mouseStart.Y,boxEnd.Y),
                    Math.Max(mouseStart.X,boxEnd.X),Math.Max(mouseStart.Y,boxEnd.Y));
                using(var fill=new SolidBrush(Color.FromArgb(35,94,214,248)))g.FillRectangle(fill,box);
                using(var rim=new Pen(Color.FromArgb(94,214,248),1){DashStyle=DashStyle.Dash})g.DrawRectangle(rim,box);
            }
            if(LivePosition.HasValue){
                var live=LivePosition.Value;
                var point=ScreenPoint(live.X,live.Y);
                using(var halo=new SolidBrush(Color.FromArgb(22,29,41)))g.FillEllipse(halo,point.X-7,point.Y-7,14,14);
                using(var rim=new Pen(Color.White,1.5f))g.DrawEllipse(rim,point.X-5,point.Y-5,10,10);
                using(var center=new SolidBrush(Color.FromArgb(255,221,55)))g.FillEllipse(center,point.X-4,point.Y-4,8,8);
            }
            if(pendingActionStart.HasValue){
                var point=ScreenPoint(pendingActionStart.Value.X,pendingActionStart.Value.Y);
                using(var ring=new Pen(ActionColor("rope_mount"),2))
                    g.DrawEllipse(ring,point.X-7,point.Y-7,14,14);
            }
            if(TargetPoint.HasValue){
                var target=ScreenPoint(TargetPoint.Value.X,TargetPoint.Value.Y);
                using(var outline=new Pen(Color.SpringGreen,2)){
                    g.DrawEllipse(outline,target.X-6,target.Y-6,12,12);
                    g.DrawLine(outline,target.X-8,target.Y,target.X+8,target.Y);
                    g.DrawLine(outline,target.X,target.Y-8,target.X,target.Y+8);
                }
            }
            if(Document.roiWidth==0)TextRenderer.DrawText(g,"请先标定地图",Font,ClientRectangle,Color.LightSteelBlue,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
    }
    static double Distance(double x,double y,double a,double b){double dx=x-a,dy=y-b;return Math.Sqrt(dx*dx+dy*dy);}
    static double ToSegment(Point p,PointF a,PointF b){
        double x=b.X-a.X,y=b.Y-a.Y,square=x*x+y*y;
        if(square==0)return Distance(p.X,p.Y,a.X,a.Y);
        double t=Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Y-a.Y)*y)/square));
        return Distance(p.X,p.Y,a.X+t*x,a.Y+t*y);
    }
    string HitEdge(Point point){
        RouteEdge hit=null;double distance=9;
        foreach(var edge in Document.edges){
            var points=EdgePoints(edge);
            for(int i=1;i<points.Length;i++){
                double next=ToSegment(point,points[i-1],points[i]);
                if(next<distance){distance=next;hit=edge;}
            }
        }
        return hit==null?null:hit.id;
    }
    void ShowEditMenu(Point at){
        RouteEdge edge=null;Point split=Point.Empty;string[] directions=null;
        lock(Gate){
            LayoutMap();selectedNode=HitNode(at);
            if(selectedNode==null){
                string id=HitEdge(at);
                edge=Document.edges.FirstOrDefault(e=>e.id==id);
                if(RouteGraph.CanEditWalkAction(edge)){
                    SelectedEdge=id;split=WorldPoint(at);
                    var from=Document.nodes.First(n=>n.id==edge.from);
                    var to=Document.nodes.First(n=>n.id==edge.to);
                    directions=to.x>from.x?new[]{"向右跳","向左跳"}:to.x<from.x?new[]{"向左跳","向右跳"}:
                        to.y>from.y?new[]{"向下跳","向上跳"}:new[]{"向上跳","向下跳"};
                }
            }
        }
        Invalidate();
        if(selectedNode!=null){nodeMenu.Show(this,at);return;}
        if(directions==null)return;
        if(EdgeSelected!=null)EdgeSelected(edge.id);
        while(edgeMenu.Items.Count>0){var old=edgeMenu.Items[0];edgeMenu.Items.RemoveAt(0);old.Dispose();}
        edgeMenu.Items.Add("从这里拆分",null,delegate{if(SplitWalk!=null)SplitWalk(edge,split);});
        var jump=new ToolStripMenuItem("改为跳跃");
        jump.DropDownItems.Add(directions[0],null,delegate{if(ConvertWalkToJump!=null)ConvertWalkToJump(edge,false);});
        jump.DropDownItems.Add(directions[1],null,delegate{if(ConvertWalkToJump!=null)ConvertWalkToJump(edge,true);});
        edgeMenu.Items.Add(jump);
        edgeMenu.Show(this,at);
    }
    RouteNode HitNode(Point point){
        RouteNode hit=null;double distance=8;
        var gripNodes=HiddenRopeGrips();
        foreach(var node in Document.nodes){
            if(gripNodes.Contains(node.id))continue;
            var center=ScreenPoint(node.x,node.y);
            double next=Distance(point.X,point.Y,center.X,center.Y);
            if(next<distance){hit=node;distance=next;}
        }
        return hit;
    }
    HashSet<string> HiddenRopeGrips(){
        var grips=new HashSet<string>(Document.edges.Where(e=>e.ropeEntries!=null)
            .SelectMany(e=>e.ropeEntries.Where(id=>id!=e.from)));
        foreach(var edge in Document.edges.Where(e=>e.kind!="rope_mount"||e.id==SelectedEdge)){
            grips.Remove(edge.from);grips.Remove(edge.to);
        }
        return grips;
    }
    void EraseAt(Point point){
        var node=HitNode(point);
        if(node!=null&&!Document.edges.Any(e=>e.from==node.id||e.to==node.id)){
            if(EraseNode!=null)EraseNode(node);
            return;
        }
        string target=HitEdge(point);
        if(target!=null&&EraseEdge!=null)EraseEdge(target);
    }
    void UpdateDragTarget(Point point){
        if(chooseSegmentFirst){
            if(Distance(point.X,point.Y,mouseStart.X,mouseStart.Y)>=4&&EditHint!=null)
                EditHint("请先选中要调整的路线段");
            return;
        }
        if(dragTarget.HasValue||Distance(point.X,point.Y,mouseStart.X,mouseStart.Y)>=4){
            var origin=ScreenPoint(dragNode.x,dragNode.y);
            dragTarget=WorldPoint(new PointF(origin.X+point.X-mouseStart.X,origin.Y+point.Y-mouseStart.Y));
        }
    }
    void AppendStroke(Point position,RouteNode endpoint=null){
        var point=endpoint==null?WorldPoint(position):new Point(endpoint.x,endpoint.y);
        var last=stroke.LastOrDefault();
        if(last==null||last.x!=point.X||last.y!=point.Y)stroke.Add(new RoutePoint(point.X,point.Y));
    }
    Point ActionPoint(Point screen,bool destination,out RouteNode marker,out RouteEdge support){
        var raw=WorldPoint(screen);
        marker=HitNode(screen);
        if(marker!=null&&marker.marker==(destination?"grip":"launch")){
            var marked=new Point(marker.x,marker.y);
            support=destination?RouteGraph.MapActionDestination(Document,Brush,marked,marker):
                RouteGraph.MapSupport(Document,marked,"walk",marker);
            if(support!=null)return marked;
        }
        marker=null;
        support=destination?RouteGraph.MapActionDestination(Document,Brush,raw,null):
            RouteGraph.MapSupport(Document,raw,"walk",null);
        return raw;
    }
    List<RoutePoint> FinishMapAction(Point position,out RouteNode from,out RouteNode to){
        to=null;
        bool click=Distance(position.X,position.Y,mouseStart.X,mouseStart.Y)<4;
        RouteEdge platform;
        Point first;
        if(click&&pendingActionStart.HasValue){
            first=pendingActionStart.Value;from=pendingActionNode;
            platform=RouteGraph.MapSupport(Document,first,"walk",from);
        }else first=ActionPoint(mouseStart,false,out from,out platform);
        if(platform==null){
            if(EditHint!=null)EditHint("请先在已画的平台线上点选起跳点");
            return null;
        }
        if(click&&!pendingActionStart.HasValue){
            pendingActionStart=first;pendingActionNode=from;
            if(EditHint!=null)EditHint(Brush=="rope_mount"?"再点梯绳上的抓点":Brush=="jump"?
                "再点目标平台或梯绳上的抓点":"再点目标平台的落脚位置");
            return null;
        }
        RouteEdge landing;
        var last=ActionPoint(position,true,out to,out landing);
        if(landing==null){
            if(EditHint!=null)EditHint(Brush=="rope_mount"?"请点已画的梯绳线":Brush=="jump"?
                "请点已画的平台线或梯绳线":"请点已画的目标平台线");
            return null;
        }
        if(landing.kind=="climb"){
            var start=RouteGraph.SnapToPath(Document,first,platform);
            var grip=RouteGraph.SnapToPath(Document,last,landing);
            if(grip.Y>start.Y||grip==start){
                if(EditHint!=null)EditHint("请选同高或更高的抓点，起跳和抓点要分开");
                return null;
            }
        }
        pendingActionStart=null;pendingActionNode=null;
        return new List<RoutePoint>{new RoutePoint(first.X,first.Y),new RoutePoint(last.X,last.Y)};
    }
    List<RoutePoint> FinishStroke(Point position,out RouteNode from,out RouteNode to){
        if(RouteGraph.IsMapAction(Brush)&&(Document.manualMap||Brush=="rope_mount"||Brush=="jump_down"))
            return FinishMapAction(position,out from,out to);
        from=strokeStart;to=HitNode(position);
        if(RouteGraph.IsMapAction(Brush)){
            if(Distance(position.X,position.Y,mouseStart.X,mouseStart.Y)<4){
                var clicked=to==null?WorldPoint(position):new Point(to.x,to.y);
                if(!pendingActionStart.HasValue){
                    pendingActionStart=clicked;pendingActionNode=to;
                    if(EditHint!=null)EditHint("再点目标平台的落脚位置");
                    return null;
                }
                Point start=pendingActionStart.Value;from=pendingActionNode;
                pendingActionStart=null;pendingActionNode=null;
                return new List<RoutePoint>{new RoutePoint(start.X,start.Y),new RoutePoint(clicked.X,clicked.Y)};
            }
            pendingActionStart=null;pendingActionNode=null;
        }
        if(from!=null&&from==to&&Distance(position.X,position.Y,mouseStart.X,mouseStart.Y)<4){
            if(connectionStart==from){connectionStart=null;return null;}
            if(connectionStart==null||!Document.nodes.Contains(connectionStart)){
                connectionStart=from;return null;
            }
            from=connectionStart;connectionStart=null;
            return new List<RoutePoint>{new RoutePoint(from.x,from.y),new RoutePoint(to.x,to.y)};
        }
        connectionStart=null;
        AppendStroke(position,to);
        return stroke.Count<2?null:new List<RoutePoint>(stroke);
    }
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);if(Document==null)return;
        if(MapPointPicked!=null)return;
        Focus();
        lock(Gate){
            if(gestureButton!=MouseButtons.None)return;
            LayoutMap();mouseStart=e.Location;
            if(e.Button==MouseButtons.Middle){panning=true;panStart=pan;gestureButton=e.Button;Capture=true;return;}
            if(e.Button!=MouseButtons.Left)return;
            if(Brush=="select"){
                dragNode=AllowEdit?HitNode(e.Location):null;
                if(dragNode!=null){
                    var links=Document.edges.Where(link=>link.from==dragNode.id||link.to==dragNode.id).ToArray();
                    bool ropeGrip=Document.edges.Any(link=>link.ropeEntries!=null&&link.ropeEntries.Contains(dragNode.id));
                    if(links.Length>1||ropeGrip){
                        dragEdge=links.FirstOrDefault(link=>link.id==SelectedEdge);
                        if(dragEdge==null&&links.Length==1)dragEdge=links[0];
                        chooseSegmentFirst=dragEdge==null;
                    }
                }
                selecting=true;panStart=pan;gestureButton=e.Button;Capture=true;
            }else if(AllowEdit){
                if(Brush=="erase")EraseAt(e.Location);
                else if(Map!=null){
                    strokeStart=HitNode(e.Location);
                    stroke.Clear();AppendStroke(e.Location,strokeStart);drawing=true;gestureButton=e.Button;Capture=true;
                }
            }
            Invalidate();
        }
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        lock(Gate){
            if(Document==null)return;
            if(dragNode!=null&&Brush=="select"){
                UpdateDragTarget(e.Location);Invalidate();return;
            }
            if(selecting&&Distance(e.X,e.Y,mouseStart.X,mouseStart.Y)>=4){
                selecting=false;
                if(AllowEdit&&HitEdge(mouseStart)==null){boxSelecting=true;boxEnd=e.Location;}
                else panning=true;
            }
            if(boxSelecting){boxEnd=e.Location;Invalidate();return;}
            if(panning){pan=new PointF(panStart.X+e.X-mouseStart.X,panStart.Y+e.Y-mouseStart.Y);Invalidate();}
            else if(drawing){AppendStroke(e.Location);Invalidate();}
            else if(Brush!="erase"&&AllowEdit){
                LayoutMap();
                var node=HitNode(e.Location);
                if(node!=hoverNode){hoverNode=node;Invalidate();}
                Cursor=Brush=="select"?(node==null?Cursors.Default:Cursors.SizeAll):(node==null?Cursors.Cross:Cursors.Hand);
            }
        }
    }
    protected override void OnMouseUp(MouseEventArgs e){
        base.OnMouseUp(e);
        if(MapPointPicked!=null){
            Point? selected=null;
            lock(Gate){
                if(e.Button==MouseButtons.Left&&Document!=null&&Map!=null){
                    LayoutMap();
                    var origin=MapOrigin??new Point(Document.mapX,Document.mapY);
                    var bounds=new RectangleF(ox+origin.X*scale,oy+origin.Y*scale,Map.Width*scale,Map.Height*scale);
                    if(AllowEdit||bounds.Contains(e.Location))selected=WorldPoint(e.Location);
                }
            }
            if(selected.HasValue)MapPointPicked(selected.Value);
            return;
        }
        if(e.Button==MouseButtons.Right&&AllowEdit&&Document!=null&&gestureButton==MouseButtons.None){
            ShowEditMenu(e.Location);
            return;
        }
        List<RoutePoint> committed=null;
        RouteNode strokeFrom=null,strokeTo=null;
        RouteNode moved=null;RouteEdge selectedSegment=null;Point? destination=null;PointF anchor=PointF.Empty;float dragScale=1;
        lock(Gate){
            if(e.Button!=gestureButton)return;
            if(drawing&&AllowEdit)committed=FinishStroke(e.Location,out strokeFrom,out strokeTo);
            if(dragNode!=null&&Brush=="select"&&Document!=null&&AllowEdit){
                UpdateDragTarget(e.Location);moved=dragNode;destination=dragTarget;selectedSegment=dragEdge;
                if(destination.HasValue){anchor=ScreenPoint(destination.Value.X,destination.Value.Y);dragScale=scale;}
            }
            if(boxSelecting&&Document!=null){
                SelectBox(RectangleF.FromLTRB(Math.Min(mouseStart.X,e.X),Math.Min(mouseStart.Y,e.Y),
                    Math.Max(mouseStart.X,e.X),Math.Max(mouseStart.Y,e.Y)));
            }
            if(selecting&&Document!=null&&!destination.HasValue){
                selectedNodes.Clear();selectedEdges.Clear();
                LayoutMap();selectedNode=AllowEdit?HitNode(e.Location):null;
                if(selectedNode==null||!Document.edges.Any(edge=>edge.id==SelectedEdge&&
                    (edge.from==selectedNode.id||edge.to==selectedNode.id)))SelectedEdge=HitEdge(e.Location);
                if(EdgeSelected!=null)EdgeSelected(SelectedEdge);
                if(SelectionChanged!=null)SelectionChanged();
            }
            ResetGesture();Capture=false;
        }
        if(committed!=null&&StrokeDrawn!=null)StrokeDrawn(Brush,committed,strokeFrom,strokeTo);
        if(destination.HasValue&&destination.Value!=new Point(moved.x,moved.y)&&NodeMoved!=null){
            var adjusted=NodeMoved(moved,destination.Value,selectedSegment);
            lock(Gate){
                if(adjusted!=null&&Document!=null&&Document.nodes.Contains(adjusted)){
                    selectedNode=adjusted;
                    LayoutMap();zoom=Math.Max(.25f,Math.Min(12f,zoom*dragScale/scale));LayoutMap();
                    var point=ScreenPoint(adjusted.x,adjusted.y);
                    pan=new PointF(pan.X+anchor.X-point.X,pan.Y+anchor.Y-point.Y);
                }
            }
        }
        Invalidate();
    }
    void ResetGesture(){
        drawing=panning=selecting=boxSelecting=chooseSegmentFirst=false;gestureButton=MouseButtons.None;
        stroke.Clear();dragNode=strokeStart=null;dragTarget=null;dragEdge=null;
    }
    protected override void OnMouseCaptureChanged(EventArgs e){
        base.OnMouseCaptureChanged(e);
        if(Capture)return;
        ResetGesture();Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e){
        base.OnMouseLeave(e);
        if(hoverNode!=null){hoverNode=null;Invalidate();}
    }
    protected override void OnMouseWheel(MouseEventArgs e){
        base.OnMouseWheel(e);ZoomAt((float)Math.Pow(1.2,e.Delta/120.0),e.Location);
    }
    protected override void Dispose(bool disposing){if(disposing&&Map!=null)Map.Dispose();base.Dispose(disposing);}
}
sealed class RouteGraphSnapshot {public List<RouteNode> nodes;public List<RouteEdge> edges;public bool edited,manualMap;}
public sealed class RouteStudio:Form {
    public Action<RouteDocument,Action<RouteDocument>> CalibrateInMain;public Action EmergencyStop,BeforeMutation,RecordingStopped,RecordingReady,SurfaceChanged,RouteTrialRequested;public Action<RouteDocument> BeginKeyboardRecording,ValidateRecordingSource;bool embedded,routeMapRequested=true,trialActive;
    public bool NeedsGamePreview{get{return !routeMapRequested||IsRecording||ManualCaptureActive;}}
    public bool ManualCaptureActive{get{return marking||actionStart.HasValue;}}
    public Action<string> NavigationSelectionHint;
    public event Action TargetChanged;
    public Point? NavigationTarget{get{return homeMap.TargetPoint;}}
    public Point? TrialTarget{get{return canvas.TargetPoint;}}
    internal RouteCanvas HomeMap{get{return homeMap;}}
    public void SetHomeMapVisible(bool visible){lock(gate)homeMapVisible=visible;}
    public void CancelPointCapture(){
        if(!ManualCaptureActive)return;
        marking=false;actionStart=null;
        RefreshCaptureButtons();
        if(SurfaceChanged!=null)SurfaceChanged();
    }
    public bool CaptureShortcut(Keys key){
        if(marking){
            string marker=null;
            switch(key){
                case Keys.F6:marker="platform";break;
                case Keys.F7:marker="rope";break;
                case Keys.F9:marker="drop";break;
                case Keys.F10:marker="launch";break;
                case Keys.F11:marker="grip";break;
            }
            if(marker!=null){AddMapMark(marker);return true;}
        }
        if(actionStart.HasValue&&key==Keys.F10){ToggleActionCapture();return true;}
        return false;
    }
    public void ShowGamePreview(){routeMapRequested=false;if(SurfaceChanged!=null)SurfaceChanged();}
    public void ShowRouteMap(){CancelPointCapture();routeMapRequested=true;if(SurfaceChanged!=null)SurfaceChanged();}
    public void SurfaceState(bool showingMap){lock(gate)mapVisible=showingMap;viewSwitch.Text=showingMap?"游戏预览":"路线图";}
    public void AttachCanvas(Control host){
        canvas.Parent.Controls.Remove(canvas);
        host.Controls.Add(canvas);canvas.SendToBack();
    }
    public void FitRoute(){canvas.Fit();}
    public void ZoomRoute(float factor){canvas.ZoomBy(factor);}
    public int RouteZoomPercent{get{return canvas.ZoomPercent;}}
    public void CancelSelection(){canvas.ClearSelection();UpdateDetail();UpdateTrialButtons();SetStatus("已取消选择");}
    public event Action<int> RouteZoomChanged{add{canvas.ZoomChanged+=value;}remove{canvas.ZoomChanged-=value;}}
    public event Action RouteSelectionChanged{add{canvas.SelectionChanged+=value;}remove{canvas.SelectionChanged-=value;}}
    public bool HasRouteSelection{get{return canvas.HasSelection;}}
    readonly CheckBox navigationCombat=new CheckBox{Text="途中打怪",Name="NavigationCombat",AutoSize=true,Margin=new Padding(12,8,3,3)};
    public bool NavigationCombatEnabled{get{return navigationCombat.Checked;}}
    public bool SelectBrush(string brush){
        if(ManualCaptureActive)return false;
        if(brush!="select"&&(!HasRoute||IsRecording||engine.Document.roiWidth<24)){
            SetStatus("请先选择并标定地图，再新建或打开路线");return false;
        }
        canvas.MapPointPicked=null;
        canvas.SetBrush(brush);
        UpdateTrialButtons();
        if(RouteGraph.IsMapAction(brush))
            SetStatus(brush=="rope_mount"?"点平台起跳位置，再点梯绳抓点":brush=="jump"?
                "点平台起跳位置，再点目标平台或梯绳":"点平台起跳位置，再点目标平台落点");
        return true;
    }
    public void SetTrialActive(bool active){
        lock(gate)trialActive=active;
        UpdateTrialButtons();
        canvas.AllowEdit=!active&&!IsRecording&&HasRoute;
        navigationCombat.Enabled=!active;
    }
    public void SetLivePosition(Point? position){
        if(!trialActive)return;
        lock(gate){livePosition=position;livePositionAt=clock.ElapsedMilliseconds;liveEngine=engine;liveWindow=0;}
        canvas.SetLivePosition(position);
        homeMap.SetLivePosition(position);
    }
    public bool IsRecording{get{lock(gate)return engine.Recording;}}
    public bool HasRuntimeRoute{get{lock(gate)return filename!=null&&!dirty&&engine.Document.edges.Count>0;}}
    public void WatchReady(){lock(gate){if(engine.Recording)waitingForWatch=false;}}
    public RouteDocument RuntimeRoute(bool includeSuggestedJumps=false){
        lock(gate){
            var d=engine.Document;
            var included=d.edges.Where(e=>includeSuggestedJumps||!e.suggestedJump).ToArray();
            var connected=new HashSet<string>(included.SelectMany(e=>new[]{e.from,e.to}.Concat(e.ropeEntries??new List<string>())));
            var route=new RouteDocument{
                id=d.id,name=d.name,mapId=d.mapId,window=d.window,sourceHost=d.sourceHost,manualMap=d.manualMap,sourceWidth=d.sourceWidth,sourceHeight=d.sourceHeight,roiX=d.roiX,roiY=d.roiY,
                roiWidth=d.roiWidth,roiHeight=d.roiHeight,jumpKey=d.jumpKey,
                nodes=d.nodes.Where(n=>connected.Contains(n.id)).Select(n=>new RouteNode{id=n.id,label=n.label,marker=n.marker,x=n.x,y=n.y}).ToList(),
                edges=included.Select(e=>new RouteEdge{
                    id=e.id,from=e.from,to=e.to,kind=e.kind,both=e.both,edited=e.edited,review=e.review,
                    suggestedJump=e.suggestedJump,duration=e.duration,
                    downLead=e.downLead,jumpHold=e.jumpHold,downTail=e.downTail,upLead=e.upLead,jumpDirection=e.jumpDirection,
                    steerHold=e.steerHold,downApproach=e.downApproach,upApproach=e.upApproach,upApproachKey=e.upApproachKey,upContinuation=e.upContinuation,
                    observedJump=e.observedJump,recoveredWalk=e.recoveredWalk,
                    upApproachOverlap=e.upApproachOverlap,ropeEntries=e.ropeEntries==null?null:e.ropeEntries.ToList(),
                    points=e.points.Select(p=>new RoutePoint(p.x,p.y)).ToList()
                }).ToList()
            };
            if(d.manualMap){
                RouteGraph.ConnectNearbyGrabLaunches(route);
                RouteGraph.AddStepDescents(route);
                RouteGraph.AddRopeDescents(route);
                return route;
            }
            RouteGraph.GeneratePaintedActions(route);
            RouteGraph.MeasureUpApproaches(route,d.samples,d.events);
            RouteGraph.MergeRopeShafts(route);
            RouteGraph.GeneratePlatformRopeEntries(route);
            return route;
        }
    }
    public RouteDocument VisionCalibration(){
        lock(gate){
            var document=engine.Document;
            if(document.roiWidth>0)engine.FlushMap();else document=selectedMap;
            return document==null||document.roiWidth<=0?null:PlayerMinimap.FromRoute(document).Route();
        }
    }
    public void EmbedInto(Control parent){
        TopLevel=false;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;MinimumSize=Size.Empty;
        Dock=DockStyle.Fill;embedded=true;recordKeyboard.Visible=viewSwitch.Visible=true;
        parent.Controls.Add(this);Show();
    }
    public void PlaceSelectors(FlowLayoutPanel toolbar){
        toolbar.Controls.AddRange(new Control[]{mapCaption,mapChoice,calibrate,routeCaption,routeChoice});
        calibrate.Margin=new Padding(7,1,3,3);
        mapChoice.Width=184;
        routeChoice.Width=224;
        mapChoice.Margin=routeChoice.Margin=new Padding(3,2,3,3);
    }
    public void PlaceTrialControls(FlowLayoutPanel toolbar){
        navigationPoint.Margin=new Padding(16,3,3,3);
        trial.Margin=new Padding(3);
        toolbar.Controls.AddRange(new Control[]{navigationPoint,trial,navigationCombat});
    }
    public void ShowTrialControls(bool visible){
        navigationPoint.Visible=trial.Visible=visible;
        navigationCombat.Visible=true;
    }
    readonly string folder,mapsFolder,legacyDraft,selectionPath;readonly Func<Dictionary<string,object>> capture;readonly Func<MinimapLayout,byte[],Dictionary<string,object>> region;readonly Action<bool,RouteDocument> observe;
    readonly Func<int> roleJumpKey;
    readonly Label roleJumpLabel=new Label{Name="RoleJumpBinding",AutoSize=true,Anchor=AnchorStyles.Left};
    readonly Label mapCaption=new Label{Text="地图",AutoSize=true,Margin=new Padding(3,8,6,0)};
    readonly Label routeCaption=new Label{Text="路线",AutoSize=true,Margin=new Padding(12,8,6,0)};
    readonly object gate=new object();RouteEngine engine;readonly RouteCanvas canvas;readonly TextBox nameBox=new TextBox();readonly WorkspaceComboBox mapChoice=new WorkspaceComboBox{Placeholder="选择地图",EmptyChoiceIndex=0},routeChoice=new WorkspaceComboBox{Placeholder="选择路线"},jump=new WorkspaceComboBox();readonly CheckBox recordKeyboard=new CheckBox();readonly Label status=new Label();readonly Label detail=new Label();Label jumpCaption;readonly Button start=new AppleButton(),pause=new AppleButton();Button calibrate,newRoute,saveRoute,viewSwitch,trial,fitWalkLine,organize,suggestJumps,confirmJump;readonly ToolStripMenuItem recover,import,legacyRecord;readonly List<string> undo=new List<string>(),redo=new List<string>();readonly Stopwatch clock=Stopwatch.StartNew();long timeBase;System.Threading.Timer worker;readonly System.Windows.Forms.Timer ui=new System.Windows.Forms.Timer();int working;bool closing,loading,dirty,draftDirty,uiRecording,waitingForWatch,readyPublished;string filename;RouteDocument selectedMap;DateTime saved=DateTime.MinValue;int tick;
    bool mapVisible,homeMapVisible,marking;
    Button navigationPoint;
    RouteCanvas homeMap;
    RouteDocument homeDocument;
    byte[] liveMiniPixels,shownMiniPixels;
    int liveMiniWidth,liveMiniHeight;
    Point liveMiniOrigin;
    Button markMode,markPlatform,markRope,markDrop,markLaunch,markGrip,recordAction;
    ComboBox actionType;
    Point? actionStart;
    string actionKind;
    readonly string[] actionKinds={"jump","jump_down","drop","rope_mount"};
    Point? livePosition;
    RouteEngine liveEngine,previewEngine;
    string previewPositionIssue;
    const int PreviewPollIntervalMs=110;
    long livePositionAt,liveWindow,previewAt=-PreviewPollIntervalMs,previewSequence=-1;
    MinimapLayout previewLayout;
    RouteDocument previewCalibration;
    readonly MiniTracker previewTracker=new MiniTracker();
    sealed class LibraryItem {
        public string Path,Name;
        public override string ToString(){return Name;}
    }
    sealed class KeyOption {public int Vk;public string Name;public override string ToString(){return Name;}}
    public RouteStudio(string root,Func<Dictionary<string,object>> snapshot,Func<MinimapLayout,byte[],Dictionary<string,object>> crop,Action<bool,RouteDocument> watch,Func<int> roleJump=null){
        roleJumpKey=roleJump;
        folder=Path.Combine(root,"routes");Directory.CreateDirectory(folder);legacyDraft=Path.Combine(folder,"draft.route.json");mapsFolder=Path.Combine(folder,"maps");selectionPath=Path.Combine(folder,"selection.json");capture=snapshot;region=crop;observe=watch;engine=new RouteEngine(new RouteDocument());canvas=new RouteCanvas(gate){Dock=DockStyle.Fill};Text="路线工坊 · 录制与节点编辑";Size=new Size(1320,880);MinimumSize=new Size(940,650);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);KeyPreview=true;
        homeMap=new RouteCanvas(gate){Name="HomeMinimap",AccessibleName="小地图目标选择",AllowEdit=false,
            FitWholeMap=true,Dock=DockStyle.Fill,TabStop=false,Cursor=Cursors.Cross};
        homeMap.MapPointPicked=PickNavigationTarget;
        Disposed+=delegate{if(!homeMap.IsDisposed)homeMap.Dispose();};
        BackColor=AppUi.Surface;ForeColor=AppUi.Text;
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(0,252),
            Padding=new Padding(9,6,6,7),BackColor=AppUi.Surface};
        mapChoice.Name="RouteMapSelector";routeChoice.Name="RouteSelector";
        mapChoice.AccessibleName="地图";routeChoice.AccessibleName="路线";
        mapChoice.Width=routeChoice.Width=100;mapChoice.DropDownWidth=220;routeChoice.DropDownWidth=220;
        mapChoice.DropDownStyle=routeChoice.DropDownStyle=ComboBoxStyle.DropDownList;
        mapChoice.SelectedIndexChanged+=delegate{if(!loading)SelectMap();};
        routeChoice.SelectedIndexChanged+=delegate{if(!loading)SelectRoute();};
        bar.Controls.Add(mapCaption);bar.Controls.Add(mapChoice);
        var newMap=ButtonOf(bar,"新建地图",78,NewMap);calibrate=ButtonOf(bar,"标定地图",110,Calibrate,primary:true);
        calibrate.Name="CalibrateRouteMap";bar.SetFlowBreak(calibrate,true);
        bar.Controls.Add(routeCaption);bar.Controls.Add(routeChoice);
        newRoute=ButtonOf(bar,"新建路线",78,New);saveRoute=ButtonOf(bar,"保存路线",90,Save);
        var routeSettings=new TableLayoutPanel{Width=324,Height=30,RowCount=1,ColumnCount=4,Margin=new Padding(3,3,3,5)};
        routeSettings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        routeSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        routeSettings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        routeSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));
        nameBox.Text="新路线";nameBox.Width=155;nameBox.MaxLength=128;nameBox.AccessibleName="路线名称";
        nameBox.Anchor=AnchorStyles.Left|AnchorStyles.Right;nameBox.MinimumSize=new Size(150,0);
        nameBox.Margin=new Padding(3,2,6,2);
        nameBox.TextChanged+=delegate{if(!loading&&HasRoute){lock(gate)engine.Document.name=nameBox.Text;MarkDirty();}};
        routeSettings.Controls.Add(new Label{Text="名称",AutoSize=true,Anchor=AnchorStyles.Left},0,0);
        routeSettings.Controls.Add(nameBox,1,0);
        jumpCaption=new Label{Text="跳跃键",AutoSize=true,Anchor=AnchorStyles.Left};
        routeSettings.Controls.Add(jumpCaption,2,0);
        FillKeys(jump,18);jump.Anchor=AnchorStyles.Left|AnchorStyles.Right;
        if(roleJumpKey==null){
            routeSettings.Controls.Add(jump,3,0);
            jump.SelectedIndexChanged+=delegate{if(!loading&&HasRoute){lock(gate)engine.Document.SetJumpKey(((KeyOption)jump.SelectedItem).Vk);MarkDirty();}};
        }else{
            jump.Visible=false;
            routeSettings.Disposed+=delegate{jump.Dispose();};
            roleJumpLabel.AccessibleName="当前角色跳跃键";
            routeSettings.Controls.Add(roleJumpLabel,3,0);
            RefreshRoleJump();
        }
        bar.Controls.Add(routeSettings);bar.SetFlowBreak(routeSettings,true);
        navigationPoint=ButtonOf(bar,"导航点",96,ToggleTrialTarget);
        navigationPoint.Name="RouteTrialTargetButton";navigationPoint.Visible=false;
        trial=ButtonOf(bar,"试走路线",106,delegate{if(RouteTrialRequested!=null)RouteTrialRequested();});
        trial.Name="RouteTrialButton";trial.Visible=false;AppUi.Button(trial,true);
        viewSwitch=ButtonOf(bar,"游戏预览",88,delegate{if(NeedsGamePreview)ShowRouteMap();else ShowGamePreview();});
        viewSwitch.Visible=false;
        fitWalkLine=ButtonOf(bar,"拟合直线",88,FitSelectedWalkLine);
        fitWalkLine.Enabled=false;
        organize=ButtonOf(bar,"整理路线",94,OrganizeRoute);organize.Name="OrganizeRecordedRoute";
        suggestJumps=ButtonOf(bar,"智能补起跳",102,SuggestJumps);
        suggestJumps.Name="SuggestRouteJumps";
        confirmJump=ButtonOf(bar,"确认选中跳跃",118,ConfirmSuggestedJump);
        confirmJump.Name="ConfirmSuggestedJump";
        confirmJump.Visible=false;
        bar.SetFlowBreak(confirmJump,true);
        recordKeyboard.Text="本机键盘";recordKeyboard.Checked=true;recordKeyboard.AutoSize=true;recordKeyboard.Visible=false;
        recordKeyboard.Margin=new Padding(3,8,0,0);
        start.Text="开始录制";start.Width=86;start.Height=32;start.Click+=delegate{StartRecording();};
        pause.Text="暂停录制";pause.Width=86;pause.Height=32;pause.Click+=delegate{PauseRecording("已暂停");};
        AppUi.Button(start);AppUi.Button(pause,true);pause.Visible=false;bar.Controls.AddRange(new Control[]{start,pause,recordKeyboard});
        bar.SetFlowBreak(recordKeyboard,true);
        var undoButton=ButtonOf(bar,"撤销",52,Undo);var redoButton=ButtonOf(bar,"重做",52,Redo);
        bar.SetFlowBreak(redoButton,true);
        var tools=new ContextMenuStrip{Font=Font,ShowImageMargin=false};
        recover=new ToolStripMenuItem("恢复草稿",null,delegate{Recover();});
        import=new ToolStripMenuItem("导入旧路线",null,delegate{Open();});
        legacyRecord=new ToolStripMenuItem("旧式轨迹录制",null,delegate{if(!ManualCaptureActive)StartRecording();});
        tools.Items.AddRange(new ToolStripItem[]{recover,import,legacyRecord});
        markMode=ButtonOf(bar,"启动地图录制",116,ToggleMapMarking);
        markPlatform=ButtonOf(bar,"平台端点 F6",102,()=>AddMapMark("platform"),keepKeyboard:true);
        markRope=ButtonOf(bar,"梯绳端点 F7",102,()=>AddMapMark("rope"),keepKeyboard:true);
        markDrop=ButtonOf(bar,"下落点 F9",88,()=>AddMapMark("drop"),keepKeyboard:true);
        markLaunch=ButtonOf(bar,"抓绳起跳点 F10",136,()=>AddMapMark("launch"),keepKeyboard:true);
        markGrip=ButtonOf(bar,"梯绳抓点 F11",126,()=>AddMapMark("grip"),keepKeyboard:true);
        actionType=new WorkspaceComboBox{Width=100,AccessibleName="录制动作类型",EmptyChoiceIndex=-1};
        actionType.Items.AddRange(new object[]{"起跳","下跳","下落","跳跃抓绳"});actionType.SelectedIndex=0;
        bar.Controls.Add(actionType);
        recordAction=ButtonOf(bar,"记录动作起点",114,ToggleActionCapture,keepKeyboard:true);
        recordAction.Name="RecordMapAction";
        bar.SetFlowBreak(recordAction,true);
        foreach(var button in new[]{markPlatform,markRope,markDrop,markLaunch,markGrip})button.Visible=false;
        Button more=null;
        more=ButtonOf(bar,"更多 ▾",70,()=>tools.Show(more,new Point(0,more.Height)));
        more.ContextMenuStrip=tools;more.Disposed+=delegate{tools.Dispose();};
        var toolsOrder=new Control[]{markMode,start,pause,saveRoute,markPlatform,markRope,markDrop,markLaunch,markGrip,
            actionType,recordAction,suggestJumps,confirmJump,organize,undoButton,redoButton,routeSettings,mapCaption,mapChoice,newMap,
            calibrate,routeCaption,routeChoice,newRoute,trial,viewSwitch,fitWalkLine,more,recordKeyboard};
        foreach(var item in toolsOrder.Reverse())bar.Controls.SetChildIndex(item,0);
        bar.SetFlowBreak(saveRoute,true);
        bar.SetFlowBreak(markDrop,true);
        bar.SetFlowBreak(markGrip,true);
        AppUi.Button(saveRoute,true);
        bar.SizeChanged+=delegate{
            int width=Math.Max(100,Math.Min(148,bar.ClientSize.Width-226));
            if(mapChoice.Parent==bar)mapChoice.Width=routeChoice.Width=width;
            routeSettings.Width=Math.Max(260,bar.ClientSize.Width-bar.Padding.Horizontal-routeSettings.Margin.Horizontal);
        };
        AppUi.ApplyInputs(bar);
        status.Dock=DockStyle.Bottom;status.Height=48;status.BackColor=AppUi.Surface;status.ForeColor=AppUi.Muted;status.TextAlign=ContentAlignment.MiddleLeft;status.AutoEllipsis=true;
        var body=new Panel{Dock=DockStyle.Fill,BackColor=AppUi.Background};
        detail.Dock=DockStyle.Top;detail.Height=58;detail.ForeColor=AppUi.Text;detail.Padding=new Padding(14,7,8,0);
        detail.Text="";detail.Visible=false;
        body.Controls.Add(canvas);body.Controls.Add(detail);
        Controls.Add(body);Controls.Add(status);Controls.Add(bar);
        canvas.EdgeSelected+=delegate{UpdateDetail();};
        canvas.StrokeDrawn+=PaintRoute;canvas.EraseEdge+=EraseRouteStroke;canvas.NodeMoved+=MoveRouteNode;
        canvas.EraseNode+=node=>DeleteRouteNode(node,false);canvas.DeleteNode+=node=>DeleteRouteNode(node,true);canvas.EditHint+=SetStatus;
        canvas.SelectionChanged+=delegate{
            if(canvas.SelectedNodeIds.Length+canvas.SelectedEdgeIds.Length>1)
                SetStatus("已框选 "+canvas.SelectedNodeIds.Length+" 个点、"+canvas.SelectedEdgeIds.Length+" 段线 · Delete删除 · Esc取消");
        };
        canvas.DeleteSelectionRequested+=DeleteRouteSelection;
        canvas.SplitWalk+=SplitWalkAt;canvas.ConvertWalkToJump+=ChangeWalkToJump;
        ui.Interval=200;ui.Tick+=RefreshLive;ui.Start();worker=new System.Threading.Timer(Poll,null,50,50);FormClosing+=delegate{closing=true;PauseRecording("工坊已关闭，草稿保留");SaveDraft();ui.Stop();worker.Dispose();};Shown+=delegate{RefreshGraph();};
        RestoreSelection();
    }
    static Button ButtonOf(Control parent,string text,int width,Action action,bool keepKeyboard=false,bool primary=false){
        var button=keepKeyboard?new MapMarkerButton():new AppleButton();
        button.Text=text;button.Width=width;button.Height=32;
        AppUi.Button(button,primary);button.Click+=delegate{action();};
        parent.Controls.Add(button);
        return button;
    }
    static void FillKeys(ComboBox box,int selected){box.DropDownStyle=ComboBoxStyle.DropDownList;box.Width=100;var list=new List<KeyOption>{new KeyOption{Vk=18,Name="Alt"},new KeyOption{Vk=32,Name="Space"},new KeyOption{Vk=17,Name="Ctrl"},new KeyOption{Vk=16,Name="Shift"}};for(int i=65;i<=90;i++)list.Add(new KeyOption{Vk=i,Name=((char)i).ToString()});for(int i=48;i<=57;i++)list.Add(new KeyOption{Vk=i,Name=((char)i).ToString()});foreach(var k in list)box.Items.Add(k);box.SelectedItem=list.First(k=>k.Vk==selected);}
    void RefreshRoleJump(){
        if(roleJumpKey==null)return;
        int key=engine.Recording?engine.Document.jumpKey:roleJumpKey();
        var option=jump.Items.Cast<KeyOption>().FirstOrDefault(k=>k.Vk==key);
        string display=option!=null?option.Name:key>0?((Keys)key).ToString():"未设置";
        if(roleJumpLabel.Text!=display)roleJumpLabel.Text=display;
    }
    bool HasRoute{get{return !String.IsNullOrEmpty(engine.Document.mapId)||!embedded&&engine.Document.roiWidth>=24;}}
    void MarkDirty(){dirty=draftDirty=true;}
    void SetStatus(string text){
        string routeState="未选路线";
        if(HasRoute)routeState=dirty||filename==null?
            (dirty&&!draftDirty?"草稿已存，路线未保存":"路线未保存"):"路线已保存";
        string mapState=selectedMap==null?"未选地图":selectedMap.roiWidth>=24?"地图标定已保存":"地图待标定";
        status.Text="  "+text+"\n  "+mapState+" · "+routeState;engine.Status=text;
    }
    bool Editable(){if(ManualCaptureActive){SetStatus("请先结束标点或动作记录");return false;}if(engine.Recording){SetStatus("请先暂停录制");return false;}if(BeforeMutation!=null)BeforeMutation();return true;}
    public void PauseRecording(string reason){
        CancelPointCapture();
        bool stopped;
        lock(gate){
            stopped=engine.Recording||uiRecording;
            engine.Pause(reason);uiRecording=waitingForWatch=readyPublished=false;observe(false,engine.Document);
            if(stopped){if(!engine.Document.graphEdited)RouteGraph.Build(engine.Document);MarkDirty();}
        }
        if(stopped){if(RecordingStopped!=null)RecordingStopped();ShowRouteMap();reason=GeneratedStatus(reason);}
        if(!IsDisposed&&!closing){canvas.AllowEdit=true;RefreshGraph();SetStatus(reason);}
    }
    string GeneratedStatus(string reason){
        int edges=engine.Document.edges.Count;
        return reason+(edges==0?" · 未生成可用路线":" · 已生成 "+edges+" 段路线");
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keys){if((keys&Keys.KeyCode)==Keys.F12){if(embedded&&EmergencyStop!=null)EmergencyStop();else PauseRecording("F12已暂停录制");return true;}if(keys==(Keys.Control|Keys.Z)&&ActiveControl!=nameBox){Undo();return true;}if(keys==(Keys.Control|Keys.Y)&&ActiveControl!=nameBox){Redo();return true;}return base.ProcessCmdKey(ref msg,keys);}
    Point? FreshMapPosition(){
        if(liveEngine!=engine||engine.Recording||!livePosition.HasValue||
            clock.ElapsedMilliseconds-livePositionAt>600)return null;
        return livePosition;
    }

    Point? CaptureMapPosition(out string issue){
        var calibration=PlayerMinimap.FromRoute(engine.Document).Route();
        try{if(ValidateRecordingSource!=null)ValidateRecordingSource(calibration);}
        catch(IOException e){issue=e.Message;return null;}
        if((calibration.sourceHost??"")!=(engine.Document.sourceHost??"")){
            issue="请为当前被控端选择对应的地图";return null;
        }
        var position=FreshMapPosition();
        if(!position.HasValue||calibration.window!=liveWindow){
            issue=previewPositionIssue??"正在等待新画面里的人物黄点";return null;
        }
        issue=null;
        return position;
    }

    void RefreshCaptureButtons(){
        bool ready=HasRoute&&engine.Document.roiWidth>=24&&!engine.Recording&&!trialActive;
        markMode.Text=marking?"结束地图录制":"启动地图录制";
        markMode.Enabled=ready&&!actionStart.HasValue;
        markMode.Visible=!engine.Recording;
        markPlatform.Visible=markRope.Visible=markDrop.Visible=markLaunch.Visible=markGrip.Visible=marking;
        markPlatform.Enabled=markRope.Enabled=markDrop.Enabled=markLaunch.Enabled=markGrip.Enabled=ready;
        actionType.Visible=recordAction.Visible=!marking&&!engine.Recording;
        recordAction.Text=actionStart.HasValue?"记录动作终点":"记录动作起点";
        recordAction.Enabled=ready;
        actionType.Enabled=ready&&!actionStart.HasValue;
        legacyRecord.Enabled=ready&&!engine.Document.manualMap&&!ManualCaptureActive;
    }

    void ToggleMapMarking(){
        if(marking){if(BeforeMutation!=null)BeforeMutation();ShowRouteMap();SetStatus("请选择画线工具，连接刚才标记的端点");return;}
        if(!HasRoute||engine.Document.roiWidth<24||trialActive||!Editable())return;
        if(!engine.Document.manualMap&&(engine.Document.edges.Count!=0||engine.Document.samples.Count!=0)){
            var previous=engine;
            New();
            if(engine==previous)return;
        }
        marking=true;ShowGamePreview();
        RefreshCaptureButtons();
        SetStatus("手动移动角色，停在要标记的位置后点击对应按钮");
    }

    void AddMapMark(string marker){
        if(!marking||trialActive)return;
        lock(gate){
            string issue;
            var point=CaptureMapPosition(out issue);
            if(!point.HasValue){SetStatus(issue);return;}
            string previous=GraphState();
            var node=RouteGraph.MarkMapPoint(engine.Document,point.Value,marker);
            if(node==null){SetStatus("此位置已标记");return;}
            if(liveMiniPixels!=null)
                engine.StoreMarkedMapFrame(liveMiniPixels,liveMiniOrigin,point.Value);
            RememberGraph(previous);MarkDirty();
            string label=marker=="rope"?"梯绳端点":marker=="drop"?"下落点":
                marker=="launch"?"抓绳起跳点":marker=="grip"?"梯绳抓点":"平台端点";
            SetStatus("已标记"+label+" ("+node.x+", "+node.y+")");
        }
        RefreshGraph();
    }

    void ToggleActionCapture(){
        if(!HasRoute||trialActive||engine.Recording)return;
        lock(gate){
            string issue;
            var point=CaptureMapPosition(out issue);
            if(!point.HasValue){SetStatus(issue);return;}
            if(!actionStart.HasValue){
                if(marking)marking=false;
                actionKind=actionKinds[Math.Max(0,actionType.SelectedIndex)];
                actionStart=point;
                ShowGamePreview();
                SetStatus("起点已记录，手动走完动作后按 F10 或点击记录动作终点");
            }else{
                var document=engine.Document;
                string previous=GraphState();
                var link=RouteGraph.DrawMapLink(document,actionKind,actionStart.Value,point.Value,null,null);
                if(link==null){
                    RestoreGraph(previous,false);
                    SetStatus("起点须在已画平台，终点须在对应平台或梯绳");
                    return;
                }
                RememberGraph(previous);MarkDirty();
                canvas.SelectedEdge=link.id;actionStart=null;
                if(BeforeMutation!=null)BeforeMutation();
                ShowRouteMap();
                SetStatus("已记录"+RouteGraph.KindName(link.kind)+"的起点和终点");
            }
        }
        RefreshCaptureButtons();RefreshGraph();
    }

    void PickNavigationTarget(Point position){
        lock(gate){
            if(trialActive||engine.Recording||ManualCaptureActive)return;
            var target=RouteGraph.SnapNavigationTarget(engine.Document,position);
            if(!target.HasValue){
                SetStatus("请点选已画的平台或梯绳");
                if(NavigationSelectionHint!=null)NavigationSelectionHint("请点选已画的平台或梯绳");
                return;
            }
            homeMap.TargetPoint=target;
            homeMap.Invalidate();
        }
        if(TargetChanged!=null)TargetChanged();
    }
    void ToggleTrialTarget(){
        if(trialActive||IsRecording||ManualCaptureActive)return;
        if(canvas.MapPointPicked!=null||TrialTarget.HasValue){
            canvas.MapPointPicked=null;canvas.TargetPoint=null;
            canvas.Cursor=canvas.Brush=="select"?Cursors.Default:Cursors.Cross;
            SetStatus("已清除导航点，可试走整条路线");
        }else{
            ShowRouteMap();
            canvas.CancelConnection();canvas.MapPointPicked=PickTrialTarget;canvas.Cursor=Cursors.Cross;
            SetStatus("请点击路线图上的平台或梯绳，设置试走目标");
        }
        UpdateTrialButtons();canvas.Invalidate();
    }
    void PickTrialTarget(Point position){
        lock(gate){
            if(trialActive||engine.Recording||ManualCaptureActive)return;
            var target=RouteGraph.SnapNavigationTarget(engine.Document,position);
            if(!target.HasValue){SetStatus("请点选已画的平台或梯绳");return;}
            canvas.TargetPoint=target;
            SetStatus("已设导航点 ("+target.Value.X+", "+target.Value.Y+")，点击试走到目标");
        }
        UpdateTrialButtons();canvas.Invalidate();
    }
    void UpdateTrialButtons(){
        bool hasTarget=TrialTarget.HasValue;
        bool picking=canvas.MapPointPicked!=null;
        trial.Text=trialActive?"停止试走":hasTarget?"试走到目标":"试走路线";
        trial.Enabled=trialActive||!ManualCaptureActive&&!IsRecording&&HasRuntimeRoute;
        navigationPoint.Text=hasTarget?"清除导航点":picking?"取消选点":"导航点";
        navigationPoint.BackColor=hasTarget||picking?AppUi.Selection:AppUi.Field;
        navigationPoint.Enabled=!trialActive&&!ManualCaptureActive&&!IsRecording&&
            (hasTarget||picking||engine.Document.edges.Any(e=>e.kind=="walk"||e.kind=="climb"));
    }

    void StartRecording(){
        if(engine.Document.manualMap){SetStatus("这条路线使用手动标点与连线");return;}
        if(!HasRoute){SetStatus("先选择或新建路线");return;}
        if(BeforeMutation!=null)BeforeMutation();
        try{
            RouteDocument document;
            bool useKeyboard=BeginKeyboardRecording!=null&&recordKeyboard.Checked;
            lock(gate){
                if(engine.Recording)return;
                document=engine.Document;
                if(document.graphEdited)throw new IOException("这条路线的连接已手动编辑，请新建路线再录制");
                if(document.roiWidth<24||document.window==0)throw new IOException("请先标定当前地图");
                if(ValidateRecordingSource!=null)ValidateRecordingSource(document);
                int key=roleJumpKey==null?((KeyOption)jump.SelectedItem).Vk:roleJumpKey();
                if(!RouteDocument.CanRecordJump(key))throw new IOException("请在技能页为跳跃选择字母、数字、Alt、Space、Ctrl 或 Shift");
                document.name=nameBox.Text;document.SetJumpKey(key);document.teleportKey=0;
                engine.Start();uiRecording=true;waitingForWatch=embedded;readyPublished=false;MarkDirty();
                if(!useKeyboard)observe(true,document);
            }
            SetStatus(embedded?"正在准备录制…":"录制中");
            if(SurfaceChanged!=null)SurfaceChanged();
            if(useKeyboard)BeginKeyboardRecording(document);
        }catch(Exception e){
            if(IsRecording)PauseRecording(e.Message);else SetStatus(e.Message);
        }
        RefreshLive(null,EventArgs.Empty);
    }
    void Calibrate(){
        if(!Editable()||selectedMap==null)return;
        try{
            if(CalibrateInMain!=null){CalibrateInMain(selectedMap,ApplyCalibration);return;}
            var f=capture();
            if(Wire.Long(f,"window")==0)throw new IOException("主预览请先选择具体游戏窗口");
            using(var dialog=new RouteCalibration(f,selectedMap))if(dialog.ShowDialog(this)==DialogResult.OK)ApplyCalibration(dialog.Result);
        }catch(Exception e){SetStatus(e.Message);}
    }
    void ApplyCalibration(RouteDocument document){
        if(selectedMap==null)throw new IOException("请先选地图");
        document.id=selectedMap.id;document.name=selectedMap.name;
        RouteFiles.Save(MapFile(document.id),document);selectedMap=document;
        if(HasRoute&&filename==null&&engine.Document.samples.Count==0&&engine.Document.nodes.Count==0){
            var route=RouteFromMap(document);route.id=engine.Document.id;route.name=engine.Document.name;route.jumpKey=engine.Document.jumpKey;
            UseDocument(route,null,true);
        }
        RefreshGraph();SetStatus("小地图标定已保存");
    }
    void Poll(object ignored){
        if(closing||Interlocked.Exchange(ref working,1)!=0)return;
        RouteEngine current=null;byte[] reference;
        try{
            bool recording;
            lock(gate){
                current=engine;recording=current.Recording;
                if(recording&&waitingForWatch)return;
                if(!recording){
                    long now=clock.ElapsedMilliseconds;
                    if(!(mapVisible||homeMapVisible||ManualCaptureActive)||trialActive||
                        current.Document.roiWidth==0&&(selectedMap==null||selectedMap.roiWidth==0)||
                        now-previewAt<PreviewPollIntervalMs)return;
                    previewAt=now;
                }
                reference=current.Tracker.LastFrame;
            }
            if(!recording){PollPosition(current);return;}
            var frame=region(current.Layout,reference);
            lock(gate){
                if(engine!=current||!current.Recording)return;
                current.Process(frame,timeBase+clock.ElapsedMilliseconds);MarkDirty();
                if(!current.Recording)observe(false,current.Document);
            }
        }catch(Exception e){
            lock(gate){
                if(current==engine&&engine.Recording){engine.Pause("录制暂停："+e.Message);observe(false,engine.Document);}
                else if(current==engine)previewPositionIssue=e is IOException?e.Message:"读取小地图画面失败";
            }
        }finally{Interlocked.Exchange(ref working,0);}
    }
    void PollPosition(RouteEngine current){
        RouteDocument calibration;
        lock(gate){
            if(engine!=current||current.Recording)return;
            var source=current.Document.roiWidth>0?current.Document:selectedMap;
            if(source==null||source.roiWidth==0)return;
            calibration=PlayerMinimap.FromRoute(source).Route();
        }
        string sourceHost=calibration.sourceHost??"";
        if(ValidateRecordingSource!=null)ValidateRecordingSource(calibration);
        if((calibration.sourceHost??"")!=sourceHost)throw new IOException("请为当前被控端选择对应的地图");
        if(previewEngine!=current||previewCalibration.window!=calibration.window){
            lock(gate){
                if(engine!=current||current.Recording)return;
                previewCalibration=calibration;
            }
            previewLayout=new MinimapLayout(previewCalibration);
            var map=previewCalibration;
            previewTracker.Seed(MinimapLayout.ReferencePixels(map),map.roiWidth,map.roiHeight,map.lastOffsetX,map.lastOffsetY);
            previewEngine=current;previewSequence=-1;
        }
        var frame=region(previewLayout,previewTracker.LastFrame);
        var sequence=Wire.Long(frame,"sequence");
        if(Wire.Long(frame,"window")!=previewCalibration.window||Convert.ToDouble(frame["age_ms"])>600)return;
        if(MinimapLayout.CursorMayCoverMinimap(frame)){
            lock(gate){if(engine==current){previewPositionIssue="鼠标可能挡住小地图，移开后可继续标点";livePosition=null;}}
            return;
        }
        if(sequence==previewSequence)return;
        previewSequence=sequence;
        var dot=previewTracker.Process((byte[])frame["bgr24"],Wire.Int(frame,"width"),Wire.Int(frame,"height"),previewCalibration);
        lock(gate){
            if(engine!=current||trialActive||current.Recording)return;
            if(!dot.ok){previewPositionIssue=dot.reason.Replace("，录制已暂停","");return;}
            previewPositionIssue=null;
            liveEngine=current;
            livePositionAt=clock.ElapsedMilliseconds-(long)Convert.ToDouble(frame["age_ms"]);
            livePosition=new Point(dot.x+previewTracker.offsetX,dot.y+previewTracker.offsetY);
            liveWindow=previewCalibration.window;
            liveMiniPixels=previewTracker.LastFrame;
            liveMiniWidth=previewCalibration.roiWidth;liveMiniHeight=previewCalibration.roiHeight;
            liveMiniOrigin=new Point(previewTracker.offsetX,previewTracker.offsetY);
        }
    }
    void RefreshLive(object sender,EventArgs e){
        if(closing)return;
        bool ended=false,ready=false;
        lock(gate){
            ended=uiRecording&&!engine.Recording;uiRecording=engine.Recording;
            if(ended){
                waitingForWatch=false;
                if(!engine.Document.graphEdited)RouteGraph.Build(engine.Document);
                MarkDirty();
                engine.Status=GeneratedStatus(engine.Status);
            }
            ready=engine.Recording&&!readyPublished&&engine.lastGood>0;
            if(ready)readyPublished=true;
            canvas.Document=engine.Document;canvas.AllowEdit=!trialActive&&!engine.Recording&&!ManualCaptureActive&&HasRoute;
            var position=FreshMapPosition();
            canvas.SetLivePosition(position);
            homeMap.SetLivePosition(position);
            if(homeMapVisible&&!trialActive&&position.HasValue&&liveMiniPixels!=null&&shownMiniPixels!=liveMiniPixels){
                var prior=homeMap.Map;
                homeMap.Map=Wire.BitmapFrom(liveMiniPixels,liveMiniWidth,liveMiniHeight);
                homeMap.MapOrigin=liveMiniOrigin;
                shownMiniPixels=liveMiniPixels;
                if(prior!=null)prior.Dispose();
                homeMap.Invalidate();
            }
            RefreshCaptureButtons();
            UpdateFitWalkLine(SelectedRouteEdge());
            suggestJumps.Visible=engine.Document.manualMap&&!ManualCaptureActive&&!engine.Recording;
            suggestJumps.Enabled=!trialActive&&HasRoute&&engine.Document.edges.Any(edge=>edge.kind=="walk");
            UpdateJumpConfirmation();
            organize.Enabled=!engine.Document.manualMap&&!ManualCaptureActive&&!engine.Recording&&!trialActive&&HasRoute&&(engine.Document.edges.Count>0||engine.Document.samples.Count>1);
            start.Visible=!embedded&&!engine.Recording;pause.Visible=engine.Recording;
            start.Enabled=!engine.Recording&&!trialActive&&HasRoute&&engine.Document.roiWidth>0;
            pause.Enabled=engine.Recording;UpdateTrialButtons();viewSwitch.Enabled=!engine.Recording;mapChoice.Enabled=import.Enabled=!engine.Recording&&!ManualCaptureActive&&!trialActive;
            routeChoice.Enabled=newRoute.Enabled=calibrate.Enabled=!engine.Recording&&!ManualCaptureActive&&!trialActive&&selectedMap!=null;
            saveRoute.Enabled=!engine.Recording&&!ManualCaptureActive&&!trialActive&&HasRoute&&(dirty||filename==null);
            jump.Enabled=nameBox.Enabled=recordKeyboard.Enabled=!engine.Recording&&HasRoute;
            RefreshRoleJump();
            recover.Available=CanRecover;recover.Enabled=!engine.Recording;
            SetStatus(engine.Status);
            if(++tick%3==0&&mapVisible&&!engine.Recording){var map=engine.MapBitmap();if(map!=null){var old=canvas.Map;canvas.Map=map;if(old!=null)old.Dispose();}}
        }
        if(ended){if(RecordingStopped!=null)RecordingStopped();ShowRouteMap();RefreshGraph();}
        if(ready&&RecordingReady!=null)RecordingReady();
        canvas.Invalidate();if(draftDirty&&(DateTime.UtcNow-saved).TotalSeconds>3)SaveDraft();
    }
    void RefreshGraph(){
        lock(gate){
            canvas.Document=engine.Document;canvas.AllowEdit=!trialActive&&!engine.Recording&&!ManualCaptureActive&&HasRoute;
            if(TrialTarget.HasValue&&RouteGraph.SnapNavigationTarget(engine.Document,TrialTarget.Value)!=TrialTarget)
                canvas.TargetPoint=null;
            var map=engine.MapBitmap();
            if(map==null&&engine.Document.roiWidth>0)map=RouteFiles.Png(engine.Document.lastMiniPng);
            var prior=canvas.Map;canvas.Map=map;if(prior!=null)prior.Dispose();
            var source=engine.Document.roiWidth>0?engine.Document:selectedMap;
            if(homeDocument!=source)homeMap.TargetPoint=null;
            homeDocument=source;
            homeMap.Document=source??engine.Document;
            homeMap.MapOrigin=map==null&&source!=null?new Point(source.lastOffsetX,source.lastOffsetY):(Point?)null;
            shownMiniPixels=null;
            var oldMini=homeMap.Map;
            homeMap.Map=map!=null?new Bitmap(map):source==null?null:RouteFiles.Png(source.lastMiniPng);
            if(oldMini!=null)oldMini.Dispose();
            homeMap.Invalidate();
            if(!engine.Document.edges.Any(e=>e.id==canvas.SelectedEdge))canvas.SelectedEdge=null;
            UpdateDetail();
        }
        canvas.Invalidate();
    }
    void UpdateDetail(){
        lock(gate){
            var edge=SelectedRouteEdge();
            UpdateFitWalkLine(edge);
            UpdateJumpConfirmation();
            detail.Visible=edge!=null;
            if(edge==null){detail.Text="";return;}
            string summary=RouteGraph.KindName(edge.kind)+(edge.both?" · 可往返":" · 单向");
            if(edge.suggestedJump)summary+=" · 待试走";
            if(edge.duration>1)summary+=" · "+(edge.duration/1000.0).ToString("0.##")+" 秒";
            if(edge.jumpHold>=0)summary+=" · 跳键 "+edge.jumpHold+"ms";
            if(edge.kind=="drop"&&edge.downTail>=0)summary+=" · 下键尾随 "+edge.downTail+"ms";
            detail.Text=summary;
        }
    }
    RouteEdge SelectedRouteEdge(){
        return canvas.SelectedEdge==null?null:engine.Document.edges.FirstOrDefault(e=>e.id==canvas.SelectedEdge);
    }
    void UpdateFitWalkLine(RouteEdge edge){
        fitWalkLine.Enabled=!trialActive&&!engine.Recording&&edge!=null&&edge.kind=="walk"&&edge.points.Count>2;
    }
    void UpdateJumpConfirmation(){
        var selected=SelectedRouteEdge();
        confirmJump.Visible=!ManualCaptureActive&&!engine.Recording&&selected!=null&&selected.suggestedJump;
        confirmJump.Enabled=!trialActive;
    }

    void SuggestJumps(){
        if(!HasRoute||trialActive||!Editable())return;
        int count;
        lock(gate){
            var document=engine.Document;
            string previous=GraphState();
            var proposals=RouteGraph.SuggestMapJumps(document);
            count=proposals.Count;
            if(count==0){SetStatus("附近没有可补的起跳连接");return;}
            RememberGraph(previous);MarkDirty();
            canvas.CancelConnection();
            canvas.SelectedEdge=count==1?proposals[0]:null;
        }
        ShowRouteMap();RefreshGraph();
        SetStatus("已补 "+count+" 条待试走跳跃。保存后可试走，成功后选中该线并确认");
    }

    void ConfirmSuggestedJump(){
        if(!HasRoute||trialActive||!Editable())return;
        lock(gate){
            var edge=SelectedRouteEdge();
            if(edge==null||!edge.suggestedJump)return;
            RememberGraph(GraphState());
            edge.suggestedJump=false;
            MarkDirty();
        }
        RefreshGraph();SetStatus("已确认跳跃，保存后用于正式路线");
    }

    void FitSelectedWalkLine(){
        if(!HasRoute||trialActive||!Editable())return;
        lock(gate){
            var edge=SelectedRouteEdge();
            if(edge==null||edge.kind!="walk"||edge.points.Count<=2)return;
            var document=engine.Document;
            var from=document.nodes.First(node=>node.id==edge.from);
            var to=document.nodes.First(node=>node.id==edge.to);
            if(from.x==to.x){SetStatus("行走线的起点和终点需要有水平距离");return;}
            RememberGraph(GraphState());
            edge.points=new List<RoutePoint>{new RoutePoint(from.x,from.y),new RoutePoint(to.x,to.y)};
            edge.edited=document.graphEdited=true;
            MarkDirty();
        }
        SetStatus("已拟合成直线");RefreshGraph();
    }
    void SplitWalkAt(RouteEdge edge,Point at){
        if(!HasRoute||trialActive||!Editable())return;
        lock(gate){
            string previous=GraphState();
            if(RouteGraph.SplitWalk(engine.Document,edge,at)==null){SetStatus("请在行走线中间选择拆分点");return;}
            RememberGraph(previous);canvas.SelectedEdge=edge.id;MarkDirty();
        }
        SetStatus("已拆分路线");RefreshGraph();
    }
    void ChangeWalkToJump(RouteEdge edge,bool reverse){
        if(!HasRoute||trialActive||!Editable())return;
        lock(gate){
            string previous=GraphState();
            if(!RouteGraph.ConvertWalkToJump(engine.Document,edge,reverse))return;
            RememberGraph(previous);canvas.SelectedEdge=edge.id;MarkDirty();
        }
        SetStatus("已改为跳跃");RefreshGraph();
    }
    void OrganizeRoute(){
        if(!HasRoute||trialActive||!Editable())return;
        int before,after;
        lock(gate){
            var document=engine.Document;
            string previous=GraphState();
            before=document.edges.Count;
            try{
                if(!document.graphEdited&&document.samples.Count>1)RouteGraph.Build(document);
                else RouteGraph.OrganizeRecorded(document);
                RouteFiles.Validate(document);
            }catch(Exception error){RestoreGraph(previous,false);SetStatus("整理失败 · "+error.Message);return;}
            if(previous==GraphState()){SetStatus("路线已整理");return;}
            RememberGraph(previous);MarkDirty();after=document.edges.Count;
        }
        ShowRouteMap();RefreshGraph();
        SetStatus("已整理路线 · "+before+" → "+after+" 段");
    }
    string GraphState(){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(new RouteGraphSnapshot{nodes=engine.Document.nodes,edges=engine.Document.edges,edited=engine.Document.graphEdited,manualMap=engine.Document.manualMap});}
    void RememberGraph(string state){undo.Add(state);if(undo.Count>24)undo.RemoveAt(0);redo.Clear();}
    void RestoreGraph(string json,bool changed=true){
        var state=new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Deserialize<RouteGraphSnapshot>(json);
        engine.Document.nodes=state.nodes;engine.Document.edges=state.edges;engine.Document.graphEdited=state.edited;engine.Document.manualMap=state.manualMap;
        canvas.Path.Clear();canvas.CancelConnection();if(changed)MarkDirty();
    }
    public void Undo(){
        if(!HasRoute||!Editable()||undo.Count==0)return;
        lock(gate){redo.Add(GraphState());string state=undo.Last();undo.RemoveAt(undo.Count-1);RestoreGraph(state);}
        RefreshGraph();
    }
    public void Redo(){
        if(!HasRoute||!Editable()||redo.Count==0)return;
        lock(gate){undo.Add(GraphState());string state=redo.Last();redo.RemoveAt(redo.Count-1);RestoreGraph(state);}
        RefreshGraph();
    }
    void PaintRoute(string brush,List<RoutePoint> stroke,RouteNode from,RouteNode to){
        if(!HasRoute||!Editable())return;
        var restoreView=RouteGraph.IsMapAction(brush)?canvas.PreserveView():null;
        string kind;
        lock(gate){
            string previous=GraphState();
            var document=engine.Document;
            var first=new Point(stroke[0].x,stroke[0].y);
            var last=new Point(stroke[stroke.Count-1].x,stroke[stroke.Count-1].y);
            var edge=document.manualMap||brush=="jump_down"||brush=="rope_mount"?
                RouteGraph.DrawMapLink(document,brush,first,last,from,to):
                RouteGraph.DrawStroke(document,brush,stroke,from,to);
            if(edge==null){
                if(document.manualMap||brush=="jump_down"||brush=="rope_mount")RestoreGraph(previous,false);
                SetStatus(brush=="walk"?"请选择两个已标记的端点":brush=="climb"?
                    "从梯绳端点画竖线到另一端":brush=="rope_mount"?
                    "请从已画平台上的起跳点连到已画梯绳的抓点":"请从已画的平台线连到目标平台线");
                return;
            }
            kind=edge.kind;RememberGraph(previous);canvas.SelectedEdge=edge.id;MarkDirty();
        }
        SetStatus(kind=="rope_mount"?
            "已添加平台起跳到梯绳的抓绳动作":kind=="drop"?"已添加下落连接，平台保持原位":"已画一段路线");
        RefreshGraph();if(restoreView!=null)restoreView();
    }
    RouteNode MoveRouteNode(RouteNode node,Point destination,RouteEdge segment){
        if(!HasRoute||trialActive||!Editable())return null;
        RouteNode adjusted;
        lock(gate){
            string previous=GraphState();
            adjusted=RouteGraph.MoveRouteEndpoint(engine.Document,segment,node,destination);
            if(adjusted==null)return null;
            RememberGraph(previous);MarkDirty();
        }
        SetStatus("端点已调整");RefreshGraph();return adjusted;
    }
    void EraseRouteStroke(string edgeId){
        if(!HasRoute||!Editable())return;
        lock(gate){
            var d=engine.Document;
            var edge=d.edges.FirstOrDefault(e=>e.id==edgeId);if(edge==null)return;
            RememberGraph(GraphState());d.edges.Remove(edge);
            d.graphEdited=true;canvas.SelectedEdge=null;MarkDirty();
        }
        SetStatus("已擦除路线，端点已保留");RefreshGraph();
    }
    void DeleteRouteSelection(string[] nodeIds,string[] edgeIds){
        if(!HasRoute||trialActive||!Editable())return;
        int nodes=0,edges=0;
        lock(gate){
            var d=engine.Document;var selected=new HashSet<string>(nodeIds);var links=new HashSet<string>(edgeIds);
            RememberGraph(GraphState());
            edges=d.edges.RemoveAll(edge=>links.Contains(edge.id)||selected.Contains(edge.from)||selected.Contains(edge.to));
            nodes=d.nodes.RemoveAll(node=>selected.Contains(node.id));
            foreach(var rope in d.edges.Where(edge=>edge.ropeEntries!=null))rope.ropeEntries.RemoveAll(id=>selected.Contains(id));
            d.graphEdited=true;MarkDirty();canvas.ClearSelection();
        }
        SetStatus("已删除 "+nodes+" 个点和 "+edges+" 段线 · 可撤销");RefreshGraph();
    }
    void DeleteRouteNode(RouteNode node,bool allowConnected){
        if(!HasRoute||trialActive||!Editable())return;
        int removed;
        lock(gate){
            var document=engine.Document;
            if(!document.nodes.Contains(node))return;
            removed=document.edges.Count(e=>e.from==node.id||e.to==node.id);
            if(removed>0&&!allowConnected)return;
            RememberGraph(GraphState());
            document.edges.RemoveAll(e=>e.from==node.id||e.to==node.id);document.nodes.Remove(node);
            foreach(var rope in document.edges.Where(e=>e.ropeEntries!=null))rope.ropeEntries.Remove(node.id);
            document.graphEdited=true;MarkDirty();
        }
        SetStatus(removed==0?"已删除节点":"已删除节点及 "+removed+" 段相连路线");RefreshGraph();
    }

    string MapDirectory(string id){return Path.Combine(mapsFolder,id);}
    string MapFile(string id){return Path.Combine(MapDirectory(id),"map.json");}
    string DraftPath{get{return selectedMap==null?legacyDraft:Path.Combine(MapDirectory(selectedMap.id),"draft.route.json");}}
    bool CanRecover{get{return File.Exists(DraftPath)||File.Exists(legacyDraft);}}
    static bool IsId(string id){Guid parsed;return Guid.TryParseExact(id,"N",out parsed);}
    static RouteDocument RouteFromMap(RouteDocument map){
        var route=PlayerMinimap.FromRoute(map).Route();route.mapId=map.id;return route;
    }
    void UseDocument(RouteDocument document,string path,bool unsaved){
        RouteGraph.GeneratePaintedActions(document);
        CancelPointCapture();
        var next=new RouteEngine(document);
        lock(gate){
            engine=next;previewPositionIssue=null;timeBase=document.samples.Count==0?0:document.samples.Last().time+1;
            clock.Restart();previewAt=-PreviewPollIntervalMs;
        }
        loading=true;
        nameBox.Text=document.name;
        if(roleJumpKey==null)jump.SelectedItem=jump.Items.Cast<KeyOption>().FirstOrDefault(k=>k.Vk==document.jumpKey)??jump.Items[0];
        loading=false;undo.Clear();redo.Clear();filename=path;dirty=draftDirty=unsaved;
        canvas.SelectedEdge=null;canvas.Path.Clear();
        canvas.TargetPoint=null;canvas.MapPointPicked=null;
        canvas.Fit();RefreshGraph();
    }
    void UpdateChoices(){
        var previouslyLoading=loading;loading=true;
        try{
            mapChoice.Items.Clear();
            mapChoice.Items.Add(new LibraryItem{Name="选择地图"});
            if(Directory.Exists(mapsFolder)){
                foreach(var dir in Directory.GetDirectories(mapsFolder).OrderBy(d=>d)){
                    string id=Path.GetFileName(dir);if(!IsId(id))continue;
                    try{
                        var map=RouteFiles.Load(MapFile(id));
                        if(map.id==id)mapChoice.Items.Add(new LibraryItem{Path=MapFile(id),Name=map.name});
                    }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
                }
            }
            mapChoice.SelectedItem=mapChoice.Items.Cast<LibraryItem>().FirstOrDefault(i=>selectedMap!=null&&i.Path==MapFile(selectedMap.id))??mapChoice.Items[0];

            routeChoice.Items.Clear();
            bool hasDraft=filename==null&&HasRoute;
            routeChoice.EmptyChoiceIndex=hasDraft?-1:0;
            routeChoice.Items.Add(new LibraryItem{Name=hasDraft?"当前草稿":"选择路线"});
            if(selectedMap!=null){
                foreach(var path in Directory.EnumerateFiles(MapDirectory(selectedMap.id),"*.route.json").OrderBy(f=>f)){
                    var stem=Path.GetFileName(path).Replace(".route.json","");
                    if(!IsId(stem))continue;
                    try{
                        var route=RouteFiles.Load(path);
                        if(route.mapId==selectedMap.id&&route.id==stem)routeChoice.Items.Add(new LibraryItem{Path=path,Name=route.name});
                    }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
                }
            }
            routeChoice.SelectedItem=routeChoice.Items.Cast<LibraryItem>().FirstOrDefault(i=>i.Path==filename)??routeChoice.Items[0];
        }finally{loading=previouslyLoading;}
    }
    void PersistSelection(){
        string id=filename==null?"":engine.Document.id;
        Wire.Save(selectionPath,Wire.Map("map",selectedMap==null?"":selectedMap.id,"route",id));
    }
    void RestoreSelection(){
        try{
            if(File.Exists(selectionPath)){
                var state=Wire.Load(selectionPath);
                string map=Wire.Str(state,"map"),route=Wire.Str(state,"route");
                if(IsId(map)&&File.Exists(MapFile(map))){
                    selectedMap=RouteFiles.Load(MapFile(map));
                    if(selectedMap.id!=map)selectedMap=null;
                    if(selectedMap!=null&&IsId(route)){
                        var path=Path.Combine(MapDirectory(map),route+".route.json");
                        if(File.Exists(path))LoadSaved(path);
                    }
                }
            }
        }catch(Exception e){SetStatus("无法恢复上次选择："+e.Message);}
        UpdateChoices();RefreshLive(null,EventArgs.Empty);
    }
    void SelectMap(){
        var item=mapChoice.SelectedItem as LibraryItem;if(item==null)return;
        if(!Editable()){UpdateChoices();return;}
        try{
            var map=item.Path==null?null:RouteFiles.Load(item.Path);
            ArchiveDraft();selectedMap=map;UseDocument(new RouteDocument(),null,false);
            UpdateChoices();PersistSelection();SetStatus(map==null?"请选择地图":map.roiWidth>=24?"请选择或新建路线":"请标定这张地图");
        }catch(Exception e){UpdateChoices();SetStatus("切换地图失败："+e.Message);}
    }
    void SelectRoute(){
        var item=routeChoice.SelectedItem as LibraryItem;if(item==null||item.Path==filename)return;
        if(!Editable()){UpdateChoices();return;}
        try{
            ArchiveDraft();
            if(item.Path==null)UseDocument(new RouteDocument(),null,false);else LoadSaved(item.Path);
            UpdateChoices();PersistSelection();
        }catch(Exception e){UpdateChoices();SetStatus("切换路线失败："+e.Message);}
    }
    void NewMap(){
        if(!Editable())return;
        string name=RoleSelector.Ask(this,"新建地图","");if(String.IsNullOrWhiteSpace(name))return;
        try{
            ArchiveDraft();var map=new RouteDocument{name=name.Trim()};
            RouteFiles.Save(MapFile(map.id),map);selectedMap=map;UseDocument(new RouteDocument(),null,false);
            UpdateChoices();PersistSelection();SetStatus("地图已创建，请标定小地图");
        }catch(Exception e){SetStatus("新建地图失败："+e.Message);}
    }
    void New(){
        if(!Editable()||selectedMap==null)return;
        try{
            ArchiveDraft();var document=RouteFromMap(selectedMap);
            document.name="路线 "+(Directory.GetFiles(MapDirectory(selectedMap.id),"*.route.json").Count(p=>IsId(Path.GetFileName(p).Replace(".route.json","")))+1);
            UseDocument(document,null,true);UpdateChoices();PersistSelection();
            SetStatus(selectedMap.roiWidth>=24?"可以开始录制":"请先标定地图");
        }catch(Exception e){SetStatus("新建路线失败："+e.Message);}
    }
    void SaveDraft(){
        if(!draftDirty)return;
        try{
            lock(gate){
                if(!HasRoute)return;
                engine.FlushMap();RouteFiles.Save(DraftPath,engine.Document);draftDirty=false;saved=DateTime.UtcNow;
            }
        }catch(Exception e){SetStatus("草稿保存失败："+e.Message);}
    }
    void MoveLegacyDraft(){
        string history=Path.Combine(folder,"history");Directory.CreateDirectory(history);
        File.Move(legacyDraft,Path.Combine(history,"legacy-draft-"+Guid.NewGuid().ToString("N")+".route.json"));
    }
    void ArchiveDraft(){
        if(!dirty||!HasRoute)return;
        lock(gate){
            if(engine.Document.samples.Count==0&&engine.Document.nodes.Count==0)return;
            engine.FlushMap();
            string path=Path.Combine(folder,"history","draft-"+Guid.NewGuid().ToString("N")+".route.json");
            RouteFiles.Save(path,engine.Document);
            SaveDraft();
        }
    }
    void Save(){
        if(!Editable()||!HasRoute||selectedMap==null)return;
        try{
            string path;
            lock(gate){
                var document=engine.Document;
                if(String.IsNullOrWhiteSpace(document.name))throw new IOException("请填写路线名称");
                if(!IsId(document.id)||document.mapId!=selectedMap.id)throw new IOException("路线与所选地图不一致");
                path=Path.Combine(MapDirectory(selectedMap.id),document.id+".route.json");
                RouteGraph.GeneratePaintedActions(document);
                engine.FlushMap();RouteFiles.Save(path,document);
                filename=path;dirty=draftDirty=false;
                if(File.Exists(DraftPath)){
                    try{if(RouteFiles.Load(DraftPath).id==document.id)File.Delete(DraftPath);}catch(IOException){}
                }
            }
            UpdateChoices();PersistSelection();SetTrialActive(trialActive);SetStatus("路线已保存");RefreshGraph();
        }catch(Exception e){SetStatus("路线保存失败："+e.Message);}
    }
    void LoadSaved(string path){
        var document=RouteFiles.Load(path);
        if(selectedMap==null||document.mapId!=selectedMap.id||!IsId(document.id)||
           Path.GetFileName(path)!=document.id+".route.json")throw new IOException("路线与所选地图不一致");
        RouteGraph.RefreshWalkingDirections(document);UseDocument(document,path,false);
        SetStatus("已打开路线");
    }
    void Open(){
        if(!Editable())return;
        using(var dialog=new OpenFileDialog{Title="导入旧路线",Filter="结构化路线 (*.route.json)|*.route.json",InitialDirectory=folder})
            if(dialog.ShowDialog(this)==DialogResult.OK)LoadLegacyOrDraft(dialog.FileName,true);
    }
    void Recover(){
        if(!Editable()||!CanRecover)return;
        LoadLegacyOrDraft(File.Exists(DraftPath)?DraftPath:legacyDraft,false);
    }
    void LoadLegacyOrDraft(string path,bool importing){
        try{
            var document=RouteFiles.Load(path);
            RouteDocument map=null;
            if(IsId(document.mapId)&&File.Exists(MapFile(document.mapId))){
                map=RouteFiles.Load(MapFile(document.mapId));
                if(map.id!=document.mapId)throw new IOException("地图编号不一致");
            }
            if(map==null){
                string name=RoleSelector.Ask(this,"为这条路线选择地图名称","");if(String.IsNullOrWhiteSpace(name))return;
                map=PlayerMinimap.FromRoute(document).Route();map.name=name.Trim();
            }
            ArchiveDraft();
            if(!File.Exists(MapFile(map.id)))RouteFiles.Save(MapFile(map.id),map);
            if(!IsId(document.id)||importing)document.id=Guid.NewGuid().ToString("N");
            document.mapId=map.id;
            if(!document.graphEdited&&document.edges.Count==0&&document.samples.Count>1)RouteGraph.Build(document);
            RouteGraph.RefreshWalkingDirections(document);
            selectedMap=map;UseDocument(document,null,true);UpdateChoices();PersistSelection();
            if(!importing&&path==legacyDraft){
                SaveDraft();
                if(!draftDirty&&File.Exists(DraftPath))MoveLegacyDraft();
            }
            SetStatus(importing?"已导入，请保存路线":"已恢复草稿，请保存路线");
        }catch(Exception e){SetStatus("打开路线失败："+e.Message);}
    }
}
}
