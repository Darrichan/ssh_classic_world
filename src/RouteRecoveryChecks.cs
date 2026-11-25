using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;
namespace SshScreen {
public static class RouteRecoveryChecks {
 static readonly List<object> results=new List<object>();
 static void Need(bool v,string message){if(!v)throw new IOException(message);}
 static void Test(string name,Action run){try{run();results.Add(Wire.Map("name",name,"ok",true));}catch(Exception e){results.Add(Wire.Map("name",name,"ok",false,"error",e.Message));}}
 static RolePreset Role(){var r=new RolePreset();r.battle.mode="route";r.battle.detectMonsters=false;r.battle.inPlace=true;return r;}
 static CombatObservation O(long seq,double now,float x,float y=40){return new CombatObservation{sequence=seq,now=now,window=42,fresh=true,dot=new PointF(x,y),body=new Point(300,200),facing="right",facingFresh=true};}
 static RouteDocument Walk(){var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=0,y=40},new RouteNode{id="B",x=100,y=40}});d.edges.Add(new RouteEdge{id="W",from="A",to="B",kind="walk",both=true,duration=1});return d;}
 public static void Main(string[] args){
  Test("moving-walk-does-not-expire-at-the-old-four-second-deadline",()=>{
   var p=new CombatPlanner(Role(),0,Walk());
   for(int i=0;i<12;i++){var plan=p.Tick(O(i+1,i*1000,i*4));Need(plan!=null&&plan.stopReason==null&&plan.continuityKey==39,"A progressing walk stopped or changed direction");}
  });
  Test("combat-time-does-not-consume-the-navigation-timeout",()=>{
   var r=Role();r.battle.detectMonsters=true;r.battle.style="melee";r.Binding("aoe").enabled=false;
   var p=new CombatPlanner(r,0,Walk());p.Tick(O(1,0,0));
   for(int i=1;i<=16;i++){var o=O(i+1,i*500,5);o.monsters.Add(new Rectangle(330,180,30,40));var plan=p.Tick(o);Need(plan==null||plan.stopReason==null,"Combat paused route execution");}
   var next=p.Tick(O(18,9000,5));Need(next!=null&&next.continuityKey==39&&next.stopReason==null,"Eight seconds of combat expired the walk");
  });
  Test("stationary-direction-is-released-and-the-recorded-reverse-is-tried",()=>{
   var p=new CombatPlanner(Role(),0,Walk());var scheduler=new ActionScheduler();var held=new HashSet<int>();
   scheduler.Send=(key,down)=>{if(down)held.Add(key);else held.Remove(key);};
   var first=p.Tick(O(1,0,50));scheduler.Begin(first,0);scheduler.Tick(0);Need(held.Contains(39),"Initial forward walk missing");
   var retry=p.Tick(O(2,4100,50));Need(retry.stopReason==null&&retry.steps.Count==0&&retry.name.Contains("重新"),"Stalled normal run stopped instead of recovering");
   scheduler.Begin(retry,4100);scheduler.Tick(4100);Need(held.Count==0,"Recovery kept the blocked key held");
   var reverse=p.Tick(O(3,4500,50));Need(reverse.stopReason==null&&reverse.continuityKey==37,"Did not choose the available recorded reverse direction");
   scheduler.Begin(reverse,4500);scheduler.Tick(4500);Need(held.SetEquals(new[]{37}),"Old direction survived recovery");scheduler.Cancel();Need(held.Count==0,"Pause did not cancel recovery input");
  });
  Test("starting-on-a-walk-does-not-select-a-nearer-node-on-another-floor",()=>{
   var d=Walk();d.nodes.AddRange(new[]{new RouteNode{id="upper",x=75,y=34},new RouteNode{id="end",x=95,y=34}});
   d.edges.Insert(0,new RouteEdge{id="upper-walk",from="upper",to="end",kind="walk",both=true});
   var p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,75));Need(p.CurrentRouteEdge=="W","Selected the closer upper-floor node instead of the occupied segment");
  });
  Test("unmatched-position-moves-and-then-rejoins-without-stopping",()=>{
   var p=new CombatPlanner(Role(),0,Walk());var waiting=p.Tick(O(1,0,50,65));Need(waiting.stopReason==null&&waiting.steps.Any(step=>step.down&&(step.key==37||step.key==39)),"Unknown floor kept waiting for a recording instead of adjusting horizontally");
   p.Tick(O(2,400,50));p.Tick(O(3,500,50));var fresh=p.Tick(O(4,600,50));Need(fresh.stopReason==null&&fresh.continuityKey==39,"Confirmed on-route landing did not resume the run");
  });
  Test("failed-up-climb-can-descend-the-same-recorded-rope",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="bottom",x=84,y=81},new RouteNode{id="top",x=84,y=57}});
   d.edges.Add(new RouteEdge{id="C22",from="bottom",to="top",kind="climb",both=true,duration=1});
   var p=new CombatPlanner(Role(),0,d);Need(p.Tick(O(1,0,84,71)).continuityKey==38,"Rope midpoint did not resume upward");
   Need(p.Tick(O(2,4100,84,71)).stopReason==null,"A blocked upward climb stopped the run");
   Need(p.Tick(O(3,4500,84,71)).continuityKey==40,"Did not use the recorded downward direction");
  });
  Test("a-failed-drop-does-not-starve-an-untried-upper-branch-after-cooldown",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=80,y=58},new RouteNode{id="B",x=80,y=86},new RouteNode{id="C",x=60,y=58},new RouteNode{id="T",x=80,y=30}});
   d.edges.AddRange(new[]{new RouteEdge{id="D",from="A",to="B",kind="drop",duration=814},new RouteEdge{id="W",from="A",to="C",kind="walk",both=true,duration=1500},new RouteEdge{id="U",from="A",to="T",kind="climb",both=true,duration=4000}});
   var p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,80,58));p.Tick(O(2,350,78,53));p.Tick(O(3,450,76,58));p.Tick(O(4,550,76,58));p.Tick(O(5,650,76,58));p.Tick(O(6,1000,60,58));
   var next=p.Tick(O(7,12000,80,58));Need(p.CurrentRouteEdge=="U"&&next.continuityKey==38,"Expired failure cooldown sent the same failed drop before trying the upper branch");
  });
  Test("two-pixel-rope-offset-keeps-vertical-and-corrects-horizontal-until-aligned",()=>{
   foreach(bool trial in new[]{false,true})foreach(bool down in new[]{false,true})foreach(int side in new[]{-1,1}){
    var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=10,y=75},new RouteNode{id="B",x=10,y=42}});
    d.edges.Add(new RouteEdge{id="C8",from="A",to="B",kind="climb",both=true,duration=5000});
    var role=Role();var p=new CombatPlanner(role,0,d,trial);var log=new List<string>();var held=new HashSet<int>();
    var scheduler=new ActionScheduler{Send=(key,pressed)=>{log.Add(key+":"+pressed);if(pressed)held.Add(key);else held.Remove(key);}};
    int vertical=down?40:38,horizontal=side<0?39:37,y=down?55:73;
    var initial=p.Tick(O(1,0,10,down?42:75));scheduler.Begin(initial,0);scheduler.Tick(0);
    var first=p.Tick(O(2,100,10+side*2,y));scheduler.Begin(first,100);scheduler.Tick(100);
    Need(held.SetEquals(new[]{vertical,horizontal}),"Two-pixel mismatch was treated as an aligned shaft");
    var closer=p.Tick(O(3,200,10+side,y));scheduler.Begin(closer,200);scheduler.Tick(200);
    Need(log.Count==2&&held.SetEquals(new[]{vertical,horizontal}),"Correction released vertical or repeated the same keys");
    var aligned=p.Tick(O(4,300,10,y));scheduler.Begin(aligned,300);scheduler.Tick(300);
    Need(held.SetEquals(new[]{vertical})&&log.Last()==horizontal+":False"&&!log.Contains(vertical+":False"),"Reaching the shaft did not release only the horizontal key");
    scheduler.Cancel();Need(held.Count==0&&log.Last()==vertical+":False","Pause did not release the climbing key");
   }
  });
  Test("missing-jump-binding-remains-an-actionable-configuration-error",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=0,y=40},new RouteNode{id="B",x=10,y=20}});d.edges.Add(new RouteEdge{id="J",from="A",to="B",kind="jump"});var r=Role();r.Binding("jump").enabled=false;
   Need(new CombatPlanner(r,0,d).Tick(O(1,0,0)).stopReason=="请启用跳跃按键","Invalid configuration entered endless recovery");
  });
  if(args.Length>1)Test("current-canyon-rope-midpoint-and-long-manual-walk-replay",()=>{
   var d=RouteFiles.Load(args[1]);var p=new CombatPlanner(Role(),0,d);var onRope=p.Tick(O(1,0,84.2f,71.4f));Need(p.CurrentRouteEdge=="C22"&&onRope.continuityKey==38,"Actual yellow point was assigned to the wrong route");
   foreach(var top in d.nodes.Where(n=>n.y<=22)){
    var path=RouteGraph.Plan(d,"N82",top.id);
    Need(path!=null&&path.Any(e=>e.id=="C23")&&path.Any(e=>e.kind=="climb"),"Saved bottom-to-upper route has no directed path to "+top.id);
   }
   var segment=d.edges.Single(e=>e.id=="E008aab1b8fb643478c19ed2504210c09");Need(segment.duration==1,"Actual manually drawn segment no longer reproduces the short deadline");
   p=new CombatPlanner(Role(),0,d);for(int i=0;i<7;i++){var plan=p.Tick(O(i+1,i*900,47+i,86));Need(plan==null||plan.stopReason==null,"Actual manual segment stopped while making progress");}
  });
  Test("drop-rejoins-a-confirmed-platform-away-from-the-recorded-landing-x",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=55,y=58},new RouteNode{id="B",x=55,y=86},new RouteNode{id="C",x=80,y=86}});
   d.edges.AddRange(new[]{new RouteEdge{id="D",from="A",to="B",kind="drop",duration=878},new RouteEdge{id="W",from="B",to="C",kind="walk",both=true}});
   var p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,55,58));p.Tick(O(2,350,65,79));p.Tick(O(3,450,68,85));p.Tick(O(4,550,68,85));
   var landed=p.Tick(O(5,650,68,85));Need(landed!=null&&landed.stopReason==null&&landed.continuityKey==39&&p.CurrentRouteEdge=="W","Standing on the bottom platform still waited for X=55");
  });
  Test("failed-drop-rejoins-the-origin-platform-after-the-jump-lands",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=80,y=58},new RouteNode{id="B",x=80,y=86},new RouteNode{id="C",x=60,y=58}});
   d.edges.AddRange(new[]{new RouteEdge{id="D",from="A",to="B",kind="drop",duration=814},new RouteEdge{id="W",from="A",to="C",kind="walk",both=true}});
   var p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,80,58));p.Tick(O(2,350,78,53));p.Tick(O(3,450,76,58));p.Tick(O(4,550,76,58));
   var landed=p.Tick(O(5,650,76,58));Need(landed!=null&&landed.stopReason==null&&landed.continuityKey==37&&p.CurrentRouteEdge=="W","Failed platform drop waited for the four-second timeout");
  });
  Test("rope-top-quantization-uses-the-connected-platform-without-waiting-for-timeout",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=84,y=81},new RouteNode{id="B",x=84,y=57},new RouteNode{id="C",x=100,y=58}});
   d.edges.AddRange(new[]{new RouteEdge{id="R",from="A",to="B",kind="climb",both=true,duration=3777},new RouteEdge{id="W",from="B",to="C",kind="walk",both=true}});
   var p=new CombatPlanner(Role(),0,d);p.Tick(O(1,0,84,71));p.Tick(O(2,300,84,62));p.Tick(O(3,450,84,58));p.Tick(O(4,550,84,58));
   var exit=p.Tick(O(5,650,84,58));Need(exit!=null&&exit.continuityKey==38&&exit.secondaryContinuityKey==39&&exit.steps.All(k=>k.key==38||k.key==39)&&p.CurrentRouteEdge=="W","Rounded rope-top position did not hand off to its platform");
  });
  Test("falling-off-an-active-rope-rejoins-the-observed-floor-before-combat-turns",()=>{
   var d=new RouteDocument();d.nodes.AddRange(new[]{new RouteNode{id="A",x=84,y=81},new RouteNode{id="B",x=84,y=57},new RouteNode{id="C",x=55,y=86},new RouteNode{id="D",x=100,y=86}});
   d.edges.AddRange(new[]{new RouteEdge{id="R",from="A",to="B",kind="climb",both=true,duration=3777},new RouteEdge{id="W",from="C",to="D",kind="walk",both=true}});
   var r=Role();r.battle.detectMonsters=true;var p=new CombatPlanner(r,0,d);p.Tick(O(1,0,84,71));p.Tick(O(2,400,85,86));p.Tick(O(3,500,85,86));
   var o=O(4,600,85,86);o.monsters.Add(new Rectangle(330,180,30,40));var joined=p.Tick(o);Need(joined!=null&&joined.continuityKey==39&&p.CurrentRouteEdge=="W","Old rope or combat turning retained the fallen character");
   o=O(5,700,85,86);o.monsters.Add(new Rectangle(330,180,30,40));Need(p.Tick(o).name=="普通攻击","Combat did not resume after floor attachment");
  });
  bool okay=results.Cast<Dictionary<string,object>>().All(x=>Wire.Bool(x,"ok"));var report=Wire.Map("ok",okay,"game_input",false,"ssh_started",false,"tests",results);Wire.Save(Path.Combine(args[0],"route-recovery-checks.json"),report);Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(report));if(!okay)Environment.ExitCode=1;
 }
}}
