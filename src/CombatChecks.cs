using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;
namespace SshScreen {
public static class CombatChecks {
 static readonly List<object> results=new List<object>();static void Need(bool v,string text){if(!v)throw new Exception(text);}
 static void Test(string name,Action check){try{check();results.Add(Wire.Map("name",name,"ok",true));}catch(Exception e){results.Add(Wire.Map("name",name,"ok",false,"error",e.Message));}}
 static RolePreset Role(){var r=new RolePreset();r.battle.mode="stationary";r.battle.inPlace=true;return r;}
 static CombatObservation O(long seq,double now,params Rectangle[] monsters){return new CombatObservation{sequence=seq,window=42,now=now,fresh=true,dot=new PointF(30,20),body=new Point(300,200),monsters=monsters.ToList()};}
 static Rectangle Mob(int x){return new Rectangle(x,180,30,40);}
 public static void Main(string[] args){string root=args[0];
 Test("trial-start-at-platform-prefers-recorded-direction-over-reverse-climb",()=>{
  var r=Role();r.battle.mode="route";
  var d=new RouteDocument();
  d.nodes.AddRange(new[]{new RouteNode{id="lower",x=144,y=88},new RouteNode{id="upper",x=144,y=71},new RouteNode{id="platform",x=149,y=88}});
  d.edges.Add(new RouteEdge{id="climb",from="upper",to="lower",kind="climb",both=true});
  d.edges.Add(new RouteEdge{id="walk",from="lower",to="platform",kind="walk",both=true});
  var o=O(1,0);o.dot=new PointF(143,88);
  var plan=new CombatPlanner(r,0,d,true).Tick(o);
  Need(plan.stopReason==null&&plan.steps[0].key==39,"Lower-platform trial pressed Up instead of walking onward");
 });
 Test("trial-switches-branches-once-when-stationary-and-reports-the-blocked-segment",()=>{
  var r=Role();r.battle.mode="route";
  var d=new RouteDocument();
  d.nodes.AddRange(new[]{new RouteNode{id="lower",x=20,y=20},new RouteNode{id="upper",x=20,y=4},new RouteNode{id="platform",x=50,y=20}});
  d.edges.Add(new RouteEdge{id="climb",from="lower",to="upper",kind="climb",both=true});
  d.edges.Add(new RouteEdge{id="walk",from="platform",to="lower",kind="walk",both=true});
  var planner=new CombatPlanner(r,0,d,true);
  var o=O(1,0);o.dot=new PointF(20,20);
  Need(planner.Tick(o).steps[0].key==38,"Did not start with the forward ladder");
  o.sequence=2;o.now=1300;
  Need(planner.Tick(o).steps[0].key==39,"Did not try the adjacent walk after the ladder failed to move");
  o.sequence=3;o.now=2600;
  Need(planner.Tick(o).stopReason.Contains("左右行走"),"Repeated a blocked branch or failed to identify it");
 });
 Test("trial-uses-only-navigation-and-ends-without-looping",()=>{
  var r=Role();r.battle.mode="route";r.battle.teleportWhileWalking=true;
  r.Binding("teleport").enabled=true;r.Binding("hp").enabled=true;r.hp.enabled=true;
  r.buffs.Add(new BuffBinding{enabled=true,key=82});
  var d=new RouteDocument();
  d.nodes.AddRange(new[]{new RouteNode{id="A",x=20,y=20},new RouteNode{id="B",x=65,y=20}});
  d.edges.Add(new RouteEdge{id="walk",from="A",to="B",kind="walk",both=true});
  var p=new CombatPlanner(r,0,d,true);
  var start=O(1,0,Mob(200),Mob(350));start.hp=1;start.mp=1;start.dot=new PointF(35,20);
  var move=p.Tick(start);
  Need(move.stopReason==null&&move.steps.Count>0&&move.steps.All(k=>k.key==39),"Trial did not begin walking on the current segment");
  var end=O(2,300,Mob(200));end.dot=new PointF(65,20);
  Need(p.Tick(end).stopReason.Contains("终点"),"Trial repeated a completed segment");
  var offRoute=O(3,400);offRoute.dot=new PointF(35,50);
  Need(new CombatPlanner(r,0,d,true).Tick(offRoute).stopReason.Contains("附近"),"Off-route trial started pressing keys");
 });
 Test("original-left-right-box-parameters-and-cross-split",()=>{var c=new BattleSettings();var p=new Point(300,200);BattleGeometry.SetAttack(c,p,new Rectangle(100,100,190,150));Need(c.left.reach==200&&c.left.offsetY==-25&&c.left.halfHeight==75,"Original left conversion mismatch");BattleGeometry.SetAttack(c,p,new Rectangle(310,150,160,150));Need(c.left.reach==200&&c.right.reach==170&&c.right.offsetY==25,"Editing right lost left");Need(BattleGeometry.Attack(p,c,true)==Rectangle.FromLTRB(100,100,300,250),"Original engine left box mismatch");BattleGeometry.SetAttack(c,p,new Rectangle(220,120,170,100));Need(c.left.reach==80&&c.right.reach==90,"Crossing selection not split");});
 Test("search-and-movement-use-original-coordinates",()=>{var c=new BattleSettings{mode="route"};BattleGeometry.SetSearch(c,new Point(300,200),new Rectangle(100,100,370,200));Need(BattleGeometry.Search(new Point(300,200),c)==new Rectangle(100,100,370,200),"Search conversion drift");BattleGeometry.SetMovement(c,new Point(300,200),new Rectangle(100,100,370,200));Need(c.moveLeft==200&&c.moveRight==170,"Asymmetric movement lost");});
 Test("stationary-search-equals-independent-attack-union",()=>{var c=new BattleSettings();c.left.enabled=false;Need(BattleGeometry.Search(new Point(300,200),c)==BattleGeometry.Attack(new Point(300,200),c,false),"Disabled left still searchable");});
 Test("disabled-all-attack-bindings-produce-no-turn-or-attack",()=>{var r=Role();r.battle.inPlace=false;r.Binding("attack").enabled=r.Binding("aoe").enabled=false;var p=new CombatPlanner(r,0);Need(p.Tick(O(1,100,Mob(200),Mob(380)))==null,"Disabled attack still emits an action");});
 Test("disabled-aoe-falls-back-to-enabled-basic",()=>{var r=Role();r.Binding("aoe").enabled=false;var p=new CombatPlanner(r,0);Need(p.Tick(O(1,100,Mob(200),Mob(380))).name=="普通攻击","AOE disable did not select basic");});
 Test("aoe-threshold-counts-attack-area-not-full-frame",()=>{var r=Role();r.battle.ranged.aoeCount=2;var p=new CombatPlanner(r,0);Need(p.Tick(O(1,100,Mob(200),Mob(1200))).name=="普通攻击","Distant monster counted toward AOE");var p2=new CombatPlanner(r,0);Need(p2.Tick(O(1,100,Mob(200),Mob(380))).name=="范围技能","Two in-range monsters did not trigger AOE");});
 Test("unknown-facing-needs-accepted-turn-before-basic",()=>{var r=Role();r.battle.inPlace=false;r.Binding("aoe").enabled=false;var p=new CombatPlanner(r,0);var one=O(1,0,Mob(200));Need(p.Tick(one).name=="转向确认","Basic attacked before turn");Need(p.Tick(O(2,90,Mob(200)))==null,"No movement/face confirmation but attacked");var after=O(3,150,Mob(200));after.body=new Point(299,200);Need(p.Tick(after).name=="普通攻击","Accepted body movement failed to open attack gate");});
 Test("direction-independent-aoe-does-not-turn",()=>{var r=Role();r.battle.inPlace=false;r.battle.ranged.aoeNoTurn=true;var p=new CombatPlanner(r,0);Need(p.Tick(O(1,0,Mob(200),Mob(380))).name=="范围技能","No-turn AOE incorrectly gated");});
 Test("cooldown-monotonic-and-duplicate-frame-not-replayed",()=>{var r=Role();r.Binding("aoe").enabled=false;var p=new CombatPlanner(r,0);Need(p.Tick(O(1,0,Mob(200)))!=null,"Missing initial attack");Need(p.Tick(O(1,400,Mob(200)))==null,"Duplicate frame replayed");Need(p.Tick(O(2,200,Mob(200)))==null,"Cooldown ignored");Need(p.Tick(O(3,400,Mob(200)))!=null,"Cooldown never completes");});
 Test("stale-or-lost-player-stops-even-on-cached-sequence",()=>{var p=new CombatPlanner(Role(),0);p.Tick(O(1,0,Mob(200)));var stale=O(1,700,Mob(200));stale.age=700;Need(p.Tick(stale).stopReason!=null,"Stale duplicate remained active");});
 Test("hp-has-priority-and-disabled-key-is-hard-gate",()=>{var r=Role();r.hp.enabled=true;r.Binding("hp").enabled=true;r.buffs.Add(new BuffBinding{enabled=true});var o=O(1,0,Mob(200));o.hp=10;Need(new CombatPlanner(r,0).Tick(o).name=="补HP","HP did not preempt buff/attack");r.Binding("hp").enabled=false;Need(new CombatPlanner(r,0).Tick(o).name.StartsWith("增益"),"Disabled HP key still dispatched");});
 Test("buff-releases-key-before-action-delay",()=>{var r=Role();r.buffs.Add(new BuffBinding{enabled=true,delay=1});var p=new CombatPlanner(r,0);var plan=p.Tick(O(1,0));Need(plan.steps.Last().at==80&&plan.duration>=1080,"Buff delay held the key");Need(p.Tick(O(2,500,Mob(200)))==null,"Attack interrupted buff action delay");});
 Test("rest-suppresses-attacks-and-buffs",()=>{var r=Role();r.battle.restEveryMinutes=1;r.battle.restMinutes=1;r.buffs.Add(new BuffBinding{enabled=true});var p=new CombatPlanner(r,0);Need(p.Tick(O(1,60001,Mob(200)))==null&&p.Status=="休息中","Rest still cast a skill");});
 Test("boundary-return-and-patrol-requires-minimap",()=>{var r=Role();var p=new CombatPlanner(r,0);p.Tick(O(1,0));var o=O(2,200);o.body=new Point(340,200);o.dot=new PointF(34,20);var plan=p.Tick(o);Need(plan.name=="返回活动区"&&plan.steps[0].key==37,"Outside right boundary did not move left");r.battle.mode="patrol";var noMini=O(1,0);noMini.dot=null;Need(new CombatPlanner(r,0).Tick(noMini).stopReason!=null,"Patrol guessed uncalibrated coordinates");});
 Test("empty-patrol-keeps-moving-and-renews-direction-without-tapping",()=>{
  var r=Role();r.battle.mode="patrol";r.battle.returnToAnchor=true;r.battle.patrolRadius=10;r.battle.screenPerMini=10;
  var p=new CombatPlanner(r,0);var keys=new List<string>();var s=new ActionScheduler{Send=(key,down)=>keys.Add(key+":"+down)};
  var first=p.Tick(O(1,0));Need(first!=null&&first.name=="平台巡逻","Empty patrol waited at the anchor");
  s.Begin(first,0);s.Tick(0);s.Tick(90);
  var next=O(2,100);next.dot=new PointF(31,20);s.Begin(p.Tick(next),100);s.Tick(100);s.Tick(190);
  Need(keys.SequenceEqual(new[]{"39:True"}),"Same-direction patrol released or repressed the key");
  var turn=O(3,200);turn.dot=new PointF(39,20);s.Begin(p.Tick(turn),200);s.Tick(200);
  Need(keys.SequenceEqual(new[]{"39:True","39:False","37:True"}),"Patrol did not switch directions cleanly at the boundary");
 });
 Test("chase-holds-direction-and-releases-it-before-attacking",()=>{
  var r=Role();r.battle.mode="patrol";r.battle.style="melee";
  var p=new CombatPlanner(r,0);var keys=new List<string>();var s=new ActionScheduler{Send=(key,down)=>keys.Add(key+":"+down)};
  var a=p.Tick(O(1,0,Mob(470)));Need(a.name=="追击","The monster was not outside attack reach");
  s.Begin(a,0);s.Tick(0);s.Tick(90);s.Begin(p.Tick(O(2,100,Mob(455))),100);s.Tick(100);
  Need(keys.SequenceEqual(new[]{"39:True"}),"Chasing repeatedly pressed the direction");
  s.Begin(p.Tick(O(3,200,Mob(355))),200);s.Tick(200);
  Need(keys.SequenceEqual(new[]{"39:True","39:False","67:True"}),"Attacking did not stop the chase first");
 });
 Test("melee-patrol-passes-on-monsters-outside-its-attack-height",()=>{
  var r=Role();r.battle.mode="patrol";r.battle.style="melee";
  var p=new CombatPlanner(r,0);
  var plan=p.Tick(O(1,0,new Rectangle(80,340,30,40)));
  Need(plan.name=="平台巡逻"&&plan.continuityKey==39,"A lower monster steered the character away from patrol");
 });
 Test("unchanged-minimap-position-breaks-a-chase-and-reverses-at-a-platform-end",()=>{
  var r=Role();r.battle.mode="patrol";r.battle.style="melee";
  var p=new CombatPlanner(r,0);var keys=new List<string>();
  var s=new ActionScheduler{Send=(key,down)=>keys.Add(key+":"+down)};
  for(int i=0;i<=10;i++){
   double now=i*100;
   var plan=p.Tick(O(i+1,now,Mob(470)));
   Need(plan.name==(i<10?"追击":"平台巡逻"),"No-progress chase did not yield to patrol");
   s.Begin(plan,now);s.Tick(now);
  }
  Need(keys.SequenceEqual(new[]{"39:True","39:False","37:True"}),"Blocked chase did not release and reverse cleanly");
  Need(p.Tick(O(12,1100,Mob(470))).continuityKey==37,"Blocked target was reacquired with no movement");
  var away=O(13,1200,Mob(470));away.dot=new PointF(27,20);
  Need(p.Tick(away).continuityKey==37,"Patrol turned back to the blocked chase too soon");
 });
 Test("patrol-resumes-when-the-return-to-anchor-makes-no-progress",()=>{
  var r=Role();r.battle.mode="patrol";r.battle.style="melee";r.battle.returnDelay=.1;
  var p=new CombatPlanner(r,0);Need(p.Tick(O(1,0,Mob(470))).name=="追击","Could not start the anchor-return fixture");
  for(int i=1;i<=6;i++){
   var o=O(i+1,i*200);o.dot=new PointF(34,20);
   var plan=p.Tick(o);
   Need(plan.name==(i<6?"返回锚点":"平台巡逻")&&plan.continuityKey==(i<6?37:39),"Blocked anchor kept taking priority over patrol");
  }
  p=new CombatPlanner(r,0);p.Tick(O(1,0,Mob(470)));
  var lower=O(2,300);lower.dot=new PointF(34,26);
  Need(p.Tick(lower).name=="平台巡逻","Patrol tried to return to an anchor on another floor");
 });
 Test("blocked-patrol-on-mapped-rope-leaves-with-direction-and-configured-jump",()=>{
  var r=Role();r.battle.mode="patrol";r.Binding("jump").key=32;
  var d=new RouteDocument();
  d.nodes.AddRange(new[]{new RouteNode{id="foot",x=30,y=35},new RouteNode{id="top",x=30,y=5}});
  d.edges.Add(new RouteEdge{id="rope",kind="climb",from="foot",to="top",both=true});
  var p=new CombatPlanner(r,0,d);var keys=new List<string>();
  var scheduler=new ActionScheduler{Send=(key,down)=>keys.Add(key+":"+down)};
  for(int i=0;i<=10;i++){
   var plan=p.Tick(O(i+1,i*100));
   Need(plan.name==(i<10?"平台巡逻":"巡逻 · 离开梯绳"),"Rope exit did not follow the stuck movement");
   scheduler.Begin(plan,i*100);scheduler.Tick(i*100);
  }
  Need(keys.SequenceEqual(new[]{"39:True","39:False","37:True","32:True"}),"Wrong exit direction or jump was not simultaneous");
  scheduler.Tick(1090);Need(keys.Last()=="32:False","Jump remained held after leaving the rope");
  var landed=O(12,1300);landed.dot=new PointF(27,24);
  var resume=p.Tick(landed);Need(resume.name=="平台巡逻"&&resume.continuityKey==37,"Landing did not resume patrol");
  p=new CombatPlanner(r,0,d);
  var first=O(1,0);first.dot=new PointF(33,20);p.Tick(first);
  var near=O(2,1000);near.dot=first.dot;
  Need(p.Tick(near).steps.All(step=>step.key!=32),"A blocked step next to the rope jumped");
  r.Binding("jump").enabled=false;p=new CombatPlanner(r,0,d);p.Tick(O(1,0));
  Need(p.Tick(O(2,1000)).steps.All(step=>step.key!=32),"Disabled jump key was sent");
 });
 Test("scheduler-cancel-releases-and-removes-future-keys",()=>{var log=new List<string>();var q=new ActionScheduler{Send=(key,down)=>log.Add(key+":"+down)};q.Begin(CombatPlan.Chord("drop",40,18,60,80,150),0);q.Tick(0);q.Cancel();q.Tick(1000);Need(log.SequenceEqual(new[]{"40:True","40:False"})&&!q.Busy,"Cancel emitted a late jump or retained key");});
 Test("hp-meter-lossless-colors-and-invalid-geometry",()=>{var raw=new byte[100*10*3];for(int y=0;y<10;y++)for(int x=0;x<50;x++)raw[(y*100+x)*3+2]=200;var box=new MeterArea{x=0,y=0,width=100,height=10,sourceWidth=100,sourceHeight=10};Need(Math.Abs(BattleGeometry.Meter(raw,100,10,box,true).Value-50)<.01,"HP bar ratio incorrect");Need(!BattleGeometry.Meter(raw,101,10,box,true).HasValue,"Resized geometry reused");});
 Test("preset-save-roundtrip-and-deep-copy-preserve-body",()=>{string dir=Path.Combine(root,"preset-roundtrip-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var d=WorkspaceFiles.Load(dir);d.Current.vision.nametag.body_x=41;d.Current.vision.nametag.body_y=-29;d.Current.Binding("aoe").enabled=false;var clone=WorkspaceFiles.Copy(d.Current);clone.id=Guid.NewGuid().ToString("N");clone.name="第二个角色";clone.vision.nametag.body_x=99;d.roles.Add(clone);WorkspaceFiles.Save(dir,d);var restored=WorkspaceFiles.Load(dir);Need(restored.roles.Count==2&&restored.Current.vision.nametag.body_x==41&&restored.Current.vision.nametag.body_y==-29&&!restored.Current.Binding("aoe").enabled,"Preset persistence changed original body or binding");});
 Test("hp-preempts-buff-post-action-delay",()=>{var r=Role();r.hp.enabled=true;r.Binding("hp").enabled=true;r.buffs.Add(new BuffBinding{enabled=true,delay=5});var p=new CombatPlanner(r,0);Need(p.Tick(O(1,0)).name.StartsWith("增益"),"No initial buff");var low=O(2,200);low.hp=5;Need(p.Tick(low).name=="补HP","Buff wait blocked HP");});
 Test("teleport-disabled-falls-back-to-walking",()=>{var r=Role();r.battle.teleportWhileWalking=true;r.Binding("teleport").enabled=false;var p=new CombatPlanner(r,0);p.Tick(O(1,0));var outside=O(2,200);outside.body=new Point(350,200);outside.dot=new PointF(35,20);Need(p.Tick(outside).steps.All(s=>s.key!=32),"Disabled teleport key emitted");r.Binding("teleport").enabled=true;p=new CombatPlanner(r,0);p.Tick(O(1,0));Need(p.Tick(outside).steps.Any(s=>s.key==32),"Enabled teleport not used");});
 Test("persistent-low-hp-home-ends-run-after-key-release",()=>{var r=Role();r.battle.returnHomeOnLowHp=true;r.battle.lowHpTimeout=1;r.Binding("home").enabled=true;var p=new CombatPlanner(r,0);var first=O(1,0);first.hp=5;p.Tick(first);var next=O(2,1100);next.hp=5;var home=p.Tick(next);Need(home.name=="低HP回城"&&home.steps.Last().down==false,"Home sequence invalid");var last=O(3,1300);last.hp=5;Need(p.Tick(last).stopReason!=null,"Home did not stop automation");});
 Test("stationary-detection-off-retains-maintenance-without-attacks",()=>{var r=Role();r.battle.detectMonsters=false;var p=new CombatPlanner(r,0);Need(p.Tick(O(1,0,Mob(200)))==null,"Stationary detection-off still attacked");r.buffs.Add(new BuffBinding{enabled=true});Need(new CombatPlanner(r,0).Tick(O(1,0)).name.StartsWith("增益"),"Maintenance disabled buffs");});
 Test("patrol-projection-and-edited-extension-match-preview",()=>{var c=new BattleSettings{mode="patrol",screenPerMini=10,attackExtension=0};BattleGeometry.SetMovement(c,new Point(300,200),new Rectangle(100,100,370,200));Need(c.patrolRadius==20&&c.searchY==100&&c.searchOffsetY==0,"Original patrol conversion mismatch");Need(BattleGeometry.Search(new Point(300,200),c,300)==Rectangle.FromLTRB(100,100,500,300),"Patrol blue box differs from real bounds");c.attackExtension=50;Need(BattleGeometry.Search(new Point(300,200),c,300).Left==50,"Extension ignored");});
 Test("committed-route-jump-is-not-interrupted-by-monsters-or-apex",()=>{var r=Role();r.battle.mode="route";var d=new RouteDocument();d.nodes.Add(new RouteNode{id="A",x=10,y=10});d.nodes.Add(new RouteNode{id="B",x=20,y=0});d.edges.Add(new RouteEdge{id="J",from="A",to="B",kind="jump",duration=600});var p=new CombatPlanner(r,0,d);var a=O(1,0);a.dot=new PointF(10,10);Need(p.Tick(a).name=="路线 · 跳跃","Route jump did not start");var mid=O(2,400,Mob(200));mid.dot=new PointF(15,3);Need(p.Tick(mid)==null,"Monster interrupted airborne route");var target=O(3,600,Mob(200));target.dot=new PointF(20,0);Need(p.Tick(target)==null,"Jump ended at first crossing");target=O(4,700,Mob(200));target.dot=new PointF(20,0);Need(p.Tick(target)==null,"Jump failed to await stable landing");});
 Test("stationary-bounds-stop-when-minimap-dot-is-lost",()=>{var p=new CombatPlanner(Role(),0);p.Tick(O(1,0));var lost=O(2,200);lost.dot=null;Need(p.Tick(lost).stopReason!=null,"Stationary boundaries fell back to drifting screen coordinates");});
 Test("attack-animation-wait-keeps-key-release-short",()=>{var r=Role();r.Binding("aoe").enabled=false;r.battle.ranged.cooldown=0;r.battle.ranged.animationMs=500;var p=new CombatPlanner(r,0);var plan=p.Tick(O(1,0,Mob(200)));Need(plan.duration==500&&plan.steps.Last().at==100,"Animation delay held attack key");Need(p.Tick(O(2,300,Mob(200)))==null,"Animation delay ignored");Need(p.Tick(O(3,501,Mob(200)))!=null,"Animation never released");});
 bool okay=results.Cast<Dictionary<string,object>>().All(x=>Wire.Bool(x,"ok"));Wire.Save(Path.Combine(root,"combat-checks.json"),Wire.Map("ok",okay,"game_input",false,"tests",results));Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Wire.Map("ok",okay,"tests",results)));if(!okay)Environment.ExitCode=1;
 }
}}
