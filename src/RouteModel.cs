using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SshScreen {
public sealed class RouteKeyEvent {public long id,time,sourceTime;public int vk;public bool down;}
public sealed class RouteSample {public long time,frame;public int x,y,segment,keys,jumpKeys=-1,jumpVk;public string kind="walk";}
public sealed class RouteNode {public string id,label,marker;public int x,y;}
public sealed class RoutePoint {public int x,y;public RoutePoint(){}public RoutePoint(int a,int b){x=a;y=b;}}
public sealed class RouteEdge {
    public string id,from,to,kind="walk";public bool both,edited,review,upContinuation,observedJump,recoveredWalk,suggestedJump;public long duration,downLead=-1,jumpHold=-1,downTail=-1,upLead=-1,steerHold=-1;
    public List<string> ropeEntries;
    public int jumpDirection=-1,downApproach,upApproach,upApproachKey;
    public long upApproachOverlap;
    public List<RoutePoint> points=new List<RoutePoint>();
}
public sealed class RouteDocument {
    public int version=1;public string id=Guid.NewGuid().ToString("N"),name="新路线",mapId="",mapPng="",sourceHost="";
    public long window;public int sourceWidth,sourceHeight,roiX,roiY,roiWidth,roiHeight;
    public int dotR=255,dotG=255,dotB=136,tolerance=28,dotSeedX=-1,dotSeedY=-1,jumpKey=18,teleportKey=0,mapX,mapY;
    public List<RouteSample> samples=new List<RouteSample>();public List<RouteKeyEvent> events=new List<RouteKeyEvent>();
    public List<RouteNode> nodes=new List<RouteNode>();public List<RouteEdge> edges=new List<RouteEdge>();
    public int gaps,lastOffsetX,lastOffsetY;public string lastMiniPng="";public bool graphEdited,manualMap;
    public static bool CanRecordJump(int key){return key==16||key==17||key==18||key==32||key>=48&&key<=57||key>=65&&key<=90;}
    public void SetJumpKey(int key){
        if(jumpKey==key)return;
        foreach(var sample in samples)
            if(sample.jumpKeys>=0&&sample.jumpVk==0)sample.jumpVk=jumpKey;
        jumpKey=key;
    }
}
public static class RouteFiles {
    public static string Json(RouteDocument d){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=64}.Serialize(d);}
    public static RouteDocument Parse(string json){if(json.Length>32*1024*1024)throw new InvalidDataException("路线文件过大");var d=new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=64}.Deserialize<RouteDocument>(json);Validate(d);return d;}
    public static RouteDocument Clone(RouteDocument d){return Parse(Json(d));}
    public static void Save(string path,RouteDocument d){Validate(d);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string tmp=path+".tmp";File.WriteAllText(tmp,Json(d),new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,path+".bak",true);else File.Move(tmp,path);}
    public static RouteDocument Load(string path){if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("路线文件过大");return Parse(File.ReadAllText(path));}
    public static void Validate(RouteDocument d){
        if(d==null||d.version!=1||d.name==null||d.name.Length>128||d.samples==null||d.events==null||d.nodes==null||d.edges==null)throw new InvalidDataException("路线格式无效");
        if(d.samples.Count>100000||d.events.Count>100000||d.nodes.Count>20000||d.edges.Count>40000)throw new InvalidDataException("路线记录过多，请分开保存");
        if(d.roiWidth<0||d.roiHeight<0||d.roiWidth>1024||d.roiHeight>800||d.sourceWidth<0||d.sourceHeight<0||d.sourceWidth>16384||d.sourceHeight>16384||d.roiX<0||d.roiY<0||d.roiX+d.roiWidth>d.sourceWidth||d.roiY+d.roiHeight>d.sourceHeight)throw new InvalidDataException("小地图区域无效");
        if(d.dotR<0||d.dotR>255||d.dotG<0||d.dotG>255||d.dotB<0||d.dotB>255||d.tolerance<1||d.tolerance>80||d.jumpKey<8||d.jumpKey>254||d.teleportKey!=0&&(d.teleportKey<8||d.teleportKey>254||d.jumpKey==d.teleportKey))throw new InvalidDataException("颜色或键位无效");
        var ids=new HashSet<string>();foreach(var n in d.nodes){if(n==null||String.IsNullOrWhiteSpace(n.id)||n.id.Length>64||!ids.Add(n.id)||!Coordinate(n.x,n.y))throw new InvalidDataException("节点无效");}
        var edgeIds=new HashSet<string>();foreach(var e in d.edges){if(e==null||String.IsNullOrWhiteSpace(e.id)||!edgeIds.Add(e.id)||!ids.Contains(e.from??"")||!ids.Contains(e.to??"")||e.from==e.to||!Kinds.Contains(e.kind)||e.points==null||e.points.Count>100000||e.duration<0||e.both&&e.kind!="walk"&&e.kind!="climb")throw new InvalidDataException("连接无效；跳跃和下落请分别保留方向");foreach(var p in e.points)if(p==null||!Coordinate(p.x,p.y))throw new InvalidDataException("连接坐标无效");}
        foreach(var e in d.edges)if(e.ropeEntries!=null&&(e.kind!="climb"||e.ropeEntries.Any(id=>!ids.Contains(id)||id==e.to)))
            throw new InvalidDataException("梯绳抓点无效");
        long time=-1;foreach(var s in d.samples){if(s==null||s.time<time||!Coordinate(s.x,s.y)||s.segment<0||!Kinds.Contains(s.kind))throw new InvalidDataException("采样顺序无效");time=s.time;}
        if(d.mapPng!=null&&d.mapPng.Length>24*1024*1024)throw new InvalidDataException("底图过大");
    }
    static bool Coordinate(int x,int y){return Math.Abs((long)x)<=32768&&Math.Abs((long)y)<=32768;}
    public static readonly string[] Kinds={"walk","walk_off","climb","jump","jump_down","drop","rope_mount","rope_exit","teleport","wait"};
    public static readonly string[] MovementKinds={"walk","walk_off","climb","jump","jump_down","drop","rope_mount","rope_exit"};
    public static Bitmap Map(RouteDocument d){return Png(d.mapPng);}
    public static Bitmap Png(string png){if(String.IsNullOrEmpty(png))return null;byte[] bytes=Convert.FromBase64String(png);if(bytes.Length<24||BitConverter.ToUInt32(bytes,0)!=0x474e5089u)throw new InvalidDataException("底图必须是PNG");int w=(bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19],h=(bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];if(w<1||h<1||w>4096||h>4096||((long)w*h)>8*1024*1024)throw new InvalidDataException("底图尺寸过大");using(var ms=new MemoryStream(bytes))using(var img=Image.FromStream(ms))return new Bitmap(img);}
    public static string EncodePng(Bitmap bitmap){using(var ms=new MemoryStream()){bitmap.Save(ms,ImageFormat.Png);return Convert.ToBase64String(ms.ToArray());}}
    public static void SetMap(RouteDocument d,Bitmap bitmap){if(bitmap!=null)d.mapPng=EncodePng(bitmap);}
}
public static class RouteGraph {
    public static string KindName(string k){switch(k){case "walk":return "左右行走";case "walk_off":return "走下台阶";case "climb":return "梯绳上下";case "jump":return "台阶上跳";case "jump_down":return "台阶下跳";case "drop":return "平台下落";case "rope_mount":return "跳跃抓绳";case "rope_exit":return "离绳跳跃";case "teleport":return "传送";default:return "停留";}}
    public static double Distance(int x,int y,int a,int b){double dx=x-a,dy=y-b;return Math.Sqrt(dx*dx+dy*dy);}
    public static void Build(RouteDocument d){
        if(d.manualMap)return;
        d.nodes.Clear();d.edges.Clear();d.graphEdited=false;int at=0;
        var gaps=new bool[d.samples.Count];var climbStarts=new bool[d.samples.Count];
        var recordedJunctions=new HashSet<string>();
        var samples=VerifyClimbs(d,gaps,climbStarts);
        while(at<samples.Count){
            if(gaps[at]){at++;continue;}
            int end=at;bool hasDirection=WalkDirection(samples[at])!=0;
            while(end+1<samples.Count&&!gaps[end+1]&&samples[end+1].segment==samples[at].segment){end++;hasDirection|=WalkDirection(samples[end])!=0;}
            int begin=at,farthest=at,heading=0;bool reversing=false;
            string active=samples[at].kind;RouteNode previous=null;
            for(int i=at+1;i<=end;i++){
                var point=samples[i];
                if(point.kind!=active||active=="climb"&&climbStarts[i]){
                    Emit(d,samples,begin,i-1,active,ref previous);
                    begin=farthest=i-1;active=point.kind;heading=0;reversing=false;
                }
                if(active!="walk")continue;
                int dx=point.x-samples[farthest].x;
                if(heading==0){
                    if(Math.Abs(dx)>=2){heading=Math.Sign(dx);farthest=i;}
                    continue;
                }
                if(dx*heading>=0){farthest=i;reversing=false;continue;}
                int direction=WalkDirection(point);
                if(direction!=0)reversing=direction==-heading;
                if(dx*heading<=-3&&(!hasDirection||reversing)){
                    Emit(d,samples,begin,farthest,active,ref previous);
                    begin=farthest;farthest=i;heading=-heading;reversing=false;
                }
            }
            Emit(d,samples,begin,active=="walk"&&heading!=0?farthest:end,active,ref previous);
            if(end==at)recordedJunctions.Add(Node(d,samples[at].x,samples[at].y,true).id);
            at=end+1;
        }
        if(d.events.Count!=0){
            RecoverGroundSpans(d);
            MergeRecordedPaths(d,recordedJunctions);
        }
        SplitWalkJunctions(d);var pairs=new HashSet<string>();d.edges.RemoveAll(e=>(e.kind=="walk"||e.kind=="climb")&&e.both&&!pairs.Add(e.kind+"/"+(String.CompareOrdinal(e.from,e.to)<0?e.from+"/"+e.to:e.to+"/"+e.from)));
        MeasureUpApproaches(d,samples,d.events);
        OrganizeRecorded(d,recordedJunctions);
    }
    internal static void OrganizeRecorded(RouteDocument d,ISet<string> recordedJunctions=null){
        if(d.manualMap)return;
        if(d.events.Count!=0)RecoverGroundSpans(d);
        MergeRecordedPaths(d,recordedJunctions);
        StraightenRecordedClimbs(d);
        MergeRopeShafts(d);
        GeneratePlatformRopeEntries(d);
    }
    internal static IEnumerable<string> Departures(RouteEdge edge){
        if(edge.ropeEntries==null)yield return edge.from;
        else foreach(var entry in edge.ropeEntries)yield return entry;
        if(edge.both)yield return edge.to;
    }
    internal static string Arrival(RouteEdge edge,string start){
        if(edge.both&&edge.to==start)return edge.from;
        bool starts=edge.ropeEntries==null?edge.from==start:edge.ropeEntries.Contains(start);
        return starts?edge.to:null;
    }
    static RouteNode OtherEnd(RouteEdge edge,RouteNode at,IDictionary<string,RouteNode> nodes){
        return nodes[edge.from==at.id?edge.to:edge.from];
    }
    static void MakeUpright(RouteEdge edge,RouteNode foot,RouteNode top){
        edge.from=foot.id;edge.to=top.id;
        edge.points=new List<RoutePoint>{new RoutePoint(foot.x,foot.y),new RoutePoint(top.x,top.y)};
        edge.upApproach=edge.upApproachKey=0;edge.upApproachOverlap=0;
    }
    static RouteEdge RopeMount(string id,RouteNode takeoff,RouteNode grip){
        int heading=Math.Sign(grip.x-takeoff.x);
        return new RouteEdge{id=id,from=takeoff.id,to=grip.id,kind="rope_mount",duration=300,
            jumpDirection=heading<0?37:heading>0?39:0,
            points=new List<RoutePoint>{new RoutePoint(takeoff.x,takeoff.y),new RoutePoint(grip.x,grip.y)}};
    }
    static List<RoutePoint> PlatformApproach(RouteEdge walk,RouteNode joint,RouteNode far){
        if(walk.kind!="walk"||walk.edited||walk.review||walk.upContinuation||
            walk.from==joint.id&&!walk.both||far.y<joint.y||Math.Abs(far.x-joint.x)<2)return null;
        var approach=walk.points.Count>2?walk.points.Skip(1).Take(walk.points.Count-2):Enumerable.Empty<RoutePoint>();
        if(walk.from==joint.id)approach=approach.Reverse();
        return new[]{new RoutePoint(far.x,far.y)}.Concat(approach)
            .TakeWhile(p=>p.y>=joint.y&&p.x!=joint.x).ToList();
    }
    internal static void MergeRopeShafts(RouteDocument d){
        var nodes=d.nodes.ToDictionary(n=>n.id);
        foreach(var seam in d.nodes.ToArray()){
            var incident=d.edges.Where(e=>e.from==seam.id||e.to==seam.id).ToArray();
            var sections=incident.Where(e=>e.kind=="climb").ToArray();
            if(sections.Length!=2||sections.Any(e=>e.edited||e.review))continue;
            var lower=sections.FirstOrDefault(e=>OtherEnd(e,seam,nodes).y>seam.y);
            var upper=sections.FirstOrDefault(e=>OtherEnd(e,seam,nodes).y<seam.y);
            if(lower==null||upper==null||!upper.both)continue;
            var foot=OtherEnd(lower,seam,nodes);var top=OtherEnd(upper,seam,nodes);
            if(foot.x!=seam.x||top.x!=seam.x||Arrival(lower,seam.id)!=foot.id)continue;
            var mountLinks=incident.Where(e=>e.kind=="rope_mount"&&e.to==seam.id).ToArray();
            bool oldTail=lower.id.EndsWith(".entry.down",StringComparison.Ordinal)&&mountLinks.Any(e=>e.from==foot.id);
            if(!lower.both&&!oldTail)continue;
            var walks=incident.Except(sections).Except(mountLinks).ToArray();
            var approaches=walks.Select(e=>PlatformApproach(e,seam,OtherEnd(e,seam,nodes))).ToArray();
            if(approaches.Any(points=>points==null||points.Count==0))continue;

            var entries=new HashSet<string>(lower.ropeEntries??(lower.both?new List<string>{foot.id}:new List<string>()));
            if(upper.ropeEntries!=null)entries.UnionWith(upper.ropeEntries.Where(id=>id!=seam.id));
            if(mountLinks.Length!=0||walks.Length!=0)entries.Add(seam.id);
            for(int i=0;i<walks.Length;i++){
                var walk=walks[i];var shape=approaches[i];var takeoff=shape[shape.Count-1];
                var far=OtherEnd(walk,seam,nodes);
                var start=Node(d,takeoff.x,takeoff.y,false);
                nodes[start.id]=start;
                if(start.id==far.id)d.edges.Remove(walk);
                else{
                    if(walk.from==seam.id){walk.from=start.id;shape.Reverse();}else walk.to=start.id;
                    walk.points=shape;
                }
                d.edges.Add(RopeMount(walk.id+".rope",start,seam));
            }
            MakeUpright(upper,foot,top);
            upper.duration+=lower.duration;upper.ropeEntries=entries.OrderBy(id=>id).ToList();
            d.edges.Remove(lower);
            if(!entries.Contains(seam.id)&&!d.edges.Any(e=>e.from==seam.id||e.to==seam.id||
                e.ropeEntries!=null&&e.ropeEntries.Contains(seam.id)))d.nodes.Remove(seam);
        }
    }
    internal static void GeneratePlatformRopeEntries(RouteDocument d){
        var nodes=d.nodes.ToDictionary(n=>n.id);
        var incident=d.edges.SelectMany(e=>new[]{Tuple.Create(e.from,e),Tuple.Create(e.to,e)})
            .ToLookup(link=>link.Item1,link=>link.Item2);
        var ascents=d.edges.Where(e=>e.kind=="climb"&&!e.edited&&!e.review).Select(e=>{
            var from=nodes[e.from];var to=nodes[e.to];
            var foot=from.y>to.y?from:to;
            return new{Edge=e,Foot=foot,Top=foot==from?to:from};
        }).Where(e=>e.Foot.x==e.Top.x&&e.Foot.y-e.Top.y>=2&&Arrival(e.Edge,e.Foot.id)==e.Top.id)
          .GroupBy(e=>e.Foot).ToArray();
        foreach(var group in ascents){
            var foot=group.Key;
            var links=incident[foot.id];
            if(links.Any(e=>e.kind=="rope_mount"||e.upContinuation))continue;
            if(links.Any(e=>e.kind=="climb"&&OtherEnd(e,foot,nodes).y>foot.y))continue;
            bool platform=links.Where(e=>e.kind=="walk"&&e.points.All(p=>Math.Abs(p.y-foot.y)<=2))
                .Select(e=>nodes[e.from==foot.id?e.to:e.from])
                .Any(other=>Math.Abs(other.x-foot.x)>=2&&Math.Abs(other.y-foot.y)<=2);
            if(!platform)continue;

            int height=group.Min(e=>e.Foot.y-e.Top.y);
            int rise=Math.Min(3,Math.Max(1,height/3));
            var grip=Node(d,foot.x,foot.y-rise,false);
            string key=group.First().Edge.id+".entry";
            foreach(var ascent in group){
                var edge=ascent.Edge;
                MakeUpright(edge,foot,ascent.Top);
                if(edge.ropeEntries==null)edge.ropeEntries=new List<string>();
                edge.ropeEntries.Remove(foot.id);edge.ropeEntries.Add(grip.id);
            }
            d.edges.Add(RopeMount(key,foot,grip));
        }
    }
    static void RecoverGroundSpans(RouteDocument d){
        var candidates=d.edges.Where(e=>e.kind=="walk"&&!e.both&&!e.edited&&!e.review&&!e.recoveredWalk&&
            !e.upContinuation&&e.upApproachKey==0&&e.downApproach==0&&e.points.Count>=2).ToArray();
        if(candidates.Length==0)return;
        var positions=Enumerable.Range(0,d.samples.Count).Where(i=>HorizontalOnly(d.samples[i])!=0)
            .ToLookup(i=>new Point(d.samples[i].x,d.samples[i].y));
        var retired=new HashSet<RouteEdge>();
        var replacements=new List<RouteEdge>();
        var nodes=d.nodes.ToDictionary(n=>n.id);
        foreach(var edge in candidates){
            var points=edge.points;
            var start=nodes[edge.from];var end=nodes[edge.to];
            int begin=0;bool confirmedStep=false;
            for(int i=1;i<points.Count;i++){
                var a=points[i-1];var b=points[i];
                int dx=Math.Abs(a.x-b.x),dy=Math.Abs(a.y-b.y);
                if(dy<=Math.Max(2,dx))continue;
                if(dx>0&&dy<=4&&ObservedStep(d.samples,positions,a,b)&&ObservedStep(d.samples,positions,b,a)){
                    confirmedStep=true;continue;
                }
                retired.Add(edge);
                AddGroundSpan(d,edge,points,begin,i-1,start,end,confirmedStep,replacements);
                begin=i;confirmedStep=false;
            }
            if(begin>0)AddGroundSpan(d,edge,points,begin,points.Count-1,start,end,confirmedStep,replacements);
            else if(confirmedStep){edge.both=true;edge.recoveredWalk=true;}
        }
        d.edges.RemoveAll(retired.Contains);
        d.edges.AddRange(replacements);
        var used=new HashSet<string>(d.edges.SelectMany(e=>new[]{e.from,e.to}));
        var orphaned=retired.SelectMany(e=>new[]{e.from,e.to});
        var orphanIds=new HashSet<string>(orphaned.Where(id=>!used.Contains(id)));
        d.nodes.RemoveAll(n=>orphanIds.Contains(n.id));
    }
    static int HorizontalOnly(RouteSample sample){
        return sample.jumpKeys<0&&(sample.kind=="walk"||sample.kind=="climb")&&(sample.keys==1||sample.keys==2)?sample.keys:0;
    }
    static bool ObservedStep(List<RouteSample> samples,ILookup<Point,int> positions,RoutePoint from,RoutePoint to){
        int key=from.x<to.x?2:1;
        for(int y=from.y-1;y<=from.y+1;y++)
            foreach(int at in positions[new Point(from.x,y)]){
                var start=samples[at];
                if(start.keys!=key)continue;
                for(int i=at+1;i<samples.Count;i++){
                    var point=samples[i];
                    if(point.segment!=start.segment||point.time-start.time>500||HorizontalOnly(point)!=key)break;
                    if(point.x==to.x&&Math.Abs(point.y-to.y)<=1)return true;
                }
            }
        return false;
    }
    static void AddGroundSpan(RouteDocument d,RouteEdge edge,List<RoutePoint> points,int low,int high,
        RouteNode originalStart,RouteNode originalEnd,bool confirmedStep,List<RouteEdge> spans){
        var a=points[low];var b=points[high];
        int width=Math.Abs(b.x-a.x);
        if(width<2)return;
        var from=low==0?originalStart:Node(d,a.x,a.y,false);
        var to=high==points.Count-1?originalEnd:Node(d,b.x,b.y,false);
        if(from.id==to.id||from.x==to.x)return;
        var shape=points.GetRange(low,high-low+1);
        shape[0]=new RoutePoint(from.x,from.y);shape[shape.Count-1]=new RoutePoint(to.x,to.y);
        if(!confirmedStep&&!CanWalkBoth(shape))return;
        long duration=Math.Max(1,edge.duration*width/Math.Max(width,Math.Abs(originalEnd.x-originalStart.x)));
        spans.Add(new RouteEdge{id=edge.id+".ground"+low,from=from.id,to=to.id,kind="walk",both=true,
            recoveredWalk=confirmedStep,duration=duration,points=shape});
    }
    static void StraightenRecordedClimbs(RouteDocument d){
        var nodes=d.nodes.ToDictionary(n=>n.id);
        var incident=d.edges.SelectMany(e=>new[]{Tuple.Create(e.from,e),Tuple.Create(e.to,e)})
            .ToLookup(link=>link.Item1,link=>link.Item2);
        var actionNodes=new HashSet<string>(d.edges.Where(e=>e.edited||e.review||e.kind!="walk"&&e.kind!="climb")
            .SelectMany(e=>new[]{e.from,e.to}));
        var seen=new HashSet<string>();
        foreach(var seed in d.edges.Where(e=>e.kind=="climb"&&!e.edited)){
            if(!seen.Add(seed.from))continue;
            var shaft=new List<RouteNode>{nodes[seed.from]};
            for(int i=0;i<shaft.Count;i++)
                foreach(var edge in incident[shaft[i].id].Where(e=>e.kind=="climb"&&!e.edited)){
                    string other=edge.from==shaft[i].id?edge.to:edge.from;
                    if(seen.Add(other))shaft.Add(nodes[other]);
                }
            var fixedAxes=shaft.Where(n=>actionNodes.Contains(n.id)).Select(n=>n.x).Distinct().ToArray();
            if(fixedAxes.Length>1||shaft.Max(n=>n.x)-shaft.Min(n=>n.x)>2)continue;
            int axis=fixedAxes.Length==1?fixedAxes[0]:shaft.OrderBy(n=>n.y).First().x;
            var affected=shaft.SelectMany(n=>incident[n.id]).Distinct().ToArray();
            var shaftIds=new HashSet<string>(shaft.Select(n=>n.id));
            foreach(var edge in affected){
                var from=nodes[edge.from];var to=nodes[edge.to];
                var a=new Point(shaftIds.Contains(from.id)?axis:from.x,from.y);
                var b=new Point(shaftIds.Contains(to.id)?axis:to.x,to.y);
                if(edge.kind=="climb"&&shaftIds.Contains(from.id)&&shaftIds.Contains(to.id))
                    edge.points=new List<RoutePoint>{new RoutePoint(a.X,a.Y),new RoutePoint(b.X,b.Y)};
                else if(a.X!=from.x||b.X!=to.x)edge.points=ResizeEdge(edge,from,to,a,b);
            }
            foreach(var node in shaft)node.x=axis;
        }
    }
    sealed class RecordedPath {
        public RouteEdge Edge;
        public RouteNode Start,End;
        public int Low,High,Directions;
        public double[] Heights;
        public bool Vertical {get{return Edge.kind=="climb";}}
        public int Axis(RouteNode node){return Vertical?node.y:node.x;}
        public int Height(RouteNode node){return Vertical?node.x:node.y;}
        public RoutePoint Point(int axis,int height){return Vertical?new RoutePoint(height,axis):new RoutePoint(axis,height);}
        public double At(int axis){return Heights[axis-Low];}
        public static RecordedPath FromEdge(RouteEdge edge,RouteNode from,RouteNode to){
            if(edge.edited||edge.review||edge.ropeEntries!=null||edge.kind!="walk"&&edge.kind!="climb"||
                edge.upContinuation||edge.upApproachKey!=0||edge.downApproach!=0)return null;
            var path=new RecordedPath{Edge=edge,Start=from,End=to};
            bool forward=path.Axis(from)<path.Axis(to);
            if(!forward){path.Start=to;path.End=from;}
            path.Low=path.Axis(path.Start);
            path.High=path.Axis(path.End);
            if(path.High<=path.Low)return null;
            path.Directions=edge.both?3:forward?1:2;
            var points=new List<RoutePoint>{new RoutePoint(path.Start.x,path.Start.y)};
            var middle=edge.points.Skip(1).Take(Math.Max(0,edge.points.Count-2));
            points.AddRange(forward?middle:middle.Reverse());
            points.Add(new RoutePoint(path.End.x,path.End.y));
            path.Heights=new double[path.High-path.Low+1];
            int previous=path.Low;
            double height=path.Height(path.Start);
            foreach(var point in points.Skip(1)){
                int axis=path.Vertical?point.y:point.x;
                int nextHeight=path.Vertical?point.x:point.y;
                if(axis<path.Low||axis>path.High)continue;
                if(axis<previous||axis==previous&&Math.Abs(nextHeight-height)>1)return null;
                for(int i=previous;i<axis;i++)
                    path.Heights[i-path.Low]=height+(nextHeight-height)*(i-previous)/(axis-previous);
                previous=axis;height=nextHeight;
            }
            path.Heights[path.High-path.Low]=path.Height(path.End);
            return path;
        }
    }
    sealed class RecordedCorridor {
        public readonly List<RecordedPath> Paths=new List<RecordedPath>();
        public readonly Dictionary<int,double> Profile=new Dictionary<int,double>();
        public readonly HashSet<string> Endpoints=new HashSet<string>();
        public int Low,High;
        public RecordedCorridor(RecordedPath first){Low=first.Low;High=first.High;Add(first);}
        public bool Overlaps(RecordedPath path){
            int start=Math.Max(Low,path.Low),end=Math.Min(High,path.High);
            if(start>end)return false;
            bool connected=Endpoints.Contains(path.Start.id)||Endpoints.Contains(path.End.id);
            int minimum=Math.Min(4,Math.Min(High-Low,path.High-path.Low));
            if(end-start<minimum&&!connected)return false;
            // Preserve the longest observed profile in the overlap. An uncertain
            // nearby trace must not drag that profile toward the next platform.
            double tolerance=path.Vertical?1.05:1.5;
            for(int axis=start;axis<=end;axis++)
                if(Math.Abs(Profile[axis]-path.At(axis))>tolerance)return false;
            return true;
        }
        public void Add(RecordedPath path){
            Paths.Add(path);Endpoints.Add(path.Start.id);Endpoints.Add(path.End.id);
            for(int axis=path.Low;axis<=path.High;axis++)
                if(!Profile.ContainsKey(axis))Profile.Add(axis,path.At(axis));
            Low=Math.Min(Low,path.Low);High=Math.Max(High,path.High);
        }
    }
    internal static void MergeRecordedPaths(RouteDocument d,ISet<string> recordedJunctions=null){
        var nodes=d.nodes.ToDictionary(n=>n.id);
        var incident=d.edges.SelectMany(e=>new[]{Tuple.Create(e.from,e),Tuple.Create(e.to,e)})
            .ToLookup(pair=>pair.Item1,pair=>pair.Item2);
        var paths=d.edges.Select(e=>RecordedPath.FromEdge(e,nodes[e.from],nodes[e.to]))
            .Where(p=>p!=null).OrderByDescending(p=>p.High-p.Low).ToArray();
        var replacements=new List<RouteEdge>();
        var retired=new HashSet<RouteEdge>();
        var touched=new HashSet<string>();
        foreach(var kind in paths.GroupBy(p=>p.Edge.kind)){
            var corridors=new List<RecordedCorridor>();
            foreach(var path in kind){
                var corridor=corridors.FirstOrDefault(c=>c.Overlaps(path));
                if(corridor==null)corridors.Add(new RecordedCorridor(path));else corridor.Add(path);
            }
            foreach(var corridor in corridors.Where(c=>c.Paths.Count>1)){
                var group=new HashSet<RouteEdge>(corridor.Paths.Select(p=>p.Edge));
                var members=corridor.Endpoints.Select(id=>nodes[id]).ToArray();
                var external=members.Where(n=>recordedJunctions!=null&&recordedJunctions.Contains(n.id)||
                    incident[n.id].Any(e=>!group.Contains(e))).ToArray();
                var primary=corridor.Paths[0];
                var joints=external.ToLookup(primary.Axis);
                if(joints.Any(g=>g.Count()>1))continue;
                var directions=new int[corridor.High-corridor.Low];
                foreach(var path in corridor.Paths)
                    for(int axis=path.Low;axis<path.High;axis++)directions[axis-corridor.Low]|=path.Directions;
                var breaks=new SortedSet<int>(joints.Select(g=>g.Key)){corridor.Low,corridor.High};
                for(int i=1;i<directions.Length;i++)
                    if(directions[i]!=directions[i-1])breaks.Add(corridor.Low+i);
                if(breaks.Count-1>=group.Count)continue;
                var at=members.ToLookup(primary.Axis);
                var anchors=breaks.Select(axis=>joints[axis].FirstOrDefault()??
                    at[axis].OrderBy(n=>Math.Abs(primary.Height(n)-corridor.Profile[axis])).ThenBy(n=>n.id).First()).ToArray();
                for(int i=1;i<anchors.Length;i++){
                    var a=anchors[i-1];var b=anchors[i];
                    int low=primary.Axis(a),high=primary.Axis(b),direction=directions[low-corridor.Low];
                    var points=new List<RoutePoint>{new RoutePoint(a.x,a.y)};
                    for(int axis=low+1;axis<high;axis++)
                        points.Add(primary.Point(axis,(int)Math.Round(corridor.Profile[axis])));
                    points.Add(new RoutePoint(b.x,b.y));
                    if(direction==2||direction==3&&primary.Edge.from==primary.End.id){
                        var swap=a;a=b;b=swap;points.Reverse();
                    }
                    var support=corridor.Paths.Where(p=>p.High>low&&p.Low<high).ToArray();
                    var rates=support.Select(p=>(double)p.Edge.duration/(p.High-p.Low)).OrderBy(x=>x).ToArray();
                    replacements.Add(new RouteEdge{id=primary.Edge.id+".merged"+i,from=a.id,to=b.id,
                        kind=primary.Edge.kind,both=direction==3,recoveredWalk=primary.Edge.kind=="walk"&&
                            (direction!=3||support.Any(p=>p.Edge.both&&p.Edge.recoveredWalk)),
                        duration=Math.Max(1,(long)(rates[rates.Length/2]*(high-low))),
                        points=Simplify(points,primary.Vertical?.65:1.15)});
                }
                retired.UnionWith(group);touched.UnionWith(corridor.Endpoints);
            }
        }
        if(retired.Count==0)return;
        d.edges.RemoveAll(retired.Contains);d.edges.AddRange(replacements);
        var used=new HashSet<string>(d.edges.SelectMany(e=>new[]{e.from,e.to}));
        d.nodes.RemoveAll(n=>touched.Contains(n.id)&&!used.Contains(n.id));
    }
    static List<RouteSample> VerifyClimbs(RouteDocument d,bool[] gaps,bool[] climbStarts){
        var raw=d.samples;
        if(d.events.Count==0)return raw;
        // The live trace marks possible climbs. Keep the trace intact and verify
        // direction, alignment and sustained motion when constructing the route.
        var verified=new bool[raw.Count];
        MarkUnrecordedFalls(raw,gaps);
        int at=0;
        while(at<raw.Count){
            var first=raw[at];
            int direction=VerticalDirection(first);
            if(first.kind!="climb"||gaps[at]||direction==0){at++;continue;}
            int end=at;
            int minX=first.x,maxX=first.x;
            int anchor=at>0&&raw[at-1].segment==first.segment&&!gaps[at-1]?at-1:at;
            minX=Math.Min(minX,raw[anchor].x);maxX=Math.Max(maxX,raw[anchor].x);
            int tip=anchor,advances=0;
            long start=-1;
            for(int i=at;i<raw.Count;i++){
                var point=raw[i];
                if(gaps[i]||point.segment!=first.segment||point.kind!="climb"||VerticalDirection(point)!=direction||
                    Math.Max(maxX,point.x)-Math.Min(minX,point.x)>2)break;
                int progress=direction*(point.y-raw[tip].y);
                if(progress< -1)break;
                minX=Math.Min(minX,point.x);maxX=Math.Max(maxX,point.x);end=i;
                if(progress<=0)continue;
                if(start<0)start=Math.Max(raw[i-1].time,point.time-125);
                tip=i;advances++;
            }
            bool afterMount=at>0&&raw[at-1].segment==first.segment&&raw[at-1].kind=="rope_mount";
            if(tip>=at&&(afterMount||advances>=3&&direction*(raw[tip].y-raw[anchor].y)>=4&&raw[tip].time-start>=390)){
                climbStarts[at]=true;
                for(int i=at;i<=tip;i++)verified[i]=true;
            }
            at=end+1;
        }
        var samples=new List<RouteSample>(raw.Count);
        for(int i=0;i<raw.Count;i++){
            var s=raw[i];
            samples.Add(s.kind!="climb"||verified[i]?s:new RouteSample{
                x=s.x,y=s.y,time=s.time,frame=s.frame,segment=s.segment,keys=s.keys,jumpKeys=s.jumpKeys,jumpVk=s.jumpVk,kind="walk"
            });
        }
        return samples;
    }
    static int VerticalDirection(RouteSample s){int keys=s.keys&12;return keys==4?-1:keys==8?1:0;}
    static bool PossibleFallStep(RouteSample previous,RouteSample next){
        return next.kind=="climb"&&previous.segment==next.segment&&next.y>previous.y&&
            next.time-previous.time<=220&&Math.Abs(next.x-previous.x)<=2;
    }
    static void MarkUnrecordedFalls(List<RouteSample> samples,bool[] gaps){
        // A fast fall in a provisional ladder trace has no replayable Down+Jump
        // command. Leave a gap between the takeoff and landing instead.
        int start=1;
        while(start<samples.Count){
            if(!PossibleFallStep(samples[start-1],samples[start])){start++;continue;}
            int end=start,fast=0,fastHeight=0;
            bool falling=false;
            while(end<samples.Count&&PossibleFallStep(samples[end-1],samples[end])){
                var previous=samples[end-1];var point=samples[end];
                if(point.time-previous.time<=90){
                    fast++;fastHeight+=point.y-previous.y;
                    falling|=fast>=3&&fastHeight>=8;
                }else{fast=0;fastHeight=0;}
                end++;
            }
            if(falling&&samples[end-1].y-samples[start-1].y>=12)
                for(int i=start;i<end-1;i++)gaps[i]=true;
            start=end;
        }
    }
    static int WalkDirection(RouteSample sample){int keys=sample.kind=="walk"?sample.keys&3:0;return keys==1?-1:keys==2?1:0;}
    public static void MeasureUpApproaches(RouteDocument d,IList<RouteSample> samples,IList<RouteKeyEvent> events){
        if(samples.Count<2||events.Count==0)return;
        var nodes=d.nodes.ToDictionary(n=>n.id);
        var entries=d.edges.Where(e=>e.kind=="climb"&&!e.edited&&e.ropeEntries==null&&nodes[e.from].y>nodes[e.to].y)
            .ToLookup(e=>new Point(nodes[e.from].x,nodes[e.from].y));
        if(entries.Count==0)return;
        var ledges=d.edges.Where(e=>e.kind=="walk"&&!e.edited&&nodes[e.from].y==nodes[e.to].y&&
            Math.Abs(nodes[e.from].x-nodes[e.to].x)<=5).ToLookup(e=>new Point(nodes[e.to].x,nodes[e.to].y));
        var upwardEnds=new HashSet<string>(entries.SelectMany(group=>group).Select(e=>e.to));
        var keys=events.Where(e=>e!=null&&e.vk>=37&&e.vk<=39).OrderBy(e=>e.time).ThenBy(e=>e.id).ToArray();
        var held=new RouteKeyEvent[3];
        int nextKey=0;
        for(int i=1;i<samples.Count;i++){
            var platform=samples[i-1];var rise=samples[i];
            while(nextKey<keys.Length&&keys[nextKey].time<=rise.time){
                var key=keys[nextKey++];
                held[key.vk-37]=key.down?key:null;
            }
            if(platform.segment!=rise.segment||platform.kind!="walk"||rise.kind!="climb"||rise.y>=platform.y)continue;
            int horizontal=rise.keys&3;
            var up=held[1];
            if(rise.keys==4&&up!=null&&platform.keys==4){
                int at=i-1;
                while(at>0&&samples[at-1].segment==rise.segment&&samples[at-1].kind=="walk"&&
                    samples[at-1].keys==4&&samples[at-1].y==platform.y&&rise.time-samples[at-1].time<=800)at--;
                if(at>0){
                    var lastClimb=samples[at-1];
                    if(lastClimb.kind=="climb"&&lastClimb.segment==rise.segment&&lastClimb.keys==4&&
                        lastClimb.y==platform.y&&up.time<=lastClimb.time&&rise.time-lastClimb.time<=800){
                        var joint=new Point(platform.x,platform.y);
                        var nextStarts=new HashSet<string>(entries[joint].Select(e=>e.from));
                        foreach(var ledge in ledges[joint])
                            if(upwardEnds.Contains(ledge.from)&&nextStarts.Contains(ledge.to)&&
                                Math.Abs(nodes[ledge.from].x-lastClimb.x)<=2)ledge.upContinuation=true;
                    }
                }
                continue;
            }
            if((rise.keys&4)==0||(horizontal!=1&&horizontal!=2))continue;
            int direction=horizontal==1?37:39;
            var steer=held[direction-37];
            if(steer==null||up==null)continue;
            var start=up.time>=steer.time?up:steer;
            if(rise.time-start.time>1000)continue;
            var approach=platform;
            for(int back=i-1;back>0&&approach.time>=start.time;back--){
                var earlier=samples[back-1];
                if(earlier.segment!=platform.segment||earlier.kind!="walk"||Math.Abs(earlier.y-platform.y)>1)break;
                approach=earlier;
            }
            var release=keys.Skip(nextKey).FirstOrDefault(k=>!k.down&&(k.vk==direction||k.vk==38));
            long overlap=release==null?0:InputDelay(release,start);
            for(int x=platform.x-2;x<=platform.x+2;x++){
                int ahead=(x-approach.x)*(direction==39?1:-1);
                if(ahead<0||ahead>16)continue;
                foreach(var edge in entries[new Point(x,platform.y)]){
                    if(edge.upApproachKey!=0||Math.Abs(nodes[edge.to].x-rise.x)>4)continue;
                    edge.upApproach=Math.Max(1,ahead);
                    edge.upApproachKey=direction;
                    edge.upApproachOverlap=overlap;
                }
            }
        }
    }
    static void Emit(RouteDocument d,List<RouteSample> samples,int start,int end,string kind,ref RouteNode previous){
        if(end<=start)return;
        // Ground movement can follow a slope. A nearly vertical discontinuity
        // without a recorded action must not become a replayable walking link.
        if(kind=="walk"&&d.events.Count!=0){
            for(int i=start+1;i<=end;i++){
                int dx=Math.Abs(samples[i].x-samples[i-1].x),dy=Math.Abs(samples[i].y-samples[i-1].y);
                if(dy<5||dy<=2*dx)continue;
                EmitSpan(d,samples,start,i-1,kind,ref previous);
                previous=null;start=i;
            }
        }
        EmitSpan(d,samples,start,end,kind,ref previous);
    }
    static void EmitSpan(RouteDocument d,List<RouteSample> samples,int start,int end,string kind,ref RouteNode previous){
        if(end<=start)return;var slice=samples.GetRange(start,end-start+1);var first=slice[0];var last=slice[slice.Count-1];if(Distance(first.x,first.y,last.x,last.y)<1)return;
        bool walking=kind=="walk";
        if(walking&&d.events.Count!=0&&first.x==last.x){previous=null;return;}
        var points=walking?WalkProfile(slice):slice.Select(p=>new RoutePoint(p.x,p.y)).ToList();
        int spanX=points.Max(p=>p.x)-points.Min(p=>p.x),spanY=points.Max(p=>p.y)-points.Min(p=>p.y);
        bool flat=walking&&spanY<=2&&Math.Abs(points[0].y-points[points.Count-1].y)<=1;
        bool ladder=kind=="climb"&&spanX<=3&&spanY>=2;
        if(flat){int axis=Median(points.Select(p=>p.y));foreach(var p in points)p.y=axis;}
        if(walking){
            if(first.kind!="walk")points[0].y=first.y;
            if(end+1<samples.Count&&samples[end+1].segment==last.segment&&samples[end+1].kind!="walk")points[points.Count-1].y=last.y;
        }
        if(ladder){int axis=Median(points.Select(p=>p.x));foreach(var p in points)p.x=axis;}
        var shape=Simplify(points,walking?1.3:0.65);
        bool walkingBoth=walking&&CanWalkBoth(shape);
        var p0=points[0];var p1=points[points.Count-1];var a=previous??Node(d,p0.x,p0.y,flat);var b=Node(d,p1.x,p1.y,flat||ladder);if(a.id==b.id){previous=b;return;}if(ladder&&Math.Abs(a.x-p0.x)<=2)a.x=p0.x;
        if(kind=="drop")shape=new List<RoutePoint>{new RoutePoint(a.x,a.y),new RoutePoint(b.x,b.y)};
        var edge=new RouteEdge{id="E"+(d.edges.Count+1),from=a.id,to=b.id,kind=kind,both=walkingBoth||ladder,duration=Math.Max(1,last.time-first.time),points=shape};MeasureInput(d,edge,slice);
        if(ladder&&p1.y>p0.y) MeasureDownApproach(d,edge,start,p0);
        d.edges.Add(edge);previous=b;
    }
    static int Median(IEnumerable<int> values){var sorted=values.OrderBy(x=>x).ToArray();return sorted[sorted.Length/2];}
    static List<RoutePoint> WalkProfile(List<RouteSample> samples){
        int first=samples[0].x,last=samples[samples.Count-1].x;
        if(first==last)return samples.Select(p=>new RoutePoint(p.x,p.y)).ToList();
        int left=Math.Min(first,last),right=Math.Max(first,last);
        var columns=samples.Where(p=>p.x>=left&&p.x<=right).GroupBy(p=>p.x).OrderBy(g=>g.Key)
            .Select(g=>new RoutePoint(g.Key,Median(g.Select(p=>p.y)))).ToArray();
        var profile=new List<RoutePoint>(columns.Length);
        for(int i=0;i<columns.Length;i++){
            var center=columns[i];
            int y=center.y;
            if(i>0&&i+1<columns.Length&&center.x-columns[i-1].x<=3&&columns[i+1].x-center.x<=3){
                int local=Median(new[]{columns[i-1].y,y,columns[i+1].y});
                if(Math.Abs(local-y)<=1)y=local;
            }
            profile.Add(new RoutePoint(center.x,y));
        }
        if(first>last)profile.Reverse();
        return profile;
    }
    public static bool CanWalkBoth(IList<RoutePoint> points){
        if(points==null||points.Count<2)return false;long horizontal=0;
        for(int i=1;i<points.Count;i++){int dx=Math.Abs(points[i].x-points[i-1].x),dy=Math.Abs(points[i].y-points[i-1].y);if(dy>Math.Max(2,dx))return false;horizontal+=dx;}
        return horizontal>0;
    }
    public static void RefreshWalkingDirections(RouteDocument d){foreach(var edge in d.edges)if(edge.kind=="walk"&&!edge.edited&&!edge.recoveredWalk&&edge.points.Count>=2)edge.both=CanWalkBoth(edge.points);}
    static void MeasureDownApproach(RouteDocument d, RouteEdge edge, int start, RoutePoint top){
        // Only learn from the final continuous Down hold on the entry platform.
        for(int i=start;i>=0;i--){
            var sample=d.samples[i];
            if(sample.segment!=d.samples[start].segment||(sample.keys&8)==0||
                Math.Abs(sample.y-top.y)>3||Math.Abs(sample.x-top.x)>16)break;
            edge.downApproach=Math.Max(edge.downApproach,Math.Abs(sample.x-top.x));
        }
    }
    static long InputDelay(RouteKeyEvent later,RouteKeyEvent earlier){
        return Math.Max(0,later.sourceTime>0&&earlier.sourceTime>0?later.sourceTime-earlier.sourceTime:later.time-earlier.time);
    }
    static void MeasureInput(RouteDocument d,RouteEdge edge,List<RouteSample> slice){
        bool rope=edge.kind=="rope_mount"||edge.kind=="rope_exit";
        if(!rope&&edge.kind!="jump"&&edge.kind!="jump_down"&&edge.kind!="drop")return;
        var launch=slice.FirstOrDefault(s=>s.jumpKeys>=0);
        int jumpKey=launch!=null&&launch.jumpVk!=0?launch.jumpVk:d.jumpKey;
        long start=slice[0].time,end=slice[slice.Count-1].time;
        var presses=d.events.Where(e=>e.vk==jumpKey&&e.down&&e.time>=start-100&&e.time<=end+100);
        var press=launch!=null ? presses.OrderBy(e=>Math.Abs(e.time-launch.time)).FirstOrDefault() : presses.FirstOrDefault();
        if(rope&&launch!=null){
            int direction=launch.jumpKeys&3;
            edge.jumpDirection=direction==1?37:direction==2?39:0;
            if(edge.kind=="rope_exit"&&(launch.jumpKeys&8)!=0)edge.downLead=0;
        }
        if(press==null)return;
        var release=d.events.FirstOrDefault(e=>e.vk==jumpKey&&!e.down&&e.time>press.time);
        if(release!=null)edge.jumpHold=InputDelay(release,press);
        if(edge.kind=="rope_mount"){
            var up=d.events.LastOrDefault(e=>e.vk==38&&e.time<=press.time);
            if(up!=null&&up.down)edge.upLead=InputDelay(press,up);
        }
        if(rope&&edge.jumpDirection>0){
            var steerRelease=d.events.FirstOrDefault(e=>e.vk==edge.jumpDirection&&!e.down&&e.time>=press.time);
            edge.steerHold=steerRelease==null ? Math.Max(0,end-press.time) :
                Math.Min(Math.Max(0,end-press.time),InputDelay(steerRelease,press));
        }
        if(edge.kind!="drop"&&edge.downLead<0)return;
        var down=d.events.LastOrDefault(e=>e.vk==40&&e.down&&e.time<=press.time);
        if(down!=null&&edge.kind=="drop")edge.downLead=InputDelay(press,down);
        if(release!=null){
            var tail=d.events.FirstOrDefault(e=>e.vk==40&&!e.down&&e.time>=release.time);
            if(tail!=null)edge.downTail=InputDelay(tail,release);
        }
    }
    static RouteNode Node(RouteDocument d,int x,int y,bool merge){var old=merge?d.nodes.FirstOrDefault(n=>n.y==y&&Math.Abs(n.x-x)<=2):d.nodes.FirstOrDefault(n=>n.x==x&&n.y==y);if(old!=null)return old;int index=d.nodes.Count+1;while(d.nodes.Any(n=>n.id=="N"+index))index++;var n2=new RouteNode{id="N"+index,label="点"+index,x=x,y=y};d.nodes.Add(n2);return n2;}
    static double WalkPosition(IList<RoutePoint> points,RouteNode node,out double distance){
        distance=Double.PositiveInfinity;double along=0,bestAt=0;
        for(int i=1;i<points.Count;i++){var a=points[i-1];var b=points[i];double dx=b.x-a.x,dy=b.y-a.y,length=Math.Sqrt(dx*dx+dy*dy),t=((node.x-a.x)*dx+(node.y-a.y)*dy)/Math.Max(.000001,dx*dx+dy*dy);t=Math.Max(0,Math.Min(1,t));double gap=Math.Sqrt(Math.Pow(node.x-(a.x+t*dx),2)+Math.Pow(node.y-(a.y+t*dy),2));if(gap<distance){distance=gap;bestAt=along+t*length;}along+=length;}
        return bestAt;
    }
    internal static bool CanEditWalkAction(RouteEdge edge){
        return edge!=null&&edge.kind=="walk"&&!edge.upContinuation;
    }
    internal static RouteEdge SplitWalk(RouteDocument d,RouteEdge edge,Point at){
        return SplitGround(d,edge,at);
    }
    static RouteEdge SplitGround(RouteDocument d,RouteEdge edge,Point at,RouteNode junction=null){
        if((!CanEditWalkAction(edge)&&!(edge.kind=="climb"&&edge.ropeEntries==null))||!d.edges.Contains(edge)||d.nodes.Count>=20000||d.edges.Count>=40000)return null;
        var from=d.nodes.First(n=>n.id==edge.from);
        var to=d.nodes.First(n=>n.id==edge.to);
        var path=ResizeEdge(edge,from,to,new Point(from.x,from.y),new Point(to.x,to.y));
        double best=Double.PositiveInfinity,total=0,offset=0;
        int segment=0;
        RoutePoint split=null;
        for(int i=1;i<path.Count;i++){
            var a=path[i-1];var b=path[i];double dx=b.x-a.x,dy=b.y-a.y,squared=dx*dx+dy*dy;
            if(squared==0)continue;
            double t=Math.Max(0,Math.Min(1,((at.X-a.x)*dx+(at.Y-a.y)*dy)/squared));
            double x=a.x+t*dx,y=a.y+t*dy,length=Math.Sqrt(squared);
            double gap=(at.X-x)*(at.X-x)+(at.Y-y)*(at.Y-y);
            if(gap<best){
                best=gap;segment=i;offset=total+t*length;
                split=new RoutePoint((int)Math.Round(x),(int)Math.Round(y));
            }
            total+=length;
        }
        if(split==null||split.x==from.x&&split.y==from.y||split.x==to.x&&split.y==to.y)return null;
        var first=path.Take(segment).ToList();
        var second=path.Skip(segment).ToList();
        if(first.Last().x!=split.x||first.Last().y!=split.y)first.Add(split);
        if(second[0].x!=split.x||second[0].y!=split.y)second.Insert(0,new RoutePoint(split.x,split.y));
        var joint=junction??new RouteNode{id="N"+Guid.NewGuid().ToString("N"),x=split.x,y=split.y};
        if(joint.x!=split.x||joint.y!=split.y)return null;
        var elapsed=(long)(edge.duration*(offset/total));
        var tail=new RouteEdge{id="E"+Guid.NewGuid().ToString("N"),from=joint.id,to=to.id,
            kind=edge.kind,both=edge.both,edited=true,review=edge.review,recoveredWalk=edge.recoveredWalk,
            duration=edge.duration-elapsed,points=second};
        if(junction==null)d.nodes.Add(joint);
        d.edges.Insert(d.edges.IndexOf(edge)+1,tail);
        edge.to=joint.id;edge.points=first;edge.duration=elapsed;edge.downApproach=0;edge.edited=true;
        d.graphEdited=true;
        return tail;
    }
    internal static bool ConvertWalkToJump(RouteDocument d,RouteEdge edge,bool reverse){
        if(!CanEditWalkAction(edge)||!d.edges.Contains(edge))return false;
        var from=d.nodes.First(n=>n.id==(reverse?edge.to:edge.from));
        var to=d.nodes.First(n=>n.id==(reverse?edge.from:edge.to));
        if(from.x==to.x&&from.y==to.y)return false;
        edge.from=from.id;edge.to=to.id;
        edge.kind=to.y>from.y+1?"jump_down":"jump";
        edge.points=new List<RoutePoint>{new RoutePoint(from.x,from.y),new RoutePoint(to.x,to.y)};
        edge.both=edge.recoveredWalk=false;
        edge.downApproach=0;
        edge.duration=1;edge.edited=d.graphEdited=true;
        return true;
    }
    internal static RouteNode MarkMapPoint(RouteDocument document,Point position,string marker){
        var node=document.nodes.FirstOrDefault(n=>n.x==position.X&&n.y==position.Y);
        if(node!=null&&node.marker==marker&&document.manualMap)return null;
        if(node==null){
            if(document.nodes.Count>=20000)return null;
            node=Node(document,position.X,position.Y,false);
        }
        node.marker=marker;
        document.manualMap=document.graphEdited=true;
        return node;
    }

