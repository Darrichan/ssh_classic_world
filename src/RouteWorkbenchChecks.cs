using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SshScreen {
public static class RouteWorkbenchChecks {
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr target,int message,IntPtr key,IntPtr flags);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr target,int command);
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object obj,string name){return obj.GetType().GetField(name,Private).GetValue(obj);}
    static void Set(object obj,string name,object value){obj.GetType().GetField(name,Private).SetValue(obj,value);}
    static object Call(object obj,string name,params object[] args){return obj.GetType().GetMethod(name,Private).Invoke(obj,args);}
    static void Need(bool ok,string reason){if(!ok)throw new IOException(reason);}
    sealed class FakeController {
        readonly object gate=new object();
        readonly List<string> events=new List<string>();
        readonly HashSet<int> keys=new HashSet<int>();
        int[] watchKeys=new int[0];
        public volatile bool Watching,CursorInside;
        public volatile int WatchEpoch,FrameEpoch;
        public int[] Keys{get{lock(gate)return keys.ToArray();}}
        public int[] WatchKeys{get{lock(gate)return (int[])watchKeys.Clone();}}
        public int[] WatchedDown{get{lock(gate)return watchKeys.Where(keys.Contains).ToArray();}}
        public string[] Events{get{lock(gate)return events.ToArray();}}
        public void Serve(TcpListener listener){
            try{
                using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
                    while(true){
                        var command=Wire.Receive(stream).Header;
                        string op=Wire.Str(command,"op");
                        int key=Wire.Int(command,"vk");
                        lock(gate){
                            if(op=="record_keys"){
                                WatchEpoch++;Watching=Wire.Bool(command,"enabled");
                                watchKeys=Watching?Wire.IntArray(command["keys"]):new int[0];
                                events.Add(Watching?"watch_on":"watch_off");
                            }
                            else if(op=="key_down"){keys.Add(key);events.Add("down:"+key);}
                            else if(op=="key_up"){keys.Remove(key);events.Add("up:"+key);}
                            else if(op=="release"){keys.Clear();events.Add(op);}
                            else if(op=="arm"||op.StartsWith("mouse"))events.Add(op);
                        }
                        if(op=="record_keys"&&Watching)Thread.Sleep(350);
                        Wire.Send(stream,Wire.Map("ok",true,"recording_epoch",WatchEpoch));
                    }
                }
            }catch(IOException){}catch(SocketException){}
        }
    }
    static void CheckParking(){
        var screen=new Rectangle(0,0,1920,1080);
        var secondary=new Rectangle(-1536,0,1536,864);
        var target=new Rectangle(40,0,1440,1080);
        var blocked=new[]{target,new Rectangle(0,0,40,1080),new Rectangle(1600,0,320,1080)};
        var pointer=new Point(100,530);
        var chosen=Runtime.RecordingParkingPoint(pointer,new[]{screen},blocked);
        Need(chosen.HasValue&&screen.Contains(chosen.Value)&&!blocked.Any(r=>r.Contains(chosen.Value)),"No safe point was chosen in an available edge strip");
        Need(Runtime.RecordingParkingPoint(chosen.Value,new[]{screen},blocked)==chosen,"Already safe cursor would move");
        chosen=Runtime.RecordingParkingPoint(pointer,new[]{screen,secondary},new[]{screen});
        Need(chosen.HasValue&&secondary.Contains(chosen.Value),"Did not park on the second screen");
        Need(Runtime.RecordingParkingPoint(pointer,new[]{screen},new[]{screen})==null,"Fullscreen target was treated as safe");
    }
    static byte[] Scene(int dotX=50){
        var pixels=new byte[320*200*3];var random=new Random(22);
        for(int y=0;y<200;y++)for(int x=0;x<320;x++){
            int i=(y*320+x)*3;
            pixels[i]=(byte)random.Next(30,150);pixels[i+1]=(byte)random.Next(30,150);pixels[i+2]=(byte)random.Next(30,150);
        }
        for(int y=39;y<=41;y++)for(int x=dotX-1;x<=dotX+1;x++){
            int i=(y*320+x)*3;
            pixels[i]=136;pixels[i+1]=255;pixels[i+2]=255;
        }
        return pixels;
    }
    static string Library(string root,RouteDocument map,params string[] names){
        var folder=Path.Combine(root,"routes","maps",map.id);
        RouteFiles.Save(Path.Combine(folder,"map.json"),map);
        foreach(string name in names){
            var d=PlayerMinimap.FromRoute(map).Route();d.mapId=map.id;d.name=name;
            d.nodes.Add(new RouteNode{id="A",label="A",x=1,y=1});d.nodes.Add(new RouteNode{id="B",label="B",x=30,y=1});
            d.edges.Add(new RouteEdge{id="first",from="A",to="B",kind="walk",duration=200,both=true});
            RouteFiles.Save(Path.Combine(folder,d.id+".route.json"),d);
        }
        return folder;
    }
    static void Choose(ComboBox list,string name){
        int index=list.FindStringExact(name);Need(index>=0,"Missing option "+name);list.SelectedIndex=index;
    }
    static void Press(RemoteView view,string method,Keys key){
        typeof(Control).GetMethod(method,Private).Invoke(view,new object[]{new KeyEventArgs(key)});
    }
    static void MouseGraph(RouteCanvas graph,string method,Point position,MouseButtons button=MouseButtons.Left){
        typeof(Control).GetMethod(method,Private).Invoke(graph,new object[]{
            new MouseEventArgs(button,1,position.X,position.Y,0)});
    }
    static void PaintGraph(RouteCanvas graph,Point from,Point to){
        Call(graph,"LayoutMap");
        var a=Point.Round((PointF)Call(graph,"ScreenPoint",from.X,from.Y));
        var b=Point.Round((PointF)Call(graph,"ScreenPoint",to.X,to.Y));
        MouseGraph(graph,"OnMouseDown",a);MouseGraph(graph,"OnMouseMove",b);MouseGraph(graph,"OnMouseUp",b);
    }
    static void CheckNodeEditing(RouteStudio studio,RouteCanvas graph){
        Need(studio.SelectBrush("jump"),"Jump brush unavailable for node editing");
        PaintGraph(graph,new Point(16,59),new Point(31,40));
        Need(studio.SelectBrush("select"),"Select tool unavailable for node editing");
        var walk=graph.Document.edges.Single(e=>e.kind=="walk");
        var jump=graph.Document.edges.Single(e=>e.kind=="jump");
        var node=graph.Document.nodes.Single(n=>n.id==walk.from);
        string id=node.id;
        Need(jump.from==id,"Jump and walk did not share the edited node");
        Call(graph,"LayoutMap");
        var origin=Point.Round((PointF)Call(graph,"ScreenPoint",16,59));
        var extended=Point.Round((PointF)Call(graph,"ScreenPoint",8,59));
        graph.SelectedEdge=walk.id;
        MouseGraph(graph,"OnMouseDown",origin);MouseGraph(graph,"OnMouseMove",extended);
        Need(node.x==16&&((PointF[])Call(graph,"EdgePoints",jump)).First()==(PointF)Call(graph,"ScreenPoint",16,59),
            "The walking preview pulled the jump endpoint");
        MouseGraph(graph,"OnMouseUp",extended);
        string moved=walk.from;
        Need(node.x==16&&moved!=id&&walk.points.First().x==8&&jump.from==id&&jump.points.First().x==16&&walk.both,
            "Extending the walk moved the jump endpoint or did not detach");
        studio.Undo();Need(graph.Document.edges.Single(e=>e.kind=="walk").from==id,"One undo did not restore the shared endpoint");
        studio.Redo();Need(graph.Document.nodes.Single(n=>n.id==moved).x==8,"Redo did not restore the moved node");
        PaintGraph(graph,new Point(8,59),new Point(25,59));
        Need(graph.Document.nodes.Single(n=>n.id==moved).x==25&&graph.Document.edges.All(e=>e.kind!="walk"||e.points.First().x==25),
            "Dragging inward did not shorten the walking segment");
        Call(studio,"Save");
        Need(studio.HasRuntimeRoute&&studio.RuntimeRoute().nodes.Single(n=>n.id==moved).x==25,"Saved route used the previous node location");
        var choices=(ComboBox)Get(studio,"routeChoice");string name=graph.Document.name;
        choices.SelectedIndex=0;Choose(choices,name);
        Need(graph.Document.nodes.Single(n=>n.id==moved).x==25&&graph.Document.edges.Any(e=>e.kind=="jump"&&e.from==id),
            "Reopening lost the adjusted node or its connected jump");
    }
    static void CheckDropDrawingDoesNotMovePlatform(RouteStudio studio,RouteCanvas graph){
        Call(studio,"New");var d=graph.Document;d.manualMap=d.graphEdited=true;
        d.nodes.AddRange(new[]{new RouteNode{id="UL",x=20,y=30},new RouteNode{id="UR",x=70,y=30},
            new RouteNode{id="LL",x=20,y=60},new RouteNode{id="LR",x=70,y=60}});
        d.edges.AddRange(new[]{new RouteEdge{id="U",from="UL",to="UR",both=true},new RouteEdge{id="L",from="LL",to="LR",both=true}});
        Call(studio,"RefreshGraph");Call(graph,"LayoutMap");
        var original=d.nodes.ToDictionary(n=>n.id,n=>new Point(n.x,n.y));
        var upper=(PointF[])Call(graph,"EdgePoints",d.edges[0]);
        graph.SelectedEdge="U";
        Need(studio.SelectBrush("drop"),"Could not select the drop drawing tool");
        var first=Point.Round((PointF)Call(graph,"ScreenPoint",40,30));
        var last=Point.Round((PointF)Call(graph,"ScreenPoint",45,60));
        MouseGraph(graph,"OnMouseDown",first);MouseGraph(graph,"OnMouseMove",last);
        Need(original.All(pair=>d.nodes.Any(n=>n.id==pair.Key&&new Point(n.x,n.y)==pair.Value)),"Drawing a drop moved platform endpoints during the gesture");
        Need(upper.SequenceEqual((PointF[])Call(graph,"EdgePoints",d.edges.First(e=>e.id=="U"))),"Drawing a drop dragged the platform preview");
        MouseGraph(graph,"OnMouseUp",last);Call(graph,"LayoutMap");
        Need(d.edges.Count(e=>e.kind=="drop")==1&&original.All(pair=>d.nodes.Any(n=>n.id==pair.Key&&new Point(n.x,n.y)==pair.Value)),
            "Adding a drop connection relocated existing platform endpoints");
        var same=Point.Round((PointF)Call(graph,"ScreenPoint",20,30));
        Need(Math.Abs(same.X-upper[0].X)<=1&&Math.Abs(same.Y-upper[0].Y)<=1,"Completing a drop unexpectedly reframed the platform");
        studio.Undo();Need(d.nodes.Count==4&&d.edges.Count==2,"Drop undo did not restore the platforms");
        // An incomplete destination may not turn an action into a platform drag.
        PaintGraph(graph,new Point(40,30),new Point(45,45));
        Need(d.nodes.Count==4&&d.edges.Count==2&&original.All(pair=>d.nodes.Any(n=>n.id==pair.Key&&new Point(n.x,n.y)==pair.Value)),
            "A drop without a landing platform changed the map");
    }
    static void CheckIndependentDrop(RouteStudio studio,RouteCanvas graph){
        Call(studio,"New");var d=graph.Document;
        var launch=new RouteNode{id="lip",x=58,y=40};
        var landing=new RouteNode{id="landing",x=54,y=67};
        d.nodes.AddRange(new[]{new RouteNode{id="left",x=22,y=40},launch,landing,new RouteNode{id="right",x=78,y=67}});
        var upper=new RouteEdge{id="upper",from="left",to=launch.id,both=true};
        var lower=new RouteEdge{id="lower",from=landing.id,to="right",both=true};
        var fall=new RouteEdge{id="fall",from=launch.id,to=landing.id,kind="drop",downTail=58,
            points=new List<RoutePoint>{new RoutePoint(58,40),new RoutePoint(63,50),new RoutePoint(54,67)}};
        d.edges.AddRange(new[]{upper,lower,fall});
        Call(studio,"RefreshGraph");studio.SelectBrush("select");graph.SelectedEdge=lower.id;
        Call(graph,"LayoutMap");
        var original=(PointF[])Call(graph,"EdgePoints",fall);
        Need(original.Length==2,"A legacy fall appeared as a bent line");
        var moved=Point.Round((PointF)Call(graph,"ScreenPoint",40,67));
        MouseGraph(graph,"OnMouseDown",Point.Round(original[1]));MouseGraph(graph,"OnMouseMove",moved);
        Need(((PointF[])Call(graph,"EdgePoints",fall)).SequenceEqual(original),"The platform drag preview pulled the fall");
        MouseGraph(graph,"OnMouseUp",moved);
        Need(fall.to==landing.id&&landing.x==54&&lower.from!=landing.id&&d.nodes.Single(n=>n.id==lower.from).x==40,
            "Dragging the landing platform changed fall geometry");
        graph.SelectedEdge=fall.id;
        PaintGraph(graph,new Point(58,40),new Point(60,39));
        Need(fall.from!=upper.to&&upper.to==launch.id&&launch.x==58&&fall.points.Count==2&&fall.downTail==58,
            "Editing the fall pulled the upper platform or changed fall timing");
        studio.Undo();Need(d.edges.Single(x=>x.kind=="drop").from==launch.id,"Undo did not restore the fall anchor");
    }
    static void CheckIndependentWalkAndDelete(RouteStudio studio,RouteCanvas graph){
        Call(studio,"New");var d=graph.Document;
        var foot=new RouteNode{id="B",x=52,y=56};
        d.nodes.AddRange(new[]{new RouteNode{id="A",x=20,y=56},foot,new RouteNode{id="T",x=52,y=26}});
        var walk=new RouteEdge{id="walk",from="A",to=foot.id,both=true};
        var climb=new RouteEdge{id="climb",from=foot.id,to="T",kind="climb",both=true};
        d.edges.AddRange(new[]{walk,climb});
        Call(studio,"RefreshGraph");studio.SelectBrush("select");graph.SelectedEdge=walk.id;
        Call(graph,"LayoutMap");
        var origin=Point.Round((PointF)Call(graph,"ScreenPoint",foot.x,foot.y));
        var end=Point.Round((PointF)Call(graph,"ScreenPoint",44,56));
        MouseGraph(graph,"OnMouseDown",origin);MouseGraph(graph,"OnMouseMove",end);
        Need(Point.Round(((PointF[])Call(graph,"EdgePoints",climb))[0])==origin,"Walking preview pulled the rope with it");
        MouseGraph(graph,"OnMouseUp",end);
        Need(climb.from==foot.id&&foot.x==52&&d.nodes.Single(n=>n.id==walk.to).x==44&&walk.to!=foot.id,
            "Moving the walking endpoint moved the rope or failed to detach");
        Call(graph,"LayoutMap");origin=Point.Round((PointF)Call(graph,"ScreenPoint",foot.x,foot.y));
        Call(graph,"OnMouseUp",new MouseEventArgs(MouseButtons.Right,1,origin.X,origin.Y,0));
        var menu=(ContextMenuStrip)Get(graph,"nodeMenu");
        Need(menu.Visible&&menu.Items[0].Text=="删除节点","Node contextual delete was unavailable");
        menu.Items[0].PerformClick();menu.Close();
        Need(d.nodes.All(n=>n.id!=foot.id)&&d.edges.Count==1&&d.edges[0].id==walk.id,"Deleting a node removed the unrelated walking edge or kept the rope");
        studio.Undo();Need(d.nodes.Any(n=>n.id==foot.id)&&d.edges.Count==2,"Undo did not restore the node and attached route");
        studio.Undo();Need(d.edges.Single(e=>e.id==walk.id).to==foot.id,"Undo did not reconnect the original endpoint");
    }
    static void CheckSplitWalkAndJump(RouteStudio studio,RouteCanvas graph){
        Call(studio,"New");var d=graph.Document;
        d.nodes.AddRange(new[]{new RouteNode{id="A",x=15,y=57},
            new RouteNode{id="B",x=75,y=57},new RouteNode{id="T",x=75,y=32}});
        d.edges.Add(new RouteEdge{id="walk",from="A",to="B",both=true,duration=1400,
            points=new List<RoutePoint>{new RoutePoint(15,57),new RoutePoint(40,52),new RoutePoint(75,57)}});
        d.edges.Add(new RouteEdge{id="rope",from="B",to="T",kind="climb",both=true});
        d.samples.Add(new RouteSample{x=15,y=57});d.events.Add(new RouteKeyEvent{id=1,vk=39,down=true});
        Call(studio,"RefreshGraph");studio.SelectBrush("select");
        var menu=(ContextMenuStrip)Get(graph,"edgeMenu");
        Action<int,int> open=(x,y)=>{
            Call(graph,"LayoutMap");var click=Point.Round((PointF)Call(graph,"ScreenPoint",x,y));
            Call(graph,"OnMouseUp",new MouseEventArgs(MouseButtons.Right,1,click.X,click.Y,0));
            Need(menu.Visible&&menu.Items[0].Text=="从这里拆分","Walking context menu could not open");
        };
        open(40,52);menu.Items[0].PerformClick();menu.Close();
        var bend=d.nodes.Single(n=>n.x==40&&n.y==52);
        Need(d.edges.Count==3&&d.edges[0].to==bend.id&&d.edges[1].from==bend.id&&
            d.edges[0].duration+d.edges[1].duration==1400&&d.edges[1].both,"Split changed the bend, walk direction, or duration");
        open(61,55);menu.Items[0].PerformClick();menu.Close();
        var split=d.nodes.Single(n=>n.x==61&&n.y==55);
        open(51,54);
        var jumpMenu=(ToolStripMenuItem)menu.Items[1];
        Need(jumpMenu.Text=="改为跳跃"&&jumpMenu.DropDownItems[1].Text=="向左跳","Jump direction was unclear");
        jumpMenu.DropDownItems[1].PerformClick();menu.Close();
        var jump=d.edges.Single(e=>e.kind=="jump");
        Need(jump.from==split.id&&jump.to==bend.id&&!jump.both&&jump.points.Count==2,"Wrong jump direction or geometry");
        Need(d.edges.Count(e=>e.kind=="walk")==2&&d.edges.Single(e=>e.kind=="climb").id=="rope"&&
            d.samples.Count==1&&d.events.Count==1,"Editing the fold changed other actions or the recording");
        studio.Undo();Need(d.edges.All(e=>e.kind!="jump"),"Undo did not restore the walk");
        studio.Redo();Need(d.edges.Any(e=>e.kind=="jump"),"Redo did not restore the jump");
        Call(studio,"Save");Need(studio.RuntimeRoute().edges.Any(e=>e.kind=="jump"&&e.from==split.id),"Edited jump was not saved");
    }
    static void CheckReconnect(RouteStudio studio,RouteCanvas graph){
        Call(studio,"New");
        var document=graph.Document;
        var start=new RouteNode{id="A",x=40,y=59};
        var shared=new RouteNode{id="B",x=41,y=59};
        var landing=new RouteNode{id="C",x=41,y=47};
        document.nodes.AddRange(new[]{start,shared,landing});
        document.edges.AddRange(new[]{new RouteEdge{id="walk",from="A",to="B",kind="walk",both=true},
            new RouteEdge{id="jump",from="B",to="C",kind="jump"}});
        Need(studio.SelectBrush("erase"),"Eraser unavailable");
        Call(graph,"LayoutMap");
        foreach(var edge in document.edges.ToArray()){
            var points=(PointF[])Call(graph,"EdgePoints",edge);
            var point=points.Length==2?new PointF((points[0].X+points[1].X)/2,points[0].Y):points[points.Length/2];
            MouseGraph(graph,"OnMouseDown",Point.Round(point));
        }
        Need(document.nodes.Count==3&&document.edges.Count==0&&studio.RuntimeRoute().nodes.Count==0,
            "Erasing lines either removed the nodes or left them in navigation");
        Need(studio.SelectBrush("walk"),"Walk tool unavailable");
        foreach(var node in new[]{start,shared}){
            var point=Point.Round((PointF)Call(graph,"ScreenPoint",node.x,node.y));
            MouseGraph(graph,"OnMouseDown",point);MouseGraph(graph,"OnMouseUp",point);
        }
        Need(document.edges.Count==1&&document.edges[0].from=="A"&&document.edges[0].to=="B",
            "Two clicks did not reconnect the one-pixel gap to the original nodes");
        Need(studio.SelectBrush("jump"),"Jump tool unavailable");
        PaintGraph(graph,new Point(shared.x,shared.y),new Point(landing.x,landing.y));
        Need(document.edges.Count==2&&document.edges[1].from=="B"&&document.edges[1].to=="C"&&document.edges[1].kind=="jump",
            "A jump drag did not reuse the preserved endpoints");
        studio.Undo();Need(document.nodes.Count==3&&document.edges.Count==1,"Undo lost the reusable endpoint");
        studio.Redo();Call(studio,"Save");
        Need(studio.HasRuntimeRoute&&RouteGraph.Plan(studio.RuntimeRoute(),"A","C").Count==2,"Reconnected route did not save as a connected path");
        var choices=(ComboBox)Get(studio,"routeChoice");string name=document.name;
        choices.SelectedIndex=0;Choose(choices,name);
        Need(graph.Document.nodes.Count==3&&RouteGraph.Plan(graph.Document,"A","C").Count==2,"Reopening lost the connections");
        graph.Document.nodes.Add(new RouteNode{id="isolated",x=41,y=60});
        Need(studio.SelectBrush("erase"),"Eraser unavailable for the loose point");
        Call(graph,"LayoutMap");
        var isolated=Point.Round((PointF)Call(graph,"ScreenPoint",41,60));
        MouseGraph(graph,"OnMouseDown",isolated);
        Need(graph.Document.nodes.All(n=>n.id!="isolated")&&graph.Document.edges.Count==2,
            "Point erasure left the loose point or hit the nearby line");
        studio.Undo();Need(graph.Document.nodes.Any(n=>n.id=="isolated"),"Undo did not restore the erased point");
        studio.Redo();Call(studio,"Save");
        choices.SelectedIndex=0;Choose(choices,name);
        Need(graph.Document.nodes.Count==3&&RouteGraph.Plan(graph.Document,"A","C").Count==2,
            "Saving the point deletion did not preserve the connected path");
    }
    static void CheckFitWalkLine(RouteStudio studio,RouteCanvas graph){
        Call(studio,"New");
        var document=graph.Document;
        document.nodes.AddRange(new[]{new RouteNode{id="A",x=16,y=53},new RouteNode{id="B",x=70,y=45},new RouteNode{id="C",x=77,y=26}});
        document.edges.AddRange(new[]{new RouteEdge{id="walk",kind="walk",from="A",to="B",both=true,duration=2200,
            points=new List<RoutePoint>{new RoutePoint(16,53),new RoutePoint(41,40),new RoutePoint(70,45)}},
            new RouteEdge{id="jump",kind="jump",from="B",to="C",duration=420}});
        document.samples.Add(new RouteSample{x=16,y=53,keys=2});
        document.events.Add(new RouteKeyEvent{id=1,vk=39,down=true});
        Call(studio,"RefreshGraph");
        Need(studio.SelectBrush("select"),"Select tool unavailable for fitting");
        var fit=(Button)Get(studio,"fitWalkLine");
        Need(!fit.Enabled,"Straightening was enabled without a selected walk");
        Call(graph,"LayoutMap");
        var arc=(PointF[])Call(graph,"EdgePoints",document.edges[1]);
        var motion=Point.Round(arc[arc.Length/2]);
        MouseGraph(graph,"OnMouseDown",motion);MouseGraph(graph,"OnMouseUp",motion);
        Need(graph.SelectedEdge=="jump"&&!fit.Enabled,"Straightening was offered for an airborne action");
        var bend=Point.Round((PointF)Call(graph,"ScreenPoint",41,40));
        MouseGraph(graph,"OnMouseDown",bend);MouseGraph(graph,"OnMouseUp",bend);
        Need(graph.SelectedEdge=="walk"&&fit.Enabled,"Selecting the bent walking line did not enable the fit button");
        fit.PerformClick();
        var edge=document.edges[0];
        Need(edge.points.Count==2&&edge.points[0].x==16&&edge.points[0].y==53&&edge.points[1].x==70&&edge.points[1].y==45,
            "Fitting did not produce the diagonal between the existing nodes");
        Need(edge.both&&edge.duration==2200&&document.nodes.Count==3&&document.edges[1].from=="B"&&
            document.samples.Single().x==16&&document.events.Single().vk==39,"Fitting changed the connected action or recorded inputs");
        Need(!fit.Enabled,"The straightened line still offered the same edit");
        studio.Undo();Need(document.edges[0].points.Count==3&&fit.Enabled,"Undo did not restore the bent walking line");
        studio.Redo();Call(studio,"Save");
        var choices=(ComboBox)Get(studio,"routeChoice");string name=document.name;
        choices.SelectedIndex=0;Choose(choices,name);
        Need(studio.HasRuntimeRoute&&graph.Document.edges[0].points.Count==2&&RouteGraph.Plan(studio.RuntimeRoute(),"A","C").Count==2,
            "Fitting was lost after saving and reopening the connected route");
    }
    static IEnumerable<Control> Children(Control parent){
        return parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
    }
    static void SaveVisual(Control control,string path){
        using(var image=new Bitmap(control.Width,control.Height)){
            control.DrawToBitmap(image,new Rectangle(Point.Empty,control.Size));
            image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    static void Visual(string output,string fixture){
        var result=Wire.Map("ok",false,"game_input",false);
        try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new ControllerForm(fixture,false,false)){
                form.Show();Call(form,"OpenRoutes");
                var studio=(RouteStudio)Get(form,"routes");var graph=(RouteCanvas)Get(studio,"canvas");
                var panel=(Control)Get(form,"routeSurface");var detail=(Label)Get(studio,"detail");
                Application.DoEvents();
                Need(studio.HasRuntimeRoute&&panel.Visible&&graph.Width>=430&&graph.Height>=350,
                    "Saved route did not open on the large canvas: "+panel.Visible+" "+graph.Size);
                var runtime=studio.RuntimeRoute();
                foreach(string id in new[]{"E5","E6"}){
                    Need(graph.Document.edges.Any(edge=>edge.id==id&&edge.kind=="walk")&&
                        runtime.edges.Any(edge=>edge.id==id&&edge.kind=="walk"),
                        "Opening the saved route or preparing its trial replaced an unmarked walk");
                }
                Call(graph,"LayoutMap");
                var walk=graph.Document.edges.Single(edge=>edge.id=="E5");
                var airborne=new RouteEdge{from=walk.from,to=walk.to,kind="jump"};
                var curve=(PointF[])Call(graph,"EdgePoints",airborne);
                var from=graph.Document.nodes.Single(node=>node.id==airborne.from);
                var to=graph.Document.nodes.Single(node=>node.id==airborne.to);
                Need(curve.First()==(PointF)Call(graph,"ScreenPoint",from.x,from.y)&&curve.Last()==(PointF)Call(graph,"ScreenPoint",to.x,to.y),
                    "The jump arc did not connect the saved takeoff and landing");
                Need(curve[curve.Length/2].Y<(curve.First().Y+curve.Last().Y)/2,"The jump connection was not arced");
                airborne.points.Add(new RoutePoint(-250,-250));
                Call(graph,"LayoutMap");
                Need(curve.SequenceEqual((PointF[])Call(graph,"EdgePoints",airborne)),"The midair samples changed the visible jump connection");

                Need(!Children(studio).OfType<DataGridView>().Any()&&!detail.Visible&&graph.SelectedEdge==null,"The route editor still displayed a node or segment grid");
                SaveVisual(form,Path.Combine(output,"02-route-page-overview.png"));
                Call(graph,"LayoutMap");
                Point? hit=null;
                foreach(var edge in graph.Document.edges){
                    var points=(PointF[])Call(graph,"EdgePoints",edge);
                    for(int i=1;i<points.Length;i++){
                        var p=Point.Round(new PointF((points[i-1].X+points[i].X)/2,(points[i-1].Y+points[i].Y)/2));
                        bool clear=graph.Document.nodes.All(n=>{
                            var pt=(PointF)Call(graph,"ScreenPoint",n.x,n.y);
                            return Math.Abs(pt.X-p.X)>13||Math.Abs(pt.Y-p.Y)>13;
                        });
                        if(clear){hit=p;break;}
                    }
                    if(hit.HasValue)break;
                }
                Need(hit.HasValue,"No available action segment to inspect");
                MouseGraph(graph,"OnMouseDown",hit.Value);
                MouseGraph(graph,"OnMouseUp",hit.Value);
                Application.DoEvents();
                Need(detail.Visible&&graph.SelectedEdge!=null&&!String.IsNullOrEmpty(detail.Text),
                    "Selecting a segment did not open its details");
                SaveVisual(form,Path.Combine(output,"03-route-page-selected.png"));
                form.Size=new Size(960,640);Application.DoEvents();
                Need(graph.Width>=400&&graph.Height>=300,"Compact layout could not keep the map readable");
                var name=(TextBox)Get(studio,"nameBox");var jump=(Label)Get(studio,"roleJumpLabel");
                Need(name.Width>=145&&name.Right<jump.Left&&jump.Right<=name.Parent.Width&&Math.Abs(name.Top-jump.Top)<=8,
                    "Route name or jump key wrapped into a cramped row");
                SaveVisual(form,Path.Combine(output,"04-route-page-compact.png"));
                Need(Children(panel).OfType<Button>().Count(button=>button.Visible)>=7,"Compact layout did not show the drawing tools");
                result["compact_canvas"]=new[]{graph.Width,graph.Height};
                result["saved_route_reopened"]=true;result["selected_segment_highlighted"]=true;result["two_endpoint_jump_arc"]=true;
                result["unmarked_saved_walk_stays_walk_in_ui_and_runtime"]=true;
                result["ok"]=true;form.Close();
            }
        }catch(Exception e){result["error"]=e.GetBaseException().Message;}
        Wire.Save(Path.Combine(output,"route-layout-visual.json"),result);
    }
    static RouteDocument JumpEditorMap(){
        var document=new RouteDocument{manualMap=true,graphEdited=true,mapId="fixture",
            window=42,sourceWidth=320,sourceHeight=200,roiWidth=117,roiHeight=100};
        using(var image=new Bitmap(document.roiWidth,document.roiHeight))
            RouteFiles.SetMap(document,image);
        var lowerLeft=RouteGraph.MarkMapPoint(document,new Point(10,50),"platform");
        var lowerRight=RouteGraph.MarkMapPoint(document,new Point(48,50),"platform");
        var upperLeft=RouteGraph.MarkMapPoint(document,new Point(15,46),"platform");
        var upperRight=RouteGraph.MarkMapPoint(document,new Point(40,46),"platform");
        var rope=RouteGraph.MarkMapPoint(document,new Point(50,47),"rope");
        var farLeft=RouteGraph.MarkMapPoint(document,new Point(80,26),"platform");
        var farRight=RouteGraph.MarkMapPoint(document,new Point(105,26),"platform");
        RouteGraph.DrawMapLink(document,"walk",new Point(10,50),new Point(48,50),lowerLeft,lowerRight);
        RouteGraph.DrawMapLink(document,"walk",new Point(15,46),new Point(40,46),upperLeft,upperRight);
        RouteGraph.DrawMapLink(document,"walk",new Point(80,26),new Point(105,26),farLeft,farRight);
        RouteGraph.DrawMapLink(document,"climb",new Point(50,47),new Point(50,29),rope,null);
        return document;
    }

    static void ClickMap(RouteCanvas canvas,int x,int y){
        Call(canvas,"LayoutMap");
        var at=Point.Round((PointF)Call(canvas,"ScreenPoint",x,y));
        MouseGraph(canvas,"OnMouseDown",at);
        MouseGraph(canvas,"OnMouseUp",at);
    }

    static void CheckJumpEditor(string root){
        using(var studio=new RouteStudio(root,()=>null,(layout,reference)=>null,(active,map)=>{})){
            var canvas=(RouteCanvas)Get(studio,"canvas");
            var manual=JumpEditorMap();
            Call(studio,"UseDocument",manual,null,true);
            canvas.Size=new Size(600,410);
            Call(studio,"ToggleTrialTarget");
            ClickMap(canvas,50,38);
            Need(studio.TrialTarget==new Point(50,38),"Workbench target picking refused the rope's interior");
            Call(studio,"ToggleTrialTarget");
            Need(studio.SelectBrush("jump"),"Could not select the upward jump tool");
            ClickMap(canvas,24,50);
            Need((Point?)Get(canvas,"pendingActionStart")==new Point(24,50)&&manual.edges.Count==4,
                "Clicking the middle of a platform did not remember the jump start");
            ClickMap(canvas,79,57);
            Need((Point?)Get(canvas,"pendingActionStart")==new Point(24,50)&&manual.edges.Count==4,
                "A bad destination discarded the jump start or mutated the platform");
            ClickMap(canvas,27,46);
            var jump=manual.edges.SingleOrDefault(e=>e.kind=="jump");
            Need(jump!=null&&manual.nodes.Any(n=>n.id==jump.from&&n.x==24&&n.y==50)&&
                manual.nodes.Any(n=>n.id==jump.to&&n.x==27&&n.y==46),
                "Two valid interior clicks did not connect the lower and upper platform");
            Need(RouteGraph.Plan(manual,manual.nodes.First(n=>n.x==10).id,manual.nodes.First(n=>n.x==40).id).Contains(jump),
                "The click-to-click jump did not split and attach its platform links");
            studio.Undo();
            Need(manual.edges.Count==4,"The click-to-click jump did not undo in one step");
            ClickMap(canvas,46,50);
            ClickMap(canvas,50,42);
            var manualGrab=manual.edges.SingleOrDefault(e=>e.kind=="rope_mount");
            Need(manualGrab!=null&&manualGrab.jumpDirection==39&&manual.nodes.Any(n=>n.id==manualGrab.to&&n.y==42),
                "The jump tool could not connect the interior of a platform to a rope");

            var suggested=JumpEditorMap();
            Call(studio,"UseDocument",suggested,null,true);
            Call(studio,"SuggestJumps");
            var proposals=suggested.edges.Where(e=>e.suggestedJump).ToArray();
            Need(proposals.Length==2&&proposals.Any(e=>e.kind=="jump")&&proposals.Any(e=>e.kind=="rope_mount"),
                "Expected one missing upper-platform jump and one missing rope entrance");
            Need(RouteGraph.SuggestMapJumps(suggested).Count==0,"Repeating smart completion duplicated links");
            Need(studio.RuntimeRoute().edges.All(e=>!e.suggestedJump)&&studio.RuntimeRoute(true).edges.Count(e=>e.suggestedJump)==2,
                "Unconfirmed proposals leaked into normal execution or were unavailable for trial");
            canvas.SelectedEdge=proposals.First(e=>e.kind=="jump").id;
            Call(studio,"ConfirmSuggestedJump");
            Need(studio.RuntimeRoute().edges.Count(e=>e.kind=="jump")==1&&
                studio.RuntimeRoute().edges.All(e=>e.kind!="rope_mount"),
                "Confirmation did not enable only the selected jump");
        }
    }

    static void CheckManualMap(){
        var map=new RouteDocument();
        var left=RouteGraph.MarkMapPoint(map,new Point(10,48),"platform");
        var right=RouteGraph.MarkMapPoint(map,new Point(68,48),"platform");
        var rope=RouteGraph.MarkMapPoint(map,new Point(52,43),"rope");
        var drop=RouteGraph.MarkMapPoint(map,new Point(60,27),"drop");
        var west=RouteGraph.MarkMapPoint(map,new Point(28,27),"platform");
        var east=RouteGraph.MarkMapPoint(map,new Point(74,27),"platform");
        Need(map.nodes.Count==6&&map.edges.Count==0&&map.samples.Count==0,"Marking inferred an edge or recorded input");
        Need(RouteGraph.DrawMapLink(map,"walk",new Point(left.x,left.y),new Point(right.x,right.y),left,right)!=null,
            "Lower endpoints did not form a platform");
        Need(RouteGraph.DrawMapLink(map,"walk",new Point(west.x,west.y),new Point(east.x,east.y),west,east)!=null,
            "Upper endpoints did not form a platform");
        var climb=RouteGraph.DrawMapLink(map,"climb",new Point(52,43),new Point(56,27),rope,null);
        Need(climb!=null&&climb.both&&map.nodes.Single(n=>n.id==climb.to).x==52,
            "Single rope endpoint could not be extended to the upper platform");
        Need(RouteGraph.Plan(map,left.id,east.id)==null,"Unconnected rope had an implicit lower entrance");
        var launch=RouteGraph.MarkMapPoint(map,new Point(48,49),"launch");
        var grip=RouteGraph.MarkMapPoint(map,new Point(53,40),"grip");
        var entry=RouteGraph.DrawMapLink(map,"rope_mount",new Point(launch.x,launch.y),
            new Point(grip.x,grip.y),launch,grip);
        Need(entry!=null&&entry.from==launch.id&&entry.to==grip.id&&entry.jumpDirection==39&&
            launch.y==48&&grip.x==52&&RouteGraph.Plan(map,left.id,east.id).Contains(entry),
            "The marked platform takeoff and rope grab did not snap and connect the two platforms");
        var fall=RouteGraph.DrawMapLink(map,"drop",new Point(drop.x,drop.y),new Point(60,48),drop,null);
        Need(fall!=null&&fall.from==drop.id&&!fall.both,"Explicit drop did not keep the recorded marked origin");
        var down=RouteGraph.DrawMapLink(map,"jump_down",new Point(33,27),new Point(41,48),null,null);
        Need(down!=null&&!down.both&&down.jumpDirection==39,"Downward jump was not separate from dropping");
        var hop=RouteGraph.DrawMapLink(map,"jump",new Point(15,48),new Point(34,27),null,null);
        Need(hop!=null&&hop.kind=="jump"&&hop.jumpDirection==39,"Platform jump lacked an explicit origin and landing");
        var snapshot=RouteFiles.Json(map);
        RouteGraph.Build(map);RouteGraph.OrganizeRecorded(map);
        Need(RouteFiles.Json(map)==snapshot&&RouteFiles.Clone(map).nodes.Any(n=>n.marker=="rope"),
            "A manual map was rebuilt from player movements or lost marker types");

        var sides=new RouteDocument{manualMap=true};
        var a=RouteGraph.MarkMapPoint(sides,new Point(20,50),"platform");
        var b=RouteGraph.MarkMapPoint(sides,new Point(40,50),"platform");
        var foot=RouteGraph.MarkMapPoint(sides,new Point(30,47),"rope");
        RouteGraph.DrawMapLink(sides,"walk",new Point(20,50),new Point(40,50),a,b);
        var shaft=RouteGraph.DrawMapLink(sides,"climb",new Point(30,47),new Point(30,30),foot,null);
        shaft.ropeEntries=new List<string>();
        var straight=RouteGraph.DrawMapLink(sides,"rope_mount",new Point(30,50),new Point(30,46),null,null);
        Need(straight!=null&&shaft.ropeEntries.Contains(straight.to),"An existing rope shaft rejected a new grab point");
        var added=RouteGraph.SuggestMapJumps(sides);
        var paired=sides.edges.Where(e=>added.Contains(e.id)).ToArray();
        Need(paired.Length==2&&paired.All(e=>e.kind=="rope_mount"&&e.suggestedJump)&&
            paired.Select(e=>e.jumpDirection).OrderBy(key=>key).SequenceEqual(new[]{37,39}),
            "The existing straight rope grab blocked the other two approach directions");
        Need(RouteGraph.SuggestMapJumps(sides).Count==0,"Smart completion duplicated a side after both were added");
        var fromLeft=paired.Single(e=>e.jumpDirection==39);
        var start=sides.nodes.Single(n=>n.id==fromLeft.from);
        var land=sides.nodes.Single(n=>n.id==fromLeft.to);
        var written=RouteGraph.DrawMapLink(sides,"jump",new Point(start.x,start.y),new Point(land.x,land.y),null,null);
        Need(written==fromLeft&&!written.suggestedJump,"Drawing an existing suggested grab did not confirm that link");
    }

    static void CheckZoomAndBoxSelection(string root){
        using(var studio=new RouteStudio(root,()=>null,(a,b)=>null,(watch,map)=>{})){
            var d=new RouteDocument{mapId=Guid.NewGuid().ToString("N"),manualMap=true,graphEdited=true,sourceWidth=120,sourceHeight=80,roiWidth=120,roiHeight=80};
            d.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=10},new RouteNode{id="B",x=30,y=10},
                new RouteNode{id="C",x=70,y=40},new RouteNode{id="D",x=90,y=40}});
            d.edges.AddRange(new[]{new RouteEdge{id="AB",from="A",to="B",both=true},new RouteEdge{id="CD",from="C",to="D",both=true}});
            Call(studio,"UseDocument",d,null,false);studio.Show();Application.DoEvents();
            var graph=(RouteCanvas)Get(studio,"canvas");graph.Fit();Call(graph,"LayoutMap");
            string before=RouteFiles.Json(graph.Document);
            var anchor=Point.Round((PointF)Call(graph,"ScreenPoint",10,10));graph.ZoomAt(1.25f,anchor);
            var after=(PointF)Call(graph,"ScreenPoint",10,10);
            Need(graph.ZoomPercent==125&&Math.Abs(after.X-anchor.X)<1&&Math.Abs(after.Y-anchor.Y)<1,"Zoom moved the anchored map point");
            Call(graph,"LayoutMap");var centerBefore=Call(graph,"WorldPoint",new PointF(graph.Width/2f,graph.Height/2f));
            studio.Size=new Size(studio.Width+180,studio.Height+80);Application.DoEvents();Call(graph,"LayoutMap");
            var centerAfter=Call(graph,"WorldPoint",new PointF(graph.Width/2f,graph.Height/2f));
            Need(centerBefore.Equals(centerAfter),"Resizing lost the focused portion of the zoomed route");
            graph.Fit();Call(graph,"LayoutMap");
            var first=Point.Round((PointF)Call(graph,"ScreenPoint",5,5));var last=Point.Round((PointF)Call(graph,"ScreenPoint",35,15));
            MouseGraph(graph,"OnMouseDown",first);MouseGraph(graph,"OnMouseMove",last);MouseGraph(graph,"OnMouseUp",last);
            Need(graph.SelectedNodeIds.OrderBy(id=>id).SequenceEqual(new[]{"A","B"})&&graph.SelectedEdgeIds.SequenceEqual(new[]{"AB"}),"Box selection missed enclosed nodes or edges");
            Need(RouteFiles.Json(graph.Document)==before,"Zooming or selecting changed the saved graph");
            studio.CancelSelection();Need(!graph.HasSelection,"Cancel did not clear the selection");
            MouseGraph(graph,"OnMouseDown",first);MouseGraph(graph,"OnMouseMove",last);MouseGraph(graph,"OnMouseUp",last);
            var message=Message.Create(graph.Handle,0,IntPtr.Zero,IntPtr.Zero);
            typeof(RouteCanvas).GetMethod("ProcessCmdKey",Private).Invoke(graph,new object[]{message,Keys.Escape});
            Need(!graph.HasSelection,"Escape left a box selection active");
            MouseGraph(graph,"OnMouseDown",first);MouseGraph(graph,"OnMouseMove",last);MouseGraph(graph,"OnMouseUp",last);
            typeof(RouteCanvas).GetMethod("ProcessCmdKey",Private).Invoke(graph,new object[]{message,Keys.Delete});
            Need(graph.Document.nodes.Count==2&&graph.Document.edges.Single().id=="CD","Batch deletion removed unrelated objects");
            studio.Undo();Need(graph.Document.nodes.Count==4&&graph.Document.edges.Count==2,"Batch deletion did not undo as one edit");
            studio.Close();
        }
    }
    [STAThread]public static void Main(string[] args){
        if(args.Length>1){Visual(args[0],args[1]);return;}
        string root=Path.Combine(args[0],"workbench-"+Guid.NewGuid().ToString("N"));
        var report=Wire.Map("ok",false,"ssh_started",false,"game_input",false);
        var server=new TcpListener(IPAddress.Loopback,0);ControllerForm form=null;
        Thread worker=null;Link link=null;
        try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            CheckParking();report["parking_uses_safe_strip_and_second_screen"]=true;
            CheckZoomAndBoxSelection(Path.Combine(root,"box-check"));report["zoom_anchor_box_selection_cancel_escape_delete_undo"]=true;
            CheckManualMap();report["manual_markers_stay_separate_from_explicit_ground_rope_and_action_links"]=true;
            CheckJumpEditor(Path.Combine(root,"jump-check"));
            report["two_click_jump_between_platform_interiors_with_retry_and_undo"]=true;
            report["suggested_jumps_can_be_tested_and_selectively_confirmed"]=true;
            var scenes=new[]{Scene(),Scene(52),Scene(54)};var pixels=scenes[0];var source=new FakeController();long sequence=0;int moved=0;
            Func<Dictionary<string,object>> capture=()=>Wire.Map("width",320,"height",200,"native_width",320,"native_height",200,
                "window",42L,"sequence",Interlocked.Increment(ref sequence),"age_ms",0.0,"bgr24",scenes[Volatile.Read(ref moved)],
                "cursor_in_game",source.CursorInside,"route_input",Wire.Map("enabled",source.Watching,"foreground",true,
                    "window",42L,"epoch",source.FrameEpoch,"down",source.WatchedDown,"events",new object[0]));
            var original=capture();
            var first=MinimapLayout.Calibrate(original,new Rectangle(10,10,96,72),new Point(50,40),Color.FromArgb(255,255,136),new RouteDocument{name="北区"});
            var second=RouteFiles.Clone(first);second.id=Guid.NewGuid().ToString("N");second.name="南区";
            var folder=Library(root,first,"平台往返","梯绳上行");
            Library(root,second,"南区巡逻");
            form=new ControllerForm(root,false,false);form.FixtureSource=capture;
            var studio=(RouteStudio)Get(form,"routes");
            var skills=(SkillsPanel)Get(form,"skillsPanel");
            var jumpRow=Children(skills).OfType<FlowLayoutPanel>().Single(row=>row.Controls.OfType<Label>().Any(l=>l.Text=="跳跃"));
            var jumpInput=jumpRow.Controls.OfType<TextBox>().Single();
            var jumpDisplay=(Label)Get(studio,"roleJumpLabel");
            var maps=(ComboBox)Get(studio,"mapChoice");var routes=(ComboBox)Get(studio,"routeChoice");
            var start=(Button)Get(studio,"start");var view=(RemoteView)Get(form,"view");
            ((TabControl)Get(form,"sideTabs")).SelectedIndex=3;
            Choose(maps,"南区");Need(routes.Items.Count==2&&routes.FindStringExact("平台往返")<0,"Routes leaked into a different map");
            Choose(maps,"北区");Need(routes.Items.Count==3&&routes.FindStringExact("南区巡逻")<0,"Map did not filter saved routes");
            Need(!studio.HasRuntimeRoute,"Selecting a map implicitly selected a battle route");
            Choose(routes,"平台往返");Need(studio.HasRuntimeRoute,"Selected saved route not available to the script");
            Call(studio,"New");Need(!studio.HasRuntimeRoute,"A route draft could be run");
            Call(studio,"ApplyCalibration",RouteFiles.Clone(first));
            Need(RouteFiles.Load(Path.Combine(folder,"map.json")).lastMiniPng==first.lastMiniPng&&
                 Directory.GetFiles(folder,"*.route.json").Length==2,"Calibrating a map changed saved routes");
            report["map_filters_routes_and_calibration_is_saved_separately"]=true;

            server.Start();
            new Thread(()=>source.Serve(server)){IsBackground=true}.Start();
            var decoder=(FrameDecoder)Get(form,"nativeFrames");
            original["encoding"]="bgr_deflate";original["raw_sha256"]=Wire.Hash(pixels);
            view.Frame=decoder.Decode(new Packet{Header=original,Data=Wire.Deflate(pixels)});
            view.SourceSize=view.Frame.Size;
            Set(form,"target",42L);Set(form,"displayedWindow",42L);
            Set(form,"running",true);Set(form,"generation",1);
            link=new Link(((IPEndPoint)server.LocalEndpoint).Port,"isolated-fixture","control");
            var workerLink=link;
            worker=new Thread(()=>Call(form,"ControlWorker",1,workerLink)){IsBackground=true};
            worker.Start();
            int phase=0,trialEventStart=0;DateTime deadline=DateTime.UtcNow.AddSeconds(22);DateTime nextFrame=DateTime.MaxValue;
            var timer=new System.Windows.Forms.Timer{Interval=35};
            timer.Tick+=delegate{
                try{
                    if(DateTime.UtcNow>=deadline)throw new IOException("Route workbench timed out at phase "+phase);
                    Set(form,"lastFrame",DateTime.UtcNow);
                    switch(phase){
                    case 0:
                        typeof(Control).GetMethod("OnKeyDown",Private).Invoke(jumpInput,new object[]{new KeyEventArgs(Keys.Space)});
                        Need((bool)Call(form,"SaveSettings")&&WorkspaceFiles.Load(root).Current.Binding("jump").key==32,"Space was not saved through the Skills entry point");
                        Call(form,"OpenRoutes");
                        Call(form,"UpdateRouteSurface");
                        Need(((Control)Get(form,"routeSurface")).Visible&&!view.Visible,"Paused routes did not default to the map");
                        studio.ShowGamePreview();
                        view.Focus();Need(view.Focused,"Preview cannot receive keyboard focus");
                        Call(form,"ArmManualControl",42L);
                        phase++;break;
                    case 1:
                        if(!(bool)Get(form,"armed"))break;
                        studio.ShowRouteMap();Call(studio,"StartRecording");
                        Need(studio.IsRecording&&view.Focused&&view.Visible,"Record button did not restore game preview and keyboard focus");
                        phase++;break;
                    case 2:
                        if(!source.Watching||(bool)Get(form,"routeKeyboardReady"))break;
                        Need(jumpDisplay.Text=="Space"&&source.WatchKeys.SequenceEqual(new[]{37,38,39,40,32}),
                            "Recording honored the old route key instead of the role's Space");
                        Press(view,"OnKeyDown",Keys.Left);
                        source.FrameEpoch=source.WatchEpoch-1;source.CursorInside=false;
                        nextFrame=DateTime.UtcNow.AddMilliseconds(580);
                        phase++;break;
                    case 3:
                        if(DateTime.UtcNow<nextFrame)break;
                        Need(!((bool)Get(form,"routeKeyboardReady"))&&((RouteEngine)Get(studio,"engine")).Document.samples.Count==0,
                            "The previous watch's frame incorrectly began a recording");
                        source.CursorInside=true;source.FrameEpoch=source.WatchEpoch;
                        nextFrame=DateTime.UtcNow.AddMilliseconds(250);
                        phase=30;break;
                    case 30:
                        if(DateTime.UtcNow<nextFrame)break;
                        Need(studio.IsRecording&&!((bool)Get(form,"routeKeyboardReady"))&&((RouteEngine)Get(studio,"engine")).Document.samples.Count==0,
                            "A frame with the cursor inside interrupted or began the recording");
                        source.CursorInside=false;
                        report["recording_waits_for_current_watch_and_cursor_outside"]=true;
                        phase=31;break;
                    case 31:
                        if(!(bool)Get(form,"routeKeyboardReady"))break;
                        Press(view,"OnKeyDown",Keys.Right);
                        Press(view,"OnKeyDown",Keys.Space);
                        var altDown=Message.Create(view.Handle,0x104,new IntPtr(18),new IntPtr(0x20380001));
                        if(!view.PreProcessMessage(ref altDown))SendMessage(view.Handle,altDown.Msg,altDown.WParam,altDown.LParam);
                        Call(form,"Mouse","mouse_move",new MouseEventArgs(MouseButtons.None,0,20,20,0));
                        phase=4;break;
                    case 4:
                        var engine=(RouteEngine)Get(studio,"engine");
                        if(!source.Events.Contains("down:39")||!engine.Document.samples.Any(s=>s.jumpVk==32))break;
                        var altUp=Message.Create(view.Handle,0x105,new IntPtr(18),new IntPtr(unchecked((int)0xc0380001)));
                        if(!view.PreProcessMessage(ref altUp))SendMessage(view.Handle,altUp.Msg,altUp.WParam,altUp.LParam);
                        Press(view,"OnKeyUp",Keys.Space);
                        Press(view,"OnKeyUp",Keys.Right);phase++;break;
                    case 5:
                        if(!source.Events.Contains("up:39"))break;
                        Need(source.Events.Contains("down:18")&&source.Events.Contains("up:18"),"Windows Alt messages did not forward both jump transitions");
                        Need(source.Events.Contains("up:32"),"The recorded Space key did not release");
                        report["recording_uses_role_space_instead_of_old_route_alt"]=true;
                        report["computer2_alt_jump_forwards_both_transitions"]=true;
                        Need(!source.Events.Contains("down:37")&&!source.Events.Any(op=>op.StartsWith("mouse")),"Input escaped before recording ready or during mouse suppression");
                        studio.PauseRecording("测试暂停");
                        report["computer2_record_button_arms_and_forwards_keyboard"]=true;phase++;break;
                    case 6:
                        if(source.Watching||source.Keys.Length>0)break;
                        Need(!studio.IsRecording&&!(bool)Get(form,"armed")&&((Control)Get(form,"routeSurface")).Visible,"Pausing recording did not release input and return to the map");
                        Call(studio,"Save");
                        Need(Directory.GetFiles(folder,"*.route.json").Length==3,"Route was not saved under selected map");
                        var selection=Wire.Load(Path.Combine(root,"routes","selection.json"));
                        Need(Wire.Str(selection,"map")==first.id&&Wire.Str(selection,"route").Length>0,"Saved selection was not persisted");
                        report["pause_releases_and_route_save_sets_selection"]=true;
                        Call(studio,"New");var graph=(RouteCanvas)Get(studio,"canvas");
                        Need(studio.SelectBrush("walk"),"Unable to select the walk brush");
                        Call(graph,"LayoutMap");
                        var sweepStart=Point.Round((PointF)Call(graph,"ScreenPoint",16,59));
                        var sweepEnd=Point.Round((PointF)Call(graph,"ScreenPoint",82,59));
                        MouseGraph(graph,"OnMouseDown",sweepStart);MouseGraph(graph,"OnMouseMove",sweepEnd);
                        MouseGraph(graph,"OnMouseMove",sweepStart);MouseGraph(graph,"OnMouseUp",sweepStart);
                        Need(graph.Document.edges.Count==1&&graph.Document.edges[0].both,"Out-and-back drawing did not produce a route segment");
                        Need(studio.SelectBrush("select"),"Unable to use the Select tool for panning");
                        var grip=new Point(100,100);
                        MouseGraph(graph,"OnMouseDown",grip,MouseButtons.Middle);MouseGraph(graph,"OnMouseMove",new Point(150,100),MouseButtons.Middle);
                        Need(((PointF)Get(graph,"pan")).X==50,"Select drag did not pan right");
                        MouseGraph(graph,"OnMouseMove",new Point(75,100),MouseButtons.Middle);MouseGraph(graph,"OnMouseUp",new Point(75,100),MouseButtons.Middle);
                        Need(((PointF)Get(graph,"pan")).X==-25&&graph.Document.edges.Count==1,"Select drag did not pan left without editing");
                        graph.Fit();report["out_and_back_paint_and_left_drag_pan"]=true;
                        Need(studio.SelectBrush("climb"),"Unable to select the ladder brush");
                        PaintGraph(graph,new Point(45,60),new Point(45,22));
                        Need(graph.Document.edges.Count==3,"Drawing a ladder did not attach it to the walk");
                        studio.Undo();Need(graph.Document.edges.Count==1,"Undo did not restore the previous strokes");
                        studio.Redo();Need(graph.Document.edges.Count==3,"Redo did not restore the ladder and junction");
                        Need(studio.SelectBrush("erase"),"Unable to select the eraser");
                        Call(graph,"LayoutMap");
                        var eraserPoint=Point.Round((PointF)Call(graph,"ScreenPoint",62,59));
                        MouseGraph(graph,"OnMouseDown",eraserPoint);
                        Need(graph.Document.edges.Count==2,"Eraser did not remove the targeted segment");
                        Call(studio,"Save");Need(studio.HasRuntimeRoute,"The drawn route did not save through the normal entry point");
                        report["canvas_draw_erase_undo_redo_and_save"]=true;
                        CheckNodeEditing(studio,graph);
                        report["selected_segment_moves_without_pulling_shared_jump_and_saves"]=true;
                        CheckDropDrawingDoesNotMovePlatform(studio,graph);
                        report["drop_drawing_keeps_platform_coordinates_preview_and_undo"]=true;
                        CheckIndependentDrop(studio,graph);
                        report["fall_is_straight_and_platform_drag_is_independent"]=true;
                        CheckIndependentWalkAndDelete(studio,graph);
                        report["walk_drag_isolated_from_rope_and_node_context_delete_undo"]=true;
                        CheckSplitWalkAndJump(studio,graph);
                        report["split_walk_and_convert_only_selected_direction_with_undo_and_save"]=true;
                        CheckReconnect(studio,graph);
                        report["erase_keeps_nodes_and_short_connections_reuse_them"]=true;
                        report["eraser_removes_isolated_nodes_with_undo"]=true;
                        CheckFitWalkLine(studio,graph);
                        report["fit_selected_walk_button_keeps_endpoints_and_undo"]=true;
                        Call(studio,"New");
                        var draft=graph.Document;
                        draft.graphEdited=true;
                        draft.nodes.AddRange(new[]{new RouteNode{id="L",x=12,y=23},new RouteNode{id="M",x=32,y=23},
                            new RouteNode{id="R",x=58,y=23},new RouteNode{id="B",x=58,y=41}});
                        draft.edges.AddRange(new[]{new RouteEdge{id="wide",from="L",to="R",both=true},
                            new RouteEdge{id="overlap",from="M",to="R",both=true},
                            new RouteEdge{id="manual",from="R",to="B",kind="drop",edited=true,downLead=23}});
                        draft.samples.Add(new RouteSample{x=12,y=23,keys=2});
                        draft.events.Add(new RouteKeyEvent{id=1,vk=39,down=true});
                        Call(studio,"RefreshLive",null,EventArgs.Empty);
                        var organize=(Button)Get(studio,"organize");
                        Need(organize.Visible&&organize.Enabled,"Organize action was unavailable");
                        organize.PerformClick();
                        Need(draft.edges.Count==2&&draft.edges.Any(edge=>edge.id=="manual"&&edge.downLead==23&&edge.from=="R")&&
                            draft.samples.Count==1&&draft.events.Count==1,"Organize did not merge the walk or preserve the manual action and raw recording");
                        studio.Undo();Need(draft.edges.Count==3,"Organize was not undoable");
                        studio.Redo();Need(draft.edges.Count==2,"Redo did not restore the organized graph");
                        Call(studio,"Save");
                        var organized=RouteFiles.Load((string)Get(studio,"filename"));
                        Need(organized.edges.Count==2&&organized.events.Count==1,"Saved route did not retain the organized graph and raw events");
                        report["organize_action_merges_and_preserves_manual_input_with_undo"]=true;
                        Call(studio,"New");Need(studio.SelectBrush("walk"),"Walk brush not available for trial");
                        PaintGraph(graph,new Point(40,30),new Point(73,30));Call(studio,"Save");
                        var trial=(Button)Get(studio,"trial");
                        trialEventStart=source.Events.Length;
                        trial.PerformClick();
                        Need(trial.Text=="停止试走","Route trial did not start: "+Get(form,"message")+", enabled="+trial.Enabled+", visible="+trial.Visible);
                        phase++;break;
                    case 7:
                        var commands=source.Events.Skip(trialEventStart).ToArray();
                        if(!commands.Contains("down:39"))break;
                        var trialCanvas=(RouteCanvas)Get(studio,"canvas");
                        if(!trialCanvas.LivePosition.HasValue)break;
                        Need(trialCanvas.Visible&&trialCanvas.LivePosition.Value==new Point(40,30),
                            "Trial did not show the current minimap position: "+trialCanvas.LivePosition);
                        Need(commands.Where(op=>op.StartsWith("down:")).All(op=>op=="down:39"),"Trial sent non-movement input");
                        Volatile.Write(ref moved,1);
                        phase=70;break;
                    case 70:
                        if(((RouteCanvas)Get(studio,"canvas")).LivePosition!=new Point(42,30))break;
                        Press(view,"OnKeyDown",Keys.F12);
                        phase=8;break;
                    case 8:
                        if(source.Keys.Length>0||!(source.Events.Skip(trialEventStart).Contains("release")))break;
                        Need(!(bool)Get(form,"routeTrial")&&!(bool)Get(form,"autoRunning"),"F12 did not stop the trial");
                        Volatile.Write(ref moved,2);
                        phase=80;break;
                    case 80:
                        if(((RouteCanvas)Get(studio,"canvas")).LivePosition!=new Point(44,30))break;
                        report["trial_map_shows_live_position"]=true;
                        report["trial_button_drives_only_navigation_and_f12_releases"]=true;
                        form.Size=new Size(960,640);form.PerformLayout();
                        var targetButton=(Button)Get(studio,"navigationPoint");
                        var toolbar=targetButton.Parent;
                        Need(targetButton.Visible&&targetButton.Enabled&&targetButton.Text=="导航点","No navigation point in the workbench toolbar");
                        Need(toolbar.Name=="WorkspaceNavigation"&&toolbar.ClientRectangle.Contains(targetButton.Bounds)&&
                            toolbar.ClientRectangle.Contains(((Button)Get(studio,"trial")).Bounds),
                            "The trial controls were not fully visible in the main top row at minimum window size");
                        using(var screenshot=new Bitmap(toolbar.Width,toolbar.Height)){
                            toolbar.DrawToBitmap(screenshot,new Rectangle(Point.Empty,toolbar.Size));
                            screenshot.Save(Path.Combine(args[0],"workbench-navigation-toolbar.png"));
                        }
                        report["trial_target_visible_in_main_toolbar_at_minimum_size"]=true;
                        targetButton.PerformClick();
                        ClickMap((RouteCanvas)Get(studio,"canvas"),40,30);
                        var targetedTrial=(Button)Get(studio,"trial");
                        Need(studio.TrialTarget==new Point(40,30)&&targetedTrial.Text=="试走到目标"&&studio.HasRuntimeRoute,
                            "Setting a target edited the route or failed to select the point");
                        trialEventStart=source.Events.Length;
                        targetedTrial.PerformClick();phase=801;break;
                    case 801:
                        if(!source.Events.Skip(trialEventStart).Contains("down:37"))break;
                        Volatile.Write(ref moved,0);phase=802;break;
                    case 802:
                        if((bool)Get(form,"routeTrial")||source.Keys.Length>0)break;
                        Need((string)Get(form,"message")=="已到达导航目标","Workbench navigation did not stop at the selected point");
                        ((Button)Get(studio,"navigationPoint")).PerformClick();
                        Need(!studio.TrialTarget.HasValue&&((Button)Get(studio,"trial")).Text=="试走路线",
                            "Clearing a target did not restore whole-route trial");
                        report["workbench_trial_uses_selected_target_and_releases_at_arrival"]=true;
                        Volatile.Write(ref moved,2);
                        phase=803;break;
                    case 803:
                        if(((RouteCanvas)Get(studio,"canvas")).LivePosition!=new Point(44,30))break;
                        Call(form,"ReturnToPreview");
                        phase=81;break;
                    case 81:
                        var mini=studio.HomeMap;
                        if(!mini.Visible||mini.Map==null)break;
                        Need(!((Button)Get(studio,"navigationPoint")).Visible,"Workbench navigation button leaked into the homepage");
                        Call(mini,"LayoutMap");
                        var click=Point.Round((PointF)Call(mini,"ScreenPoint",40,30));
                        Call(mini,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,click.X,click.Y,0));
                        Need(studio.NavigationTarget==new Point(40,30),"Clicking the home minimap did not select its platform coordinate");
                        trialEventStart=source.Events.Length;
                        phase=82;break;
                    case 82:
                        var navigate=(Button)Get(form,"navigateButton");
                        if(!navigate.Enabled)break;
                        navigate.PerformClick();phase=83;break;
                    case 83:
                        var navigation=source.Events.Skip(trialEventStart).ToArray();
                        if(!navigation.Contains("down:37"))break;
                        Need(navigation.Where(op=>op.StartsWith("down:")).All(op=>op=="down:37"),
                            "Point navigation emitted a combat key");
                        Volatile.Write(ref moved,0);phase=84;break;
                    case 84:
                        if((bool)Get(form,"routeTrial")||source.Keys.Length>0)break;
                        Need((string)Get(form,"message")=="已到达导航目标","Point navigation failed to stop at its selected goal");
                        report["home_minimap_click_navigates_and_releases_movement_at_goal"]=true;
                        using(var shot=new Bitmap(form.Width,form.Height)){
                            form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));
                            shot.Save(Path.Combine(args[0],"manual-map-home.png"));
                        }
                        Call(form,"OpenRoutes");Call(studio,"ToggleMapMarking");phase=85;break;
                    case 85:
                        var mark=(Button)Get(studio,"markRope");
                        if(studio.HomeMap.LivePosition!=new Point(40,30)||!mark.Visible)break;
                        var marked=((RouteEngine)Get(studio,"engine")).Document;
                        Need(marked.edges.Count==0,"Opening manual marking altered the occupied saved route");
                        Need(((Button)Get(studio,"markLaunch")).Visible&&((Button)Get(studio,"markGrip")).Visible,
                            "The takeoff and grab buttons were not available while marking");
                        var validate=studio.ValidateRecordingSource;
                        try{
                            studio.ValidateRecordingSource=unused=>{throw new IOException("预览已断开，请重新连接");};
                            Call(studio,"AddMapMark","rope");
                            Need(marked.nodes.Count==0&&((RouteEngine)Get(studio,"engine")).Status=="预览已断开，请重新连接",
                                "Failed preview validation was reported as a missing yellow dot");
                        }finally{studio.ValidateRecordingSource=validate;}
                        report["mark_failure_reports_preview_connection_reason"]=true;
                        Call(form,"ArmManualControl",42L);
                        phase=86;break;
                    case 86:
                        if(!(bool)Get(form,"armed"))break;
                        Need(view.Focused,"Manual steering did not hold preview focus");
                        trialEventStart=source.Events.Length;
                        Press(view,"OnKeyDown",Keys.Right);
                        phase=87;break;
                    case 87:
                        if(!source.Keys.Contains(39))break;
                        var ropeButton=(Button)Get(studio,"markRope");
                        var markingDocument=((RouteEngine)Get(studio,"engine")).Document;
                        int x=ropeButton.ClientSize.Width/2,y=ropeButton.ClientSize.Height/2;
                        var point=new IntPtr((y<<16)|x);
                        SendMessage(ropeButton.Handle,0x201,new IntPtr(1),point);
                        SendMessage(ropeButton.Handle,0x202,IntPtr.Zero,point);
                        Need(view.Focused&&(bool)Get(form,"armed")&&source.Keys.Contains(39),
                            "Clicking a map point stole keyboard focus or paused manual control");
                        Need(markingDocument.nodes.Count==1&&markingDocument.nodes[0].marker=="rope"&&
                            markingDocument.edges.Count==0&&markingDocument.samples.Count==0&&!source.Watching,
                            "Rope capture recorded more than the one requested node");
                        Need(!source.Events.Skip(trialEventStart).Contains("release"),"Marking triggered a remote key release");
                        Press(view,"OnKeyUp",Keys.Right);
                        using(var shot=new Bitmap(form.Width,form.Height)){
                            form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));
                            shot.Save(Path.Combine(args[0],"manual-map-marking.png"));
                        }
                        Call(studio,"ToggleMapMarking");
                        Need(((RouteCanvas)Get(studio,"canvas")).Visible,"Ending map recording did not open the connection canvas");
                        report["single_rope_point_button_records_without_continuous_key_watch"]=true;
                        report["clicking_map_point_keeps_manual_control_and_pressed_key"]=true;
                        report["explicit_takeoff_and_grip_controls_available"]=true;
                        report["ok"]=true;timer.Stop();Set(form,"running",false);form.Close();break;
                    }
                }catch(Exception e){report["error"]=e.GetBaseException().Message;timer.Stop();Set(form,"running",false);form.Close();}
            };
            form.Shown+=delegate{
                // The batch runner starts hidden. Keyboard-focus checks need a
                // visible native window in the isolated test desktop.
                if(!Win.IsWindowVisible(form.Handle))ShowWindow(form.Handle,5);
                timer.Start();
            };
            Application.Run(form);timer.Dispose();
        }catch(Exception e){report["error"]=e.GetBaseException().Message;}
        finally{
            if(form!=null){Set(form,"running",false);if(worker!=null)worker.Join(2000);form.Dispose();}
            if(link!=null)link.Dispose();server.Stop();Wire.Save(Path.Combine(args[0],"route-workbench-checks.json"),report);
        }
    }
}
}
