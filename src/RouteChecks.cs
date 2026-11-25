using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
namespace SshScreen {
public static class RouteChecks {
    static readonly List<object> results=new List<object>();
    static void Check(string name,Action action){try{action();results.Add(Wire.Map("case",name,"ok",true));}catch(Exception e){results.Add(Wire.Map("case",name,"ok",false,"error",e.Message));}}
    static void Assert(bool pass,string message){if(!pass)throw new InvalidDataException(message);}
    public static RouteDocument Doc(){return new RouteDocument{window=42,sourceWidth=320,sourceHeight=200,roiX=10,roiY=10,roiWidth=96,roiHeight=72};}
    public static byte[] Background(int w,int h,int seed){var rng=new Random(seed);var p=new byte[w*h*3];for(int i=0;i<p.Length;i++)p[i]=(byte)rng.Next(25,140);return p;}
    public static byte[] Crop(byte[] p,int width,int x,int y,int w,int h){var a=new byte[w*h*3];for(int n=0;n<h;n++)Buffer.BlockCopy(p,((y+n)*width+x)*3,a,n*w*3,w*3);return a;}
    public static void Dot(byte[] p,int w,int x,int y){for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++){int at=((y+a)*w+x+b)*3;p[at]=136;p[at+1]=255;p[at+2]=255;}}
    public static Dictionary<string,object> Frame(long seq,byte[] pixels,int[] keys,params Dictionary<string,object>[] events){return Wire.Map("sequence",seq,"window",42L,"native_width",320,"native_height",200,"captured_utc",DateTime.UtcNow.ToString("o"),"bgr24",pixels,"route_input",Wire.Map("enabled",true,"foreground",true,"window",42L,"epoch",1L,"down",keys,"events",events),"cursor_in_game",false);}
    static RouteSample S(long t,int x,int y,int segment=1,string kind="walk"){return new RouteSample{time=t,frame=t,x=x,y=y,segment=segment,kind=kind};}
    static RouteDocument Graph(){var d=Doc();d.nodes.AddRange(new[]{new RouteNode{id="a",label="A",x=0,y=0},new RouteNode{id="b",label="B",x=10,y=0},new RouteNode{id="c",label="C",x=10,y=10},new RouteNode{id="z",label="Z",x=80,y=0}});d.edges.Add(new RouteEdge{id="ab",from="a",to="b",kind="walk",both=true});d.edges.Add(new RouteEdge{id="bc",from="b",to="c",kind="drop"});return d;}
    static Dictionary<string,object> Event(long id,int key,bool down){return Wire.Map("id",id,"vk",key,"down",down);}
    static void Sample(RouteEngine engine,long number,int x,int y,int[] held,params Dictionary<string,object>[] events){
        var image=Background(96,72,3);Dot(image,96,x,y);engine.Process(Frame(number,image,held,events),(number-1)*100);
    }
    static Dictionary<string,object> FullFrame(long seq,int width,int height,Rectangle area,byte[] map){
        var pixels=Background(width,height,39);
        for(int y=0;y<area.Height;y++)Buffer.BlockCopy(map,y*area.Width*3,pixels,((area.Y+y)*width+area.X)*3,area.Width*3);
        var frame=Frame(seq,pixels,new[]{39});
        frame["width"]=width;frame["height"]=height;
        frame["native_width"]=width;frame["native_height"]=height;
        return frame;
    }
    static byte[] Enlarged(byte[] map,int width,int height,int newWidth,int newHeight){
        var result=new byte[newWidth*newHeight*3];
        for(int y=0;y<newHeight;y++)for(int x=0;x<newWidth;x++){
            int sx=(int)((x+0.5)*width/newWidth),sy=(int)((y+0.5)*height/newHeight);
            Buffer.BlockCopy(map,(sy*width+sx)*3,result,(y*newWidth+x)*3,3);
        }
        return result;
    }
    public static int Main(string[] args){
        Check("画线在平台中点自动衔接，与梯绳和单向动作共享连接",delegate{
            var map=Doc();
            var walk=RouteGraph.DrawStroke(map,"walk",new[]{new RoutePoint(10,60),new RoutePoint(88,60)});
            var rope=RouteGraph.DrawStroke(map,"climb",new[]{new RoutePoint(49,61),new RoutePoint(51,20)});
            Assert(walk!=null&&rope!=null&&map.edges.Count==3,"连接梯绳时没有在平台内分段");
            Assert(map.edges.Count(e=>e.kind=="walk"&&e.both)==2&&rope.both,"平台与梯绳的往返属性错误");
            var mount=RouteGraph.DrawStroke(map,"jump",new[]{new RoutePoint(32,60),new RoutePoint(51,39)});
            Assert(mount!=null&&mount.kind=="rope_mount"&&!mount.both&&mount.jumpDirection==39,"没有将向上跳绳保存为单向抓绳");
            var exit=RouteGraph.DrawStroke(map,"jump",new[]{new RoutePoint(50,32),new RoutePoint(67,60)});
            Assert(exit!=null&&exit.kind=="rope_exit"&&!exit.both,"离绳跳未连接到路线");
            var drop=RouteGraph.DrawStroke(map,"drop",new[]{new RoutePoint(80,23),new RoutePoint(83,60)});
            Assert(drop!=null&&drop.kind=="drop"&&!drop.both&&drop.points.Last().x==80,"下落未按竖直单向保存");
            Assert(RouteGraph.Plan(map,mount.from,rope.to)!=null,"绘制接点形成了公共画面却无法导航");
            Assert(RouteGraph.DrawStroke(map,"drop",new[]{new RoutePoint(10,60),new RoutePoint(10,40)})==null,"向上笔画被误保存为下落");
            var reopened=RouteFiles.Parse(RouteFiles.Json(map));
            Assert(reopened.graphEdited&&reopened.edges.Count==map.edges.Count,"重新打开丢失绘制路线");
        });
        Check("同一平台来回拖动覆盖两端",delegate{
            var d=Doc();
            var edge=RouteGraph.DrawStroke(d,"walk",new[]{new RoutePoint(42,54),new RoutePoint(12,54),
                new RoutePoint(81,55),new RoutePoint(42,54)});
            Assert(edge!=null&&edge.both&&d.edges.Count==1,"来回拖动没有生成一段双向平台");
            Assert(d.nodes.Single(n=>n.id==edge.from).x==12&&d.nodes.Single(n=>n.id==edge.to).x==81,"没有保留拖过的两端");
            Assert(RouteGraph.Plan(d,edge.to,edge.from).Count==1,"双向平台无法折返");
        });
        Check("双向平地与有向下落",delegate{var d=Graph();Assert(RouteGraph.Plan(d,"b","a").Count==1,"平地没有反向");Assert(RouteGraph.Plan(d,"a","c").Count==2,"正向不可达");Assert(RouteGraph.Plan(d,"c","a")==null,"把下落反转了");Assert(RouteGraph.Plan(d,"a","z")==null,"猜测了断开连接");Assert(RouteGraph.Check(d).Any(s=>s.Contains("孤立")),"未标记孤立点");Assert(RouteGraph.Check(d).Any(s=>s.Contains("回程")),"未标记缺回程");});
        Check("自动整理往返和分支",delegate{var d=Doc();d.samples.AddRange(new[]{S(0,10,30),S(100,30,30),S(200,50,30),S(300,30,30),S(400,10,30),S(500,30,30,2),S(600,30,10,2,"climb")});RouteGraph.Build(d);Assert(d.edges.Any(e=>e.both),"没有平地往返边");Assert(d.nodes.Count>=3,"分支节点缺失");RouteFiles.Validate(d);});
        Check("断段不跨越及动作单向",delegate{var d=Doc();d.samples.AddRange(new[]{S(0,10,30),S(100,30,30),S(200,70,50,2,"jump"),S(300,90,30,2,"jump")});RouteGraph.Build(d);Assert(d.edges.Count==2,"硬连了断段");Assert(d.edges.Single(e=>e.kind=="jump").both==false,"跳跃自动反转");Assert(RouteGraph.Plan(d,d.nodes[0].id,d.nodes.Last().id)==null,"断段可达");});
        Check("游科精确黄点取整和最小像素数",delegate{
            const int width=64,height=48;
            var pixels=Background(width,height,3);
            Action<int,int,int> paint=(x,y,blue)=>{
                int index=(y*width+x)*3;
                pixels[index]=(byte)blue;pixels[index+1]=pixels[index+2]=255;
            };
            paint(1,2,136);paint(2,2,136);paint(1,3,136);
            paint(2,3,137);
            Assert(!MiniTracker.Locate(pixels,width,height).ok,"不足四个精确像素仍输出位置");
            paint(2,3,136);
            var dot=MiniTracker.Locate(pixels,width,height);
            Assert(dot.ok&&dot.x==2&&dot.y==2,"四点质心或取整与游科不同");
            paint(13,2,136);paint(14,2,136);paint(13,3,136);paint(14,3,136);
            dot=MiniTracker.Locate(pixels,width,height);
            Assert(dot.ok&&dot.x==8&&dot.y==2,"分离的精确像素未按全部像素取中心");
        });
        Check("已有手动点色沿用精确匹配",delegate{
            const int width=96,height=72;
            var pixels=Background(width,height,3);
            for(int y=39;y<=41;y++)for(int x=29;x<=31;x++){
                int index=(y*width+x)*3;
                pixels[index]=137;pixels[index+1]=pixels[index+2]=255;
            }
            var selected=MiniTracker.Snap(pixels,width,height,new Point(30,40),18);
            Assert(selected.ok&&selected.color.B==137,"手动选择未保存实际像素颜色");
            var route=Doc();route.dotB=selected.color.B;
            var dot=new MiniTracker().Process(pixels,width,height,route);
            Assert(dot.ok&&dot.x==30&&dot.y==40,"以前手动选的颜色未能精确定位");
        });
        Check("预览粗点吸附到真实黄点并拒绝歧义",delegate{
            var d=Doc();var area=new MinimapLayout(d).CalibratedArea;
            var map=Background(96,72,3);Dot(map,96,20,30);Dot(map,96,70,30);
            var frame=FullFrame(1,320,200,area,map);
            var dot=MinimapLayout.Pick(frame,area,new Point(52,42),0.45);
            Assert(dot.ok&&dot.x==30&&dot.y==40&&dot.color.ToArgb()==Color.FromArgb(255,255,136).ToArgb(),"粗选没有找到更靠近的黄点");
            Assert(!MinimapLayout.Pick(frame,area,new Point(55,40),0.45).ok,"等距黄点被擅自选中");
            Assert(!MinimapLayout.Pick(frame,area,new Point(108,92),1).ok,"点到远处时错误吸附");
            var saved=MinimapLayout.Calibrate(frame,area,new Point(dot.x,dot.y),dot.color,d);
            Assert(saved.dotSeedX==20&&saved.dotSeedY==30&&MinimapLayout.ReferencePixels(saved).SequenceEqual(map),"标定未保存原始位置和无损底图");
        });
        Check("窗口尺寸变化后路线仍使用原坐标",delegate{
            var d=Doc();var original=new MinimapLayout(d).CalibratedArea;
            var first=Background(96,72,3);Dot(first,96,20,30);
            var frame=FullFrame(1,320,200,original,first);
            d=MinimapLayout.Calibrate(frame,original,new Point(30,40),Color.FromArgb(255,255,136),d);
            var engine=new RouteEngine(RouteFiles.Parse(RouteFiles.Json(d)));engine.Start();
            engine.Process(engine.Layout.Extract(frame,engine.Tracker.LastFrame),0);
            var next=Background(96,72,3);Dot(next,96,26,30);
            var scaledArea=new Rectangle(15,15,144,108);
            var scaled=FullFrame(2,480,300,scaledArea,Enlarged(next,96,72,144,108));
            var roi=engine.Layout.Extract(scaled,engine.Tracker.LastFrame);
            Assert(roi!=null&&Wire.IntArray(roi["minimap_roi"]).SequenceEqual(new[]{15,15,144,108}),"同比例放大后定位到错误区域");
            engine.Process(roi,100);
            Assert(engine.Recording&&engine.Document.samples.Last().x==26&&engine.Document.samples.Last().y==30,"放大造成坐标跳变或录制停止");
            var fixedMap=Background(96,72,3);Dot(fixedMap,96,32,30);
            var fixedSize=FullFrame(3,640,360,original,fixedMap);
            var fixedRoi=engine.Layout.Extract(fixedSize,engine.Tracker.LastFrame);
            Assert(fixedRoi!=null&&Wire.IntArray(fixedRoi["minimap_roi"]).SequenceEqual(new[]{10,10,96,72}),"未识别固定像素尺寸的小地图");
            engine.Process(fixedRoi,200);
            Assert(engine.Document.samples.Last().x==32&&engine.Document.roiWidth==96,"固定角落移动后改写了路线比例");
            engine.FlushMap();
            Assert(MinimapLayout.ReferencePixels(engine.Document).Length==96*72*3,"缩放录制后底图尺寸无效");
            var changed=FullFrame(4,500,320,original,Background(96,72,7));
            Assert(engine.Layout.Extract(changed,engine.Tracker.LastFrame)==null,"分辨率变化时把其他地图当成旧地图");
            changed["window"]=43L;
            Assert(engine.Layout.Extract(changed,engine.Tracker.LastFrame)==null,"把标定套用到另一个窗口");
        });
        Check("窗口缩小后小地图跟随右下角锚点",delegate{
            var d=Doc();d.roiX=210;d.roiY=116;
            var map=new byte[96*72*3];
            var colors=Background(16,12,33);
            for(int y=0;y<72;y++)for(int x=0;x<96;x++)Buffer.BlockCopy(colors,((y/6)*16+x/6)*3,map,(y*96+x)*3,3);
            var original=new MinimapLayout(d).CalibratedArea;
            var first=(byte[])map.Clone();Dot(first,96,40,30);
            var frame=FullFrame(1,320,200,original,first);
            d=MinimapLayout.Calibrate(frame,original,new Point(250,146),Color.FromArgb(255,255,136),d);
            var engine=new RouteEngine(d);engine.Start();
            var next=(byte[])map.Clone();Dot(next,96,46,36);
            var area=new Rectangle(164,86,72,54);
            var smaller=FullFrame(2,246,149,area,Enlarged(next,96,72,72,54));
            var roi=engine.Layout.Extract(smaller,engine.Tracker.LastFrame);
            Assert(roi!=null&&Wire.IntArray(roi["minimap_roi"]).SequenceEqual(new[]{164,86,72,54}),"未适配右下角缩放");
            engine.Process(roi,100);
            Assert(engine.Recording&&Math.Abs(d.samples.Last().x-46)<=1&&Math.Abs(d.samples.Last().y-36)<=1,"缩小后角色坐标误差超过一个路线像素");
        });
        Check("小地图平移配准",delegate{var world=Background(180,130,8);var d=Doc();var t=new MiniTracker();var a=Crop(world,180,0,0,96,72);Dot(a,96,35,30);Assert(t.Process(a,96,72,d).ok,"初始失败");var b=Crop(world,180,4,2,96,72);Dot(b,96,33,29);Assert(t.Process(b,96,72,d).ok,"平移失败");Assert(t.offsetX==4&&t.offsetY==2,"相机方向/位移错误："+t.offsetX+","+t.offsetY);var bad=Background(96,72,99);Dot(bad,96,25,20);Assert(!t.Process(bad,96,72,d).ok,"换图未拒绝");});
        Check("组合与帧间短按保留",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();var p=Background(96,72,3);Dot(p,96,20,30);e.Process(Frame(1,p,new[]{39,38},Wire.Map("id",1L,"vk",18,"down",true),Wire.Map("id",2L,"vk",18,"down",false)),100);Assert(d.samples.Count==1,"未写位置");Assert((d.samples[0].keys&(2|4|16))==(2|4|16),"组合/短按丢失");Assert(d.events.Count==2&&!d.events[1].down,"短按松开丢失");e.Process(Frame(1,p,new[]{39,38}),110);Assert(d.samples.Count==1,"重复帧重复录制");});
        Check("丢点不用旧坐标及恢复断段",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();var baseMap=Background(96,72,3);var p=(byte[])baseMap.Clone();Dot(p,96,20,30);e.Process(Frame(1,p,new[]{39}),100);int segment=d.samples[0].segment;e.Process(Frame(2,baseMap,new[]{39}),150);Assert(d.samples.Count==1,"丢点仍写旧点");var q=(byte[])baseMap.Clone();Dot(q,96,30,30);e.Process(Frame(3,q,new[]{39}),200);Assert(d.samples.Count==2&&d.samples[1].segment!=segment,"恢复点与旧段连接");RouteGraph.Build(d);Assert(d.edges.Count==0,"跨越丢点连接");});
        Check("持续丢点和失焦暂停",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();var p=Background(96,72,3);e.Process(Frame(1,p,new int[0]),100);e.Process(Frame(2,p,new int[0]),1500);Assert(!e.Recording,"持续丢点未暂停");e.Start();var f=Frame(3,p,new int[0]);((Dictionary<string,object>)f["route_input"])["foreground"]=false;e.Process(f,1600);Assert(!e.Recording,"失焦未暂停");});
        Check("原地停滞标记",delegate{var d=Doc();d.samples.AddRange(new[]{S(0,20,30),S(500,20,30),S(2500,20,30)});Assert(RouteGraph.Check(d).Any(s=>s.Contains("卡住")),"未检查停滞");});
        Check("保存恢复配准基准与原图",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();var p=Background(96,72,3);Dot(p,96,20,30);e.Process(Frame(1,p,new[]{39}),100);e.FlushMap();var copy=RouteFiles.Parse(RouteFiles.Json(d));var resumed=new RouteEngine(copy);resumed.Start();var q=Background(96,72,3);Dot(q,96,25,30);resumed.Process(Frame(2,q,new[]{39}),200);Assert(copy.samples.Count==2&&copy.samples[1].x==25,"恢复坐标失配");using(var b=RouteFiles.Map(copy))Assert(b.Width==96&&b.Height==72,"底图损坏");string path=Path.Combine(args[0],"checks-roundtrip.route.json");RouteFiles.Save(path,copy);RouteFiles.Save(path,copy);Assert(RouteFiles.Load(path).samples.Count==2&&File.Exists(path+".bak"),"原子保存/备份失败");});
        Check("拒绝坏引用及无依据反向",delegate{var d=Graph();d.edges[1].both=true;bool caught=false;try{RouteFiles.Validate(d);}catch(InvalidDataException){caught=true;}Assert(caught,"接受反向下落");d.edges[1].both=false;d.edges[0].to="missing";caught=false;try{RouteFiles.Validate(d);}catch(InvalidDataException){caught=true;}Assert(caught,"接受坏引用");});
            Check("原图ROI独立快照",delegate{int w=320,h=200;var bytes=Background(w,h,3);var decoder=new FrameDecoder();var packet=new Packet{Header=Wire.Map("ok",true,"width",w,"height",h,"encoding","bgr_deflate","window",42L,"sequence",1L,"raw_sha256",Wire.Hash(bytes)),Data=Wire.Deflate(bytes)};using(var image=decoder.Decode(packet,96)){}var snap=decoder.NativeRegion(new Rectangle(10,12,96,72));var expected=Crop(bytes,w,10,12,96,72);Assert(Wire.Hash((byte[])snap["bgr24"])==Wire.Hash(expected),"ROI像素失真");((byte[])snap["bgr24"])[0]^=255;Assert(Wire.Hash((byte[])decoder.NativeRegion(new Rectangle(10,12,96,72))["bgr24"])==Wire.Hash(expected),"快照修改污染原图");});
            Check("原图ROI保留鼠标坐标",delegate{
                const int width=320,height=200;
                var pixels=Background(width,height,3);
                var decoder=new FrameDecoder();
                var packet=new Packet{Header=Wire.Map("width",width,"height",height,"encoding","bgr_deflate",
                    "window",42L,"sequence",1L,"raw_sha256",Wire.Hash(pixels),"cursor_in_game",true,
                    "cursor_samples",new[]{150,100,160,100}),Data=Wire.Deflate(pixels)};
                using(var image=decoder.Decode(packet,96)){}
                Assert(!MinimapLayout.CursorMayCoverMinimap(decoder.NativeRegion(new Rectangle(10,12,96,72))),
                    "远离小地图的鼠标拦住了定位");
                Assert(MinimapLayout.CursorMayCoverMinimap(decoder.NativeRegion(new Rectangle(125,65,96,72))),
                    "裁剪后丢失了原图的鼠标遮挡位置");
            });
        Check("平台水平整理保留原始点",delegate{var d=Doc();d.samples.AddRange(new[]{S(0,10,30),S(100,20,31),S(200,30,29),S(300,40,30)});RouteGraph.Build(d);Assert(d.edges.Count==1&&d.edges[0].both,"平台未整理为双向水平段");Assert(d.nodes[0].y==d.nodes[1].y,"平台图仍然上下抖动");Assert(d.samples[1].y==31,"改写了原始点");});
        Check("梯绳与平台共享接点后仍保持竖直",delegate{
            var d=Doc();
            d.samples.AddRange(new[]{S(0,13,10),S(100,27,10),S(300,26,22,2,"climb"),
                S(450,26,18,2,"climb"),S(600,26,14,2,"climb"),S(750,26,10,2,"climb")});
            RouteGraph.Build(d);
            var rope=d.edges.Single(e=>e.kind=="climb");
            var top=d.nodes.Single(n=>n.id==rope.to);
            Assert(rope.both&&rope.points.Count==2&&rope.points.All(p=>p.x==top.x)&&
                d.edges.Any(e=>e.kind=="walk"&&e.to==top.id)&&d.samples[2].x==26,
                "共享接点把梯绳拉斜或改写了原始采样");
        });
        Check("同平台分段重复走和反向经过只生成一条线",delegate{
            var d=Doc();
            for(int i=0;i<4;i++){
                int start=i==0?10:i==1?18:i==2?50:10;
                int end=i==0?42:i==1?50:i==2?10:50;
                int y=i==3?35:30+i%2;
                for(int x=start; ;x+=Math.Sign(end-start)){
                    var point=S(d.samples.Count*60,x,y,i+1);
                    point.keys=start<end?2:1;d.samples.Add(point);
                    if(x==end)break;
                }
            }
            RouteGraph.Build(d);
            var spans=d.edges.OrderBy(e=>e.points[0].y).ToArray();
            Assert(spans.Length==2&&spans.All(e=>e.both)&&spans[0].points.Count==2&&
                spans[0].points.Min(p=>p.x)==10&&spans[0].points.Max(p=>p.x)==50&&spans[1].points[0].y==35,
                "重复录制留下多条重叠线，或合并了不同高度的平台");
            Assert(d.samples.Count==148,"合并改写了原始样本");
        });
        Check("重叠的同向路段合并时保留单向和真实下落接点",delegate{
            var d=Doc();
            d.nodes.AddRange(new[]{new RouteNode{id="a",x=10,y=20},new RouteNode{id="b",x=25,y=20},
                new RouteNode{id="c",x=16,y=21},new RouteNode{id="d",x=36,y=20},new RouteNode{id="e",x=25,y=40}});
            d.edges.AddRange(new[]{new RouteEdge{id="W1",from="a",to="b"},new RouteEdge{id="W2",from="c",to="d"},
                new RouteEdge{id="W3",from="a",to="d"},new RouteEdge{id="D",kind="drop",from="b",to="e",edited=true,duration=600}});
            var drop=d.edges.Last();
            RouteGraph.MergeRecordedPaths(d);
            Assert(d.edges.Count==3&&d.edges.Contains(drop)&&drop.from=="b"&&drop.to=="e"&&drop.duration==600,
                "去重丢失下落连接");
            RouteGraph.RefreshWalkingDirections(d);
            Assert(RouteGraph.Plan(d,"a","e").Count==2&&RouteGraph.Plan(d,"a","d").Count==2&&RouteGraph.Plan(d,"d","a")==null,
                "去重改变了通行方向或分支");
        });
        Check("手画行走只取起止两点并保留高低差",delegate{
            var d=Doc();
            var walk=RouteGraph.DrawStroke(d,"walk",new[]{new RoutePoint(14,48),new RoutePoint(27,38),new RoutePoint(43,51),new RoutePoint(60,40)});
            Assert(walk!=null&&walk.both&&walk.points.Count==2&&walk.points[0].x==14&&walk.points[0].y==48&&
                walk.points[1].x==60&&walk.points[1].y==40,"行走工具保留了笔画抖动或拉平了实际高低差");
        });
        Check("长行走去掉黄点细碎抖动并保留起伏",delegate{
            var d=Doc();
            for(int x=10;x<=82;x++){
                int y=40+Math.Max(0,Math.Min(5,Math.Min(x-31,68-x)));
                var point=S(d.samples.Count*100,x,y+(x%2));point.keys=2;d.samples.Add(point);
                if(x%5==0){var wobble=S(d.samples.Count*100,x-1,y);wobble.keys=2;d.samples.Add(wobble);}
            }
            string original=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d.samples);
            RouteGraph.Build(d);
            var edge=d.edges.Single();
            Assert(edge.kind=="walk"&&edge.both&&edge.points.Count<=10&&edge.points.Max(p=>p.y)>=45,
                "行走线仍有短段或坡度丢失");
            Assert(edge.points.Zip(edge.points.Skip(1),(a,b)=>a.x<b.x).All(x=>x),"行走轨迹仍在横向回抖");
            Assert(original==new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d.samples),"平滑改动了原始样本");
        });
        Check("真实掉头仍保留到达的最远点",delegate{
            var d=Doc();int[] trail={10,15,22,28,27,30,29,26,18,4};
            for(int i=0;i<trail.Length;i++){var point=S(i*100,trail[i],40);point.keys=i<6?2:1;d.samples.Add(point);}
            RouteGraph.Build(d);
            Assert(d.nodes.Any(n=>n.x==30)&&d.nodes.Any(n=>n.x==4)&&d.edges.All(e=>e.both),"去抖吞掉了真正的回头路");
        });
        Check("1366宽预览缩放时保留无损原帧与ROI",delegate{
            const int width=1366,height=768;
            var raw=Background(width,height,18);var decoder=new FrameDecoder();
            var header=Wire.Map("width",width,"height",height,"window",42L,"sequence",1L,"encoding","bgr_deflate","raw_sha256",Wire.Hash(raw));
            using(var preview=decoder.Decode(new Packet{Header=header,Data=Wire.Deflate(raw)},960))
                Assert(preview.Size==new Size(960,540),"非对齐宽度仍使用整幅原图做预览");
            Assert(((byte[])decoder.NativeSnapshot()["bgr24"]).SequenceEqual(raw),"预览缩放改动了完整原帧");
            var area=new Rectangle(9,11,96,72);
            Assert(((byte[])decoder.NativeRegion(area)["bgr24"]).SequenceEqual(Crop(raw,width,area.X,area.Y,area.Width,area.Height)),"预览缩放改动了地图原像素");
        });
        Check("只按左右通过高低差时保存为行走",delegate{
            var d=Doc();var engine=new RouteEngine(d);engine.Start();
            int[] heights={44,44,40,36,39,39,42,46,46};
            for(int i=0;i<heights.Length;i++)Sample(engine,i+1,24+i*3,heights[i],new[]{39});
            for(int i=heights.Length-2;i>=0;i--)Sample(engine,heights.Length*2-1-i,24+i*3,heights[i],new[]{37});
            RouteGraph.Build(d);
            Assert(d.samples.All(s=>s.kind=="walk"&&s.jumpKeys<0)&&d.edges.Count>0&&d.edges.All(e=>e.kind=="walk"),
                "只按方向键的高低差被记录成跳跃");
        });
        Check("行走中监听已配置跳跃键的快速点按",delegate{
            var d=Doc();d.jumpKey=90;var engine=new RouteEngine(d);engine.Start();
            var right=new[]{39};
            Sample(engine,1,20,50,right);
            Sample(engine,2,23,50,right,Event(1,32,true),Event(2,32,false));
            Sample(engine,3,26,50,right);
            Sample(engine,4,29,45,right,Event(3,90,true),Event(4,90,false));
            int[] heights={39,35,38,43,44,44,44,44,44};
            for(int i=0;i<heights.Length;i++)Sample(engine,i+5,32+i*3,heights[i],right);
            RouteGraph.Build(d);
            Assert(d.samples.Take(3).All(s=>s.kind=="walk"&&s.jumpKeys<0)&&
                d.samples.Count(s=>s.jumpKeys>=0)==1&&d.edges.Select(e=>e.kind).SequenceEqual(new[]{"walk","jump","walk"}),
                "没有按路线配置识别跳跃键，或丢失起跳前后的行走");
        });
        Check("改变跳跃键后续录保留旧 Alt 事件，并把下加 Space 记为下落",delegate{
            var d=Doc();
            var launch=S(100,23,42,1,"jump");launch.jumpKeys=18;
            d.samples.AddRange(new[]{S(0,20,48),launch,S(480,31,37,1,"jump")});
            d.events.AddRange(new[]{new RouteKeyEvent{id=1,time=100,vk=18,down=true},new RouteKeyEvent{id=2,time=190,vk=18}});
            d.SetJumpKey(32);
            var engine=new RouteEngine(d);engine.Start();
            int[] heights={40,40,42,50,57,60,60,60,60,60,60};
            for(int i=0;i<heights.Length;i++){
                var pixels=Background(96,72,3);Dot(pixels,96,50,heights[i]);
                var keys=i==1?new[]{40}:i==2?new[]{40,32}:i==3?new[]{40}:new int[0];
                var events=i==1?new[]{Event(1,40,true)}:i==2?new[]{Event(2,32,true)}:
                    i==3?new[]{Event(3,32,false)}:i==4?new[]{Event(4,40,false)}:new Dictionary<string,object>[0];
                engine.Process(Frame(i+1,pixels,keys,events),1200+i*100);
            }
            RouteGraph.Build(d);
            var saved=RouteFiles.Parse(RouteFiles.Json(d));
            Assert(saved.samples.Where(s=>s.jumpKeys>=0).Select(s=>s.jumpVk).SequenceEqual(new[]{18,32}),"续录没有区分两次使用的跳跃键");
            Assert(saved.edges.Single(e=>e.kind=="jump").jumpHold==90&&
                saved.edges.Single(e=>e.kind=="drop").jumpHold==100,"更换绑定后旧跳或新下落的节奏丢失");
        });
        Check("梯绳连续上下与横移离梯衔接",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();int[,] pose={{30,60},{31,56},{30,52},{30,48},{30,44},{30,40},{34,40},{40,40}};for(int i=0;i<8;i++){var p=Background(96,72,3);Dot(p,96,pose[i,0],pose[i,1]);e.Process(Frame(i+1,p,i==0?new int[0]:i<6?new[]{38,39}:new[]{39}),i*100);}RouteGraph.Build(d);var ladder=d.edges.Single(a=>a.kind=="climb");Assert(ladder.both,"梯绳未保存上下通行");var lower=d.nodes.First(n=>n.id==ladder.from);var z=d.nodes.First(n=>n.id==ladder.to);Assert(lower.x==z.x,"梯绳轴线不直");Assert(d.edges.Any(edge=>edge.kind=="walk"&&(edge.from==z.id||edge.to==z.id)),"离梯后平台未衔接");});
        Check("短暂上浮和坠落不生成梯绳，持续按键的上下爬保留",delegate{
            var d=Doc();
            int[,] trace={
                {0,20,40,6,0},{100,21,38,6,1},{178,22,36,6,1},{255,23,38,6,1},{340,25,40,2,0},
                {460,28,40,10,0},{600,30,40,8,0},{671,30,42,8,1},{740,30,45,8,1},{809,30,49,0,1},
                {880,30,53,0,1},{950,30,56,0,1},{1200,30,56,2,1},{1350,35,56,6,0},{1450,40,56,4,0},
                {1530,40,55,4,1},{1670,40,54,4,1},{1790,41,53,4,1},{1920,40,51,4,1},{2100,40,50,4,1},
                {2230,40,50,8,1},{2350,40,51,8,1},{2480,40,52,8,1},{2610,40,54,8,1},{2750,40,56,8,1},
                {3000,40,57,0,1},{3200,45,57,2,0},{3500,50,57,2,0}
            };
            for(int i=0;i<trace.GetLength(0);i++)
                d.samples.Add(new RouteSample{time=trace[i,0],x=trace[i,1],y=trace[i,2],keys=trace[i,3],kind=trace[i,4]==1?"climb":"walk"});
            d.events.Add(new RouteKeyEvent{time=0,vk=38,down=true});
            string source=RouteFiles.Json(d);
            RouteGraph.Build(d);
            var ladder=d.edges.Single(e=>e.kind=="climb"&&e.both);
            var nodes=d.nodes.ToDictionary(n=>n.id);
            var mount=d.edges.Single(e=>e.kind=="rope_mount");
            Assert(nodes[ladder.from].x==40&&nodes[mount.from].y==56&&ladder.ropeEntries.Contains(mount.to)&&nodes[ladder.to].y==50&&
                RouteGraph.Plan(d,ladder.to,mount.from).All(e=>e.kind=="climb"),"没保留同一条真实梯绳的往返及抓绳入口");
            Assert(d.edges.All(e=>(e.kind=="walk"||e.kind=="climb"||e==mount)&&
                !(e.points.Any(p=>p.y<=40)&&e.points.Any(p=>p.y>=56))),"没有按键的快速坠落被建成可执行路段");
            var rebuilt=RouteFiles.Parse(source);
            Assert(d.samples.Select(p=>p.kind).SequenceEqual(rebuilt.samples.Select(p=>p.kind))&&d.events.Count==rebuilt.events.Count,"原始录制被更改");
        });
        Check("下加跳平台下落及真实节奏",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();long[] ts={0,100,280,380,420,500,580,660,740,820,900};int[] ys={40,40,42,50,58,64,64,64,64,64,64};for(int i=0;i<ts.Length;i++){var p=Background(96,72,3);Dot(p,96,20,ys[i]);Dictionary<string,object>[] events=i==1?new[]{Wire.Map("id",1L,"vk",40,"down",true)}:i==2?new[]{Wire.Map("id",2L,"vk",18,"down",true)}:i==3?new[]{Wire.Map("id",3L,"vk",18,"down",false)}:i==4?new[]{Wire.Map("id",4L,"vk",40,"down",false)}:new Dictionary<string,object>[0];e.Process(Frame(i+1,p,i==1?new[]{40}:i==2?new[]{40,18}:i==3?new[]{40}:new int[0],events),ts[i]);}RouteGraph.Build(d);var drop=d.edges.Single(a=>a.kind=="drop");Assert(!drop.both,"下落被倒放");Assert(drop.downLead==180&&drop.jumpHold==100&&drop.downTail==40,"下落节奏错误："+drop.downLead+","+drop.jumpHold+","+drop.downTail);Assert(d.nodes.First(n=>n.id==drop.to).y==64,"没等到实际落点");RouteFiles.Save(Path.Combine(args[0],"checks-drop.route.json"),d);});
        Check("普通下跳台阶与下加跳不混淆",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();int[] ys={40,36,32,34,45,48,48,48,48,48,48};for(int i=0;i<ys.Length;i++){var p=Background(96,72,3);Dot(p,96,20+i,ys[i]);var events=i==1?new[]{Wire.Map("id",1L,"vk",18,"down",true)}:i==2?new[]{Wire.Map("id",2L,"vk",18,"down",false)}:new Dictionary<string,object>[0];e.Process(Frame(i+1,p,i==1?new[]{18}:new int[0],events),i*100);}RouteGraph.Build(d);Assert(d.edges.Any(a=>a.kind=="jump_down")&&!d.edges.Any(a=>a.kind=="drop"),"普通台阶下跳误判为下加跳");});
        Check("台阶上跳不在最高点提前结束",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();int[] ys={52,48,40,36,34,34,36,38,38,38,38,38,38};for(int i=0;i<ys.Length;i++){var p=Background(96,72,3);Dot(p,96,20+i*2,ys[i]);var events=i==1?new[]{Wire.Map("id",1L,"vk",18,"down",true)}:i==2?new[]{Wire.Map("id",2L,"vk",18,"down",false)}:new Dictionary<string,object>[0];e.Process(Frame(i+1,p,i==1?new[]{18}:new int[0],events),i*100);if(i==5||i==9)Assert(d.samples.Last().kind=="jump","未稳定落点即退出跳跃段");}RouteGraph.Build(d);var hop=d.edges.Single(a=>a.kind=="jump");Assert(d.nodes.First(n=>n.id==hop.to).y==38&&hop.jumpHold==100,"上跳落点或按键节奏不对");});
        Check("同帧先松下再跳不是平台下落",delegate{var d=Doc();var e=new RouteEngine(d);e.Start();var p=Background(96,72,3);Dot(p,96,20,40);e.Process(Frame(1,p,new int[0]),0);var q=Background(96,72,3);Dot(q,96,22,36);e.Process(Frame(2,q,new int[0],Wire.Map("id",1L,"vk",40,"down",true),Wire.Map("id",2L,"vk",40,"down",false),Wire.Map("id",3L,"vk",18,"down",true),Wire.Map("id",4L,"vk",18,"down",false)),100);Assert(d.samples.Last().kind=="jump","把先后按键误当同时组合");});
        Check("跳跃后持续按上并沿绳上升生成独立抓绳连接",delegate{
            var d=Doc();d.jumpKey=32;var e=new RouteEngine(d);e.Start();
            Sample(e,1,20,52,new[]{39});
            Sample(e,2,24,52,new[]{38,39},Event(1,38,true));
            Sample(e,3,28,48,new[]{38,39,32},Event(2,32,true));
            Sample(e,4,35,41,new[]{38,39},Event(3,32,false));
            Sample(e,5,42,38,new[]{38},Event(4,39,false));
            for(int i=0;i<7;i++)Sample(e,6+i,42,38-i*2,new[]{38});
            RouteGraph.Build(d);
            var mount=d.edges.Single(x=>x.kind=="rope_mount");
            var climb=d.edges.Single(x=>x.kind=="climb");
            Assert(!mount.both&&mount.to==climb.from,"抓绳和上爬未共用节点");
            Assert(mount.upLead==100&&mount.jumpHold==100&&mount.jumpDirection==39,"抓绳按键顺序或方向丢失");
            var saved=RouteFiles.Parse(RouteFiles.Json(d));
            Assert(saved.edges.Single(x=>x.kind=="rope_mount").upLead==100,"保存丢失抓绳时序");
        });
        Check("按上普通跳回原平台不误判抓绳",delegate{
            var d=Doc();var e=new RouteEngine(d);e.Start();
            Sample(e,1,30,50,new[]{38});
            Sample(e,2,30,46,new[]{38,18},Event(1,18,true));
            Sample(e,3,30,40,new[]{38},Event(2,18,false));
            int[] ys={36,34,37,41,46,50,50,50,50,50};
            for(int i=0;i<ys.Length;i++)Sample(e,4+i,30,ys[i],new[]{38});
            RouteGraph.Build(d);
            Assert(!d.edges.Any(x=>x.kind=="rope_mount"),"普通跳回原处误判抓绳");
        });
        Check("从绳中段只按跳生成离绳边",delegate{
            var d=Doc();var e=new RouteEngine(d);e.Start();
            Sample(e,1,34,49,new[]{38});
            Sample(e,2,34,45,new[]{38});
            Sample(e,3,34,40,new[]{38});
            Sample(e,4,34,37,new[]{18},Event(1,18,true));
            Sample(e,5,34,43,new int[0],Event(2,18,false));
            for(int i=0;i<6;i++)Sample(e,6+i,34,55,new int[0]);
            RouteGraph.Build(d);
            var exit=d.edges.Single(x=>x.kind=="rope_exit");
            Assert(!exit.both&&exit.jumpDirection==0&&exit.downLead<0,"给离绳跳错误加了方向");
            Assert(d.nodes.Single(n=>n.id==exit.from).y==40&&d.nodes.Single(n=>n.id==exit.to).y==55,"未保存实际离绳高度和落点");
        });
        Check("走向梯绳前按下的路径距离保留",delegate{
            var d=Doc();var e=new RouteEngine(d);e.Start();
            Sample(e,1,20,30,new[]{39});
            Sample(e,2,24,30,new[]{39,40},Event(1,40,true));
            Sample(e,3,28,30,new[]{39,40});
            Sample(e,4,34,30,new[]{39,40});
            Sample(e,5,35,32,new[]{40});
            Sample(e,6,35,36,new[]{40});
            Sample(e,7,35,40,new[]{40});
            Sample(e,8,35,45,new[]{40});
            RouteGraph.Build(d);
            var ladder=d.edges.Single(x=>x.kind=="climb");
            Assert(ladder.downApproach>=10&&ladder.both,"未保存提前按下的横向距离");
        });
        Check("同一帧方向键在跳跃后到达也记入离绳",delegate{
            var d=Doc();d.jumpKey=32;var e=new RouteEngine(d);e.Start();
            Sample(e,1,34,49,new[]{38});Sample(e,2,34,45,new[]{38});Sample(e,3,34,40,new[]{38});
            Sample(e,4,33,37,new[]{37,32},Event(1,32,true),Event(2,37,true));
            Sample(e,5,29,43,new[]{37},Event(3,32,false));
            for(int i=0;i<6;i++)Sample(e,6+i,24,55,new[]{37});
            RouteGraph.Build(d);
            var leave=d.edges.Single(edge=>edge.kind=="rope_exit");
            Assert(leave.jumpDirection==37&&leave.jumpHold==100,"同帧的左加跳没有保留在离绳动作里");
        });
        bool ok=results.Cast<Dictionary<string,object>>().All(r=>Wire.Bool(r,"ok"));Wire.Save(Path.Combine(args[0],"route-checks.json"),Wire.Map("ok",ok,"game_input",false,"cases",results));return ok?0:1;
    }
}
}