    static PointF ProjectSegment(PointF position,RoutePoint a,RoutePoint b){
        double dx=b.x-a.x,dy=b.y-a.y;
        double length=dx*dx+dy*dy;
        double t=length==0?0:Math.Max(0,Math.Min(1,((position.X-a.x)*dx+(position.Y-a.y)*dy)/length));
        return new PointF((float)(a.x+t*dx),(float)(a.y+t*dy));
    }

    internal static Point SnapToPath(RouteDocument document,PointF position,RouteEdge edge){
        var from=document.nodes.First(n=>n.id==edge.from);
        var to=document.nodes.First(n=>n.id==edge.to);
        var points=ResizeEdge(edge,from,to,new Point(from.x,from.y),new Point(to.x,to.y));
        var projected=points.Zip(points.Skip(1),(a,b)=>ProjectSegment(position,a,b))
            .OrderBy(p=>Math.Pow(p.X-position.X,2)+Math.Pow(p.Y-position.Y,2)).First();
        return Point.Round(projected);
    }

    static RouteNode AttachToPath(RouteDocument document,RouteEdge edge,Point position,RouteNode marked=null){
        var endpoint=document.nodes.FirstOrDefault(n=>(n.id==edge.from||n.id==edge.to)&&n.x==position.X&&n.y==position.Y);
        if(edge.kind=="climb"&&edge.ropeEntries!=null){
            var entry=endpoint??document.nodes.FirstOrDefault(n=>n.x==position.X&&n.y==position.Y);
            if(entry==null&&marked!=null&&!document.edges.Any(link=>link.from==marked.id||link.to==marked.id||
                link.ropeEntries!=null&&link.ropeEntries.Contains(marked.id))){
                marked.x=position.X;marked.y=position.Y;entry=marked;
            }
            entry=entry??Node(document,position.X,position.Y,false);
            if(entry.id!=edge.to&&!edge.ropeEntries.Contains(entry.id))edge.ropeEntries.Add(entry.id);
            return entry;
        }
        if(endpoint!=null)return endpoint;
        var junction=document.nodes.FirstOrDefault(n=>n.x==position.X&&n.y==position.Y&&
            !document.edges.Any(link=>link.from==n.id||link.to==n.id));
        if(junction==null&&marked!=null&&!document.edges.Any(link=>link.from==marked.id||link.to==marked.id))
            junction=marked;
        var original=junction==null?Point.Empty:new Point(junction.x,junction.y);
        if(junction!=null){junction.x=position.X;junction.y=position.Y;}
        var tail=SplitGround(document,edge,position,junction);
        if(tail==null&&junction!=null){junction.x=original.X;junction.y=original.Y;}
        return tail==null?null:document.nodes.First(n=>n.id==tail.from);
    }

