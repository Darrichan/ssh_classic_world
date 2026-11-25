using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;
namespace SshScreen {
public static class RouteFlowChecks {
 static readonly List<object> results=new List<object>();
 static void Need(bool v,string text){if(!v)throw new IOException(text);}
 static void Test(string name,Action action){try{action();results.Add(Wire.Map("name",name,"ok",true));}catch(Exception e){results.Add(Wire.Map("name",name,"ok",false,"error",e.Message));}}
 static RolePreset Role(){var r=new RolePreset();r.battle.mode="route";r.battle.detectMonsters=false;return r;}
 static RouteDocument Graph(string next="jump"){var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=0,y=40},new RouteNode{id="B",x=20,y=40},new RouteNode{id="C",x=40,y=next=="walk"?40:20}});d.edges.Add(new RouteEdge{id="W",from="A",to="B",kind="walk",both=true,duration=1000});d.edges.Add(new RouteEdge{id="J",from="B",to="C",kind=next,duration=600});return d;}
 static CombatObservation O(long seq,double now,int x,int y=40){return new CombatObservation{sequence=seq,now=now,window=42,fresh=true,dot=new PointF(x,y),body=new Point(300,200)};}
 public static void Main(string[] args){
  Test("platform-offset-keeps-moving-falls-without-reversing-and-rejoins-goal",()=>{
   foreach(bool combat in new[]{false,true}){
    var map=new RouteDocument();
    map.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=40},new RouteNode{id="B",x=30,y=40},
     new RouteNode{id="C",x=30,y=60},new RouteNode{id="D",x=70,y=60}});
    map.edges.AddRange(new[]{new RouteEdge{id="W",from="A",to="B",both=true},
     new RouteEdge{id="F",from="B",to="C",kind="walk_off"},new RouteEdge{id="L",from="C",to="D",both=true}});
    var planner=new CombatPlanner(Role(),0,map,"A","D",map.edges.ToList(),combat);
    planner.Tick(O(1,0,10,40));
    int[,] positions={{16,44},{20,44},{28,48},{38,56},{44,60},{44,60},{44,60}};
    for(int i=0;i<positions.GetLength(0);i++){
     var plan=planner.Tick(O(i+2,300+i*200,positions[i,0],positions[i,1]));
     Need(plan==null||plan.stopReason==null,"A platform mismatch stopped navigation");
     Need(plan==null||plan.steps.All(step=>!step.down||step.key!=37),"Recovery reversed in flight to chase the recorded platform");
     if(i==0)Need(plan!=null&&plan.continuityKey==39,"Unknown ground kept waiting for the recording instead of moving");
    }
    Need(planner.Tick(O(10,2100,70,60)).stopReason=="已到达导航目标","Recovery lost the selected navigation goal after the actual landing");
   }
  });
  Test("platform-recovery-reverses-only-after-blocked-fresh-ground-observations",()=>{
   var map=Graph("walk");var planner=new CombatPlanner(Role(),0,map);
   planner.Tick(O(1,0,5,40));
   var first=planner.Tick(O(2,300,8,45));
   Need(first!=null&&first.continuityKey==39,"Offset recovery did not start toward the planned route");
   Need(planner.Tick(O(2,900,8,45))==null,"A repeated observation renewed movement");
   planner.Tick(O(3,500,8,45));planner.Tick(O(4,900,8,45));
   var turn=planner.Tick(O(5,1600,8,45));
   Need(turn!=null&&turn.continuityKey==37&&turn.stopReason==null,"A blocked unrecorded floor kept pressing into the same obstacle");
   var stale=O(6,1900,8,45);stale.fresh=false;
   Need(planner.Tick(stale).stopReason!=null,"Recovery bypassed the fresh-position input guard");
  });
  Test("route-clears-ground-monsters-before-mount-but-never-in-the-catch",()=>{
   var role=Role();role.battle.detectMonsters=true;role.Binding("attack").key=65;role.Binding("aoe").enabled=false;
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=40},new RouteNode{id="B",x=30,y=40},
    new RouteNode{id="G",x=32,y=36},new RouteNode{id="T",x=32,y=20}});
   map.edges.AddRange(new[]{new RouteEdge{id="W",from="A",to="B",both=true},
    new RouteEdge{id="M",from="B",to="G",kind="rope_mount",edited=true},new RouteEdge{id="C",from="G",to="T",kind="climb"}});
   var planner=new CombatPlanner(role,0,map);
   planner.Tick(O(1,0,28,40));
   var mob=O(2,300,30,40);mob.facing="right";mob.facingFresh=true;
   mob.monsters.Add(new Rectangle(320,180,20,40));
   var attack=planner.Tick(mob);
   Need(attack!=null&&attack.steps.Any(step=>step.key==65&&step.down)&&attack.steps.All(step=>step.key!=role.Key("jump")),
    "Nearby attackable monsters were ignored just because the next segment was a rope");
   var mount=planner.Tick(O(3,1000,30,40));
   Need(mount!=null&&mount.steps.Any(step=>step.key==role.Key("jump")&&step.down),"The route did not resume its mount after the threat cleared");
   var air=O(4,1500,31,38);air.monsters.Add(new Rectangle(320,180,20,40));
   var catchPlan=planner.Tick(air);
   Need(catchPlan==null||catchPlan.steps.All(step=>step.key!=65),"Combat interrupted the launched rope catch");
  });
  Test("combat-turn-is-observed-when-reversing-momentum-before-a-rope-launch",()=>{
   var role=Role();role.battle.detectMonsters=true;role.Binding("attack").key=65;role.Binding("aoe").enabled=false;
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=40},new RouteNode{id="B",x=30,y=40},
    new RouteNode{id="G",x=32,y=36},new RouteNode{id="T",x=32,y=20}});
   map.edges.AddRange(new[]{new RouteEdge{id="W",from="A",to="B",both=true},
    new RouteEdge{id="M",from="B",to="G",kind="rope_mount",edited=true},new RouteEdge{id="C",from="G",to="T",kind="climb"}});
   var planner=new CombatPlanner(role,0,map);planner.Tick(O(1,0,26,40));
   var mob=O(2,300,30,40);mob.monsters.Add(new Rectangle(260,180,20,40));
   var turn=planner.Tick(mob);Need(turn!=null&&turn.steps.Any(step=>step.down&&step.key==37),"The fixture did not turn left for the nearby monster");
   mob=O(3,500,30,40);mob.body=new Point(299,200);mob.monsters.Add(new Rectangle(260,180,20,40));
   Need(planner.Tick(mob).steps.Any(step=>step.down&&step.key==65),"Confirmed ground turn did not attack");
   var resume=planner.Tick(O(4,1000,30,40));
   Need(resume!=null&&resume.continuityKey==37&&resume.steps.All(step=>step.key!=role.Key("jump")),
    "The rope launch ignored combat's actual left turn and jumped with stale rightward momentum");
   var back=planner.Tick(O(5,1200,28,40));Need(back!=null&&back.continuityKey==39,"The character did not reverse back toward the rope");
   var jump=planner.Tick(O(6,1400,30,40));
   Need(jump!=null&&jump.steps.Any(step=>step.down&&step.key==role.Key("jump"))&&jump.steps.Any(step=>step.down&&step.key==39),
    "The adjusted takeoff did not jump toward the rope with the configured key");
  });
  Test("distant-monsters-do-not-reverse-route-or-teleport-past-takeoff",()=>{
   var role=Role();role.battle.detectMonsters=true;role.battle.screenPerMini=10;role.battle.Profile.rangeX=80;role.battle.teleportWhileWalking=true;
   role.Binding("teleport").enabled=true;role.Binding("teleport").key=84;
   var map=Graph("walk");map.nodes.Last().x=100;
   var planner=new CombatPlanner(role,0,map);planner.Tick(O(1,0,50,40));
   var sight=O(2,300,52,40);sight.monsters.Add(new Rectangle(45,180,20,40));
   var move=planner.Tick(sight);
   Need(move!=null&&move.continuityKey==39&&move.steps.All(step=>step.key!=84),"Route hunting abandoned the path to independently chase or teleport toward a distant monster");
  });
  Test("unconfirmed-combat-turn-yields-to-route-and-retries-later",()=>{
   foreach(bool navigation in new[]{false,true}){
    var role=Role();role.battle.detectMonsters=true;role.Binding("attack").key=65;role.Binding("aoe").enabled=false;
    var map=Graph("walk");map.nodes.Last().x=100;
    var planner=navigation?new CombatPlanner(role,0,map,"B","C",new List<RouteEdge>{map.edges[1]},true):new CombatPlanner(role,0,map);
    planner.Tick(O(1,0,50,40));
    var sight=O(2,300,50,40);sight.monsters.Add(new Rectangle(320,180,20,40));
    Need(planner.Tick(sight).name=="转向确认","The fixture did not enter turn confirmation");
    sight=O(3,1500,50,40);sight.monsters.Add(new Rectangle(320,180,20,40));
    var resumed=planner.Tick(sight);
    Need(resumed!=null&&resumed.continuityKey==39&&resumed.steps.All(step=>step.key!=65),"A blocked or unreadable facing kept route hunting waiting forever");
   }
  });

  Test("tiny-platforms-start-moving-instead-of-cycling-through-arrival-tolerance",()=>{
   var map=new RouteDocument();
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=84},new RouteNode{id="B",x=11,y=84},new RouteNode{id="C",x=12,y=84}});
   map.edges.AddRange(new[]{new RouteEdge{id="W1",from="A",to="B",both=true},new RouteEdge{id="W2",from="B",to="C",both=true}});
   var planner=new CombatPlanner(Role(),0,map);
   var first=planner.Tick(O(1,0,10,84));
   Need(first!=null&&first.stopReason==null&&first.steps.Any(step=>step.key==39&&step.down),"A short real platform was considered already walked in both directions");
   for(int i=2;i<=4;i++){var plan=planner.Tick(O(i,i*50,10,84));Need(plan!=null&&plan.stopReason==null&&plan.steps.All(step=>step.key==39),"A stationary fresh sample reversed the short walk before movement");}
   var moved=planner.Tick(O(5,300,12,84));
   Need(moved==null||moved.stopReason==null,"Actual movement through short junctions stopped the route");
  });
  Test("dense-navigation-chain-yields-and-completes-instead-of-stopping-at-sixteen",()=>{
   var map=new RouteDocument();
   for(int i=0;i<40;i++)map.nodes.Add(new RouteNode{id="N"+i,x=10,y=40});
   for(int i=0;i<39;i++)map.edges.Add(new RouteEdge{id="W"+i,from="N"+i,to="N"+(i+1)});
   var planner=new CombatPlanner(Role(),0,map,"N0","N39",map.edges.ToList());
   Need(planner.Tick(O(1,0,10,40))==null,"A logical chain budget was mistaken for a broken route");
   Need(planner.Tick(O(2,100,10,40))==null,"Dense chain did not preserve its progress to the next observation");
   Need(planner.Tick(O(3,200,10,40)).stopReason=="已到达导航目标","Dense navigation did not finish after yielding");
  });
  Test("optional-navigation-combat-uses-current-role-then-resumes-and-stops-at-goal",()=>{
   foreach(bool combat in new[]{false,true}){
    var map=new RouteDocument();map.nodes.AddRange(new[]{new RouteNode{id="A",x=0,y=40},new RouteNode{id="B",x=50,y=40}});
    map.edges.Add(new RouteEdge{id="W",from="A",to="B",both=true});
    var role=Role();role.battle.detectMonsters=true;role.Binding("attack").key=65;role.Binding("aoe").enabled=false;
    var planner=new CombatPlanner(role,0,map,"A","B",map.edges.ToList(),combat);
    planner.Tick(O(1,0,0,40));
    var observation=O(2,300,5,40);observation.facing="right";observation.facingFresh=true;
    observation.monsters.Add(new Rectangle(320,180,20,40));
    var plan=planner.Tick(observation);
    Need(plan!=null&&plan.stopReason==null,"Navigation stopped when a nearby monster was observed");
    Need(plan.steps.Any(step=>step.key==65&&step.down)==combat,"Navigation ignored the opt-in or current attack binding");
    var resume=planner.Tick(O(3,800,10,40));
    Need(resume!=null&&resume.steps.Any(step=>step.key==39&&step.down),"Navigation did not resume after combat");
    Need(planner.Tick(O(4,1200,50,40)).stopReason=="已到达导航目标","Combat navigation did not stop at its selected goal");
   }
  });
  Test("navigation-combat-keeps-critical-climb-and-ignores-unreliable-combat-data",()=>{
   var role=Role();role.battle.detectMonsters=true;role.Binding("attack").key=65;
   var map=new RouteDocument();map.nodes.AddRange(new[]{new RouteNode{id="A",x=20,y=60},new RouteNode{id="B",x=20,y=40}});
   map.edges.Add(new RouteEdge{id="C",from="A",to="B",kind="climb",both=true});
   var planner=new CombatPlanner(role,0,map,"A","B",map.edges.ToList(),true);
   var o=O(1,0,20,60);o.monsters.Add(new Rectangle(320,180,20,40));o.facingFresh=true;o.facing="right";
   Need(planner.Tick(o).steps.All(step=>step.key==38),"Combat interrupted an active rope climb");
   var nav=Wire.Map("sequence",21L,"window",42L,"found",true,"position_fresh",true,"capture_age_ms",0.0,"minimap_dot",new[]{20,50});
   var vision=Wire.Map("window",43L,"found",true,"position_fresh",true,"body",new[]{300,200},"capture_age_ms",0.0);
   var sight=CombatObservation.Parse(ControllerForm.ComposeNavigationSight(nav,vision,0),100);
   Need(sight.fresh&&sight.dot.HasValue&&!sight.combatReady,"A different window allowed combat or blocked reliable navigation");
   vision["window"]=42L;
   Need(!CombatObservation.Parse(ControllerForm.ComposeNavigationSight(nav,vision,700),100).combatReady,"Stale vision allowed combat during navigation");
   Need(CombatObservation.Parse(ControllerForm.ComposeNavigationSight(nav,vision,100),100).combatReady,"Fresh same-window combat data was rejected");
  });
  Test("confirmed-slow-descent-releases-before-the-rope-bottom",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=26,y=44},new RouteNode{id="B",x=26,y=60},
    new RouteNode{id="UL",x=16,y=44},new RouteNode{id="UR",x=50,y=44},
    new RouteNode{id="L",x=4,y=64},new RouteNode{id="R",x=56,y=64}});
   map.edges.AddRange(new[]{new RouteEdge{id="rope",from="A",to="B",kind="climb",both=true},
    new RouteEdge{id="upper1",from="UL",to="A",both=true},new RouteEdge{id="upper2",from="A",to="UR",both=true},
    new RouteEdge{id="lower",from="L",to="R",both=true}});
   var goal=RouteGraph.NavigationPoint(map,new Point(43,64),true);
   RouteGraph.AddRopeDescents(map);
   var role=Role();role.Binding("jump").key=90;
   var planner=new CombatPlanner(role,0,map,"A",goal.id,RouteGraph.Plan(map,"A",goal.id));
   Need(planner.Tick(O(1,0,26,44)).continuityKey==40,"Descent did not enter the rope");
   Need(planner.Tick(O(2,250,26,46)).continuityKey==40,"One changing sample was treated as confirmed descent");
   var release=planner.Tick(O(3,500,26,48));
   Need(release!=null&&release.name=="路线 · 离绳跳跃"&&release.steps.Any(step=>step.key==90&&step.down)&&
    release.steps.Any(step=>step.key==39&&step.down)&&release.steps.All(step=>step.key!=40),
    "Confirmed descent kept crawling to the rope bottom or used the wrong jump chord");
   planner.Tick(O(4,850,34,64));planner.Tick(O(5,950,34,64));
   Need(planner.Tick(O(6,1050,34,64)).name=="路线 · 行走","A landing shifted by inertia lost the navigation tail");
   Need(planner.Tick(O(7,1400,43,64)).stopReason=="已到达导航目标","Early release failed to reach its lower target");
   var rope=RouteFiles.Clone(map);var middle=RouteGraph.NavigationPoint(rope,new Point(26,54),true);
   var toMiddle=new CombatPlanner(role,0,rope,"A",middle.id,RouteGraph.Plan(rope,"A",middle.id));
   toMiddle.Tick(O(1,0,26,44));toMiddle.Tick(O(2,250,26,46));
   Need(toMiddle.Tick(O(3,500,26,48)).continuityKey==40,"A target on the rope was incorrectly jumped past");
  });
  Test("navigation-enters-painted-takeoff-from-its-drawn-side-with-configured-jump",()=>{
   foreach(int jump in new[]{18,32,90})foreach(int side in new[]{-1,1}){
    int launch=36+side*2;
    var map=new RouteDocument{manualMap=true};
    map.nodes.AddRange(new[]{new RouteNode{id="A",x=26,y=44},new RouteNode{id="L",x=launch,y=44},
     new RouteNode{id="B",x=36,y=39},new RouteNode{id="T",x=36,y=32}});
    map.edges.AddRange(new[]{new RouteEdge{id="W",from="A",to="L",kind="walk",both=true},
     new RouteEdge{id="M",from="L",to="B",kind="rope_mount",edited=true},
     new RouteEdge{id="U",from="B",to="T",kind="climb",both=true}});
    var role=Role();role.Binding("jump").key=jump;
    var path=map.edges.ToList();
    map.nodes.Add(new RouteNode{id="R",x=50,y=44});
    map.edges.Add(new RouteEdge{id="floor",from="L",to="R",kind="walk",both=true});
    var planner=new CombatPlanner(role,0,map,"A","T",path);
    var approach=planner.Tick(O(1,0,36,44));
    Need(approach!=null&&approach.continuityKey==(side<0?37:39)&&approach.steps.All(step=>step.key!=jump),
     "Walking stopped on the shaft before entering the drawn launch interval");
    var takeoff=planner.Tick(O(2,150,launch,44));
    {
     Need(takeoff!=null&&takeoff.continuityKey==(side<0?37:39)&&!takeoff.steps.Any(step=>step.key==jump),
      "Opposite incoming momentum was not turned before jumping");
     var returning=planner.Tick(O(3,300,launch+side*2,44));
     Need(returning!=null&&returning.continuityKey==(side<0?39:37)&&!returning.steps.Any(step=>step.key==jump),
      "Overshoot did not turn back toward the drawn rope");
     takeoff=planner.Tick(O(4,450,launch,44));
    }
    Need(takeoff!=null&&takeoff.continuityKey==38&&takeoff.secondaryContinuityKey==(side<0?39:37)&&
     takeoff.steps.Any(step=>step.key==jump&&step.down),"The drawn jump direction or current jump binding was lost");
   }
  });
  Test("painted-rope-miss-readjusts-after-landing-and-retries",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=38,y=44},new RouteNode{id="B",x=36,y=39},
    new RouteNode{id="L",x=16,y=44},new RouteNode{id="R",x=50,y=44},new RouteNode{id="T",x=36,y=32}});
   map.edges.AddRange(new[]{new RouteEdge{id="M",from="A",to="B",kind="rope_mount",edited=true},
    new RouteEdge{id="W",from="L",to="R",kind="walk",both=true},new RouteEdge{id="U",from="B",to="T",kind="climb",both=true}});
   var role=Role();role.Binding("jump").key=90;
   var planner=new CombatPlanner(role,0,map,"A","T",new List<RouteEdge>{map.edges[0],map.edges[2]});
   Need(planner.Tick(O(1,0,38,44)).steps.Any(step=>step.key==90&&step.down),"Initial jump missing");
   planner.Tick(O(2,250,38,38));planner.Tick(O(3,500,36,44));planner.Tick(O(4,650,36,44));
   var retry=planner.Tick(O(5,800,36,44));
   Need(retry!=null&&retry.stopReason==null&&retry.name.Contains("重试"),"A landed miss stopped navigation immediately");
   Need(planner.Tick(O(6,900,36,44)).continuityKey==39,"Retry did not restore the drawn takeoff side");
   Need(planner.Tick(O(7,1100,40,44)).continuityKey==37,"Retry did not turn back toward the rope");
   var second=planner.Tick(O(8,1300,38,44));
   Need(second!=null&&second.steps.Any(step=>step.key==90&&step.down),"Adjusted jump was not retried with the configured binding");
  });
  Test("automatic-rope-release-reads-a-non-space-jump-binding",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=26,y=54},new RouteNode{id="B",x=26,y=44},
    new RouteNode{id="L",x=4,y=64},new RouteNode{id="R",x=56,y=64}});
   map.edges.AddRange(new[]{new RouteEdge{id="rope",from="A",to="B",kind="climb",both=true},
    new RouteEdge{id="floor",from="L",to="R",kind="walk",both=true}});
   RouteGraph.AddRopeDescents(map);
   var exit=map.edges.First(e=>e.kind=="rope_exit"&&e.from=="A");
   var role=Role();role.Binding("jump").key=90;
   var plan=new CombatPlanner(role,0,map,"A",exit.to,new List<RouteEdge>{exit}).Tick(O(1,0,26,54));
   Need(plan.steps.Where(step=>step.down).Select(step=>step.key).OrderBy(key=>key)
    .SequenceEqual(new[]{exit.jumpDirection,90}.OrderBy(key=>key)),"Release hard-coded Space or added Down");
  });
  Test("hanging-rope-release-lands-on-first-lower-platform-and-resumes-walking",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="F",x=26,y=60},new RouteNode{id="G",x=26,y=59},
    new RouteNode{id="T",x=26,y=44},new RouteNode{id="L",x=4,y=64},new RouteNode{id="R",x=56,y=64},
    new RouteNode{id="D",x=4,y=80},new RouteNode{id="E",x=56,y=80}});
   map.edges.AddRange(new[]{new RouteEdge{id="rope",kind="climb",from="F",to="T",both=true,ropeEntries=new List<string>{"G"}},
    new RouteEdge{id="ground",from="L",to="R",both=true},new RouteEdge{id="deeper",from="D",to="E",both=true}});
   var target=RouteGraph.NavigationPoint(map,new Point(43,64),true);
   var start=RouteGraph.NavigationPoint(map,new Point(26,54),false);
   RouteGraph.AddRopeDescents(map);
   int count=map.edges.Count;
   RouteGraph.AddRopeDescents(map);
   Need(map.edges.Count==count,"Replanning repeated the same rope release");
   var path=RouteGraph.Plan(map,start.id,target.id);
   Need(path!=null&&path.Count==2&&path[0].kind=="rope_exit"&&path[1].kind=="walk",
    "The hanging character had no direct downward release to its target platform");
   var land=map.nodes.First(n=>n.id==path[0].to);
   Need(land.y==64&&land.x==30&&map.edges.Where(e=>e.kind=="rope_exit").All(e=>map.nodes.First(n=>n.id==e.to).y==64),
    "Rope release skipped a nearer drawn platform");
   RouteFiles.Validate(map);
   var role=Role();role.Binding("jump").key=32;
   var planner=new CombatPlanner(role,0,map,start.id,target.id,path);
   var log=new List<string>();
   var scheduler=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};
   var release=planner.Tick(O(1,0,26,54));
   Need(release!=null&&release.name=="路线 · 离绳跳跃","Release did not use the rope-exit action");
   scheduler.Begin(release,0);scheduler.Tick(0);scheduler.Tick(250);
   Need(log.SequenceEqual(new[]{"39:True","32:True","32:False","39:False"}),
    "Downward release failed to use a directional jump once");
   Need(planner.Tick(O(2,350,30,64))==null,"First touchdown was treated as stable");
   Need(planner.Tick(O(3,450,30,64))==null,"Touchdown was confirmed without enough fresh samples");
   var walking=planner.Tick(O(4,550,30,64));
   Need(walking!=null&&walking.name=="路线 · 行走"&&walking.steps.All(step=>step.key==39),
    "The next platform could not continue without another rope-exit jump");
   var done=planner.Tick(O(5,850,43,64));
   Need(done!=null&&done.stopReason=="已到达导航目标","The lower-platform target was not reached");
  });
  Test("navigation-target-can-stop-on-a-rope-with-saved-entry-points",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="F",x=20,y=60},new RouteNode{id="G",x=20,y=55},new RouteNode{id="T",x=20,y=20}});
   map.edges.Add(new RouteEdge{id="rope",kind="climb",from="F",to="T",both=true,ropeEntries=new List<string>{"G"}});
   Need(RouteGraph.SnapNavigationTarget(map,new Point(21,40))==new Point(20,40),"Rope was rejected by target picking");
   var goal=RouteGraph.NavigationPoint(map,new Point(21,40),true);
   var start=RouteGraph.NavigationPoint(map,new Point(20,51),false);
   var path=RouteGraph.Plan(map,start.id,goal.id);
   Need(path!=null&&path.Count==1&&path[0].kind=="climb","An occupied rope could not get to its middle target");
   Need(RouteGraph.Plan(map,"G",goal.id)!=null&&RouteGraph.Plan(map,"T",goal.id)!=null,
    "Splitting a rope for navigation lost its recorded entrance or downward travel");
   var planner=new CombatPlanner(Role(),0,map,start.id,goal.id,path);
   var climb=planner.Tick(O(1,0,20,51));
   Need(climb!=null&&climb.steps.Count>0&&climb.steps.All(step=>step.key==38),"Rope navigation did not climb toward its goal");
   var stopped=planner.Tick(O(2,250,20,40));
   Need(stopped!=null&&stopped.stopReason=="已到达导航目标"&&stopped.steps.Count==0,"The target did not stop at the selected rope position");
  });
  Test("navigation-reconnects-slightly-shifted-grab-launches-to-their-platform",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=50},new RouteNode{id="B",x=32,y=50},
    new RouteNode{id="P",x=25,y=51},new RouteNode{id="G",x=28,y=45},new RouteNode{id="T",x=28,y=30},
    new RouteNode{id="H",x=45,y=30},new RouteNode{id="far",x=35,y=57}});
   map.edges.AddRange(new[]{new RouteEdge{id="ground",from="A",to="B",both=true},
    new RouteEdge{id="upper",from="T",to="H",both=true},new RouteEdge{id="rope",from="G",to="T",kind="climb",both=true},
    new RouteEdge{id="grab",from="P",to="G",kind="rope_mount"},new RouteEdge{id="far-grab",from="far",to="G",kind="rope_mount"}});
   Need(RouteGraph.Plan(map,"A","H")==null,"Fixture already had a platform-to-rope junction");
   RouteGraph.ConnectNearbyGrabLaunches(map);
   var connected=RouteGraph.Plan(map,"A","H");
   Need(connected!=null&&connected.Any(e=>e.id=="grab"&&e.from!="P"&&e.jumpDirection==39),"A one-pixel takeoff drift broke the upward route");
   Need(map.edges.Single(e=>e.id=="far-grab").from=="far","A distant takeoff was arbitrarily moved to a platform");
  });
  Test("walk-off-keeps-both-departure-directions-and-resumes-from-overshot-landings",()=>{
   foreach(int sign in new[]{-1,1})foreach(bool navigation in new[]{false,true}){
    var map=new RouteDocument{manualMap=true};
    map.nodes.AddRange(new[]{new RouteNode{id="H",x=100,y=20},
     new RouteNode{id="L",x=100+sign*2,y=24},new RouteNode{id="G",x=100+sign*10,y=24}});
    map.edges.AddRange(new[]{new RouteEdge{id="fall",from="H",to="L",kind="walk_off"},
     new RouteEdge{id="ground",from="L",to="G",kind="walk",both=true}});
    var role=Role();role.Binding("jump").key=0;
    var planner=navigation?new CombatPlanner(role,0,map,"H","G",map.edges.ToList()):new CombatPlanner(role,0,map,true);
    var keys=new List<int>();
    var scheduler=new ActionScheduler{Send=(key,down)=>{if(down)keys.Add(key);}};
    int direction=sign<0?37:39;
    int[,] points={{100,20},{100+sign*2,20},{100+sign*4,22},{100+sign*4,22},
     {100+sign*4,24},{100+sign*4,24}};
    for(int i=0;i<points.GetLength(0);i++){
     var plan=planner.Tick(O(i+1,i*100,points[i,0],points[i,1]));
     Need(plan!=null&&plan.stopReason==null&&plan.steps.All(step=>step.key==direction),
      "The fall braked or reversed to chase the nominal landing point");
     scheduler.Begin(plan,i*100);scheduler.Tick(i*100);
    }
    Need(keys.SequenceEqual(new[]{direction}),"Consecutive falls interrupted the continuing direction");
    var done=planner.Tick(O(7,700,100+sign*10,24));
    Need(done!=null&&done.stopReason==(navigation?"已到达导航目标":"本次路线试走已到终点"),
     "A shifted landing did not continue to the target");
    scheduler.Cancel();
   }
  });
  Test("walk-off-steps-use-direction-and-wait-for-a-stable-lower-platform",()=>{
   var map=new RouteDocument{manualMap=true};
   map.nodes.AddRange(new[]{new RouteNode{id="L",x=4,y=50},new RouteNode{id="A",x=20,y=50},
    new RouteNode{id="H",x=22,y=46},new RouteNode{id="R",x=40,y=46}});
   map.edges.AddRange(new[]{new RouteEdge{id="lower",kind="walk",from="L",to="A",both=true},
    new RouteEdge{id="upper",kind="walk",from="H",to="R",both=true},new RouteEdge{id="up",kind="jump",from="A",to="H"}});
   var covered=RouteFiles.Clone(map);
   covered.nodes.Add(new RouteNode{id="more",x=17,y=46});
   covered.edges.Add(new RouteEdge{id="covered",kind="walk",from="more",to="H",both=true});
   RouteGraph.AddStepDescents(covered);
   Need(covered.edges.All(e=>e.kind!="walk_off"),"An interior point was treated as an exposed ledge");
   RouteGraph.AddStepDescents(map);
   RouteGraph.AddStepDescents(map);
   var step=map.edges.Single(e=>e.kind=="walk_off");
   Need(!step.both&&step.from=="H"&&step.to=="A","Downward stair generated a reverse jump or duplicated a link");
   var path=RouteGraph.Plan(map,"H","L");
   Need(path!=null&&path.SequenceEqual(new[]{step,map.edges.First(e=>e.id=="lower")}),"Navigation could not get down to the lower platform");
   var role=Role();role.Binding("jump").key=0;
   var planner=new CombatPlanner(role,0,map,"H","L",path);
   var descend=planner.Tick(O(1,0,22,46));
   Need(descend!=null&&descend.name=="路线 · 走下台阶"&&descend.steps.All(k=>k.key==37)&&descend.steps.Count>0,
    "Moving down the ledge did not use only the horizontal key");
   var air=planner.Tick(O(2,250,20,48));
   Need(air!=null&&air.name=="路线 · 走下台阶"&&air.steps.All(motion=>motion.key==37),"Falling corrected back toward the predicted X or stopped horizontal travel");
   var firstGround=planner.Tick(O(3,350,20,50));
   Need(firstGround!=null&&firstGround.name=="路线 · 走下台阶"&&firstGround.steps.All(motion=>motion.key==37),"A single lower-level sample changed the falling action");
   var ground=planner.Tick(O(4,450,20,50));
   Need(ground!=null&&ground.name=="路线 · 行走"&&ground.steps.All(k=>k.key==37),"The lower platform did not continue after landing");
  });
  Test("recovers-exact-minimap-position-after-restart",()=>{
   const int width=64,height=48;
   var image=new byte[width*height*3];
   foreach(var candidate in new[]{new Point(15,10),new Point(48,32)})
    for(int y=candidate.Y;y<candidate.Y+2;y++)for(int x=candidate.X;x<candidate.X+2;x++){
     int at=(y*width+x)*3;image[at]=(byte)(candidate.X==48?136:137);image[at+1]=image[at+2]=255;
    }
   var route=new RouteDocument{roiWidth=width,roiHeight=height,dotSeedX=15,dotSeedY=10};
   var tracker=new MiniTracker();tracker.Seed(null,width,height,0,0);
   var dot=tracker.Process(image,width,height,route);
   Need(dot.ok&&dot.x==48&&dot.y==32,"Restart selected the old route color over the exact current position");
  });
  Test("gentle-slope-walk-is-reversible-with-real-heights",()=>{var d=new RouteDocument();d.samples.AddRange(new[]{new RouteSample{time=0,x=0,y=30},new RouteSample{time=100,x=6,y=27},new RouteSample{time=200,x=12,y=24},new RouteSample{time=300,x=18,y=21}});RouteGraph.Build(d);Need(d.edges.Count==1&&d.edges[0].both,"Slope still restricted to flat platforms");Need(d.nodes[0].y==30&&d.nodes[1].y==21,"Slope height was flattened");Need(RouteGraph.Plan(d,d.nodes[1].id,d.nodes[0].id)!=null,"Slope has no return direction");});
  Test("curved-walk-junctions-follow-recorded-path-not-endpoint-chord",()=>{var d=new RouteDocument();d.samples.AddRange(new[]{new RouteSample{time=0,x=0,y=20,segment=1},new RouteSample{time=100,x=5,y=15,segment=1},new RouteSample{time=200,x=10,y=10,segment=1},new RouteSample{time=300,x=15,y=15,segment=1},new RouteSample{time=400,x=20,y=20,segment=1},new RouteSample{time=500,x=10,y=20,segment=2},new RouteSample{time=600,x=10,y=10,segment=3}});RouteGraph.Build(d);var off=d.nodes.First(n=>n.x==10&&n.y==20);var on=d.nodes.First(n=>n.x==10&&n.y==10);Need(!d.edges.Any(e=>e.from==off.id||e.to==off.id),"Slope shortcut connected a point outside the recorded path");Need(d.edges.Count==2&&d.edges.All(e=>e.from==on.id||e.to==on.id),"Actual slope junction was not preserved");});
  Test("vertical-fall-is-not-reversed-as-walking",()=>{Need(!RouteGraph.CanWalkBoth(new[]{new RoutePoint(0,20),new RoutePoint(1,30),new RoutePoint(2,40)}),"Vertical descent became reversible walking");});
  Test("legacy-generated-walk-upgrades-in-memory-manual-direction-survives",()=>{var d=Graph();d.edges[0].points=new List<RoutePoint>{new RoutePoint(0,40),new RoutePoint(10,35),new RoutePoint(20,30)};d.nodes.First(n=>n.id=="B").y=30;d.edges[0].both=false;RouteGraph.RefreshWalkingDirections(d);Need(d.edges[0].both,"Legacy slope did not gain a return");d.edges[0].both=false;d.edges[0].edited=true;RouteGraph.RefreshWalkingDirections(d);Need(!d.edges[0].both&&!d.edges[1].both,"Manual one-way or jump direction overwritten");});
  Test("walking-renewal-keeps-key-down-and-has-a-bounded-lease",()=>{var log=new List<string>();var s=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};for(int i=0;i<3;i++){s.Tick(i*100);s.Begin(CombatPlan.Move("walk",39),i*100);s.Tick(i*100);}Need(log.SequenceEqual(new[]{"39:True"}),"Walking released/repressed on every observation");s.Tick(439);Need(log.Count==1,"Movement lease released early");s.Tick(441);Need(log.SequenceEqual(new[]{"39:True","39:False"})&&!s.Busy,"Movement did not expire without a fresh decision");});
  Test("walk-node-to-walk-node-does-not-wait-another-frame",()=>{var p=new CombatPlanner(Role(),0,Graph("walk"));Need(p.Tick(O(1,0,0)).name=="路线 · 行走","Initial walk missing");var next=p.Tick(O(2,100,19));Need(next!=null&&next.name=="路线 · 行走"&&next.steps[0].key==39,"Intermediate node inserted an idle frame");});
  Test("walk-to-jump-keeps-direction-without-an-immediate-ground-threat",()=>{var role=Role();role.battle.detectMonsters=true;var p=new CombatPlanner(role,0,Graph());var events=new List<string>();double at=0;var s=new ActionScheduler{Send=(key,down)=>events.Add(at+":"+key+":"+down)};var walking=p.Tick(O(1,0,0));s.Begin(walking,0);s.Tick(0);at=100;s.Tick(at);s.Begin(p.Tick(O(2,at,10)),at);s.Tick(at);at=200;var o=O(3,at,19);o.monsters.Add(new Rectangle(20,180,30,40));var jump=p.Tick(o);Need(jump!=null&&jump.name=="路线 · 跳跃","Takeoff was delayed by node or monster handling");Need(jump.steps.Any(x=>x.key==18&&x.down&&x.at==0),"Joined jump has an extra startup delay");s.Begin(jump,at);s.Tick(at);Need(events.SequenceEqual(new[]{"0:39:True","200:18:True"}),"Walk-to-jump released direction or replayed it");at=280;s.Tick(at);Need(events.Last()=="280:18:False","Recorded jump hold changed");at=430;s.Tick(at);Need(events.Last()=="430:39:False","Jump direction tail never released");});
  Test("reversal-releases-old-direction-before-opposite",()=>{var log=new List<string>();var s=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};s.Begin(CombatPlan.Move("right",39),0);s.Tick(0);s.Begin(CombatPlan.Move("left",37),100);s.Tick(100);Need(log.SequenceEqual(new[]{"39:True","39:False","37:True"}),"Opposite directions overlap");s.Cancel();Need(log.Last()=="37:False","Pause retained a continued key");});
  Test("pause-after-continuation-removes-pending-jump-and-tail",()=>{var log=new List<string>();var s=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};s.Begin(CombatPlan.Move("walk",39),0);s.Tick(0);var hop=CombatPlan.Chord("jump",39,18,30,80,150);hop.continuityKey=39;s.Begin(hop,100);s.Tick(100);s.Cancel();s.Tick(2000);Need(log.SequenceEqual(new[]{"39:True","39:False"})&&!s.Busy,"Cancelled movement emitted a delayed jump");});
  Test("terminal-walk-releases-on-arrival-while-looking-for-a-recorded-return",()=>{var d=Graph();d.edges.RemoveAt(1);d.edges[0].both=false;var p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,0));var end=p.Tick(O(2,100,19));Need(end!=null&&end.stopReason==null&&end.steps.Count==0,"Terminal arrival left movement running");var trial=new CombatPlanner(Role(),0,d,true);trial.Tick(O(1,0,0));Need(trial.Tick(O(2,100,19)).stopReason=="本次路线试走已到终点","Explicit trial no longer stops at its terminal point");});
  Test("up-climb-holds-up-and-direction-until-x-changes-without-jumping",()=>{
   var d=new RouteDocument();
   d.nodes.AddRange(new[]{new RouteNode{id="A",x=20,y=50},new RouteNode{id="B",x=20,y=40},new RouteNode{id="C",x=40,y=40}});
   d.edges.Add(new RouteEdge{id="rope",from="A",to="B",kind="climb",both=true,duration=900});
   d.edges.Add(new RouteEdge{id="platform",from="B",to="C",kind="walk",both=true,duration=1200});
   foreach(bool trial in new[]{false,true}){
    var role=Role();role.Binding("jump").key=32;
    var p=new CombatPlanner(role,0,d,trial);var keys=new List<string>();
    var scheduler=new ActionScheduler{Send=(key,down)=>keys.Add(key+":"+down)};
    var first=p.Tick(O(1,0,20,50));Need(first.continuityKey==38,"The climb did not start");scheduler.Begin(first,0);scheduler.Tick(0);
    var approach=p.Tick(O(2,100,20,42));Need(approach.secondaryContinuityKey==0,"Horizontal motion began before reaching the top");scheduler.Begin(approach,100);scheduler.Tick(100);
    var exit=p.Tick(O(3,200,20,40));
    Need(exit.continuityKey==38&&exit.secondaryContinuityKey==39&&exit.steps.All(k=>k.key==38||k.key==39),"Upward handoff did not combine Up and Right without Jump");
    scheduler.Begin(exit,200);scheduler.Tick(200);
    var held=p.Tick(O(4,300,20,40));Need(held.continuityKey==38&&held.secondaryContinuityKey==39,"Up was released before any X movement");scheduler.Begin(held,300);scheduler.Tick(300);
    Need(keys.SequenceEqual(new[]{"38:True","39:True"}),"Handoff released/repressed Up or added a jump");
    var walk=p.Tick(O(5,400,21,40));Need(walk.continuityKey==39&&walk.steps.All(k=>k.key==39),"Up remained held after confirmed X movement");scheduler.Begin(walk,400);scheduler.Tick(400);
    Need(keys.SequenceEqual(new[]{"38:True","39:True","38:False"}),"X-confirmed handoff interrupted horizontal movement");
    scheduler.Cancel();Need(keys.Last()=="39:False","Pause did not release the continuing direction");
    var standing=new CombatPlanner(role,0,d,trial).Tick(O(1,0,20,40));Need(standing.steps.All(k=>k.key==39),"Starting on the upper platform unnecessarily climbed back down");
   }
  });
  Test("down-climb-to-platform-retains-one-configured-exit-jump",()=>{
   var d=new RouteDocument();
   d.nodes.AddRange(new[]{new RouteNode{id="A",x=20,y=20},new RouteNode{id="B",x=20,y=40},new RouteNode{id="C",x=40,y=40}});
   d.edges.Add(new RouteEdge{id="rope",from="A",to="B",kind="climb",both=true,duration=900});
   d.edges.Add(new RouteEdge{id="platform",from="B",to="C",kind="walk",both=true,duration=1200});
   foreach(bool trial in new[]{false,true}){
    var role=Role();role.Binding("jump").key=32;var p=new CombatPlanner(role,0,d,trial);
    Need(p.Tick(O(1,0,20,20)).continuityKey==40,"The downward climb did not start");
    var exit=p.Tick(O(2,450,20,40));
    Need(exit.continuityKey==39&&exit.steps.Where(k=>k.down&&k.at==0).Select(k=>k.key).SequenceEqual(new[]{39,32}),"Downward exit lost its configured jump");
    Need(p.Tick(O(3,560,20,40)).steps.All(k=>k.key==39),"Downward exit repeated its jump");
   }
  });
  Test("hand-painted-branch-can-return-and-mount-a-rope",()=>{
   var d=new RouteDocument();
   RouteGraph.DrawStroke(d,"walk",new[]{new RoutePoint(154,88),new RoutePoint(171,88)});
   RouteGraph.DrawStroke(d,"walk",new[]{new RoutePoint(151,83),new RoutePoint(157,83)});
   var jump=RouteGraph.DrawStroke(d,"jump",new[]{new RoutePoint(163,88),new RoutePoint(159,85),
    new RoutePoint(158,82),new RoutePoint(157,82),new RoutePoint(157,83)});
   Need(jump.kind=="jump","A platform jump was treated as a rope grab before the rope was drawn");
   RouteGraph.DrawStroke(d,"climb",new[]{new RoutePoint(157,83),new RoutePoint(157,75)});
   Need(jump.kind=="rope_mount"&&jump.jumpDirection==37,"The shared platform and rope endpoint lost the painted ascent");
   Need(RouteGraph.GeneratePaintedActions(d)==0,"Repeated generation changed a generated action");
   var role=Role();role.Binding("jump").key=32;
   var p=new CombatPlanner(role,0,d,true);
   Need(p.Tick(O(1,0,154,88)).continuityKey==39,"Painted platform did not start moving");
   p.Tick(O(2,90,163,88));
   Need(p.Tick(O(3,200,171,88)).continuityKey==37,"Trial stopped at a reversible dead end");
   var launch=p.Tick(O(4,300,163,88));
   Need(launch.continuityKey==38&&launch.secondaryContinuityKey==37&&launch.steps.Any(s=>s.key==32&&s.down),
    "Preset takeoff did not hold Up, Left and the configured jump key");
   var flight=p.Tick(O(5,450,161,86));
   Need(flight.continuityKey==38&&flight.secondaryContinuityKey==37&&flight.steps.All(s=>s.key!=32),
    "Airborne approach lost steering or pressed Jump again");
   Need(p.Tick(O(6,650,159,85)).secondaryContinuityKey==37,"Released the direction too early in flight");
   var grab=p.Tick(O(7,990,157,83));
   Need(grab.continuityKey==38&&grab.secondaryContinuityKey==0,"Released Up while starting to grab");
   p.Tick(O(8,1120,157,82));p.Tick(O(9,1180,157,82));
   Need(p.Tick(O(10,1290,157,81)).continuityKey==38,"Confirmed rope grip did not continue climbing");
  });
  Test("platform-drop-accepts-nearby-takeoff-without-pixel-chasing",()=>{
   foreach(int offset in new[]{-3,-2,2,3}){
    var map=new RouteDocument{manualMap=true};
    map.nodes.AddRange(new[]{new RouteNode{id="L",x=4,y=40},new RouteNode{id="A",x=20,y=40},
     new RouteNode{id="R",x=40,y=40},new RouteNode{id="B",x=20,y=60},
     new RouteNode{id="D",x=4,y=60},new RouteNode{id="E",x=40,y=60}});
    map.edges.AddRange(new[]{new RouteEdge{id="approach",from="L",to="A",both=true},
     new RouteEdge{id="upper",from="A",to="R",both=true},new RouteEdge{id="drop",from="A",to="B",kind="drop"},
     new RouteEdge{id="lower",from="D",to="B",both=true},new RouteEdge{id="tail",from="B",to="E",both=true}});
    var role=Role();role.Binding("jump").key=90;
    var planner=new CombatPlanner(role,0,map,"L","B",new List<RouteEdge>{map.edges[0],map.edges[2]});
    var plan=planner.Tick(O(1,0,20+offset,40));
    Need(plan!=null&&plan.name=="路线 · 下落"&&plan.steps.Where(step=>step.down)
     .Select(step=>step.key).SequenceEqual(new[]{40,90}),"Nearby drop launch chased the exact X before Down + configured Jump");
    planner.Tick(O(2,350,20+offset,60));planner.Tick(O(3,450,20+offset,60));
    var landing=planner.Tick(O(4,550,20+offset,60));
    Need(landing!=null&&landing.stopReason==null&&landing.name=="路线 · 行走"&&
     landing.steps.All(step=>step.key==(offset<0?39:37)),"A shifted landing failed to finish the selected target on the ground");
    Need(planner.Tick(O(5,850,20,60)).stopReason=="已到达导航目标","Drop navigation failed after landing near its goal");
   }
  });
  Test("platform-drop-sends-down-then-configured-jump-in-the-same-tick",()=>{
   var d=new RouteDocument();
   d.nodes.AddRange(new[]{new RouteNode{id="A",x=18,y=40},new RouteNode{id="B",x=18,y=56}});
   d.edges.Add(new RouteEdge{id="D",from="A",to="B",kind="drop",downLead=180,duration=650});
   var role=Role();role.Binding("jump").key=90;
   var p=new CombatPlanner(role,0,d,true);
   var scheduler=new ActionScheduler();
   var keys=new List<int>();
   scheduler.Send=(key,down)=>{if(down)keys.Add(key);};
   scheduler.Begin(p.Tick(O(1,0,18,40)),0);scheduler.Tick(0);
   Need(keys.SequenceEqual(new[]{40,90}),"Platform drop had a delay or pressed the jump key before Down");
  });
  Test("generated-platform-ascent-jumps-onto-rope-and-preserves-downward-travel",()=>{
   var d=new RouteDocument();
   d.samples.AddRange(new[]{
    new RouteSample{time=0,x=10,y=40,keys=2},
    new RouteSample{time=100,x=26,y=40,keys=2},
    new RouteSample{time=200,x=30,y=40,keys=6},
    new RouteSample{time=300,x=29,y=39,keys=6,kind="climb"},
    new RouteSample{time=400,x=30,y=37,keys=6,kind="climb"},
    new RouteSample{time=500,x=30,y=35,keys=4,kind="climb"},
    new RouteSample{time=600,x=30,y=30,keys=4,kind="climb"}});
   d.events.AddRange(new[]{
    new RouteKeyEvent{time=0,vk=39,down=true},new RouteKeyEvent{time=150,vk=38,down=true},
    new RouteKeyEvent{time=450,vk=39,down=false}});
   RouteGraph.Build(d);d=RouteFiles.Clone(d);
   var mount=d.edges.Single(x=>x.kind=="rope_mount");
   var ascent=d.edges.Single(x=>x.kind=="climb"&&x.both);
   var foot=d.nodes.Single(n=>n.id==mount.from);
   var grip=d.nodes.Single(n=>n.id==mount.to);
   Need(foot.y==40&&grip.y<foot.y&&grip.x==foot.x&&ascent.from==foot.id&&ascent.ropeEntries.SequenceEqual(new[]{grip.id}),"The platform still leads directly into climbing");
   Need(d.edges.Count(e=>e.kind=="climb")==1&&RouteGraph.Plan(d,foot.id,ascent.to).SequenceEqual(new[]{mount,ascent})&&
    RouteGraph.Plan(d,ascent.to,foot.id).SequenceEqual(new[]{ascent}),"A single rope should require a jump to ascend and descend in one movement");
   Need(d.samples[2].kind=="walk"&&d.samples[2].y==40&&d.events.Count==3,"The platform or original key events were rewritten");
   var role=Role();role.Binding("jump").key=32;
   var p=new CombatPlanner(role,0,d,true);
   var launch=p.Tick(O(1,0,foot.x,foot.y));
   Need(launch.steps.Where(step=>step.down).Select(step=>step.key).SequenceEqual(new[]{38,32}),"The lower platform entry did not hold Up and use configured Space");
   p.Tick(O(2,150,grip.x,grip.y+1));
   var confirming=p.Tick(O(3,220,grip.x,grip.y));
   Need(confirming.continuityKey==38,"The attempt released Up before confirming the rope");
   Need(p.Tick(O(4,320,grip.x,grip.y-2)).continuityKey==38,"The rope climb interrupted Up");
   int edges=d.edges.Count;
   RouteGraph.GeneratePlatformRopeEntries(d);
   Need(d.edges.Count==edges,"Repeated generation duplicated the entry");
  });
  if(args.Length>1)Test("saved-user-route-replays-actual-right-up-events",()=>{
   var d=RouteFiles.Load(args[1]);
   var platform=d.samples.Single(s=>s.time==10430);
   var rise=d.samples.Single(s=>s.time==10555);
   Need(platform.kind=="walk"&&platform.keys==6&&rise.kind=="climb"&&rise.y<platform.y,"Historical sample did not match recorded walk-to-climb boundary");
   RouteGraph.MeasureUpApproaches(d,d.samples,d.events);
   var entry=d.edges.Single(x=>x.id=="E2");var other=d.edges.Single(x=>x.id=="E4");
   Need(entry.upApproachKey==39&&entry.upApproach==1&&entry.upApproachOverlap==223&&other.upApproachKey==0,
    "Historic overlap assigned to the wrong ladder or missed: "+entry.upApproachKey+","+entry.upApproach+","+entry.upApproachOverlap+","+other.upApproachKey);
   var p=new CombatPlanner(Role(),0,d,true);
   Need(p.Tick(O(1,0,149,88)).continuityKey==39,"Saved route failed to start on the platform");
   Need(p.Tick(O(2,100,152,88)).secondaryContinuityKey==39,"Before reaching the joint, both keys should be held");
   Need(p.Tick(O(3,200,154,88)).secondaryContinuityKey==39,"Reaching the joint alone should not release Right");
   Need(p.Tick(O(4,300,153,87)).continuityKey==38,"First rise was not checked with Up still held");
   var climbing=p.Tick(O(5,400,154,85));
   Need(climbing.continuityKey==38&&climbing.secondaryContinuityKey==0,"Confirmed ascent did not hand off to Up alone");
   Need(d.edges.Single(x=>x.id=="E3").upContinuation,"Recorded Up-only ledge was treated as walking");
   Need(p.Tick(O(6,490,155,83)).continuityKey==38,"First ledge interrupted Up");
   Need(p.Tick(O(7,570,156,83)).continuityKey==38,"Entering second ladder interrupted Up");
   Need(p.Tick(O(8,670,157,77)).continuityKey==38,"Second ladder released Up before reaching its top");
  });
  Test("height-changes-with-left-or-right-stay-walking",()=>{
   var route=new RouteDocument();
   int[] heights={40,40,36,34,37,37,37,40,43,43,43};
   for(int i=0;i<heights.Length;i++)
    route.samples.Add(new RouteSample{time=i*100,x=10+i*3,y=heights[i],keys=2});
   route.events.Add(new RouteKeyEvent{time=0,vk=39,down=true});
   RouteGraph.Build(route);
   Need(route.edges.Count==1&&route.edges[0].kind=="walk","Height changes inserted a jump while building the route");
   Need(route.edges[0].points.Min(p=>p.y)==34&&route.edges[0].points.Last().y==43,"Walking height profile was lost");
   var planner=new CombatPlanner(Role(),0,RouteFiles.Clone(route),true);
   for(int i=0;i<heights.Length-1;i++){
    var plan=planner.Tick(O(i+1,i*100,10+i*3,heights[i]));
    Need(plan!=null&&plan.name=="路线 · 行走"&&plan.steps.All(step=>step.key==39),
     "Trial jumped or stopped on an unmarked height change");
   }
   Need(planner.Tick(O(heights.Length,1000,40,43)).stopReason=="本次路线试走已到终点","Slope trial missed its end");
  });
  Test("explicitly-saved-step-still-uses-configured-jump-and-landing",()=>{
   var d=new RouteDocument();
   d.nodes.AddRange(new[]{new RouteNode{id="A",x=166,y=75},new RouteNode{id="B",x=170,y=71}});
   d.edges.Add(new RouteEdge{id="jump",from="A",to="B",kind="jump",observedJump=true,duration=500,jumpHold=100,jumpDirection=39});
   var role=Role();role.Binding("jump").key=74;
   var planner=new CombatPlanner(role,0,RouteFiles.Clone(d),true);
   Need(planner.Tick(O(1,0,166,75)).steps.Any(step=>step.down&&step.key==74),"An already marked action lost the configured jump");
   Need(planner.Tick(O(2,520,168,75)).stopReason!=null,"Missing jump feedback was treated as a landing");
   planner=new CombatPlanner(role,0,d,true);
   planner.Tick(O(1,0,166,75));
   planner.Tick(O(2,200,169,68));
   planner.Tick(O(3,400,170,71));
   planner.Tick(O(4,500,170,71));
   Need(planner.Tick(O(5,600,170,71)).stopReason==null,"Jump ended before the landing was stable");
   Need(planner.Tick(O(6,700,170,71)).stopReason=="本次路线试走已到终点","Stable landing did not complete the marked jump");
  });
  Test("rope-mount-holds-up-through-jump-and-climb",()=>{
   var d=new RouteDocument();
   d.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=50},new RouteNode{id="B",x=30,y=24},new RouteNode{id="C",x=30,y=6}});
   d.edges.AddRange(new[]{new RouteEdge{id="M",from="A",to="B",kind="rope_mount",duration=400,upLead=40,jumpHold=80,jumpDirection=39,steerHold=120},new RouteEdge{id="U",from="B",to="C",kind="climb",both=true,duration=800}});
   var planner=new CombatPlanner(Role(),0,d);var log=new List<string>();var scheduler=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};
   var plan=planner.Tick(O(1,0,10,50));Need(plan.name=="路线 · 跳跃抓绳","Mount failed to start");
   scheduler.Begin(plan,0);scheduler.Tick(0);Need(log.SequenceEqual(new[]{"38:True"}),"Up not held before jump");
   scheduler.Tick(40);scheduler.Tick(120);scheduler.Tick(160);
   Need(log.Contains("18:False")&&!log.Contains("38:False"),"Jump release incorrectly released Up");
   for(int i=0;i<5;i++){
    double time=200+i*100;var update=planner.Tick(O(2+i,time,i==0?24:30,32-i*3));
    Need(update!=null&&update.continuityKey==38,"Up not renewed as grab is confirmed");
    scheduler.Begin(update,time);scheduler.Tick(time);
   }
   Need(log.Count(x=>x=="38:True")==1&&!log.Contains("38:False"),"Up was interrupted across rope grab and climb");
   Need(planner.Status=="路线 · 梯绳","Mount did not hand off to climbing");
   scheduler.Cancel();Need(log.Last()=="38:False","Pause did not release Up");
  });
  Test("down-is-held-before-walking-onto-rope-and-after-entry",()=>{
   var d=new RouteDocument();
   d.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=18},new RouteNode{id="B",x=30,y=18},new RouteNode{id="C",x=30,y=40}});
   d.edges.AddRange(new[]{new RouteEdge{id="W",from="A",to="B",kind="walk",both=true,duration=900},new RouteEdge{id="D",from="B",to="C",kind="climb",both=true,downApproach=10,duration=800}});
   var planner=new CombatPlanner(Role(),0,d);var log=new List<string>();var scheduler=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};
   var frames=new[]{O(1,0,10,18),O(2,100,21,18),O(3,200,27,18),O(4,300,30,23),O(5,400,30,29)};
   foreach(var frame in frames){
    scheduler.Tick(frame.now);var next=planner.Tick(frame);Need(next!=null,"No plan for descent");scheduler.Begin(next,frame.now);scheduler.Tick(frame.now);
    if(frame.sequence==2)Need(log.SequenceEqual(new[]{"39:True","40:True"}),"Did not hold Down before rope entry");
   }
   Need(log.SequenceEqual(new[]{"39:True","40:True","39:False"}),"Down did not stay held across the rope handoff");
   scheduler.Cancel();Need(log.Last()=="40:False","Down remained held after pause");
  });
  Test("rope-exit-can-jump-without-horizontal-or-down",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=25,y=20},new RouteNode{id="B",x=25,y=40}});
   d.edges.Add(new RouteEdge{id="E",from="A",to="B",kind="rope_exit",jumpDirection=0,jumpHold=110,duration=600});
   var p=new CombatPlanner(Role(),0,d);var plan=p.Tick(O(1,0,25,20));
   Need(plan.name=="路线 · 离绳跳跃"&&plan.steps.All(x=>x.key==18)&&plan.steps.Last().at==110,"Jump-only rope exit generated unrelated keys");
  });
  Test("starting-near-rope-still-preholds-down",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=30,y=10},new RouteNode{id="B",x=30,y=40}});
   d.edges.Add(new RouteEdge{id="D",from="A",to="B",kind="climb",both=true,duration=600});
   var p=new CombatPlanner(Role(),0,d);
   var plan=p.Tick(O(1,0,25,10));Need(plan.steps[0].key==40&&plan.steps[0].down&&plan.secondaryContinuityKey==39,"Starting near rope missed the Down prehold");
   plan=p.Tick(O(2,100,30,14));Need(plan.continuityKey==40&&plan.secondaryContinuityKey==0,"Descent did not keep Down");
  });
  Test("rope-mount-aligns-at-takeoff-and-stops-when-not-grabbed",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=18,y=40},new RouteNode{id="B",x=38,y=20}});
   d.edges.Add(new RouteEdge{id="M",from="A",to="B",kind="rope_mount",jumpDirection=39,duration=500});
   var p=new CombatPlanner(Role(),0,d);
   Need(p.Tick(O(1,0,13,40)).name=="路线 · 对齐起跳点","Jumped before the actual takeoff point");
   Need(p.Tick(O(2,100,18,40)).name=="路线 · 跳跃抓绳","Did not jump at the takeoff point");
   Need(p.Tick(O(3,500,22,40)).steps.All(x=>x.key==38),"Failed grab caused a second jump");
   var retry=p.Tick(O(4,2100,22,40));
   Need(retry.stopReason==null&&retry.steps.Count==0&&retry.name.Contains("重新接路"),"Missing rope confirmation did not release and replan");
  });
  Test("rope-takeoff-allows-every-position-between-preset-and-shaft",()=>{
   foreach(bool painted in new[]{false,true})foreach(int direction in new[]{37,39})foreach(bool trial in new[]{false,true}){
    var d=new RouteDocument();int start=direction==39?80:89;
    d.nodes.AddRange(new[]{new RouteNode{id="L",x=start,y=86},new RouteNode{id="B",x=84,y=81},new RouteNode{id="T",x=84,y=57}});
    d.edges.AddRange(new[]{new RouteEdge{id="M",from="L",to="B",kind="rope_mount",edited=painted,duration=300,jumpDirection=direction},new RouteEdge{id="U",from="B",to="T",kind="climb",both=true}});
    for(int x=Math.Min(start,84);x<=Math.Max(start,84);x++){
     var role=Role();role.Binding("jump").key=32;var p=new CombatPlanner(role,0,d,trial);
     var plan=p.Tick(O(1,0,start,86));
     if(x!=start){p=new CombatPlanner(role,0,d,trial);p.Tick(O(1,0,start-2*(direction==39?1:-1),86));plan=p.Tick(O(2,100,x,86));}
     Need(plan!=null&&plan.name=="路线 · 跳跃抓绳"&&plan.stopReason==null,"A closer approved takeoff moved back to a fixed coordinate");
     var keys=new List<string>();var held=new HashSet<int>();var scheduler=new ActionScheduler{Send=(key,down)=>{keys.Add(key+":"+down);if(down)held.Add(key);else held.Remove(key);}};
     scheduler.Begin(plan,0);scheduler.Tick(painted?20:0);
     Need(held.Contains(38)&&held.Contains(32),"Up and Jump were not both sent at takeoff");
     if(x==84)Need(!held.Contains(37)&&!held.Contains(39),"Aligned takeoff sent horizontal movement");
     else Need(held.Contains(direction)&&!held.Contains(direction==37?39:37)&&keys.IndexOf(direction+":True")<keys.IndexOf("32:True"),"Horizontal key was missing, reversed or sent after Jump");
     scheduler.Cancel();Need(held.Count==0,"Pause retained takeoff keys");
    }
   }
  });
  Test("takeoff-outside-the-approved-interval-approaches-only-its-nearest-boundary",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=80,y=86},new RouteNode{id="B",x=84,y=81}});d.edges.Add(new RouteEdge{id="M",from="A",to="B",kind="rope_mount",edited=true,duration=300,jumpDirection=39});
   var p=new CombatPlanner(Role(),0,d);var approach=p.Tick(O(1,0,78,86));Need(approach.continuityKey==39&&approach.steps.All(k=>k.key==39),"Behind-preset position did not approach the allowed interval");
   var jump=p.Tick(O(2,100,82,86));Need(jump.name=="路线 · 跳跃抓绳"&&jump.steps.Any(k=>k.key==39&&k.down),"Crossing the preset forced a backward correction");
   p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,78,86));var wrongFloor=p.Tick(O(2,100,82,82));Need(wrongFloor==null||wrongFloor.name!="路线 · 跳跃抓绳","Another height was accepted solely because X was in range");
  });
  bool okay=results.Cast<Dictionary<string,object>>().All(x=>Wire.Bool(x,"ok"));var report=Wire.Map("ok",okay,"game_input",false,"ssh_started",false,"tests",results);Wire.Save(Path.Combine(args[0],"route-flow-checks.json"),report);Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(report));if(!okay)Environment.ExitCode=1;
 }
}}
