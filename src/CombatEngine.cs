using System;using System.Collections.Generic;using System.Drawing;using System.Linq;
namespace SshScreen {
public sealed class ActionStep {public double at;public int key;public bool down;}
public sealed class CombatPlan {
 public string name,stopReason;public int continuityKey,secondaryContinuityKey;public List<ActionStep> steps=new List<ActionStep>();public double duration;
 public static CombatPlan Stop(string reason){return new CombatPlan{name="已暂停",stopReason=reason};}
 public static CombatPlan Tap(string name,int key,double hold=80){var p=new CombatPlan{name=name,duration=hold+20};if(key>0){p.steps.Add(new ActionStep{key=key,down=true});p.steps.Add(new ActionStep{key=key,down=false,at=hold});}return p;}
 public static CombatPlan Move(string name,int direction,double actionDuration=0){var p=Tap(name,direction,240+actionDuration);p.duration=actionDuration;p.continuityKey=direction;return p;}
 public static CombatPlan LadderApproach(int horizontal,int vertical){
  var plan=Move(vertical==38?"路线 · 走向梯绳并按上":"路线 · 走向下梯",vertical);
  plan.secondaryContinuityKey=horizontal;
  plan.steps.Add(new ActionStep{key=horizontal,down=true});
  plan.steps.Add(new ActionStep{key=horizontal,at=240});
  return plan;
 }
 public static CombatPlan JumpTowardsRope(int horizontal,int jump){
  var plan=LadderApproach(horizontal,38);
  plan.name="路线 · 提前跳向梯绳";
  plan.steps.Add(new ActionStep{at=20,key=jump,down=true});
  plan.steps.Add(new ActionStep{at=120,key=jump});
  plan.duration=135;
  return plan;
 }
 public static CombatPlan PlatformDrop(string name,int jump,double hold,double tail){
  var plan=Chord(name,40,jump,0,hold,tail);
  plan.continuityKey=40;
  return plan;
 }
 public static CombatPlan Chord(string name,int direction,int key,double lead,double hold,double tail){var p=new CombatPlan{name=name,duration=lead+hold+tail+20};if(direction>0)p.steps.Add(new ActionStep{key=direction,down=true});if(key>0){p.steps.Add(new ActionStep{at=lead,key=key,down=true});p.steps.Add(new ActionStep{at=lead+hold,key=key,down=false});}if(direction>0)p.steps.Add(new ActionStep{at=lead+hold+tail,key=direction,down=false});return p;}
}
public sealed class CombatPlanner {
 readonly RolePreset role;readonly Dictionary<string,double> due=new Dictionary<string,double>();readonly Dictionary<string,int> visits=new Dictionary<string,int>();readonly RouteDocument route;
 readonly bool routeOnly;
 readonly bool navigationCombat;
 readonly bool hasPaintedSegments;
 readonly HashSet<string> blockedTrialEdges=new HashSet<string>();
 PointF? trialProgressPoint;
 double trialProgressAt;
 double routeObservedAt=-1,edgeProgressAt,edgeBestGap=Double.PositiveInfinity;
 bool routeClockPaused,planningRoute;
 readonly Dictionary<string,double> blockedRouteDirections=new Dictionary<string,double>();
 readonly Dictionary<string,int> routeFailures=new Dictionary<string,int>();
 double routeNow,rejoinAfter;
 string routeRecovery,failedRouteEdge;
 bool recoveringPlatform,recoveryFalling;
 int recoveryDirection;
 PointF recoveryPosition;
 double recoveryProgressAt;
 double routeTurnStarted=-1,routeTurnRetryAfter;
 PointF? previousRoutePoint,walkIssuedFrom;
 long routePointSequence=-1;
 int stableFloorFrames;
 double routeVerticalDelta;
 bool edgeDescending;
 double? ropeExitX;
 double? upExitX;
 double ropeExitUntil;
 bool ropeExitJumped;
 RouteNode joinPlatform;
 double joinStarted;
 bool observedLift;
 double stepSettledAt,stepLaunchY;
 PointF? stepOrigin;
 double upApproachStarted=-1;
 double upLadderSeenAt=-1,upProbeAt=-1;
 bool upApproachConfirmed,aligningUpApproach;
 bool movingHorizontally;
 int patrolDirection=39,watchedDirection,blockedDirection;
 PointF motionOrigin,blockedPosition;
 double motionSince;
 double returnedForTarget=Double.NegativeInfinity;
 int upApproachAttempts,routeHorizontalKey,mountTurnPhase,mountAttempts,slowDescentChanges;
 double mountTurnPeak;
 string pendingStop;double lowHpSince=-1,lastPulse;PointF? turnDot;double busyUntil,lastSeen=-10000,lastSideChange=-10000,emptySince=-1,lastTargetAt,restAt,restUntil,turnAt;long sequence=-1;int side,turnSide;Point turnBody;Point? anchor;PointF? anchorDot;bool turnConfirmed;List<Rectangle> cached=new List<Rectangle>();RouteEdge edge;string routeNode;bool edgeFired;double edgeAt;double? edgeLastY;int arrivalFrames,ropeMountDirection;double ropeConfirmAfter;bool downApproachStarted;
 public string Status="等待定位";public int? AnchorX;public int LeftBoundary,RightBoundary;public bool HasAnchor{get{return anchor.HasValue;}}
 public string CurrentRouteEdge{get{return edge==null?null:edge.id;}}
 public CombatPlanner(RolePreset preset,double now,RouteDocument document=null,bool routeOnly=false){role=WorkspaceFiles.Copy(preset);route=document==null?null:RouteFiles.Clone(document);this.routeOnly=routeOnly;hasPaintedSegments=route!=null&&route.edges.Any(e=>e.edited);restAt=now+role.battle.restEveryMinutes*60000;lastPulse=now;}
 readonly string navigationGoal;
 readonly Dictionary<string,string> navigationSteps;
 internal CombatPlanner(RolePreset preset,double now,RouteDocument document,string start,string goal,List<RouteEdge> path,bool combatWhileNavigating=false):
     this(preset,now,document,!combatWhileNavigating){
  navigationCombat=combatWhileNavigating;
  routeNode=start;
  navigationGoal=goal;
  navigationSteps=new Dictionary<string,string>();
  string departure=start;
  foreach(var link in path){
   navigationSteps.Add(departure,link.id);
   departure=Destination(link,departure);
  }
 }
 bool Ready(string key,double now){double at;return !due.TryGetValue(key,out at)||now>=at;}
 CombatPlan Issue(CombatPlan plan,double now){
  routeClockPaused=!planningRoute;
  busyUntil=now+plan.duration;Status=plan.name;
  movingHorizontally=plan.continuityKey==37||plan.continuityKey==39;
  if(planningRoute&&edge!=null&&edge.kind=="walk"&&!walkIssuedFrom.HasValue&&
   plan.steps.Any(step=>step.down&&(step.key==37||step.key==39)))walkIssuedFrom=previousRoutePoint;
  if(role.battle.mode=="route")foreach(var step in plan.steps)
   if(step.at==0&&step.down&&(step.key==37||step.key==39))routeHorizontalKey=step.key;
  return plan;
 }
 CombatPlan Wait(string status){
  routeClockPaused=true;
  Status=status;
  if(!movingHorizontally)return null;
  movingHorizontally=false;
  return new CombatPlan{name=status};
 }
 bool AtAnchor(CombatObservation o){
  if(o.dot.HasValue&&anchorDot.HasValue)return Math.Abs(o.dot.Value.X-anchorDot.Value.X)<=1;
  return Math.Abs(o.body.X-AnchorX.Value)<=4;
 }
 CombatPlan Use(string action,double now,double seconds,string title,double hold=100){int key=role.Key(action);if(key==0||!Ready(action,now))return null;due[action]=now+seconds*1000;return Issue(CombatPlan.Tap(title,key,hold),now);}
 internal string ObservationIssue(CombatObservation o){
  if(o==null||o.age>CombatObservation.MaxAgeMs)return "识别结果超时";
  if(!o.fresh)return "人物定位丢失";
  var battle=role.battle;
  if(!o.dot.HasValue&&(battle.mode!="stationary"||battle.moveLeft>0||battle.moveRight>0))return "小地图黄点未匹配";
  return null;
 }
 public CombatPlan Tick(CombatObservation o){
  if(o!=null&&o.sequence!=sequence&&o.fresh&&o.age<=CombatObservation.MaxAgeMs){
   if(edge!=null&&routeClockPaused&&routeObservedAt>=0){
    double waiting=Math.Max(0,o.now-routeObservedAt);
    edgeAt+=waiting;edgeProgressAt+=waiting;
   }
   routeObservedAt=o.now;
  }
  if(navigationCombat&&!o.combatReady){
   if(!o.fresh||o.age>CombatObservation.MaxAgeMs||!o.dot.HasValue)return CombatPlan.Stop("小地图位置已丢失，导航已停止");
   if(o.sequence==sequence)return null;sequence=o.sequence;
   return o.now<busyUntil?null:Route(o);
  }
  if(routeOnly){
   if(!o.fresh||o.age>CombatObservation.MaxAgeMs||!o.dot.HasValue)return CombatPlan.Stop("小地图位置已丢失，试走已停止");
   if(o.sequence==sequence)return null;
   sequence=o.sequence;return o.now<busyUntil?null:Route(o);
  }
  var problem=ObservationIssue(o);
  if(problem!=null)return CombatPlan.Stop(problem);
  var c=role.battle;var a=c.Profile;if(o.sequence==sequence)return null;sequence=o.sequence;
  if(!anchor.HasValue){anchor=o.body;anchorDot=o.dot;lastTargetAt=o.now;edgeAt=o.now;}
  AnchorX=o.dot.HasValue&&anchorDot.HasValue?(int)Math.Round(o.body.X+(anchorDot.Value.X-o.dot.Value.X)*c.screenPerMini):anchor.Value.X;
  int rangeLeft=c.mode=="patrol"?(int)(c.patrolRadius*c.screenPerMini):c.moveLeft,rangeRight=c.mode=="patrol"?rangeLeft:c.moveRight;LeftBoundary=AnchorX.Value-rangeLeft;RightBoundary=AnchorX.Value+rangeRight;
  if(pendingStop!=null)return o.now>=busyUntil?CombatPlan.Stop(pendingStop):null;
  if(c.mode=="route")ObserveRoutePosition(o);
  bool groundThreat=c.mode=="route"&&ImmediateGroundThreat(o,c);
  if(c.mode=="route"&&!groundThreat&&(recoveringPlatform||Math.Abs(routeVerticalDelta)>.5||edge==null||RouteDrift(o)||IsRopeAction(edge.kind)||edge.kind=="walk_off"||edge.upContinuation||ropeExitX.HasValue||upExitX.HasValue||downApproachStarted||DescendingApproach(o)||UpcomingAscent(o)!=null))
   return !o.dot.HasValue?CombatPlan.Stop("小地图人物位置已丢失"):o.now<busyUntil?null:Route(o);
  if(o.hp.HasValue&&o.hp.Value<role.hp.threshold){if(lowHpSince<0)lowHpSince=o.now;}else lowHpSince=-1;
  if(c.returnHomeOnLowHp&&lowHpSince>=0&&o.now-lowHpSince>=c.lowHpTimeout*1000&&role.Key("home")!=0&&pendingStop==null){pendingStop="已发送回城，运行已暂停";return Issue(CombatPlan.Tap("低HP回城",role.Key("home"),80),o.now);}
  if(role.hp.enabled&&o.hp.HasValue&&o.hp.Value<role.hp.threshold){var p=Use("hp",o.now,role.hp.cooldown,"补HP",80);if(p!=null)return p;}
  if(role.mp.enabled&&o.mp.HasValue&&o.mp.Value<role.mp.threshold){var p=Use("mp",o.now,role.mp.cooldown,"补MP",80);if(p!=null)return p;}
  if(o.now<busyUntil)return null;
  if(c.mode=="patrol"){
   ObservePatrolMovement(o);
   var exit=TryPatrolRopeExit(o);
   if(exit!=null)return exit;
  }
  if(c.mode!="route"&&(rangeLeft>0||rangeRight>0)&&(o.body.X<LeftBoundary||o.body.X>RightBoundary)){
   int inward=o.body.X<LeftBoundary?39:37;
   if(c.mode!="patrol"||inward!=blockedDirection)return Walk("返回活动区",inward,o);
   return Movement(o,c);
  }
  if(c.restEveryMinutes>0&&c.restMinutes>0&&o.now>=restAt){if(restUntil==0)restUntil=o.now+c.restMinutes*60000;if(o.now<restUntil){if(!AtAnchor(o))return Issue(CombatPlan.Move("休息回锚",o.body.X<AnchorX.Value?39:37),o.now);return Wait("休息中");}restUntil=0;restAt=o.now+c.restEveryMinutes*60000;}
  if(c.mode=="stationary"&&c.antiStuck&&o.now-lastPulse>=c.antiStuckInterval*1000){lastPulse=o.now;int first=c.antiStuckVertical?38:o.body.X>=AnchorX.Value?37:39,second=c.antiStuckVertical?40:first==37?39:37;var pulse=CombatPlan.Tap("定时小幅位移",first,c.antiStuckPulseMs);pulse.steps.Add(new ActionStep{at=c.antiStuckPulseMs+250,key=second,down=true});pulse.steps.Add(new ActionStep{at=c.antiStuckPulseMs*2+250,key=second,down=false});pulse.duration=c.antiStuckPulseMs*2+500;return Issue(pulse,o.now);}
  if(c.mode=="route"&&!groundThreat&&edge!=null&&(edge.kind!="walk"||AtWalkExit(o)))return Route(o);
  foreach(var buff in role.buffs.Where(b=>b.enabled)){if(Ready("buff:"+buff.id,o.now)){due["buff:"+buff.id]=o.now+buff.interval*1000;var plan=CombatPlan.Tap("增益 · "+buff.name,buff.key,80);plan.duration=80+buff.delay*1000;return Issue(plan,o.now);}}
  Rectangle search=BattleGeometry.Search(o.body,c,AnchorX),left=BattleGeometry.Attack(o.body,c,true),right=BattleGeometry.Attack(o.body,c,false);
  var monsters=c.detectMonsters?o.monsters.Where(m=>BattleGeometry.Hit(search,m)).ToList():new List<Rectangle>();
  if(monsters.Count>0){cached=monsters;lastSeen=o.now;}else if(c.detectMonsters&&o.now-lastSeen<=c.lossToleranceMs)monsters=cached;
  if(!c.detectMonsters){if(c.mode=="patrol"){var attack=Use("attack",o.now,c.periodicAttack,"定时攻击");if(attack!=null)return attack;}return Movement(o,c);}
  var inLeft=monsters.Where(m=>BattleGeometry.Hit(left,m)).ToList();var inRight=monsters.Where(m=>BattleGeometry.Hit(right,m)).ToList();int count=monsters.Count(m=>BattleGeometry.Hit(left,m)||BattleGeometry.Hit(right,m));
  if(c.mode=="route"&&count==0)return Route(o);
  if(c.mode=="route"&&o.now<routeTurnRetryAfter)return Route(o);
  if(count>0){
   lastTargetAt=o.now;
   if(role.Key("attack")==0&&role.Key("aoe")==0)return Wait("攻击按键未启用");
   int desired=ChooseSide(o,inLeft,inRight,c);if(desired!=0&&desired!=side&&o.now-lastSideChange>=c.sideHoldMs){side=desired;lastSideChange=o.now;emptySince=-1;}
   if(side==0)side=desired;bool has=side<0?inLeft.Count>0:inRight.Count>0;
   if(!has){if(emptySince<0)emptySince=o.now;if(o.now-emptySince<c.clearHoldMs||o.now-lastSideChange<c.sideHoldMs)return Wait("等待切换攻击方向");side=desired;lastSideChange=o.now;emptySince=-1;}
   bool aoe=count>=a.aoeCount&&role.Key("aoe")!=0&&Ready("aoe",o.now);
   bool facing=(o.facingFresh&&o.facing==(side<0?"left":"right"));
   if(!c.inPlace&&!(aoe&&a.aoeNoTurn)&&!facing){
    if(c.mode=="route"){
     if(routeTurnStarted<0)routeTurnStarted=o.now;
    }
    if(turnSide!=side){turnSide=side;turnAt=o.now;turnBody=o.body;turnDot=o.dot;turnConfirmed=false;return Issue(CombatPlan.Tap("转向确认",side<0?37:39,Math.Max(1,a.turnHoldMs)),o.now);}
    turnConfirmed|=Math.Sign(o.body.X-turnBody.X)==side&&Math.Abs(o.body.X-turnBody.X)>=1;turnConfirmed|=o.dot.HasValue&&turnDot.HasValue&&Math.Sign(o.dot.Value.X-turnDot.Value.X)==side&&Math.Abs(o.dot.Value.X-turnDot.Value.X)>=.1;
    if(!turnConfirmed){
     if(c.mode=="route"&&o.now-routeTurnStarted>=1000){
      routeTurnRetryAfter=o.now+1500;routeTurnStarted=-1;turnSide=0;
      routeRecovery="转向暂未确认，先继续沿路线移动";
      return Route(o);
     }
     if(o.now-turnAt>=Math.Max(a.animationMs,a.turnHoldMs)){turnAt=o.now;turnBody=o.body;turnDot=o.dot;return Issue(CombatPlan.Tap("重试转向",side<0?37:39,Math.Max(1,a.turnHoldMs)),o.now);}Status="等待转向确认";return null;
    }
    if(o.now-turnAt<a.turnHoldMs+a.turnDelay*1000)return null;
   }else{turnSide=side;turnConfirmed=true;}
   routeTurnStarted=-1;
   if(aoe){var p=Use("aoe",o.now,a.aoeCooldown,"范围技能");if(p!=null){p.duration=Math.Max(p.duration,a.animationMs);return Issue(p,o.now);}}
   if(role.Key("attack")!=0&&Ready("attack",o.now)){due["attack"]=o.now+a.cooldown*1000;var plan=a.jumpAttack&&role.Key("jump")!=0?CombatPlan.Chord("跳跃攻击",role.Key("jump"),role.Key("attack"),50,100,0):CombatPlan.Tap("普通攻击",role.Key("attack"),100);plan.duration=Math.Max(plan.duration,a.animationMs);return Issue(plan,o.now);}
   return Wait("攻击冷却");
  }
  turnSide=0;turnConfirmed=false;routeTurnStarted=-1;
  if(c.mode=="stationary")return Wait("定点等待");
  foreach(var target in monsters.OrderBy(m=>Math.Abs(m.Left+m.Width/2-o.body.X))){
   int dx=target.Left+target.Width/2-o.body.X;
   if(dx==0)continue;
   int direction=dx<0?37:39;
   {
    Rectangle height=dx<0?left:right;
    if(height.IsEmpty||target.Top>=height.Bottom||target.Bottom<=height.Top||
       direction==blockedDirection||!Ready("chase:"+direction,o.now))continue;
    double margin=Math.Max(10,rangeLeft*c.softMargin);
    if(dx<0?o.body.X<=LeftBoundary+margin:o.body.X>=RightBoundary-margin)continue;
   }
   lastTargetAt=o.now;
   return Walk("追击",direction,o);
  }
  return Movement(o,c);
 }
 void ObservePatrolMovement(CombatObservation o){
  PointF point=o.dot.Value;
  if(blockedDirection!=0&&(Math.Abs(point.X-blockedPosition.X)>=2||Math.Abs(point.Y-blockedPosition.Y)>=2))blockedDirection=0;
  if(!movingHorizontally||watchedDirection==0){watchedDirection=0;return;}
  int progress=watchedDirection==37?-1:1;
  if(progress*(point.X-motionOrigin.X)>=1||Math.Abs(point.Y-motionOrigin.Y)>=2){
   motionOrigin=point;motionSince=o.now;
  }else if(o.now-motionSince>=950){
   blockedDirection=watchedDirection;blockedPosition=point;
   due["chase:"+blockedDirection]=o.now+2500;
   motionOrigin=point;motionSince=o.now;
  }
 }
 CombatPlan TryPatrolRopeExit(CombatObservation o){
  if(blockedDirection==0||route==null)return null;
  int jump=role.Key("jump");
  if(jump==0||jump>=37&&jump<=40)return null;
  var point=o.dot.Value;
  bool onShaft=route.edges.Where(e=>e.kind=="climb").Any(e=>{
   var a=Node(e.from);var b=Node(e.to);
   return point.Y>Math.Min(a.y,b.y)&&point.Y<=Math.Max(a.y,b.y)&&
    RouteGraph.StrokeDistance(route,e,point)<=1.25;
  });
  if(!onShaft)return null;
  int direction=blockedDirection==37?39:37;
  blockedDirection=0;patrolDirection=watchedDirection=direction;
  motionOrigin=point;motionSince=o.now;
  var exit=CombatPlan.Move("巡逻 · 离开梯绳",direction,260);
  exit.steps.Add(new ActionStep{key=jump,down=true});
  exit.steps.Add(new ActionStep{key=jump,at=90});
  return Issue(exit,o.now);
 }
 bool ImmediateGroundThreat(CombatObservation o,BattleSettings c){
  if(edge==null||!o.dot.HasValue||!c.detectMonsters||recoveringPlatform||stableFloorFrames<1||
   edgeFired||downApproachStarted||upApproachStarted>=0||ropeExitX.HasValue||upExitX.HasValue||edge.upContinuation||
   edge.kind!="walk"&&edge.kind!="rope_mount"&&edge.kind!="jump"&&edge.kind!="jump_down"&&edge.kind!="drop")return false;
  if(StandingWalk(o.dot.Value,null,1.25)==null||role.Key("attack")==0&&role.Key("aoe")==0)return false;
  var left=BattleGeometry.Attack(o.body,c,true);var right=BattleGeometry.Attack(o.body,c,false);
  var search=BattleGeometry.Search(o.body,c,AnchorX);
  return o.monsters.Any(m=>BattleGeometry.Hit(search,m)&&(BattleGeometry.Hit(left,m)||BattleGeometry.Hit(right,m)));
 }
 CombatPlan Walk(string title,int direction,CombatObservation o){
  if(role.battle.mode=="patrol"&&o.dot.HasValue&&(!movingHorizontally||watchedDirection!=direction)){
   watchedDirection=direction;motionOrigin=o.dot.Value;motionSince=o.now;
  }
  int teleport=role.Key("teleport");
  if(role.battle.teleportWhileWalking&&teleport!=0&&Ready("teleport",o.now)){
   due["teleport"]=o.now+role.battle.teleportCooldown*1000;
   var move=CombatPlan.Move(title+" · 传送",direction,250);
   move.steps.Add(new ActionStep{at=30,key=teleport,down=true});
   move.steps.Add(new ActionStep{at=150,key=teleport});
   return Issue(move,o.now);
  }
  return Issue(CombatPlan.Move(title,direction),o.now);
 }
 int ChooseSide(CombatObservation o,List<Rectangle> left,List<Rectangle> right,BattleSettings c){if(left.Count==0)return right.Count==0?0:1;if(right.Count==0)return -1;double l=left.Min(m=>Math.Abs(m.Left+m.Width/2-o.body.X)),r=right.Min(m=>Math.Abs(m.Left+m.Width/2-o.body.X));int preferred=side!=0?side:o.facing=="left"?-1:o.facing=="right"?1:0;if(preferred<0)l-=c.faceBonus;if(preferred>0)r-=c.faceBonus;return l<=r?-1:1;}
 CombatPlan Movement(CombatObservation o,BattleSettings c){
  if(c.mode=="route")return Route(o);
  if(c.mode!="patrol")return Wait("等待目标");
  if(c.returnToAnchor&&returnedForTarget!=lastTargetAt){
   if(AtAnchor(o)){
    returnedForTarget=lastTargetAt;
   }
   else if(o.now-lastTargetAt>=c.returnDelay*1000){
    int towardsAnchor=o.dot.Value.X<anchorDot.Value.X?39:37;
    if(towardsAnchor!=blockedDirection&&Math.Abs(o.dot.Value.Y-anchorDot.Value.Y)<=3)
     return Walk("返回锚点",towardsAnchor,o);
    returnedForTarget=lastTargetAt;
   }
  }
  double margin=Math.Max(10,c.patrolRadius*c.screenPerMini*c.softMargin);
  if(o.body.X>=RightBoundary-margin)patrolDirection=37;
  else if(o.body.X<=LeftBoundary+margin)patrolDirection=39;
  if(patrolDirection==blockedDirection)patrolDirection=patrolDirection==37?39:37;
  return Walk("平台巡逻",patrolDirection,o);
 }
 static bool IsRopeAction(string kind){
  return kind=="climb"||kind=="rope_mount"||kind=="rope_exit";
 }
 static string Destination(RouteEdge e,string from){return RouteGraph.Arrival(e,from);}
 RouteNode Node(string id){return route.nodes.First(n=>n.id==id);}
 RouteEdge NextEdge(string from,RouteEdge completed=null){
  if(navigationSteps!=null){
   string next;
   if(!navigationSteps.TryGetValue(from,out next)||blockedTrialEdges.Contains(next))return null;
   return route.edges.First(e=>e.id==next);
  }
  return route.edges.Where(e=>Destination(e,from)!=null)
   .Where(e=>!routeOnly||!visits.ContainsKey(e.id)&&!blockedTrialEdges.Contains(e.id))
   .Where(e=>routeOnly||RouteDirectionReady(e,from))
   .OrderBy(e=>(visits.ContainsKey(e.id)?visits[e.id]:0)+FailedAttempts(e,from)+(e==completed?1:0))
   .ThenBy(e=>e.to==from?1:0).FirstOrDefault();
 }
 int FailedAttempts(RouteEdge link,string from){
  int count;return routeFailures.TryGetValue(link.id+":"+from,out count)?count:0;
 }
 void FailDirection(double now){
  string key=edge.id+":"+routeNode;
  routeFailures[key]=FailedAttempts(edge,routeNode)+1;
  blockedRouteDirections[key]=now+10000;
 }
 bool RouteDirectionReady(RouteEdge link,string from){
  double until;
  return !blockedRouteDirections.TryGetValue(link.id+":"+from,out until)||routeNow>=until;
 }
 string RouteDeparture(RouteEdge link){
  return RouteGraph.Departures(link).Where(from=>routeOnly||RouteDirectionReady(link,from))
   .OrderBy(from=>FailedAttempts(link,from)).FirstOrDefault();
 }
 bool RouteDrift(CombatObservation o){
  return edge!=null&&(edge.kind=="walk"||edge.kind=="climb")&&o.dot.HasValue&&
   RouteGraph.StrokeDistance(route,edge,o.dot.Value)>3;
 }
 RouteEdge StandingWalk(PointF point,string connectedNode=null,double tolerance=2){
  return route.edges.Where(e=>e.kind=="walk"&&(connectedNode==null||e.from==connectedNode||e.to==connectedNode))
   .Where(e=>RouteGraph.StrokeDistance(route,e,point)<=tolerance).FirstOrDefault();
 }
 bool AdaptiveRoute{get{return !routeOnly||navigationGoal!=null;}}
 CombatPlan BeginPlatformRecovery(CombatObservation o){
  var point=o.dot.Value;
  var toward=edge!=null&&routeNode!=null?Node(Destination(edge,routeNode)):
   navigationGoal==null?null:Node(navigationGoal);
  recoveryDirection=toward!=null&&Math.Abs(toward.x-point.X)>=1?(toward.x<point.X?37:39):routeHorizontalKey;
  if(recoveryDirection!=37&&recoveryDirection!=39){
   var nearest=route.nodes.OrderBy(n=>RouteGraph.Distance(n.x,n.y,(int)point.X,(int)point.Y)).FirstOrDefault();
   recoveryDirection=nearest!=null&&nearest.x<point.X?37:39;
  }
  failedRouteEdge=CurrentRouteEdge;
  edge=null;routeNode=null;joinPlatform=null;trialProgressPoint=null;
  edgeFired=observedLift=false;edgeLastY=null;arrivalFrames=0;downApproachStarted=false;stepOrigin=null;
  ropeExitX=upExitX=null;upApproachStarted=upLadderSeenAt=upProbeAt=-1;
  upApproachConfirmed=aligningUpApproach=false;upApproachAttempts=0;
  recoveryPosition=point;recoveryProgressAt=o.now;
  recoveringPlatform=true;recoveryFalling=routeVerticalDelta>.5;
  routeRecovery="平台位置与已画路线不符，沿方向移动并按实际落点接路";
  return ContinuePlatformRecovery(o);
 }
 bool ReconnectAtPosition(CombatObservation o){
  if(navigationGoal==null){routeNode=null;return true;}
  // Replanning is transactional: a failed attempt must not split the live graph.
  var candidate=RouteFiles.Clone(route);
  var start=RouteGraph.NavigationPoint(candidate,Point.Round(o.dot.Value),false);
  if(start==null)return false;
  var goal=candidate.nodes.First(n=>n.id==navigationGoal);
  if(start.id==navigationGoal&&(Math.Abs(goal.x-o.dot.Value.X)>1||Math.Abs(goal.y-o.dot.Value.Y)>1))return false;
  var path=RouteGraph.Plan(candidate,start.id,navigationGoal);
  if(path==null)return false;
  route.nodes=candidate.nodes;route.edges=candidate.edges;
  routeNode=start.id;navigationSteps.Clear();string departure=start.id;
  foreach(var link in path){navigationSteps[departure]=link.id;departure=Destination(link,departure);}
  return true;
 }
 CombatPlan ContinuePlatformRecovery(CombatObservation o){
  var point=o.dot.Value;
  bool falling=routeVerticalDelta>.5;
  if(falling)recoveryFalling=true;
  if(stableFloorFrames>=2&&StandingWalk(point)!=null&&ReconnectAtPosition(o)){
   recoveringPlatform=recoveryFalling=false;
   routeRecovery="已按实际落点接回路线";
   return RouteCore(o);
  }
  if(Math.Abs(point.X-recoveryPosition.X)>=1||Math.Abs(point.Y-recoveryPosition.Y)>=1){
   recoveryPosition=point;recoveryProgressAt=o.now;
  }
  // Maintain the departure direction in flight. Only reverse after a stable,
  // blocked landing, never to chase the nominal X while falling.
  if(stableFloorFrames>=2&&o.now-recoveryProgressAt>=1200){
   recoveryDirection=recoveryDirection==37?39:37;
   recoveryProgressAt=o.now;recoveryPosition=point;recoveryFalling=false;
  }
  return Issue(CombatPlan.Move(recoveryFalling?"路线 · 自然下落后接路":"路线 · 调整平台偏差",recoveryDirection),o.now);
 }
 void RejoinAtLanding(CombatObservation o,bool completed){
  if(completed)visits[edge.id]=visits.ContainsKey(edge.id)?visits[edge.id]+1:1;
  else{failedRouteEdge=edge.id;routeRecovery="实际落点已变化，按当前平台接路";FailDirection(o.now);}
  edge=null;routeNode=null;joinPlatform=null;ropeExitX=upExitX=null;trialProgressPoint=null;
  if(navigationGoal!=null&&ReconnectAtPosition(o))routeRecovery="已按实际落点接回目标路线";
 }
 RouteEdge ReturnToUntriedBranch(string from){
  if(!routeOnly||!hasPaintedSegments||navigationGoal!=null)return null;
  var available=route.edges.Where(e=>!visits.ContainsKey(e.id)&&!blockedTrialEdges.Contains(e.id));
  var branches=new HashSet<string>(available.SelectMany(RouteGraph.Departures));
  if(branches.Count==0)return null;
  var walked=route.edges.Where(e=>e.both&&visits.ContainsKey(e.id)&&!blockedTrialEdges.Contains(e.id))
   .SelectMany(e=>RouteGraph.Departures(e).Select(id=>Tuple.Create(id,e))).ToLookup(x=>x.Item1,x=>x.Item2);
  var seen=new HashSet<string>{from};
  var queue=new Queue<Tuple<string,RouteEdge>>();
  queue.Enqueue(Tuple.Create(from,(RouteEdge)null));
  while(queue.Count>0){
   var path=queue.Dequeue();
   if(path.Item2!=null&&branches.Contains(path.Item1))return path.Item2;
   foreach(var link in walked[path.Item1]){
    string next=Destination(link,path.Item1);
    if(seen.Add(next))queue.Enqueue(Tuple.Create(next,path.Item2??link));
   }
  }
  return null;
 }
 bool DescendingApproach(CombatObservation o){
  if(edge==null||edge.kind!="walk"||!o.dot.HasValue)return false;
  var top=Node(Destination(edge,routeNode));
  var position=o.dot.Value;
  if(Math.Abs(top.y-position.Y)>4||Math.Abs(top.x-position.X)>16)return false;
  var next=NextEdge(top.id,edge);
  if(next==null||next.kind!="climb"||Node(Destination(next,top.id)).y<=top.y)return false;
  int ahead=Math.Max(6,Math.Min(16,next.downApproach));
  return Math.Abs(top.x-position.X)<=ahead;
 }
 static bool HasUpApproach(RouteEdge climb,string from){
  return climb!=null&&climb.kind=="climb"&&climb.from==from&&(climb.upApproachKey==37||climb.upApproachKey==39);
 }
 static bool InUpApproach(RouteEdge climb,RouteNode start,PointF point){
  double ahead=(start.x-point.X)*(climb.upApproachKey==39?1:-1);
  return Math.Abs(point.Y-start.y)<=1&&ahead>0&&ahead<=Math.Max(2,Math.Min(16,climb.upApproach+1.0));
 }
 static int StepTakeoffX(RouteEdge step,RouteNode start){
  if(step.kind!="jump_down")return start.x;
  return start.x+(step.jumpDirection==37?1:step.jumpDirection==39?-1:0);
 }
 RouteEdge UpcomingAscent(CombatObservation o){
  if(edge==null||edge.kind!="walk"||!o.dot.HasValue)return null;
  var entry=Node(Destination(edge,routeNode));
  var next=NextEdge(entry.id,edge);
  return HasUpApproach(next,entry.id)&&InUpApproach(next,entry,o.dot.Value)?next:null;
 }
 bool AtWalkExit(CombatObservation o){
  if(edge==null||edge.kind!="walk"||!o.dot.HasValue)return false;
  var n=Node(Destination(edge,routeNode));
  return Math.Abs(n.x-o.dot.Value.X)<=2&&Math.Abs(n.y-o.dot.Value.Y)<=3;
 }
 double? RopeTakeoff(RouteNode start,RouteNode rope,PointF position){
  if(Math.Abs(position.Y-start.y)>1)return null;
  double left=Math.Min(start.x,rope.x),right=Math.Max(start.x,rope.x);
  return Math.Max(left,Math.Min(right,position.X));
 }
 bool ReleaseDuringDescent(CombatObservation o,RouteNode origin,RouteNode bottom){
  var planned=NextEdge(bottom.id,edge);
  if(planned==null||planned.kind!="rope_exit"||!route.manualMap)return false;
  var position=o.dot.Value;
  if(Math.Abs(position.X-origin.x)>1.5||position.Y<=origin.y)return false;
  if(routeVerticalDelta>3){slowDescentChanges=0;return false;}
  if(routeVerticalDelta>=.5)slowDescentChanges++;
  if(slowDescentChanges<2)return false;
  var revised=RouteFiles.Clone(route);
  var start=RouteGraph.NavigationPoint(revised,Point.Round(position),false);
  if(start==null)return false;
  RouteGraph.AddRopeDescents(revised);
  var previousLanding=Node(planned.to);
  var release=revised.edges.Where(link=>link.from==start.id&&link.kind=="rope_exit"&&
    link.jumpDirection==planned.jumpDirection)
   .FirstOrDefault(link=>Math.Abs(revised.nodes.First(n=>n.id==link.to).y-previousLanding.y)<=1);
  if(release==null)return false;
  List<RouteEdge> tail=null;
  if(navigationGoal!=null){
   tail=RouteGraph.Plan(revised,release.to,navigationGoal);
   if(tail==null)return false;
  }
  route.nodes=revised.nodes;route.edges=revised.edges;
  if(tail!=null){
   navigationSteps.Clear();string departure=start.id;
   foreach(var link in new[]{release}.Concat(tail)){
    navigationSteps[departure]=link.id;departure=Destination(link,departure);
   }
  }
  routeNode=start.id;edge=release;edgeFired=edgeDescending=false;edgeLastY=null;
  edgeAt=edgeProgressAt=o.now;arrivalFrames=0;trialProgressPoint=null;
  routeRecovery="已确认沿绳缓慢下降，提前离绳到下层";
  return true;
 }
 CombatPlan PrepareRopeTurn(RouteNode start,RouteNode grip,PointF position,double now){
  if(mountTurnPhase==0||start.x==grip.x)return null;
  int toward=grip.x<start.x?37:39,sign=toward==39?1:-1;
  if(Math.Abs(position.Y-start.y)>1)return Issue(new CombatPlan{name="路线 · 等待起跳平台落稳"},now);
  if(mountTurnPhase==1){
   int beyond=start.x;
   int distance=Math.Min(3,Math.Max(2,Math.Abs(start.x-grip.x)));
   for(int offset=1;offset<=distance;offset++){
    int candidate=start.x-sign*offset;
    if(!route.edges.Any(link=>link.kind=="walk"&&RouteGraph.StrokeDistance(route,link,new PointF(candidate,start.y))<.5))break;
    beyond=candidate;
   }
   if((position.X-beyond)*sign>.25)
    return Issue(CombatPlan.Move("路线 · 越过起跳点转向",sign>0?37:39),now);
   mountTurnPeak=position.X;mountTurnPhase=2;
  }
  var takeoff=RopeTakeoff(start,grip,position);
  bool inside=takeoff.HasValue&&Math.Abs(position.X-takeoff.Value)<.5;
  bool reversed=(position.X-mountTurnPeak)*sign>=1;
  if(!inside||!reversed)return Issue(CombatPlan.Move("路线 · 回走建立起跳方向",toward),now);
  mountTurnPhase=0;
  return null;
 }
 CombatPlan BeginRopeMount(RouteEdge link,int jump,PointF position,RouteNode target,double now){
  double lead=link.upLead<0?0:Math.Min(300,link.upLead);
  double hold=link.jumpHold<0?80:Math.Min(650,Math.Max(20,link.jumpHold));
  int horizontal=RopeJumpDirection(target,position);
  double steer=link.steerHold<0?110:Math.Min(1000,Math.Max(20,link.steerHold));
  var plan=new CombatPlan{name="路线 · 跳跃抓绳",continuityKey=38};
  plan.steps.Add(new ActionStep{key=38,down=true});
  if(horizontal==37||horizontal==39){
   plan.steps.Add(new ActionStep{key=horizontal,at=lead,down=true});
   plan.steps.Add(new ActionStep{key=horizontal,at=lead+steer});
  }
  plan.steps.Add(new ActionStep{key=jump,at=lead,down=true});
  plan.steps.Add(new ActionStep{key=jump,at=lead+hold});
  plan.duration=lead+Math.Max(hold,horizontal!=0?steer:0);
  plan.steps.Add(new ActionStep{key=38,at=plan.duration+240});
  ropeConfirmAfter=now+lead+Math.Max(160,Math.Min(550,link.duration*.65));
  return plan;
 }
 static int RopeJumpDirection(RouteNode rope,PointF position){
  return Math.Abs(rope.x-position.X)<.5?0:position.X<rope.x?39:37;
 }
 CombatPlan BeginRopeExit(RouteEdge link,int jump){
  int direction=link.jumpDirection==37||link.jumpDirection==39?link.jumpDirection:0;
  double hold=link.jumpHold<0?80:Math.Min(650,Math.Max(20,link.jumpHold));
  double steer=link.steerHold<0?130:Math.Min(1000,Math.Max(20,link.steerHold));
  var plan=CombatPlan.Tap("路线 · 离绳跳跃",jump,hold);
  if(direction!=0){
   plan.steps.Insert(0,new ActionStep{key=direction,down=true});
   plan.steps.Add(new ActionStep{key=direction,at=steer});
   plan.duration=Math.Max(plan.duration,steer);
  }
  if(link.downLead>=0){
   double tail=link.downTail<0?0:Math.Min(400,link.downTail);
   plan.steps.Insert(0,new ActionStep{key=40,down=true});
   plan.steps.Add(new ActionStep{key=40,at=hold+tail});
   plan.duration=Math.Max(plan.duration,hold+tail);
   plan.continuityKey=40;
  }
  return plan;
 }
 public Dictionary<string,object> RouteState(CombatObservation o){
  string destination=edge==null?null:Destination(edge,routeNode);
  var goal=destination==null?null:Node(destination);
  return Wire.Map("edge",CurrentRouteEdge,"kind",edge==null?null:edge.kind,"from",routeNode,"to",destination,
   "position",o.dot.HasValue?(object)new[]{o.dot.Value.X,o.dot.Value.Y}:null,
   "goal",goal==null?null:new[]{goal.x,goal.y},"status",Status,
   "navigation_ms",edge==null?0:Math.Round(o.now-edgeAt),
   "without_progress_ms",edge==null?0:Math.Round(o.now-edgeProgressAt),
   "recovery_reason",routeRecovery,"failed_edge",failedRouteEdge);
 }
 void ObserveRoutePosition(CombatObservation o){
  if(o.dot.HasValue&&routePointSequence!=o.sequence){
   var point=o.dot.Value;
   routeVerticalDelta=previousRoutePoint.HasValue?point.Y-previousRoutePoint.Value.Y:0;
   stableFloorFrames=previousRoutePoint.HasValue&&Math.Abs(routeVerticalDelta)<.5?stableFloorFrames+1:0;
   previousRoutePoint=point;routePointSequence=o.sequence;
  }
 }
 CombatPlan Route(CombatObservation o){
  planningRoute=true;routeClockPaused=false;routeNow=o.now;
  ObserveRoutePosition(o);
  try{
   if(!routeOnly&&o.now<rejoinAfter)return Issue(new CombatPlan{name="路线 · 重新接路"},o.now);
   var plan=RouteCore(o);
   if(!routeOnly&&plan!=null&&plan.stopReason!=null&&RecoverableRouteIssue(plan.stopReason)){
    routeRecovery=plan.stopReason;failedRouteEdge=CurrentRouteEdge;
    if(edge!=null&&routeNode!=null)FailDirection(o.now);
    edge=null;routeNode=null;joinPlatform=null;trialProgressPoint=null;
    edgeFired=observedLift=false;edgeLastY=null;arrivalFrames=0;downApproachStarted=false;stepOrigin=null;
    ropeExitX=upExitX=null;upApproachStarted=upLadderSeenAt=upProbeAt=-1;
    upApproachConfirmed=aligningUpApproach=false;upApproachAttempts=0;
    rejoinAfter=o.now+300;
    return Issue(new CombatPlan{name="路线 · 重新接路"},o.now);
   }
   return plan;
  }finally{planningRoute=false;}
 }
 static bool RecoverableRouteIssue(string reason){
  return reason!="已到达导航目标"&&reason!="请选择已标定的小地图路线"&&reason!="请启用跳跃按键"&&
   reason!="梯绳动作的跳跃键不能与方向键相同";
 }
 CombatPlan RouteCore(CombatObservation o){
  if(route==null||route.edges.Count==0||!o.dot.HasValue)return CombatPlan.Stop("请选择已标定的小地图路线");
  if(recoveringPlatform)return ContinuePlatformRecovery(o);
  if(navigationGoal!=null&&edge==null&&routeNode==navigationGoal){
   var goal=Node(navigationGoal);
   if(Math.Abs(goal.x-o.dot.Value.X)<=1&&Math.Abs(goal.y-o.dot.Value.Y)<=1)return CombatPlan.Stop("已到达导航目标");
   return BeginPlatformRecovery(o);
  }
  PointF point=o.dot.Value;
  if(AdaptiveRoute&&edge!=null&&edge.kind=="walk"&&RouteDrift(o)&&
   !downApproachStarted&&upApproachStarted<0&&!edge.upContinuation&&!ropeExitX.HasValue&&!upExitX.HasValue&&
   (stableFloorFrames>=2||routeVerticalDelta>.5)&&StandingWalk(point)==null)
   return BeginPlatformRecovery(o);
  if(!routeOnly&&edge!=null&&RouteDrift(o)&&stableFloorFrames>=2&&StandingWalk(point)!=null){
   RejoinAtLanding(o,false);
  }
  if(joinPlatform!=null){
   if(Math.Abs(joinPlatform.y-point.Y)>2||o.now-joinStarted>6000)
    return AdaptiveRoute?BeginPlatformRecovery(o):CombatPlan.Stop("无法接入同层路线，试走已停止");
   double gap=joinPlatform.x-point.X;
   if(Math.Abs(gap)>2)return Issue(CombatPlan.Move("路线 · 接近同层路线",gap<0?37:39),o.now);
   routeNode=joinPlatform.id;joinPlatform=null;
  }
  if(routeNode==null){
    var closest=route.nodes.Where(n=>NextEdge(n.id)!=null).OrderBy(n=>RouteGraph.Distance(n.x,n.y,(int)point.X,(int)point.Y)).FirstOrDefault();
    double closestGap=closest==null?Double.PositiveInfinity:RouteGraph.Distance(closest.x,closest.y,(int)point.X,(int)point.Y);
    if(closest!=null&&(closestGap<=2||closestGap<=4&&Math.Abs(closest.y-point.Y)<=1))routeNode=closest.id;
    else{
     edge=route.edges.Where(e=>(e.kind=="walk"||e.kind=="climb")&&RouteDeparture(e)!=null)
       .Select(e=>new{Edge=e,Gap=RouteGraph.StrokeDistance(route,e,point)})
       .Where(candidate=>candidate.Gap<=2).OrderBy(candidate=>candidate.Gap).Select(candidate=>candidate.Edge).FirstOrDefault();
     if(edge==null){
      var ropeEntry=route.nodes.Where(n=>Math.Abs(n.y-point.Y)<=1&&Math.Abs(n.x-point.X)<=16&&
       route.edges.Any(e=>(e.kind=="rope_mount"||e.kind=="climb")&&Destination(e,n.id)!=null&&RouteDirectionReady(e,n.id)))
       .OrderBy(n=>Math.Abs(n.x-point.X)).FirstOrDefault();
      if(!routeOnly&&ropeEntry!=null){routeNode=ropeEntry.id;return RouteCore(o);}
      joinPlatform=route.nodes.Where(n=>Math.Abs(n.y-point.Y)<=1&&Math.Abs(n.x-point.X)<=20&&NextEdge(n.id)!=null&&
       route.edges.Any(e=>e.kind=="walk"&&(e.from==n.id||e.to==n.id)))
       .OrderBy(n=>Math.Abs(n.x-point.X)).FirstOrDefault();
      if(joinPlatform==null)return AdaptiveRoute?BeginPlatformRecovery(o):CombatPlan.Stop("请先把角色移到选中的路线附近");
      joinStarted=o.now;
      return Issue(CombatPlan.Move("路线 · 接近同层路线",point.X>joinPlatform.x?37:39),o.now);
     }
     routeNode=RouteDeparture(edge);
     if(routeNode==null)return CombatPlan.Stop("当前梯绳没有可用的入口");
     edgeAt=edgeProgressAt=o.now;edgeBestGap=Double.PositiveInfinity;
    }
  }
  bool joinedWalk=false,leavingDownClimb=false,leavingUpClimb=false;
  RouteEdge upcomingAscent=null;
  for(int advance=0;advance<16;advance++){
   if(edge==null){
    edge=upcomingAscent??NextEdge(routeNode)??ReturnToUntriedBranch(routeNode);
    upcomingAscent=null;
    if(edge==null&&navigationGoal!=null)return CombatPlan.Stop(routeNode==navigationGoal?"已到达导航目标":"导航连接不可用");
    if(edge==null)return CombatPlan.Stop(!routeOnly?"路线已到终点":blockedTrialEdges.Count>0?"本次试走结束，有路段未走通":"本次路线试走已到终点");
    edgeAt=edgeProgressAt=o.now;edgeBestGap=Double.PositiveInfinity;edgeFired=observedLift=edgeDescending=false;edgeLastY=null;arrivalFrames=0;downApproachStarted=false;stepOrigin=null;
    ropeExitX=upExitX=null;
    if(edge.kind=="walk"&&!edge.upContinuation&&leavingDownClimb){
     ropeExitX=point.X;ropeExitUntil=o.now+1180;ropeExitJumped=false;
    }
    if(edge.kind=="walk"&&!edge.upContinuation&&leavingUpClimb)upExitX=point.X;
    upApproachStarted=upLadderSeenAt=upProbeAt=-1;
    upApproachConfirmed=aligningUpApproach=false;upApproachAttempts=0;
    mountAttempts=mountTurnPhase=slowDescentChanges=0;walkIssuedFrom=null;
    if(edge.kind=="rope_mount"&&edge.edited){
     var origin=Node(routeNode);var grip=Node(Destination(edge,routeNode));
     int toward=grip.x<origin.x?37:grip.x>origin.x?39:0;
     if(toward!=0&&routeHorizontalKey!=0&&routeHorizontalKey!=toward)mountTurnPhase=1;
    }
   }
   string target=Destination(edge,routeNode);
   var node=Node(target);
   double dx=node.x-point.X,dy=node.y-point.Y;
   if(edgeFired&&routeVerticalDelta>.5)edgeDescending=true;
   if(edge.kind=="walk"||edge.kind=="climb"){
    double gap=Math.Sqrt(dx*dx+dy*dy);
    if(gap<=edgeBestGap-0.75){edgeBestGap=gap;edgeProgressAt=o.now;}
   }
   bool mount=edge.kind=="rope_mount";
   bool stepDown=edge.kind=="walk_off";
   bool paintedMount=mount&&edge.edited&&edge.steerHold<0;
   bool jumpAction=edge.kind=="jump"||edge.kind=="jump_down"||edge.kind=="drop"||edge.kind=="rope_exit";
   upcomingAscent=UpcomingAscent(o);
   bool descentApproach=edge.kind=="walk"&&(downApproachStarted||DescendingApproach(o));
   var climbStart=edge.kind=="climb"?Node(routeNode):null;
   bool measuredAscent=HasUpApproach(edge,routeNode);
   bool continueUp=edge.upContinuation&&edge.from==routeNode;
   if(measuredAscent&&!upApproachConfirmed){
    int direction=edge.upApproachKey;
    double ahead=(climbStart.x-point.X)*(direction==39?1:-1);
    if(o.now-edgeAt>6500)return CombatPlan.Stop("未确认抓住梯绳，请检查起爬点");
    double rise=climbStart.y-point.Y;
    if(Math.Abs(ahead)<=4&&rise>=2)upApproachConfirmed=true;
    else{
     if(rise==0)upProbeAt=-1;
     if(Math.Abs(ahead)<=4&&rise>=1){
      if(upProbeAt<0)upProbeAt=o.now;
      if(o.now-upProbeAt<=650)return Issue(CombatPlan.Move("路线 · 确认上爬",38),o.now);
     }
     if(upApproachStarted>=0&&Math.Abs(ahead)<=1&&upLadderSeenAt<0)upLadderSeenAt=o.now;
     double maxAtLadder=Math.Max(850,Math.Min(1300,edge.upApproachOverlap+750.0));
     bool approachTimedOut=upApproachStarted>=0&&(ahead< -4||o.now-upApproachStarted>2200||
      upLadderSeenAt>=0&&o.now-upLadderSeenAt>maxAtLadder);
     if(approachTimedOut||upProbeAt>=0&&o.now-upProbeAt>650){
      if(upApproachAttempts>=3)return CombatPlan.Stop("未确认抓住梯绳，请检查起爬点");
      aligningUpApproach=true;upApproachStarted=upLadderSeenAt=-1;
     }
     if(upApproachStarted<0){
      if(ahead<=0)aligningUpApproach=true;
      if(aligningUpApproach&&ahead<Math.Max(2,edge.upApproach))
       return Issue(CombatPlan.Move("路线 · 回到起爬点",direction==39?37:39),o.now);
      if(InUpApproach(edge,climbStart,point)){
       aligningUpApproach=false;upApproachStarted=o.now;upApproachAttempts++;
      }
     }
     if(upApproachStarted>=0)return Issue(CombatPlan.LadderApproach(direction,38),o.now);
    }
   }
   bool near=Math.Abs(dx)<=(descentApproach?1:2)&&Math.Abs(dy)<=3;
   bool arrived=upcomingAscent!=null||near||descentApproach&&Math.Abs(dx)<=2&&point.Y>node.y&&point.Y<=node.y+12;
   if(edge.kind=="walk"){
    var origin=Node(routeNode);
    bool tiny=Math.Abs(node.x-origin.x)<=4&&Math.Abs(node.y-origin.y)<=1&&!edge.upContinuation;
    if(tiny&&Math.Abs(dx)>=1&&(!walkIssuedFrom.HasValue||Math.Abs(point.X-walkIssuedFrom.Value.X)<1))arrived=false;
    var next=NextEdge(target,edge);
    if(next!=null&&next.observedJump)arrived=upcomingAscent!=null||
     Math.Abs(StepTakeoffX(next,node)-point.X)<.5&&Math.Abs(dy)<=1;
    if(next!=null&&next.kind=="drop")arrived=Math.Abs(dx)<=3&&Math.Abs(dy)<=1;
    else if(next!=null&&route.manualMap&&(next.kind=="jump"||next.kind=="jump_down"))
     arrived=Math.Abs(dx)<.5&&Math.Abs(dy)<=1;
    if(route.manualMap&&next!=null&&next.kind=="rope_mount"){
     var grip=Node(Destination(next,node.id));
     var takeoff=RopeTakeoff(node,grip,point);
     arrived=takeoff.HasValue&&Math.Abs(point.X-takeoff.Value)<.5&&Math.Abs(dy)<=1;
     // Preserve the drawn side when approaching: a coarse walk tolerance must not launch on the shaft.
     if(node.x!=grip.x&&Math.Abs(point.X-grip.x)<.5)arrived=false;
    }
    if(ropeExitX.HasValue&&Math.Abs(point.X-ropeExitX.Value)<1)arrived=false;
    if(upExitX.HasValue&&Math.Abs(point.X-upExitX.Value)<1)arrived=false;
   }
   if(climbStart!=null){
    int vertical=Math.Sign(node.y-climbStart.y);
    if(vertical>0&&ReleaseDuringDescent(o,climbStart,node))return RouteCore(o);
    bool reachedHeight=vertical!=0&&vertical*(point.Y-node.y)>=0;
    bool linkedAscent=vertical<0&&route.edges.Any(ledge=>ledge.upContinuation&&ledge.to==routeNode);
    bool onExit=!routeOnly&&stableFloorFrames>=2&&Math.Abs(dy)<=2&&StandingWalk(point,target)!=null&&
     vertical*(point.Y-climbStart.y)>=2;
    arrived=Math.Abs(dx)<=(measuredAscent||linkedAscent?4:2)&&reachedHeight||onExit;
   }
   if(continueUp)arrived=Math.Abs(dx)<=1&&Math.Abs(dy)<=3;
   if(stepDown){
    var ledge=Node(routeNode);
    bool onLowerPlatform=point.Y>ledge.y+1&&StandingWalk(point,null,1)!=null;
    arrivalFrames=onLowerPlatform&&edgeLastY.HasValue&&Math.Abs(point.Y-edgeLastY.Value)<.5?arrivalFrames+1:0;
    edgeLastY=onLowerPlatform?(double?)point.Y:null;
    bool landed=arrivalFrames>=1;
    if(landed&&(navigationGoal!=null||Math.Abs(dy)>1)){
     RejoinAtLanding(o,Math.Abs(dy)<=1);
     return RouteCore(o);
    }
    arrived=landed;
   }else if(mount){
    bool onAxis=Math.Abs(dx)<=2&&point.Y<=node.y+3;
    bool checking=edgeFired&&o.now>=ropeConfirmAfter&&onAxis;
    if(paintedMount){
     if(!checking)edgeLastY=null;
     else if(!edgeLastY.HasValue||point.Y>edgeLastY.Value+1)edgeLastY=point.Y;
     arrived=edgeLastY.HasValue&&edgeLastY.Value-point.Y>=2;
    }else{
     arrivalFrames=checking&&edgeLastY.HasValue&&edgeLastY.Value-point.Y>=1&&edgeLastY.Value-point.Y<=5?arrivalFrames+1:0;
     edgeLastY=point.Y;
     arrived=arrivalFrames>=2;
    }
   }else if(edge.observedJump){
    var launch=Node(routeNode);
    observedLift|=edgeFired&&point.Y<=stepLaunchY-2;
    double overshot=edge.jumpDirection==37?dx:-dx;
    bool sameLanding=Math.Abs(dx)<=1||edge.kind=="jump_down"&&overshot>=0&&overshot<=2;
    bool aligned=sameLanding&&Math.Abs(dy)<=1;
    arrivalFrames=edgeFired&&observedLift&&aligned&&edgeLastY.HasValue&&Math.Abs(point.Y-edgeLastY.Value)<.5?arrivalFrames+1:0;
    edgeLastY=point.Y;
    arrived=arrivalFrames>=3&&o.now-edgeAt>=Math.Max(300,edge.duration*.65);
   }else if(jumpAction){
    bool onLanding=Math.Abs(dy)<=1&&(edge.kind=="drop"?StandingWalk(point,target,1)!=null||Math.Abs(dx)<=2:Math.Abs(dx)<=2);
    arrivalFrames=onLanding&&edgeLastY.HasValue&&Math.Abs(point.Y-edgeLastY.Value)<.5?arrivalFrames+1:0;
    edgeLastY=point.Y;
    arrived=edgeFired&&o.now-edgeAt>=300&&arrivalFrames>=2;
    if((!routeOnly||navigationGoal!=null)&&edgeFired&&stableFloorFrames>=2&&o.now-edgeAt>=300&&StandingWalk(point)!=null&&
       (edgeDescending||edge.kind=="drop"&&o.now-edgeAt>=650)){
     bool completed=Math.Abs(dy)<=2;
     if(!arrived||Math.Abs(dx)>(navigationGoal==target?1:2)){RejoinAtLanding(o,completed);return RouteCore(o);}
    }
   }
   if(navigationGoal==target)arrived=arrived&&Math.Abs(dx)<=1&&Math.Abs(dy)<=1;
   if(arrived){
    visits[edge.id]=visits.ContainsKey(edge.id)?visits[edge.id]+1:1;
    bool climbedDown=edge.kind=="climb"&&node.y>Node(routeNode).y;
    leavingUpClimb=edge.kind=="climb"&&node.y<Node(routeNode).y;
    routeNode=target;joinedWalk=edge.kind=="walk";leavingDownClimb=climbedDown&&Math.Abs(dx)<=1.25;edge=null;trialProgressPoint=null;continue;
   }
   if(routeOnly&&(edge.kind=="walk"||edge.kind=="climb")){
    if(!trialProgressPoint.HasValue||Math.Abs(point.X-trialProgressPoint.Value.X)>=1||Math.Abs(point.Y-trialProgressPoint.Value.Y)>=1){
     trialProgressPoint=point;trialProgressAt=o.now;
    }else if(o.now-trialProgressAt>=1250){
     string action=RouteGraph.KindName(edge.kind);
     var start=Node(routeNode);
     blockedTrialEdges.Add(edge.id);
     if(RouteGraph.Distance(start.x,start.y,(int)point.X,(int)point.Y)<=4&&NextEdge(routeNode)!=null){
      edge=null;trialProgressPoint=null;continue;
     }
     return CombatPlan.Stop("角色位置未变化，卡在"+action+"，试走已停止");
    }
   }
   double timeout=paintedMount?3000:mount?Math.Max(1600,Math.Min(5000,edge.duration*2.0+800)):Math.Max(4000,edge.duration*2.0+1000);
   double lastProgress=edge.kind=="walk"||edge.kind=="climb"?edgeProgressAt:edgeAt;
   if(o.now-lastProgress>timeout)return CombatPlan.Stop(mount?"未确认抓住梯绳，已暂停":"路线段 "+edge.id+"（"+RouteGraph.KindName(edge.kind)+"）持续无进展，已暂停");
   if(edge.kind=="walk"){
    if(continueUp)return Issue(CombatPlan.Move("路线 · 继续上爬",38),o.now);
    var next=NextEdge(target,edge);
    if(next!=null&&next.kind=="drop"&&Math.Abs(dx)<.5&&Math.Abs(dy)>1)
     return CombatPlan.Stop("未到下落起点的高度");
    double walkGap=next!=null&&next.observedJump?StepTakeoffX(next,node)-point.X:dx;
    int direction=walkGap<0?37:39;
    if(upExitX.HasValue){
     if(Math.Abs(point.X-upExitX.Value)>=1)upExitX=null;
     else{
      var across=CombatPlan.LadderApproach(direction,38);
      across.name="路线 · 上爬接平台";
      return Issue(across,o.now);
     }
    }
    if(ropeExitX.HasValue){
     if(Math.Abs(point.X-ropeExitX.Value)>=1)ropeExitX=null;
     else{
      if(o.now>ropeExitUntil)return CombatPlan.Stop("未从梯绳出口踏上平台");
      int exitJump=role.Key("jump");
      if(!ropeExitJumped&&exitJump!=0&&exitJump!=direction){
       ropeExitJumped=true;
       var exit=CombatPlan.Move("路线 · 离绳跳跃",direction,100);
       exit.steps.Add(new ActionStep{key=exitJump,down=true});
       exit.steps.Add(new ActionStep{key=exitJump,at=80});
       return Issue(exit,o.now);
      }
      return Issue(CombatPlan.Move("路线 · 离开梯绳",direction),o.now);
     }
    }
    if(descentApproach){downApproachStarted=true;return Issue(CombatPlan.LadderApproach(direction,40),o.now);}
    return routeOnly?Issue(CombatPlan.Move("路线 · 行走",direction),o.now):Walk("路线 · 行走",direction,o);
   }
   if(edge.kind=="climb"){
    int horizontal=dx<0?37:39;
    if(measuredAscent&&!upApproachConfirmed)
     return Issue(CombatPlan.Move("路线 · 接近起爬点",edge.upApproachKey),o.now);
    int vertical=dy<0?38:40;
    if(Math.Abs(dx)>.5){
     var align=CombatPlan.LadderApproach(horizontal,vertical);
     align.name="路线 · 对齐梯绳并"+(vertical==38?"上爬":"下爬");
     return Issue(align,o.now);
    }
    return Issue(CombatPlan.Move("路线 · 梯绳",vertical),o.now);
   }
   if(stepDown){
    var ledge=Node(routeNode);
    // Keep the departure direction through the fall; correct the path only after landing.
    int direction=node.x<ledge.x?37:39;
    return Issue(CombatPlan.Move("路线 · 走下台阶",direction),o.now);
   }
   int jump=role.Key("jump");
   if(jump==0)return CombatPlan.Stop("请启用跳跃按键");
   if(edge.observedJump){
    var launch=Node(routeNode);
    if(!edgeFired){
     if(Math.Abs(point.Y-launch.y)>1)return CombatPlan.Stop("未到台阶起跳高度");
     bool stationary=edge.jumpDirection==0;
     int takeoffX=StepTakeoffX(edge,launch);
     if(Math.Abs(point.X-takeoffX)>(stationary?1:0)){
      stepOrigin=null;
      if(o.now-edgeAt>3500)return CombatPlan.Stop("未对齐台阶起跳点");
      return Issue(CombatPlan.Move("路线 · 对齐台阶",point.X<takeoffX?39:37),o.now);
     }
     if(stationary){
      if(!stepOrigin.HasValue||stepOrigin.Value.X!=point.X){stepOrigin=point;stepSettledAt=o.now;}
      if(o.now-edgeAt>3500)return CombatPlan.Stop("台阶起跳点未落稳");
      if(o.now-stepSettledAt<420)return Issue(new CombatPlan{name="路线 · 等待落稳后起跳"},o.now);
     }
     edgeAt=o.now;edgeFired=true;stepLaunchY=point.Y;
     var hop=CombatPlan.Chord("路线 · 台阶跳跃",edge.jumpDirection,jump,0,edge.jumpHold,100);
     hop.continuityKey=edge.jumpDirection;hop.duration=edge.jumpHold+20;
     return Issue(hop,o.now);
    }
    if(!observedLift&&o.now-edgeAt>(edge.jumpDirection==0?520:390))
     return CombatPlan.Stop("未检测到台阶起跳，请检查跳跃键");
    if(point.Y>Math.Max(launch.y,node.y)+2||o.now-edgeAt>1650)return CombatPlan.Stop("未到达台阶落点");
    if(edge.jumpDirection>0&&Math.Abs(dx)>=.5&&(dx>0)==(edge.jumpDirection==39))
     return Issue(CombatPlan.Move("路线 · 对准台阶落点",edge.jumpDirection),o.now);
    return Issue(new CombatPlan{name="路线 · 等待落到台阶"},o.now);
   }
   if(paintedMount&&edgeFired&&mountAttempts<3&&o.now-edgeAt>=750&&stableFloorFrames>=2&&
      Math.Abs(point.Y-Node(routeNode).y)<=1&&StandingWalk(point)!=null){
    edgeFired=false;edgeAt=edgeProgressAt=o.now;edgeLastY=null;arrivalFrames=0;
    mountTurnPhase=Node(routeNode).x==node.x?0:1;
    routeRecovery="抓绳未接上，落回平台后重新调整起跳";
    return Issue(new CombatPlan{name="路线 · 调整起跳后重试抓绳"},o.now);
   }
   if((mount||edge.kind=="rope_exit")&&jump>=37&&jump<=40)
    return CombatPlan.Stop("梯绳动作的跳跃键不能与方向键相同");
   if(!edgeFired){
    if(edge.kind=="drop"||route.manualMap&&(edge.kind=="jump"||edge.kind=="jump_down")){
     var start=Node(routeNode);
     string origin=edge.kind=="drop"?"下落起点":"起跳点";
     if(Math.Abs(point.Y-start.y)>1)return CombatPlan.Stop("未到"+origin+"的高度");
     if(Math.Abs(point.X-start.x)>(edge.kind=="drop"?3:0))
      return Issue(CombatPlan.Move("路线 · 对齐"+origin,point.X<start.x?39:37),o.now);
    }
    if(paintedMount){
     var turn=PrepareRopeTurn(Node(routeNode),node,point,o.now);
     if(turn!=null)return turn;
    }
    if(mount||edge.kind=="rope_exit"){
     var start=Node(routeNode);
     if(Math.Abs(point.Y-start.y)>3)return CombatPlan.Stop("未到录制的梯绳起跳高度，已暂停");
     double? takeoff=mount?RopeTakeoff(start,node,point):start.x;
     if(!takeoff.HasValue)return CombatPlan.Stop("绳口附近缺少能站稳的起跳位置");
     if(Math.Abs(point.X-takeoff.Value)>(mount?0:2)){
      if(!mount)return CombatPlan.Stop("未对齐录制的离绳位置，已暂停");
      return Issue(CombatPlan.Move("路线 · 对齐起跳点",point.X<takeoff.Value?39:37),o.now);
     }
    }
    edgeFired=true;
    if(paintedMount){
     edgeAt=o.now;mountAttempts++;ropeConfirmAfter=o.now+500;
     ropeMountDirection=RopeJumpDirection(node,point);
     var entry=ropeMountDirection!=0?CombatPlan.JumpTowardsRope(ropeMountDirection,jump):
      CombatPlan.Chord("路线 · 跳跃抓绳",38,jump,20,100,240);
     entry.name="路线 · 跳跃抓绳";entry.continuityKey=38;entry.duration=135;
     return Issue(entry,o.now);
    }
    if(mount)return Issue(BeginRopeMount(edge,jump,point,node,o.now),o.now);
    if(edge.kind=="rope_exit")return Issue(BeginRopeExit(edge,jump),o.now);
    if(edge.kind=="drop")return Issue(CombatPlan.PlatformDrop("路线 · 下落",jump,
     edge.jumpHold<0?80:edge.jumpHold,edge.downTail<0?150:edge.downTail),o.now);
    int direction=Math.Abs(dx)>1?(dx<0?37:39):0;
    var plan=CombatPlan.Chord("路线 · 跳跃",direction,jump,joinedWalk?0:30,edge.jumpHold<0?80:edge.jumpHold,150);
    plan.continuityKey=direction;
    return Issue(plan,o.now);
   }
   if(paintedMount&&ropeMountDirection!=0&&RopeJumpDirection(node,point)==ropeMountDirection)
    return Issue(CombatPlan.LadderApproach(ropeMountDirection,38),o.now);
   if(mount)return Issue(CombatPlan.Move("路线 · 等待抓绳",38),o.now);
   Status="等待落点";return null;
  }
  Status="衔接相邻路段";return null;
 }

}
public sealed class ActionScheduler {
 readonly List<ActionStep> pending=new List<ActionStep>();
 readonly HashSet<int> held=new HashSet<int>(),continuing=new HashSet<int>();
 double started;
 public bool Busy{get{return pending.Count>0;}}
 public bool HasKeys{get{return held.Count>0;}}
 public Action<int,bool> Send;
 public int Epoch{get;private set;}
 public void Begin(CombatPlan plan,double now){
  var next=new HashSet<int>();
  foreach(int key in new[]{plan.continuityKey,plan.secondaryContinuityKey}){
   if(key>=37&&key<=40&&plan.steps.Any(s=>s.at==0&&s.down&&s.key==key))next.Add(key);
  }
  Epoch++;
  pending.Clear();
  foreach(int key in held.ToArray()){
   if(continuing.Contains(key)&&next.Contains(key))continue;
   if(Send!=null)Send(key,false);
   held.Remove(key);
  }
  continuing.Clear();
  continuing.UnionWith(next);
  started=now;
  pending.AddRange(plan.steps.OrderBy(s=>s.at));
 }

 public void Tick(double now){while(pending.Count>0&&now-started>=pending[0].at){var s=pending[0];pending.RemoveAt(0);if(s.down){if(held.Add(s.key)&&Send!=null)Send(s.key,true);}else if(held.Remove(s.key)&&Send!=null)Send(s.key,false);}}
 public void ReleaseOnly(double now){pending.RemoveAll(step=>step.down);Tick(now);}
 public void Cancel(){Epoch++;continuing.Clear();pending.Clear();foreach(int key in held.ToArray())if(Send!=null)Send(key,false);held.Clear();}
}
}