    static bool IsMarkedActionPoint(RouteNode point,string kind){
        return point!=null&&(kind=="walk"&&point.marker=="launch"||kind=="climb"&&point.marker=="grip");
    }

    internal static RouteEdge MapSupport(RouteDocument document,Point position,string kind,RouteNode selected){
        bool marked=IsMarkedActionPoint(selected,kind);
        double tolerance=selected==null||marked?2:0.5;
        var support=document.edges.Where(e=>e.kind==kind)
            .Select(e=>new{Edge=e,Gap=StrokeDistance(document,e,position)})
            .Where(item=>item.Gap<=tolerance).OrderBy(item=>item.Gap).Take(2).ToArray();
        if(support.Length==0)return null;
        if((selected==null||marked)&&support.Length>1&&Math.Abs(support[1].Gap-support[0].Gap)<0.25&&
            SnapToPath(document,position,support[0].Edge)!=SnapToPath(document,position,support[1].Edge))return null;
        return support[0].Edge;
    }

    static RouteNode BindMapPosition(RouteDocument document,Point position,string kind,RouteNode selected,bool exactAxis=false){
        var support=MapSupport(document,position,kind,selected);
        if(support==null)return null;
        Point point=SnapToPath(document,position,support);
        return exactAxis&&point.X!=position.X?null:AttachToPath(document,support,point,
            IsMarkedActionPoint(selected,kind)?selected:null);
    }

    internal static bool IsMapAction(string brush){
        return brush=="jump"||brush=="jump_down"||brush=="drop"||brush=="rope_mount";
    }

    internal static RouteEdge MapActionDestination(RouteDocument document,string action,Point position,RouteNode selected){
        if(action=="rope_mount")return MapSupport(document,position,"climb",selected);
        var platform=MapSupport(document,position,"walk",selected);
        if(action!="jump")return platform;
        var rope=MapSupport(document,position,"climb",selected);
        return rope!=null&&(platform==null||selected!=null&&selected.marker=="grip"||
            StrokeDistance(document,rope,position)+0.25<StrokeDistance(document,platform,position))?rope:platform;
    }

    internal static RouteEdge DrawMapLink(RouteDocument document,string kind,Point first,Point last,
        RouteNode start,RouteNode end){
        if(document.edges.Count>=39996||document.nodes.Count>=19996||first==last)return null;
        if(kind=="walk"){
            if(start==null||end==null||Math.Abs(last.X-first.X)<1)return null;
        }else if(kind=="climb"){
            if(start==null&&end==null)return null;
            if(start!=null&&end!=null&&first.X!=last.X)return null;
            int axis=start==null?last.X:first.X;
            first.X=last.X=axis;
            if(first.Y==last.Y)return null;
            start=start??BindMapPosition(document,first,"walk",null,true)??Node(document,first.X,first.Y,false);
            end=end??BindMapPosition(document,last,"walk",null,true)??Node(document,last.X,last.Y,false);
        }else if(IsMapAction(kind)){
            var endSupport=MapActionDestination(document,kind,last,end);
            if(endSupport==null)return null;
            if(endSupport.kind=="climb")kind="rope_mount";
            var landing=SnapToPath(document,last,endSupport);
            start=BindMapPosition(document,first,"walk",start);
            if(start==null||(kind=="drop"||kind=="jump_down")&&landing.Y<=start.y||
                kind=="rope_mount"&&landing.Y>start.y)return null;
            // A platform can supply both ends of a jump and may have just been split.
            end=kind=="rope_mount"?AttachToPath(document,endSupport,landing,IsMarkedActionPoint(end,"climb")?end:null):
                BindMapPosition(document,last,"walk",end);
            if(end==null)return null;
        }else return null;
        if(start==end)return null;
        var existing=document.edges.FirstOrDefault(e=>e.kind==kind&&(e.from==start.id&&e.to==end.id||
            e.both&&e.from==end.id&&e.to==start.id));
        if(existing!=null){
            if(!existing.suggestedJump)return null;
            existing.suggestedJump=false;
            return existing;
        }

        var link=new RouteEdge{
            id="E"+Guid.NewGuid().ToString("N"),kind=kind,from=start.id,to=end.id,
            both=kind=="walk"||kind=="climb",duration=1,edited=true,
            points=new List<RoutePoint>{new RoutePoint(start.x,start.y),new RoutePoint(end.x,end.y)}
        };
        if(kind=="jump"||kind=="jump_down"||kind=="rope_mount")
            link.jumpDirection=end.x<start.x?37:end.x>start.x?39:0;
        document.edges.Add(link);
        document.graphEdited=true;
        // A marked launch or drop point on this platform is a real junction.
        if(kind!="walk")return link;
        SplitWalkJunctions(document,link,0.5,true);
        return document.edges.First(e=>e.id==link.id||e.id.StartsWith(link.id+"_",StringComparison.Ordinal));
    }

    sealed class JumpProposal {
        public Point Start,End;
        public string Kind;
        public int Cost;
    }

    static Dictionary<string,string> ConnectedGroups(IEnumerable<RouteEdge> edges){
        var neighbors=edges.SelectMany(edge=>new[]{Tuple.Create(edge.from,edge.to),Tuple.Create(edge.to,edge.from)})
            .ToLookup(pair=>pair.Item1,pair=>pair.Item2);
        var groups=new Dictionary<string,string>();
        foreach(var vertex in neighbors){
            if(groups.ContainsKey(vertex.Key))continue;
            var queue=new Queue<string>();
            groups.Add(vertex.Key,vertex.Key);
            queue.Enqueue(vertex.Key);
            while(queue.Count!=0){
                foreach(string next in neighbors[queue.Dequeue()]){
                    if(groups.ContainsKey(next))continue;
                    groups.Add(next,vertex.Key);
                    queue.Enqueue(next);
                }
            }
        }
        return groups;
    }

    static Point? AtPlatformColumn(List<RoutePoint> path,int x){
        for(int i=1;i<path.Count;i++){
            var a=path[i-1];var b=path[i];
            if(x<Math.Min(a.x,b.x)||x>Math.Max(a.x,b.x)||a.x==b.x)continue;
            double fraction=(double)(x-a.x)/(b.x-a.x);
            return new Point(x,(int)Math.Round(a.y+(b.y-a.y)*fraction));
        }
        return null;
    }

    static JumpProposal CheckJump(Point? start,Point? end,string kind){
        if(!start.HasValue||!end.HasValue)return null;
        int rise=start.Value.Y-end.Value.Y;
        int gap=Math.Abs(start.Value.X-end.Value.X);
        // A tight search window keeps suggestions local. They still require
        // an in-game trial before they can join the normal movement route.
        if(rise<2||rise>6||gap>4||rise+gap>9)return null;
        return new JumpProposal{Start=start.Value,End=end.Value,Kind=kind,Cost=2*gap+rise};
    }

    static JumpProposal AcrossPlatforms(List<RoutePoint> lower,List<RoutePoint> upper){
        int lowerLeft=lower.Min(p=>p.x),lowerRight=lower.Max(p=>p.x);
        int upperLeft=upper.Min(p=>p.x),upperRight=upper.Max(p=>p.x);
        if(lowerRight+4<upperLeft||upperRight+4<lowerLeft||
            lower.Max(p=>p.y)<upper.Min(p=>p.y)+2||lower.Min(p=>p.y)>upper.Max(p=>p.y)+6)return null;
        var landColumns=new List<int>();
        int overlapLeft=Math.Max(lowerLeft,upperLeft),overlapRight=Math.Min(lowerRight,upperRight);
        if(overlapLeft<=overlapRight){
            landColumns.Add((overlapLeft+overlapRight)/2);
            landColumns.Add(overlapLeft);
            landColumns.Add(overlapRight);
        }
        landColumns.Add(upperLeft);
        landColumns.Add(upperRight);
        JumpProposal best=null;
        foreach(int x in landColumns.Distinct()){
            var end=AtPlatformColumn(upper,x);
            for(int from=Math.Max(lowerLeft,x-4);from<=Math.Min(lowerRight,x+4);from++){
                var candidate=CheckJump(AtPlatformColumn(lower,from),end,"jump");
                if(candidate!=null&&(best==null||candidate.Cost<best.Cost))best=candidate;
            }
        }
        return best;
    }

    internal static void AddStepDescents(RouteDocument route){
        var nodes=route.nodes.ToDictionary(node=>node.id);
        var platforms=route.edges.Where(edge=>edge.kind=="walk")
            .SelectMany(edge=>new[]{Tuple.Create(edge.from,edge),Tuple.Create(edge.to,edge)})
            .ToLookup(pair=>pair.Item1,pair=>pair.Item2);
        Func<RouteEdge,RouteNode,int,bool> extends=(edge,at,side)=>
            (nodes[edge.from].x-at.x)*side>0||(nodes[edge.to].x-at.x)*side>0||edge.points.Any(p=>(p.x-at.x)*side>0);
        var descents=route.edges.Where(edge=>edge.kind=="jump"&&!edge.both)
            .Select(edge=>new{Jump=edge,High=nodes[edge.to],Low=nodes[edge.from]})
            .Where(step=>step.High.y<step.Low.y&&step.Low.y-step.High.y<=6&&step.High.x!=step.Low.x&&Math.Abs(step.High.x-step.Low.x)<=4)
            .Select(step=>new{step.Jump,step.High,step.Low,Side=Math.Sign(step.Low.x-step.High.x)})
            .Where(step=>platforms[step.High.id].Any(edge=>extends(edge,step.High,-step.Side))&&
                !platforms[step.High.id].Any(edge=>extends(edge,step.High,step.Side))&&
                platforms[step.Low.id].Any(edge=>extends(edge,step.Low,step.Side)))
            .GroupBy(step=>Tuple.Create(step.High.id,step.Side))
            .Select(group=>group.OrderBy(step=>step.Low.y).ThenBy(step=>Math.Abs(step.Low.x-step.High.x)).First()).ToArray();
        var departures=route.edges.SelectMany(edge=>Departures(edge).Select(from=>Tuple.Create(from,edge)))
            .ToLookup(pair=>pair.Item1,pair=>pair.Item2);
        foreach(var step in descents){
            if(route.edges.Count>=40000)return;
            if(departures[step.High.id].Any(edge=>Arrival(edge,step.High.id)==step.Low.id))continue;
            route.edges.Add(new RouteEdge{id=step.Jump.id+".walk_off",kind="walk_off",from=step.High.id,to=step.Low.id,
                duration=900,edited=true,suggestedJump=step.Jump.suggestedJump,
                points=new List<RoutePoint>{new RoutePoint(step.High.x,step.High.y),new RoutePoint(step.Low.x,step.Low.y)}});
        }
    }

    internal static void ConnectNearbyGrabLaunches(RouteDocument route){
        var platforms=route.edges.Where(edge=>edge.kind=="walk").ToArray();
        var joined=new HashSet<string>(platforms.SelectMany(edge=>new[]{edge.from,edge.to}));
        var nodes=route.nodes.ToDictionary(node=>node.id);
        foreach(var grab in route.edges.Where(edge=>edge.kind=="rope_mount").ToArray()){
            if(joined.Contains(grab.from))continue;
            var launch=nodes[grab.from];
            var grip=nodes[grab.to];
            var position=new Point(launch.x,launch.y);
            var support=MapSupport(route,position,"walk",null);
            if(support==null)continue;
            var point=SnapToPath(route,position,support);
            if(point.Y<grip.y||point.X==grip.x&&point.Y==grip.y)continue;
            var standing=AttachToPath(route,support,point);
            if(standing==null)continue;
            grab.from=standing.id;
            grab.jumpDirection=grip.x<standing.x?37:grip.x>standing.x?39:0;
            if(grab.points.Count!=0)grab.points[0]=new RoutePoint(standing.x,standing.y);
        }
    }

    internal static void AddRopeDescents(RouteDocument route){
        var nodes=route.nodes.ToDictionary(node=>node.id);
        var onRopes=new HashSet<string>(route.edges.Where(edge=>edge.kind=="climb")
            .SelectMany(edge=>new[]{edge.from,edge.to}.Concat(edge.ropeEntries??new List<string>())));
        var ground=route.edges.Where(edge=>edge.kind=="walk")
            .Select(edge=>ResizeEdge(edge,nodes[edge.from],nodes[edge.to],
                new Point(nodes[edge.from].x,nodes[edge.from].y),new Point(nodes[edge.to].x,nodes[edge.to].y))).ToArray();
        var exits=new HashSet<string>(route.edges.Where(edge=>edge.kind=="rope_exit"||edge.kind=="drop")
            .Select(edge=>edge.from));
        foreach(var id in onRopes){
            if(exits.Contains(id)||route.nodes.Count>=19999||route.edges.Count>=39998)continue;
            var start=nodes[id];
            var position=new Point(start.x,start.y);
            if(ground.Any(path=>{
                var standing=AtPlatformColumn(path,start.x);
                return standing.HasValue&&Math.Abs(standing.Value.Y-start.y)<=1;
            }))continue;
            // A downward release lands on the first drawn platform under this rope.
            var landings=ground.Select(path=>AtPlatformColumn(path,start.x))
                .Where(point=>point.HasValue&&point.Value.Y>start.y+1)
                .Select(point=>point.Value).OrderBy(point=>point.Y).ToArray();
            if(landings.Length==0)continue;
            var below=landings[0];
            // Detach with Left/Right + Jump; Down keeps the character on the rope.
            foreach(int direction in new[]{37,39}){
                int x=start.x+(direction==37?-4:4);
                var side=ground.Select(path=>AtPlatformColumn(path,x))
                    .Where(point=>point.HasValue&&Math.Abs(point.Value.Y-below.Y)<=1)
                    .Select(point=>point.Value).ToArray();
                if(side.Length==0)continue;
                var landing=side[0];
                var support=MapSupport(route,landing,"walk",null);
                if(support==null)continue;
                var end=AttachToPath(route,support,landing);
                if(end==null)continue;
                route.edges.Add(new RouteEdge{
                    id="EXIT"+start.id+"_"+end.id,kind="rope_exit",from=start.id,to=end.id,edited=true,
                    jumpDirection=direction,jumpHold=120,steerHold=200,
                    duration=600+(landing.Y-start.y)*20,
                    points=new List<RoutePoint>{new RoutePoint(start.x,start.y),new RoutePoint(end.x,end.y)}
                });
            }
        }
    }

    internal static List<string> SuggestMapJumps(RouteDocument document){
        var added=new List<string>();
        if(!document.manualMap)return added;
        var walks=document.edges.Where(e=>e.kind=="walk").ToArray();
        var ropes=document.edges.Where(e=>e.kind=="climb").ToArray();
        var walkGroups=ConnectedGroups(walks);
        var ropeGroups=ConnectedGroups(ropes);
        foreach(var rope in ropes.Where(e=>e.ropeEntries!=null))
            foreach(string entry in rope.ropeEntries)ropeGroups[entry]=ropeGroups[rope.from];
        var nodes=document.nodes.ToDictionary(n=>n.id);
        var ground=walks.ToDictionary(e=>e.id,e=>ResizeEdge(e,nodes[e.from],nodes[e.to],
            new Point(nodes[e.from].x,nodes[e.from].y),new Point(nodes[e.to].x,nodes[e.to].y)));
        var existingLandings=new HashSet<string>(document.edges.Where(e=>e.kind=="jump"&&walkGroups.ContainsKey(e.to))
            .Select(e=>walkGroups[e.to]));
        var existingGrabs=new HashSet<Tuple<string,int>>(document.edges.Where(e=>e.kind=="rope_mount"&&ropeGroups.ContainsKey(e.to))
            .Select(e=>Tuple.Create(ropeGroups[e.to],Math.Sign(nodes[e.from].x-nodes[e.to].x))));
        var proposals=new List<JumpProposal>();
        foreach(var group in walks.GroupBy(e=>walkGroups[e.from])){
            if(existingLandings.Contains(group.Key))continue;
            JumpProposal best=null;
            foreach(var upper in group)foreach(var lower in walks){
                if(walkGroups[lower.from]==group.Key)continue;
                var candidate=AcrossPlatforms(ground[lower.id],ground[upper.id]);
                if(candidate!=null&&(best==null||candidate.Cost<best.Cost))best=candidate;
            }
            if(best!=null)proposals.Add(best);
        }
        foreach(var shaft in ropes.GroupBy(e=>ropeGroups[e.from])){
            var anchors=shaft.SelectMany(e=>new[]{nodes[e.from],nodes[e.to]}).Distinct().ToArray();
            var foot=anchors.OrderByDescending(n=>n.y).First();
            if(anchors.Any(n=>n.x!=foot.x)||anchors.Min(n=>n.y)>=foot.y-1)continue;
            var grip=new Point(foot.x,foot.y-1);
            var best=new Dictionary<int,JumpProposal>();
            foreach(var walk in walks){
                for(int x=grip.X-4;x<=grip.X+4;x++){
                    int side=Math.Sign(x-grip.X);
                    if(existingGrabs.Contains(Tuple.Create(shaft.Key,side)))continue;
                    var candidate=CheckJump(AtPlatformColumn(ground[walk.id],x),grip,"rope_mount");
                    JumpProposal current;
                    if(candidate!=null&&(!best.TryGetValue(side,out current)||candidate.Cost<current.Cost))best[side]=candidate;
                }
            }
            proposals.AddRange(best.Where(pair=>pair.Key!=0||best.Count==1&&
                !existingGrabs.Contains(Tuple.Create(shaft.Key,-1))&&!existingGrabs.Contains(Tuple.Create(shaft.Key,1)))
                .OrderBy(pair=>pair.Key).Select(pair=>pair.Value));
        }

        foreach(var candidate in proposals){
            var from=MapSupport(document,candidate.Start,"walk",null);
            var to=MapSupport(document,candidate.End,candidate.Kind=="jump"?"walk":"climb",null);
            if(from==null||to==null||from==to)continue;
            // Commit each complete link as a unit. A failed connection leaves
            // the drawn platforms and ropes as they were.
            var copy=new RouteDocument{manualMap=true,graphEdited=true,nodes=document.nodes,edges=document.edges};
            copy=RouteFiles.Clone(copy);
            var edge=DrawMapLink(copy,candidate.Kind,candidate.Start,candidate.End,null,null);
            if(edge==null||document.edges.Any(link=>link.id==edge.id))continue;
            edge.suggestedJump=true;
            document.nodes=copy.nodes;
            document.edges=copy.edges;
            added.Add(edge.id);
        }
        return added;
    }

    static RouteEdge NavigationSupport(RouteDocument document,PointF position,bool target,out Point point){
        point=Point.Empty;
        var options=document.edges.Where(e=>e.kind=="walk"||e.kind=="climb")
            .Select(e=>new{Edge=e,Gap=StrokeDistance(document,e,position)})
            .Where(item=>item.Gap<=(target?3:2)).OrderBy(item=>item.Gap).Take(3).ToArray();
        if(options.Length==0)return null;
        var best=options[0];
        var projected=SnapToPath(document,position,best.Edge);
        if(options.Skip(1).Any(item=>item.Gap-best.Gap<0.25&&SnapToPath(document,position,item.Edge)!=projected))
            return null;
        point=projected;
        return best.Edge;
    }

    internal static Point? SnapNavigationTarget(RouteDocument document,PointF position){
        Point point;
        return NavigationSupport(document,position,true,out point)==null?(Point?)null:point;
    }

    internal static RouteNode NavigationPoint(RouteDocument document,PointF position,bool target){
        Point point;
        var edge=NavigationSupport(document,position,target,out point);
        if(edge==null)return null;
        if(edge.kind!="climb"||edge.ropeEntries==null)
            return AttachToPath(document,edge,point);

        if(target)return SplitNavigationRope(document,edge,point);

        // An already occupied rope has its own initial junction. Keep the
        // saved entrances and downward connection intact.
        var entry=Node(document,point.X,point.Y,false);
        var endpoints=new HashSet<string>(edge.ropeEntries){edge.from,edge.to};
        if(entry.id==edge.to||edge.ropeEntries.Contains(entry.id))return entry;
        endpoints.Remove(entry.id);
        foreach(var endpoint in endpoints){
            if(endpoint!=edge.to&&!edge.both)continue;
            var node=document.nodes.First(n=>n.id==endpoint);
            document.edges.Add(new RouteEdge{
                id="NAVI"+Guid.NewGuid().ToString("N"),kind="climb",from=entry.id,to=node.id,
                duration=edge.duration
            });
        }
        return entry;
    }

    static RouteNode SplitNavigationRope(RouteDocument document,RouteEdge edge,Point point){
        var nodes=document.nodes.ToDictionary(node=>node.id);
        var from=nodes[edge.from];var to=nodes[edge.to];
        if(point==new Point(from.x,from.y))return from;
        if(point==new Point(to.x,to.y))return to;
        int direction=Math.Sign(to.y-from.y);
        if(direction==0||(point.Y-from.y)*direction<=0||(to.y-point.Y)*direction<=0)return null;
        var junction=Node(document,point.X,point.Y,false);
        var firstEntries=edge.ropeEntries.Where(id=>(nodes[id].y-point.Y)*direction<0).ToList();
        var laterEntries=edge.ropeEntries.Where(id=>(nodes[id].y-point.Y)*direction>0).ToList();
        laterEntries.Add(junction.id);
        long firstDuration=(long)(edge.duration*(double)(point.Y-from.y)/(to.y-from.y));
        document.edges.Add(new RouteEdge{
            id="NAVI"+Guid.NewGuid().ToString("N"),kind="climb",from=junction.id,to=to.id,both=edge.both,
            duration=edge.duration-firstDuration,ropeEntries=laterEntries,
            points=new List<RoutePoint>{new RoutePoint(junction.x,junction.y),new RoutePoint(to.x,to.y)}
        });
        edge.to=junction.id;edge.ropeEntries=firstEntries;edge.duration=firstDuration;
        edge.points=new List<RoutePoint>{new RoutePoint(from.x,from.y),new RoutePoint(junction.x,junction.y)};
        return junction;
    }

    static void SplitWalkJunctions(RouteDocument d,RouteEdge chosen=null,double endpointRadius=2,bool markedOnly=false){
        foreach(var original in chosen==null?d.edges.ToArray():new[]{chosen}){
            if(chosen==null&&(original.kind!="walk"||!original.both))continue;
            var a=d.nodes.First(n=>n.id==original.from);var b=d.nodes.First(n=>n.id==original.to);
            var points=original.points.Count>=2?original.points:new List<RoutePoint>{new RoutePoint(a.x,a.y),new RoutePoint(b.x,b.y)};var offsets=new double[points.Count];for(int i=1;i<points.Count;i++)offsets[i]=offsets[i-1]+Distance(points[i-1].x,points[i-1].y,points[i].x,points[i].y);double total=offsets[offsets.Length-1];
            var middle=new List<Tuple<RouteNode,double>>();foreach(var n in d.nodes){if(n.id==a.id||n.id==b.id||Distance(a.x,a.y,n.x,n.y)<=endpointRadius||Distance(b.x,b.y,n.x,n.y)<=endpointRadius||markedOnly&&(n.marker=="rope"||n.marker=="grip"||String.IsNullOrEmpty(n.marker)))continue;double gap;double at=WalkPosition(points,n,out gap);if(gap<(markedOnly?0.55:1.2)&&at>0&&at<total)middle.Add(Tuple.Create(n,at));}
            if(middle.Count==0)continue;d.edges.Remove(original);var chain=new List<Tuple<RouteNode,double>>{Tuple.Create(a,0.0)};chain.AddRange(middle.OrderBy(n=>n.Item2));chain.Add(Tuple.Create(b,total));
            for(int i=1;i<chain.Count;i++){var begin=chain[i-1];var end=chain[i];var path=new List<RoutePoint>{new RoutePoint(begin.Item1.x,begin.Item1.y)};for(int k=1;k<points.Count-1;k++)if(offsets[k]>begin.Item2+.001&&offsets[k]<end.Item2-.001)path.Add(new RoutePoint(points[k].x,points[k].y));path.Add(new RoutePoint(end.Item1.x,end.Item1.y));d.edges.Add(new RouteEdge{id=original.id+"_"+i,from=begin.Item1.id,to=end.Item1.id,kind=original.kind,both=original.both,review=original.review,edited=original.edited,recoveredWalk=original.recoveredWalk,downApproach=original.downApproach,duration=Math.Max(1,(long)(original.duration*(end.Item2-begin.Item2)/Math.Max(1,total))),points=path});}
        }
    }
    sealed class PaintAnchor {
        public RoutePoint Point;
        public RouteNode Node;
        public RouteEdge Edge;
    }
    static PaintAnchor StrokeAnchor(RouteDocument document,RoutePoint point){
        const double radius=3;
        var node=document.nodes.OrderBy(n=>Distance(point.x,point.y,n.x,n.y)).FirstOrDefault();
        if(node!=null&&Distance(point.x,point.y,node.x,node.y)<=radius)
            return new PaintAnchor{Point=new RoutePoint(node.x,node.y),Node=node};
        var anchor=new PaintAnchor{Point=point};
        double best=radius;
        foreach(var edge in document.edges.Where(e=>e.kind=="walk"||e.kind=="climb")){
            var from=document.nodes.First(n=>n.id==edge.from);
            var to=document.nodes.First(n=>n.id==edge.to);
            var path=new List<RoutePoint>{new RoutePoint(from.x,from.y)};
            path.AddRange(edge.points.Skip(1).Take(Math.Max(0,edge.points.Count-2)));
            path.Add(new RoutePoint(to.x,to.y));
            for(int i=1;i<path.Count;i++){
                var a=path[i-1];var b=path[i];
                double dx=b.x-a.x,dy=b.y-a.y;
                double t=Math.Max(0,Math.Min(1,((point.x-a.x)*dx+(point.y-a.y)*dy)/Math.Max(1,dx*dx+dy*dy)));
                int x=(int)Math.Round(a.x+t*dx),y=(int)Math.Round(a.y+t*dy);
                double gap=Distance(point.x,point.y,x,y);
                if(gap>=best)continue;
                best=gap;anchor.Point=new RoutePoint(x,y);anchor.Edge=edge;
            }
        }
        return anchor;
    }
    static RouteNode StrokeEndpoint(RouteDocument document,RouteNode node,RoutePoint point){
        return node!=null&&point.x==node.x&&point.y==node.y&&document.nodes.Contains(node)?node:null;
    }
    static bool OnDrawnRope(RouteDocument document,PaintAnchor anchor){
        if(anchor.Edge!=null)return anchor.Edge.kind=="climb";
        if(anchor.Node==null)return false;
        string id=anchor.Node.id;
        var moves=document.edges.Where(e=>e.from==id||e.to==id).ToArray();
        return moves.Any(e=>e.kind=="climb")&&!moves.Any(e=>e.kind=="walk");
    }
    static RouteNode StrokeNode(RouteDocument document,PaintAnchor anchor){
        if(anchor.Node!=null)return anchor.Node;
        var node=new RouteNode{id="N"+Guid.NewGuid().ToString("N"),x=anchor.Point.x,y=anchor.Point.y};
        document.nodes.Add(node);
        return node;
    }
    public static int GeneratePaintedActions(RouteDocument document){
        if(document.manualMap)return 0;
        var painted=document.edges.Where(e=>e.edited&&e.duration<=1&&e.kind=="jump"&&e.points.Count>2).ToArray();
        if(painted.Length==0)return 0;
        var nodes=document.nodes.ToDictionary(n=>n.id);
        var ladders=document.edges.Where(e=>e.kind=="climb").ToArray();
        int count=0;
        foreach(var jump in painted){
            var start=nodes[jump.from];var end=nodes[jump.to];
            if(start.y<=end.y)continue;
            var approach=jump.points.Skip(Math.Max(1,jump.points.Count-4)).Take(Math.Min(3,jump.points.Count-2));
            bool entersRope=ladders.Where(e=>e.from==end.id||e.both&&e.to==end.id)
                .Select(e=>nodes[e.from==end.id?e.to:e.from]).Any(top=>top.y<end.y&&approach.Any(p=>
                    p.y<end.y&&PointLine(p.x,p.y,end.x,end.y,top.x,top.y)<=1.25));
            if(!entersRope)continue;
            jump.kind="rope_mount";
            jump.jumpDirection=Math.Abs(end.x-start.x)<=1?0:end.x<start.x?37:39;
            count++;
        }
        return count;
    }
    internal static List<RoutePoint> NormalizeWalkingSweep(List<RoutePoint> points){
        int left=points.Min(p=>p.x),right=points.Max(p=>p.x);
        long travel=points.Zip(points.Skip(1),(a,b)=>Math.Abs((long)b.x-a.x)).Sum();
        if(right-left<2||travel-(right-left)<2)return points;
        var columns=points.GroupBy(p=>p.x).OrderBy(column=>column.Key).ToArray();
        if(columns.Any(column=>column.Max(p=>p.y)-column.Min(p=>p.y)>2))return points;
        var span=columns.Select(column=>new RoutePoint(column.Key,
            column.Select(p=>p.y).OrderBy(y=>y).ElementAt(column.Count()/2))).ToList();
        if(!CanWalkBoth(span))return points;
        if(points.FindIndex(p=>p.x==right)<points.FindIndex(p=>p.x==left))span.Reverse();
        return span;
    }
    public static RouteEdge DrawStroke(RouteDocument document,string brush,IList<RoutePoint> stroke,
        RouteNode fromNode=null,RouteNode toNode=null){
        if(stroke.Count<2||brush!="walk"&&brush!="climb"&&brush!="jump"&&brush!="drop")return null;
        var points=stroke.Select(p=>new RoutePoint(p.x,p.y)).ToList();
        if(brush=="walk")points=NormalizeWalkingSweep(points);
        var first=points[0];var last=points[points.Count-1];
        fromNode=StrokeEndpoint(document,fromNode,first);
        toNode=StrokeEndpoint(document,toNode,last);
        if(brush=="climb"){
            int axis=fromNode!=null?fromNode.x:toNode!=null?toNode.x:(first.x+last.x)/2;
            if(fromNode==null)first.x=axis;
            if(toNode==null)last.x=axis;
        }
        if(brush=="drop"&&toNode==null)last.x=first.x;
        var start=fromNode==null?StrokeAnchor(document,first):new PaintAnchor{Point=first,Node=fromNode};
        var end=toNode==null?StrokeAnchor(document,last):new PaintAnchor{Point=last,Node=toNode};
        int dx=end.Point.x-start.Point.x,dy=end.Point.y-start.Point.y;
        int minimum=start.Node!=null&&end.Node!=null?1:2;
        if(Distance(start.Point.x,start.Point.y,end.Point.x,end.Point.y)<minimum||
           brush=="walk"&&Math.Abs(dx)<minimum||brush=="climb"&&Math.Abs(dy)<minimum||brush=="drop"&&dy<minimum)return null;
        string kind=brush;
        if(brush=="jump"){
            bool fromRope=OnDrawnRope(document,start),toRope=OnDrawnRope(document,end);
            kind=toRope&&!fromRope&&dy<0?"rope_mount":fromRope&&!toRope?"rope_exit":dy>1?"jump_down":"jump";
        }
        var from=StrokeNode(document,start);var to=StrokeNode(document,end);
        foreach(var edge in new[]{start.Edge,end.Edge}.Where(e=>e!=null).Distinct())SplitWalkJunctions(document,edge);
        points[0]=new RoutePoint(from.x,from.y);points[points.Count-1]=new RoutePoint(to.x,to.y);
        if(brush!="jump")points=new List<RoutePoint>{points[0],points[points.Count-1]};
        var result=new RouteEdge{id="E"+Guid.NewGuid().ToString("N"),from=from.id,to=to.id,kind=kind,
            both=brush=="walk"||brush=="climb",edited=true,duration=1,points=Simplify(points,0.7)};
        if(kind=="rope_mount"||kind=="rope_exit")result.jumpDirection=Math.Abs(dx)<=1?0:dx<0?37:39;
        document.edges.Add(result);
        if(kind=="walk"||kind=="climb")SplitWalkJunctions(document,result);
        document.graphEdited=true;
        GeneratePaintedActions(document);
        return document.edges.First(edge=>edge.id==result.id||edge.id.StartsWith(result.id+"_",StringComparison.Ordinal));
    }
    internal static List<RoutePoint> ResizeEdge(RouteEdge edge,RouteNode from,RouteNode to,Point start,Point end){
        var points=new List<RoutePoint>{new RoutePoint(start.X,start.Y)};
        if(edge.kind=="drop"){points.Add(new RoutePoint(end.X,end.Y));return points;}
        double dx=to.x-from.x,dy=to.y-from.y;
        double lengthSquared=dx*dx+dy*dy;
        for(int i=1;i<edge.points.Count-1;i++){
            var point=edge.points[i];
            double progress=lengthSquared==0?(double)i/(edge.points.Count-1):
                Math.Max(0,Math.Min(1,((point.x-from.x)*dx+(point.y-from.y)*dy)/lengthSquared));
            int x=point.x+(int)Math.Round((1-progress)*(start.X-from.x)+progress*(end.X-to.x));
            int y=point.y+(int)Math.Round((1-progress)*(start.Y-from.y)+progress*(end.Y-to.y));
            points.Add(new RoutePoint(x,y));
        }
        points.Add(new RoutePoint(end.X,end.Y));
        return points;
    }
    internal static RouteNode MoveRouteEndpoint(RouteDocument document,RouteEdge edge,RouteNode node,Point destination){
        if(!document.nodes.Contains(node)||destination==new Point(node.x,node.y))return null;
        var links=document.edges.Where(e=>e.from==node.id||e.to==node.id).ToArray();
        bool grip=document.edges.Any(e=>e.ropeEntries!=null&&e.ropeEntries.Contains(node.id));
        var existing=document.nodes.FirstOrDefault(n=>n.x==destination.X&&n.y==destination.Y);
        if(edge==null){
            if(links.Length>1)return null;
            edge=links.FirstOrDefault();
            if(edge==null){
                if(grip||existing!=null)return null;
                node.x=destination.X;node.y=destination.Y;document.graphEdited=true;
                return node;
            }
        }
        if(!links.Contains(edge))return null;
        var from=document.nodes.First(n=>n.id==edge.from);
        var to=document.nodes.First(n=>n.id==edge.to);
        var start=from==node?destination:new Point(from.x,from.y);
        var end=to==node?destination:new Point(to.x,to.y);
        if(start==end||edge.kind=="walk"&&start.X==end.X||edge.kind=="drop"&&start.Y>=end.Y)return null;
        var endpoint=existing??(links.Length>1||grip?Node(document,destination.X,destination.Y,false):node);
        edge.points=ResizeEdge(edge,from,to,start,end);
        int heading=Math.Sign(end.X-start.X);
        if(Math.Sign(to.x-from.x)!=heading&&(edge.observedJump||edge.kind=="rope_mount"||edge.kind=="rope_exit"))
            edge.jumpDirection=heading<0?37:heading>0?39:0;
        if(from==node)edge.from=endpoint.id;else edge.to=endpoint.id;
        if(edge.ropeEntries!=null){
            if(from==node&&edge.ropeEntries.Remove(node.id)&&!edge.ropeEntries.Contains(endpoint.id))
                edge.ropeEntries.Add(endpoint.id);
            edge.ropeEntries.Remove(edge.to);
        }
        endpoint.x=destination.X;endpoint.y=destination.Y;
        if(endpoint!=node&&!document.edges.Any(e=>e.from==node.id||e.to==node.id||e.ropeEntries!=null&&e.ropeEntries.Contains(node.id)))
            document.nodes.Remove(node);
        edge.edited=document.graphEdited=true;
        return endpoint;
    }
    static double PointLine(double x,double y,double ax,double ay,double bx,double by){double dx=bx-ax,dy=by-ay,t=((x-ax)*dx+(y-ay)*dy)/Math.Max(0.0001,dx*dx+dy*dy);t=Math.Max(0,Math.Min(1,t));return Math.Sqrt(Math.Pow(x-(ax+t*dx),2)+Math.Pow(y-(ay+t*dy),2));}
    public static double StrokeDistance(RouteDocument document,RouteEdge edge,PointF point){
        var from=document.nodes.First(n=>n.id==edge.from);
        var to=document.nodes.First(n=>n.id==edge.to);
        double gap=Double.PositiveInfinity;int x=from.x,y=from.y;
        for(int i=1;i<edge.points.Count-1;i++){
            var next=edge.points[i];
            gap=Math.Min(gap,PointLine(point.X,point.Y,x,y,next.x,next.y));x=next.x;y=next.y;
        }
        return Math.Min(gap,PointLine(point.X,point.Y,x,y,to.x,to.y));
    }
    static List<RoutePoint> Simplify(List<RoutePoint> pts,double tolerance){if(pts.Count<3)return pts;var keep=new bool[pts.Count];keep[0]=keep[pts.Count-1]=true;var pending=new Stack<Tuple<int,int>>();pending.Push(Tuple.Create(0,pts.Count-1));while(pending.Count>0){var part=pending.Pop();int far=0;double max=0;for(int i=part.Item1+1;i<part.Item2;i++){double n=PointLine(pts[i].x,pts[i].y,pts[part.Item1].x,pts[part.Item1].y,pts[part.Item2].x,pts[part.Item2].y);if(n>max){max=n;far=i;}}if(max>tolerance){keep[far]=true;pending.Push(Tuple.Create(part.Item1,far));pending.Push(Tuple.Create(far,part.Item2));}}return pts.Where((p,i)=>keep[i]).ToList();}
    public static List<RouteEdge> Plan(RouteDocument d,string from,string to){
        var dist=d.nodes.ToDictionary(n=>n.id,n=>Double.PositiveInfinity);var prior=new Dictionary<string,RouteEdge>();var prevNode=new Dictionary<string,string>();var used=new HashSet<string>();if(!dist.ContainsKey(from??"")||!dist.ContainsKey(to??""))return null;dist[from]=0;
        while(used.Count<dist.Count){string here=null;double best=Double.PositiveInfinity;foreach(var item in dist)if(!used.Contains(item.Key)&&item.Value<best){here=item.Key;best=item.Value;}if(here==null)break;if(here==to)break;used.Add(here);
            foreach(var edge in d.edges){string next=Arrival(edge,here);if(next==null)continue;var a=d.nodes.First(n=>n.id==here);var b=d.nodes.First(n=>n.id==next);double cost=Math.Max(1,Distance(a.x,a.y,b.x,b.y))+(edge.kind=="walk"?0:10);if(best+cost<dist[next]){dist[next]=best+cost;prior[next]=edge;prevNode[next]=here;}}
        }
        if(Double.IsPositiveInfinity(dist[to]))return null;var result=new List<RouteEdge>();string cur=to;while(cur!=from){result.Add(prior[cur]);cur=prevNode[cur];}result.Reverse();return result;
    }
    public static List<string> Check(RouteDocument d){
        var issues=new List<string>();if(d.samples.Count==0)issues.Add("尚未录制位置");if(d.gaps>0)issues.Add("有 "+d.gaps+" 处断段，请核对连接，不会自动跨越");
        foreach(var n in d.nodes)if(!d.edges.Any(e=>e.from==n.id||e.to==n.id))issues.Add(n.id+" 是孤立点");
        int review=d.edges.Count(e=>e.review||e.edited);if(review>0)issues.Add(review+" 条动作或人工连接需核对落点/方向");
        if(d.nodes.Count>1){string origin=d.nodes[0].id;var forward=Reachable(d,origin,false);var reverse=Reachable(d,origin,true);int unreachable=d.nodes.Count(n=>!forward.Contains(n.id)),noReturn=d.nodes.Count(n=>forward.Contains(n.id)&&!reverse.Contains(n.id));if(unreachable>0)issues.Add(unreachable+" 个点从起点不可达");if(noReturn>0)issues.Add(noReturn+" 个点没有已录制回程");}
        RouteSample start=null,last=null;foreach(var s in d.samples){if(start==null||last.segment!=s.segment||Distance(start.x,start.y,s.x,s.y)>1)start=s;else if(s.time-start.time>=2000){issues.Add("第 "+s.segment+" 段 ("+s.x+","+s.y+") 停留超过2秒，请检查是否卡住或有意等待");start=null;}last=s;}
        if(issues.Count==0)issues.Add("结构检查通过；实际通行仍需游戏验证");return issues.Distinct().Take(100).ToList();
    }
    static HashSet<string> Reachable(RouteDocument d,string start,bool reverse){var adj=d.nodes.ToDictionary(n=>n.id,n=>new List<string>());foreach(var e in d.edges)foreach(var from in Departures(e)){var to=Arrival(e,from);adj[reverse?to:from].Add(reverse?from:to);}var found=new HashSet<string>{start};var q=new Queue<string>();q.Enqueue(start);while(q.Count>0)foreach(string next in adj[q.Dequeue()])if(found.Add(next))q.Enqueue(next);return found;}
}
public sealed class DotResult {public bool ok;public int x,y;public string reason;public Color color;}
public sealed class MinimapLayout {
    readonly RouteDocument calibration;
    public string SourceHost {get{return calibration.sourceHost??"";}}
    readonly object gate=new object();
    Size matchedSize;
    Rectangle matchedArea;
    public Rectangle CalibratedArea{get{return new Rectangle(calibration.roiX,calibration.roiY,calibration.roiWidth,calibration.roiHeight);}}
    public MinimapLayout(RouteDocument calibration){this.calibration=calibration;}
    static bool Valid(Rectangle area,Size size){return area.Width>=24&&area.Height>=24&&area.Width<=1024&&area.Height<=800&&new Rectangle(Point.Empty,size).Contains(area);}
    public bool TryArea(long window,int width,int height,out Rectangle area){
        var size=new Size(width,height);
        lock(gate){
            bool matched=size==matchedSize;
            area=matched?matchedArea:CalibratedArea;
            return window!=0&&window==calibration.window&&
                (matched||size==new Size(calibration.sourceWidth,calibration.sourceHeight))&&Valid(area,size);
        }
    }
    public Rectangle Resolve(Dictionary<string,object> frame,byte[] reference){
        int w=Wire.Int(frame,"width"),h=Wire.Int(frame,"height");
        long window=Wire.Long(frame,"window");
        Rectangle area;
        if(TryArea(window,w,h,out area))return area;
        var original=CalibratedArea;
        if(window==0||window!=calibration.window||calibration.sourceWidth<=0||calibration.sourceHeight<=0||
           !Valid(original,new Size(calibration.sourceWidth,calibration.sourceHeight))||
           reference==null||reference.Length!=original.Width*original.Height*3)return Rectangle.Empty;
        var pixels=(byte[])frame["bgr24"];
        // HUDs may keep their original pixel size or scale with the game window.
        var size=new Size(w,h);
        double sx=(double)w/calibration.sourceWidth,sy=(double)h/calibration.sourceHeight;
        var candidates=new HashSet<Rectangle>();
        foreach(var scale in new[]{new SizeF(1,1),new SizeF((float)sx,(float)sy),new SizeF((float)Math.Min(sx,sy),(float)Math.Min(sx,sy))}){
            int mw=(int)Math.Round(original.Width*scale.Width),mh=(int)Math.Round(original.Height*scale.Height);
            int left=Anchored(original.Left,original.Right,calibration.sourceWidth,w,mw,scale.Width);
            int top=Anchored(original.Top,original.Bottom,calibration.sourceHeight,h,mh,scale.Height);
            candidates.Add(new Rectangle(left,top,mw,mh));
            candidates.Add(new Rectangle((int)Math.Round(original.X*sx),(int)Math.Round(original.Y*sy),mw,mh));
        }
        var verifier=new MiniTracker();
        verifier.Seed(reference,original.Width,original.Height,0,0);
        var matches=new List<KeyValuePair<Rectangle,double>>();
        foreach(var candidate in candidates.Where(c=>Valid(c,size))){
            var data=Resample(Crop(pixels,w,candidate),candidate.Width,candidate.Height,original.Width,original.Height);
            int dx,dy;double error;
            if(verifier.Register(data,original.Width,original.Height,out dx,out dy,out error,true))
                matches.Add(new KeyValuePair<Rectangle,double>(candidate,error+0.06*(Math.Abs(dx)+Math.Abs(dy))));
        }
        matches.Sort((a,b)=>a.Value.CompareTo(b.Value));
        if(matches.Count==0)return Rectangle.Empty;
        var best=matches[0];
        if(matches.Skip(1).Any(m=>m.Value-best.Value<0.65&&!Close(best.Key,m.Key)))return Rectangle.Empty;
        lock(gate){matchedSize=size;matchedArea=best.Key;}
        return best.Key;
    }
    static int Anchored(int start,int end,int previous,int current,int length,double scale){
        return start<=previous-end?(int)Math.Round(start*scale):current-length-(int)Math.Round((previous-end)*scale);
    }
    static bool Close(Rectangle a,Rectangle b){
        return Math.Abs(a.Left-b.Left)<=4&&Math.Abs(a.Top-b.Top)<=4&&Math.Abs(a.Right-b.Right)<=4&&Math.Abs(a.Bottom-b.Bottom)<=4;
    }
    public Dictionary<string,object> Extract(Dictionary<string,object> frame,byte[] reference){
        var area=Resolve(frame,reference);
        if(area.IsEmpty)return null;
        int w=Wire.Int(frame,"width"),h=Wire.Int(frame,"height");
        var result=new Dictionary<string,object>(frame);
        result["native_width"]=w;result["native_height"]=h;
        result["width"]=area.Width;result["height"]=area.Height;
        result["minimap_roi"]=new[]{area.X,area.Y,area.Width,area.Height};
        result["bgr24"]=Crop((byte[])frame["bgr24"],w,area);
        return result;
    }
    internal static bool CursorMayCoverMinimap(Dictionary<string,object> frame){
        if(!Wire.Bool(frame,"cursor_in_game"))return false;
        object samples,roi;
        if(!frame.TryGetValue("cursor_samples",out samples)||samples==null||
           !frame.TryGetValue("minimap_roi",out roi)||roi==null)return true;
        var points=(samples as int[])??Wire.IntArray(samples);
        var bounds=(roi as int[])??Wire.IntArray(roi);
        if(points.Length!=4||bounds.Length!=4)return true;
        // The visible pointer can extend beyond its hotspot.
        var area=Rectangle.Inflate(new Rectangle(bounds[0],bounds[1],bounds[2],bounds[3]),16,16);
        return area.Contains(points[0],points[1])||area.Contains(points[2],points[3]);
    }
    public static byte[] Crop(byte[] pixels,int width,Rectangle area){
        var data=new byte[area.Width*area.Height*3];
        for(int y=0;y<area.Height;y++)Buffer.BlockCopy(pixels,((y+area.Top)*width+area.Left)*3,data,y*area.Width*3,area.Width*3);
        return data;
    }
    public static int ScalePixel(int coordinate,int from,int to){return (int)Math.Floor((coordinate+0.5)*to/from);}
    static byte[] Resample(byte[] pixels,int w,int h,int targetWidth,int targetHeight){
        if(w==targetWidth&&h==targetHeight)return pixels;
        var data=new byte[targetWidth*targetHeight*3];
        var columns=Enumerable.Range(0,targetWidth).Select(x=>ScalePixel(x,targetWidth,w)*3).ToArray();
        for(int y=0;y<targetHeight;y++){
            int row=ScalePixel(y,targetHeight,h)*w*3;
            for(int x=0;x<targetWidth;x++){
                int source=row+columns[x],target=(y*targetWidth+x)*3;
                data[target]=pixels[source];data[target+1]=pixels[source+1];data[target+2]=pixels[source+2];
            }
        }
        return data;
    }
    public static byte[] ReferencePixels(RouteDocument d){
        using(var image=RouteFiles.Png(d.lastMiniPng)){
            if(image==null)return null;
            if(image.Width!=d.roiWidth||image.Height!=d.roiHeight)throw new InvalidDataException("小地图底图尺寸无效");
            return Wire.Pixels(image);
        }
    }
    public static RouteDocument Calibrate(Dictionary<string,object> frame,Rectangle area,Point dot,Color color,RouteDocument previous){
        int w=Wire.Int(frame,"width"),h=Wire.Int(frame,"height");
        var data=Crop((byte[])frame["bgr24"],w,area);
        using(var bitmap=Wire.BitmapFrom(data,area.Width,area.Height)){
            return new RouteDocument{name=previous.name,jumpKey=previous.jumpKey,teleportKey=previous.teleportKey,
                window=Wire.Long(frame,"window"),sourceHost=Wire.Str(frame,"source_host"),sourceWidth=w,sourceHeight=h,
                roiX=area.X,roiY=area.Y,roiWidth=area.Width,roiHeight=area.Height,
                dotR=color.R,dotG=color.G,dotB=color.B,dotSeedX=dot.X-area.X,dotSeedY=dot.Y-area.Y,
                lastMiniPng=RouteFiles.EncodePng(bitmap)};
        }
    }
    internal static DotResult AutoPick(Dictionary<string,object> frame,Rectangle area){
        int width=Wire.Int(frame,"width"),height=Wire.Int(frame,"height");
        if(!Valid(area,new Size(width,height)))return new DotResult{reason="请框选有效的小地图"};
        var pixels=Crop((byte[])frame["bgr24"],width,area);
        var result=MiniTracker.Locate(pixels,area.Width,area.Height);
        if(result.ok){result.x+=area.X;result.y+=area.Y;}
        return result;
    }

    public static DotResult Pick(Dictionary<string,object> frame,Rectangle area,Point clicked,double previewScale){
        int w=Wire.Int(frame,"width"),h=Wire.Int(frame,"height");
        if(!Valid(area,new Size(w,h)))return new DotResult{reason="请框选有效的小地图"};
        int radius=(int)Math.Min(72,Math.Max(18,Math.Ceiling(20/Math.Max(0.01,previewScale))));
        var data=Crop((byte[])frame["bgr24"],w,area);
        var result=MiniTracker.Snap(data,area.Width,area.Height,new Point(clicked.X-area.X,clicked.Y-area.Y),radius);
        if(result.ok){result.x+=area.X;result.y+=area.Y;}
        return result;
    }
    public static byte[] ForTracking(byte[] pixels,int w,int h,RouteDocument d){return Resample(pixels,w,h,d.roiWidth,d.roiHeight);}
}
public sealed class MiniTracker {
    static readonly Color PlayerDotColor=Color.FromArgb(255,255,136);
    byte[] previous;int width,height;public int offsetX,offsetY;
    public byte[] LastFrame{get{return previous;}}
    public void Seed(byte[] data,int w,int h,int x,int y){previous=data;width=w;height=h;offsetX=x;offsetY=y;}
    public void Reset(){previous=null;width=height=offsetX=offsetY=0;}
    static bool Yellow(byte[] data,int at){
        int blue=data[at],green=data[at+1],red=data[at+2];
        return red>=205&&green>=185&&Math.Abs(red-green)<=48&&Math.Min(red,green)-blue>=52;
    }
    static List<DotResult> Components(byte[] data,int w,int h,Func<int,bool> match,double scale=1){
        int maxSpan=(int)Math.Ceiling(14*scale),maxCount=(int)Math.Ceiling(120*scale*scale);
        int minCount=scale<0.8?1:2;
        var seen=new bool[w*h];
        var points=new List<DotResult>();
        var queue=new Queue<int>();
        for(int p=0;p<w*h;p++){
            if(seen[p]||!match(p*3))continue;
            seen[p]=true;queue.Enqueue(p);
            int count=0,minX=w,minY=h,maxX=0,maxY=0;
            var colors=new Dictionary<int,int>();
            long sumX=0,sumY=0;
            while(queue.Count>0){
                int id=queue.Dequeue(),x=id%w,y=id/w,at=id*3;
                count++;sumX+=x;sumY+=y;
                int rgb=data[at+2]<<16|data[at+1]<<8|data[at],frequency;
                colors.TryGetValue(rgb,out frequency);colors[rgb]=frequency+1;
                minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
                for(int yy=Math.Max(0,y-1);yy<=Math.Min(h-1,y+1);yy++)for(int xx=Math.Max(0,x-1);xx<=Math.Min(w-1,x+1);xx++){
                    int next=yy*w+xx;
                    if(!seen[next]&&match(next*3)){seen[next]=true;queue.Enqueue(next);}
                }
            }
            if(count>=minCount&&count<=maxCount&&maxX-minX<=maxSpan&&maxY-minY<=maxSpan){
                int rgb=colors.OrderByDescending(pair=>pair.Value).First().Key;
                points.Add(new DotResult{ok=true,x=(int)Math.Round((double)sumX/count),y=(int)Math.Round((double)sumY/count),color=Color.FromArgb(unchecked((int)0xff000000)|rgb)});
            }
        }
        return points;
    }
    static double Distance(DotResult dot,Point point){return RouteGraph.Distance(dot.x,dot.y,point.X,point.Y);}
    public static DotResult Snap(byte[] data,int w,int h,Point clicked,int radius){
        var points=Components(data,w,h,at=>Yellow(data,at),1.5)
            .Select(dot=>new{Dot=dot,Distance=Distance(dot,clicked)}).Where(c=>c.Distance<=radius)
            .OrderBy(c=>c.Distance).Take(2).ToArray();
        if(points.Length==0)return new DotResult{reason="附近没有找到黄点，请点近一些"};
        if(points.Length>1&&points[1].Distance-points[0].Distance<4)
            return new DotResult{reason="附近有多个黄点，请点靠近角色的一个"};
        return points[0].Dot;
    }
    public static DotResult Locate(byte[] pixels,int width,int height,Color? calibratedColor=null){
        var color=calibratedColor??PlayerDotColor;
        int red=color.R,green=color.G,blue=color.B,count=0,index=0;
        long sumX=0,sumY=0;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++,index+=3){
            if(pixels[index]!=blue||pixels[index+1]!=green||pixels[index+2]!=red)continue;
            count++;sumX+=x;sumY+=y;
        }
        if(count<4)return new DotResult{reason="没有找到人物黄点"};
        return new DotResult{ok=true,x=(int)Math.Round((double)sumX/count),
            y=(int)Math.Round((double)sumY/count),color=color};
    }
    static bool Marker(byte[] b,int i){return b[i+2]>170&&((b[i+1]>170&&b[i]<210)||(b[i+1]<110&&b[i]<110));}
    public bool Register(byte[] current,int w,int h,out int dx,out int dy,out double error,bool requireTexture=false){
        dx=dy=0;error=0;if(previous==null||w!=width||h!=height)return true;var samples=new List<int>();
        int stride=Math.Max(2,(int)Math.Sqrt(w*h/260.0));for(int y=3;y<h-3;y+=stride)for(int x=3;x<w-3;x+=stride){int p=(y*w+x)*3;if(Marker(previous,p))continue;int grad=0;for(int c=0;c<3;c++)grad+=Math.Abs(previous[p+c]-previous[p+3+c])+Math.Abs(previous[p+c]-previous[p+w*3+c]);if(grad>35)samples.Add(p/3);}
        if(samples.Count<12){if(requireTexture)return false;for(int y=3;y<h-3;y+=Math.Max(3,stride))for(int x=3;x<w-3;x+=Math.Max(3,stride))if(!Marker(previous,(y*w+x)*3))samples.Add(y*w+x);error=Score(current,w,h,samples,0,0);return error<3;}
        double zero=Score(current,w,h,samples,0,0);if(zero<0.9){error=zero;return true;}double best=Double.PositiveInfinity;int bx=0,by=0;var scores=new List<Tuple<int,int,double>>();
        for(int y=-12;y<=12;y++)for(int x=-12;x<=12;x++){double v=Score(current,w,h,samples,x,y);scores.Add(Tuple.Create(x,y,v));if(v<best){best=v;bx=x;by=y;}}
        double second=scores.Where(s=>Math.Abs(s.Item1-bx)+Math.Abs(s.Item2-by)>2).Min(s=>s.Item3);
        if(best>9||second-best<0.6)return false;dx=bx;dy=by;error=best;return true;
    }
    double Score(byte[] current,int w,int h,List<int> samples,int dx,int dy){double sum=0;int count=0;foreach(int p in samples){int x=p%w-dx,y=p/w-dy;if(x<1||y<1||x>=w-1||y>=h-1)continue;int a=p*3,b=(y*w+x)*3;if(Marker(current,b))continue;double s=0;for(int c=0;c<3;c++)s+=Math.Abs(previous[a+c]-current[b+c]);sum+=Math.Min(45,s/3);count++;}return count<Math.Max(8,samples.Count*0.65)?999:sum/count;}
    public DotResult Process(byte[] data,int w,int h,RouteDocument d){
        int cw=d.roiWidth,ch=d.roiHeight,dx,dy;
        var background=MinimapLayout.ForTracking(data,w,h,d);
        double error;
        if(!Register(background,cw,ch,out dx,out dy,out error))return new DotResult{reason="小地图背景失配或发生换图，录制已暂停"};
        var dot=Locate(background,cw,ch);
        // Older maps and profiles may contain a manually sampled marker color.
        if(!dot.ok&&d.dotR>=0&&d.dotR<=255&&d.dotG>=0&&d.dotG<=255&&d.dotB>=0&&d.dotB<=255){
            var savedColor=Color.FromArgb(d.dotR,d.dotG,d.dotB);
            if(savedColor.ToArgb()!=PlayerDotColor.ToArgb())dot=Locate(background,cw,ch,savedColor);
        }
        if(!dot.ok)return dot;
        offsetX+=dx;offsetY+=dy;
        previous=background==data?(byte[])data.Clone():background;width=cw;height=ch;
        return dot;
    }
}
public sealed class RouteEngine {
    public RouteDocument Document;public readonly MiniTracker Tracker=new MiniTracker();public readonly MinimapLayout Layout;public bool Recording;public string Status="先选择游戏窗口并标定小地图";
    public long lastSequence=-1,lastEvent,lastTime,lastGood,watchEpoch;public int Segment;bool needsBreak;long startWait=-1,jumpAt;int lastHeldMask,priorX,priorY,stableY,climbX,jumpStartY,jumpSampleStart;bool hasPosition,jumpActive,jumpDrop,rising,falling,climbActive;
    bool jumpFromRope, mountCandidate;
    long alignedSince=-1;
    int alignedSample,alignedX,alignedY,sampleJumpKeys=-1;
    byte[] atlas;int atlasW,atlasH,atlasX,atlasY;
    public RouteEngine(RouteDocument document){
        Document=document;Layout=new MinimapLayout(document);
        Segment=document.samples.Count==0?0:document.samples.Max(s=>s.segment)+1;
        var last=document.samples.LastOrDefault();
        lastTime=last==null?0:last.time;
        using(var map=RouteFiles.Map(document)){if(map!=null){atlas=Wire.Pixels(map);atlasW=map.Width;atlasH=map.Height;atlasX=document.mapX;atlasY=document.mapY;}}
        var reference=MinimapLayout.ReferencePixels(document);
        Tracker.Seed(reference,document.roiWidth,document.roiHeight,document.lastOffsetX,document.lastOffsetY);
    }
    public void Start(){Recording=true;needsBreak=true;lastSequence=-1;lastGood=0;watchEpoch=0;startWait=-1;lastHeldMask=0;hasPosition=jumpActive=climbActive=false;Status="等待指定窗口的按键观察与新画面…";}
    public void Pause(string reason){if(Recording){Recording=false;needsBreak=true;Segment++;}Status=reason;}
    public void Process(Dictionary<string,object> frame,long elapsed){
        if(!Recording)return;if(startWait<0)startWait=elapsed;long sequence=Wire.Long(frame,"sequence");if(sequence==lastSequence)return;lastSequence=sequence;
        Rectangle area;
        if(!Layout.TryArea(Wire.Long(frame,"window"),Wire.Int(frame,"native_width"),Wire.Int(frame,"native_height"),out area)){
            Pause("目标已变更或无法确认小地图位置");return;
        }
        var input=frame.ContainsKey("route_input")?frame["route_input"] as Dictionary<string,object>:null;
        if(input==null||!Wire.Bool(input,"enabled")||Wire.Long(input,"window")!=Document.window){Status="等待按键观察就绪…";if(elapsed-startWait>3000)Pause("按键观察未就绪，请检查连接后继续");return;}
        if(!Wire.Bool(input,"foreground")){Pause("游戏窗口失去前台，已暂停录制");return;}
        if(Wire.Bool(frame,"cursor_in_game")){Pause("电脑1鼠标已回到游戏窗口，录制已暂停");return;}
        long epoch=Wire.Long(input,"epoch");if(watchEpoch!=epoch){watchEpoch=epoch;lastEvent=0;needsBreak=true;}
        int heldMask=0;object values;if(input.TryGetValue("down",out values))foreach(object item in (System.Collections.IEnumerable)values)heldMask|=Mask(Convert.ToInt32(item));
        int mask=heldMask;bool jumpPressed=(heldMask&16)!=0&&(lastHeldMask&16)==0,dropPressed=(heldMask&8)!=0;int jumpKeys=heldMask;var fresh=new List<Dictionary<string,object>>();
        if(input.TryGetValue("events",out values))foreach(object item in (System.Collections.IEnumerable)values){var ev=item as Dictionary<string,object>;if(ev!=null&&Wire.Long(ev,"id")>lastEvent)fresh.Add(ev);}
        int state=heldMask;for(int i=fresh.Count-1;i>=0;i--){int bit=Mask(Wire.Int(fresh[i],"vk"));if(Wire.Bool(fresh[i],"down"))state&=~bit;else state|=bit;}
        foreach(var ev in fresh){long id=Wire.Long(ev,"id");if(id>lastEvent+1){Document.gaps++;Pause("录制中断，请继续当前段");return;}lastEvent=id;int vk=Wire.Int(ev,"vk"),bit=Mask(vk);bool down=Wire.Bool(ev,"down");if(down&&vk==Document.jumpKey){jumpPressed=true;dropPressed=(state&8)!=0;jumpKeys=state|16;}if(down){state|=bit;mask|=bit;}else state&=~bit;
            long source=Wire.Long(ev,"time"),when=elapsed;DateTime captured;if(source>0&&DateTime.TryParse(Wire.Str(frame,"captured_utc"),null,System.Globalization.DateTimeStyles.RoundtripKind,out captured))when=elapsed-Math.Max(0,captured.ToUniversalTime().Ticks/TimeSpan.TicksPerMillisecond-source);Document.events.Add(new RouteKeyEvent{id=Document.events.Count+1,time=Math.Max(lastTime,when),sourceTime=source,vk=vk,down=down});}
        lastHeldMask=heldMask;
        var pixels=(byte[])frame["bgr24"];
        if(pixels.Length!=area.Width*area.Height*3){Pause("小地图区域已改变");return;}
        var dot=Tracker.Process(pixels,area.Width,area.Height,Document);
        if(!dot.ok){if(!needsBreak){Document.gaps++;Segment++;needsBreak=true;}hasPosition=jumpActive=climbActive=false;Status=dot.reason;if(dot.reason.Contains("背景")||elapsed-(lastGood>0?lastGood:startWait)>1200)Pause(dot.reason);return;}
        lastGood=elapsed;int x=dot.x+Tracker.offsetX,y=dot.y+Tracker.offsetY;var previous=Document.samples.LastOrDefault();
        if(previous!=null&&!needsBreak&&RouteGraph.Distance(x,y,previous.x,previous.y)>45&&!jumpActive){Document.gaps++;Segment++;needsBreak=true;Status="位置突变，已断段，请核对";}
        string kind=Movement(x,y,heldMask,jumpPressed,dropPressed,jumpKeys,elapsed);
        if(needsBreak){Segment++;needsBreak=false;}
        if(previous==null||previous.segment!=Segment||previous.x!=x||previous.y!=y||previous.keys!=mask||previous.kind!=kind||sampleJumpKeys>=0||elapsed-previous.time>=250){
            Document.samples.Add(new RouteSample{time=Math.Max(lastTime,elapsed),frame=sequence,x=x,y=y,segment=Segment,
                keys=mask,jumpKeys=sampleJumpKeys,jumpVk=sampleJumpKeys>=0?Document.jumpKey:0,kind=kind});
            lastTime=Math.Max(lastTime,elapsed);
        }
        if(Document.samples.Count>=100000||Document.events.Count>=100000){Pause("本条路线记录已满，请保存后新建");return;}
        UpdateAtlas(Tracker.LastFrame,Document.roiWidth,Document.roiHeight,Tracker.offsetX,Tracker.offsetY,dot.x,dot.y);Status="录制中 · "+Document.samples.Count+" 点 · ("+x+","+y+") · "+RouteGraph.KindName(kind);
    }
    string Movement(int x,int y,int held,bool pressed,bool drop,int jumpKeys,long elapsed){
        int dx=hasPosition?x-priorX:0,dy=hasPosition?y-priorY:0;
        sampleJumpKeys=-1;
        if(!jumpActive&&pressed){
            jumpActive=true;
            jumpFromRope=climbActive&&Math.Abs(priorX-climbX)<=2;
            jumpDrop=drop;
            jumpAt=elapsed;
            jumpStartY=hasPosition?priorY:y;
            stableY=0;
            rising=falling=false;
            jumpSampleStart=Document.samples.Count;
            if(jumpFromRope&&(jumpKeys&3)==0)jumpKeys|=held&3;
            sampleJumpKeys=jumpKeys;
            mountCandidate=hasPosition&&!jumpFromRope&&!drop&&(jumpKeys&4)!=0;
            alignedSince=-1;
            climbActive=false;
        }
        string kind;
        if(jumpActive){
            rising|=dy<-1;
            falling|=dy>1;
            stableY=Math.Abs(dy)<=1?stableY+1:0;
            kind=jumpFromRope?"rope_exit":jumpDrop?"drop":falling&&y>jumpStartY+1?"jump_down":"jump";
            if(ConfirmRopeMount(x,y,held,dx,dy,elapsed)){
                for(int i=jumpSampleStart;i<Document.samples.Count;i++)
                    Document.samples[i].kind=i<=alignedSample?"rope_mount":"climb";
                climbActive=true;
                climbX=alignedX;
                jumpActive=false;
                kind="climb";
            }else if(stableY>=4&&elapsed-jumpAt>=300&&(rising||falling||elapsed-jumpAt>650)&&
                (falling||Math.Abs(y-jumpStartY)<=1||elapsed-jumpAt>=550)){
                kind=jumpFromRope?"rope_exit":jumpDrop?"drop":y>jumpStartY+1?"jump_down":"jump";
                for(int i=jumpSampleStart;i<Document.samples.Count;i++)Document.samples[i].kind=kind;
                jumpActive=false;
            }
        }else{
            if(climbActive&&Math.Abs(x-climbX)>2&&((held&3)!=0||Math.Abs(dy)<=1))climbActive=false;
            if(!climbActive&&hasPosition&&(held&12)!=0&&Math.Abs(dy)>=1&&Math.Abs(dx)<=2){
                climbActive=true;
                climbX=x;
            }
            kind=climbActive?"climb":"walk";
        }
        priorX=x;priorY=y;hasPosition=true;
        return kind;
    }
    bool ConfirmRopeMount(int x,int y,int held,int dx,int dy,long elapsed){
        // The minimap has no explicit grab flag. Require sustained upward travel
        // on one axis after the airborne approach, with Up still pressed.
        mountCandidate&=(held&4)!=0;
        if(!mountCandidate||elapsed-jumpAt<180)return false;
        if(Math.Abs(dx)>1||dy>1||alignedSince>=0&&Math.Abs(x-alignedX)>2){
            alignedSince=-1;
            return false;
        }
        if(alignedSince<0){
            alignedSince=elapsed;
            alignedSample=Document.samples.Count;
            alignedX=x;
            alignedY=y;
            return false;
        }
        return elapsed-jumpAt>=500&&elapsed-alignedSince>=320&&alignedY-y>=5&&(held&16)==0;
    }
    int Mask(int vk){return vk==37?1:vk==39?2:vk==38?4:vk==40?8:vk==Document.jumpKey?16:0;}
    void UpdateAtlas(byte[] data,int w,int h,int ox,int oy,int px,int py){
        int left=atlas==null?ox:Math.Min(ox,atlasX),top=atlas==null?oy:Math.Min(oy,atlasY),right=atlas==null?ox+w:Math.Max(ox+w,atlasX+atlasW),bottom=atlas==null?oy+h:Math.Max(oy+h,atlasY+atlasH);
        if(right-left>4096||bottom-top>2048){Pause("地图拼接范围过大，请分图保存");return;}
        if(atlas==null||left!=atlasX||top!=atlasY||right-left!=atlasW||bottom-top!=atlasH){int nw=right-left,nh=bottom-top;var next=new byte[nw*nh*3];if(atlas!=null)for(int y=0;y<atlasH;y++)Buffer.BlockCopy(atlas,y*atlasW*3,next,((y+atlasY-top)*nw+atlasX-left)*3,atlasW*3);atlas=next;atlasW=nw;atlasH=nh;atlasX=left;atlasY=top;}
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){if(Math.Abs(x-px)<=4&&Math.Abs(y-py)<=4)continue;int to=((y+oy-atlasY)*atlasW+x+ox-atlasX)*3,from=(y*w+x)*3;Buffer.BlockCopy(data,from,atlas,to,3);}Document.mapX=atlasX;Document.mapY=atlasY;
    }
    internal void StoreMarkedMapFrame(byte[] pixels,Point origin,Point position){
        var size=new Size(Document.roiWidth,Document.roiHeight);
        if(pixels.Length!=size.Width*size.Height*3)return;
        var local=new Point(position.X-origin.X,position.Y-origin.Y);
        UpdateAtlas(pixels,size.Width,size.Height,origin.X,origin.Y,local.X,local.Y);
        if(atlas==null)return;
        Tracker.Seed(pixels,size.Width,size.Height,origin.X,origin.Y);
        Document.dotSeedX=local.X;Document.dotSeedY=local.Y;
    }
    public Bitmap MapBitmap(){if(atlas==null)return RouteFiles.Map(Document);return Wire.BitmapFrom(atlas,atlasW,atlasH);}
    public void FlushMap(){using(var map=MapBitmap())if(map!=null)RouteFiles.SetMap(Document,map);if(Tracker.LastFrame!=null)using(var mini=Wire.BitmapFrom(Tracker.LastFrame,Document.roiWidth,Document.roiHeight)){Document.lastMiniPng=RouteFiles.EncodePng(mini);Document.lastOffsetX=Tracker.offsetX;Document.lastOffsetY=Tracker.offsetY;}}
}
}
