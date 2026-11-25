using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Web.Script.Serialization;
namespace SshScreen {
public sealed class KeyBinding {public string action,label;public int key;public bool enabled=true;}
public sealed class BuffBinding {public string id=Guid.NewGuid().ToString("N"),name="增益";public int key=35;public bool enabled;public double interval=1200,delay=1;}
public sealed class MeterArea {public bool enabled;public int x,y,width,height;public int sourceWidth,sourceHeight;public double threshold=40,cooldown=.5;public bool Valid(int w,int h){return sourceWidth==w&&sourceHeight==h&&width>=8&&height>=1&&new Rectangle(0,0,w,h).Contains(new Rectangle(x,y,width,height));}}
public sealed class AttackProfile {public int rangeX=240,rangeY=125,offsetY=50,aoeCount=2;public double cooldown=.3,aoeCooldown=.3,turnDelay=.03;public int animationMs=100,turnHoldMs=80;public bool jumpAttack,aoeNoTurn=true;}
public sealed class SideArea {public bool enabled=true;public int reach=240,halfHeight=125,offsetY=50;}
public sealed class BattleSettings {
 public string mode="stationary",style="ranged";public AttackProfile melee=new AttackProfile{rangeX=120,rangeY=110,offsetY=0,animationMs=350,aoeNoTurn=false},ranged=new AttackProfile();
 public SideArea left=new SideArea(),right=new SideArea();public int searchX=300,searchY=150,searchOffsetX,searchOffsetY=50,attackExtension=150;
 public int moveLeft=30,moveRight=30;public double softMargin=.25,patrolRadius=120,screenPerMini=14.5,returnDelay=5,restEveryMinutes,restMinutes;
 public bool detectMonsters=true,inPlace,returnToAnchor=true,teleportWhileWalking,returnHomeOnLowHp,antiStuck,antiStuckVertical;public double teleportCooldown=1,lowHpTimeout=10,antiStuckInterval=20;public int antiStuckPulseMs=80;public double periodicAttack=2.5;
 public int lossToleranceMs=180,sideHoldMs=300,clearHoldMs=200,faceBonus=150,deadzone=20,chaseHoldMs=400,chaseMargin=100;public double combatClear=.3;
 public AttackProfile Profile{get{return style=="melee"?melee:ranged;}}
}
public sealed class RolePreset {
 public string id=Guid.NewGuid().ToString("N"),name="当前角色";public PlayerProfile vision=new PlayerProfile();public BattleSettings battle=new BattleSettings{mode="route"};public MeterArea hp=new MeterArea(),mp=new MeterArea{threshold=30};public List<BuffBinding> buffs=new List<BuffBinding>();
 public List<KeyBinding> keys=new List<KeyBinding>{new KeyBinding{action="attack",label="普通攻击",key=67},new KeyBinding{action="aoe",label="范围技能",key=67},new KeyBinding{action="jump",label="跳跃",key=18},new KeyBinding{action="teleport",label="传送",key=32,enabled=false},new KeyBinding{action="hp",label="补HP",key=49,enabled=false},new KeyBinding{action="mp",label="补MP",key=50,enabled=false},new KeyBinding{action="home",label="回城",key=36,enabled=false}};
 public KeyBinding Binding(string action){return keys.FirstOrDefault(k=>k.action==action);}
 public int Key(string action){var k=Binding(action);return k!=null&&k.enabled?k.key:0;}
}
public sealed class WorkspaceSettings {public int version=1;public string active;public SshDestination ssh=new SshDestination();public List<RolePreset> roles=new List<RolePreset>();public RolePreset Current{get{return roles.First(r=>r.id==active);}}}
public static class WorkspaceFiles {
 static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=80};}
 public static T Copy<T>(T item){return Serializer().Deserialize<T>(Serializer().Serialize(item));}
 public static WorkspaceSettings Load(string root){string path=Path.Combine(root,"workspace.json");WorkspaceSettings result;
  if(File.Exists(path)){if(new FileInfo(path).Length>32*1024*1024)throw new IOException("角色配置过大");result=Serializer().Deserialize<WorkspaceSettings>(File.ReadAllText(path));}
  else {var role=new RolePreset{vision=PlayerFiles.Load(Path.Combine(root,"players","current.json"))};role.name=role.vision.name;result=new WorkspaceSettings{active=role.id};result.roles.Add(role);}
  if(result!=null&&result.ssh==null)result.ssh=new SshDestination();
  Validate(result);result.ssh.Validate();return result;
 }
 public static void Save(string root,WorkspaceSettings settings){Validate(settings);settings.ssh.Validate();string path=Path.Combine(root,"workspace.json"),tmp=path+".tmp";File.WriteAllText(tmp,Serializer().Serialize(settings),new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,path+".bak",true);else File.Move(tmp,path);}
 static bool Number(double n){return !Double.IsNaN(n)&&!Double.IsInfinity(n)&&n>=0&&n<=100000;}
 public static void Validate(WorkspaceSettings d){if(d==null||d.version!=1||d.roles==null||d.roles.Count<1||d.roles.Count>64||!d.roles.Any(r=>r.id==d.active))throw new IOException("角色预设无效");var ids=new HashSet<string>();foreach(var r in d.roles){if(r==null||!ids.Add(r.id)||r.name==null||r.name.Length>64||r.battle==null||r.keys==null||r.hp==null||r.mp==null||r.buffs==null)throw new IOException("角色预设无效");PlayerFiles.Validate(r.vision);foreach(var k in r.keys)if(k.key<8||k.key>254||k.key==123)throw new IOException("按键不可使用");foreach(var f in r.buffs)if(f.key<8||f.key>254||f.key==123||!Number(f.interval)||f.interval<=0||!Number(f.delay))throw new IOException("增益参数无效");foreach(var a in new[]{r.battle.melee,r.battle.ranged})if(a==null||a.rangeX<0||a.rangeY<0||a.aoeCount<1||!Number(a.cooldown)||!Number(a.aoeCooldown)||!Number(a.turnDelay))throw new IOException("战斗参数无效");}}
}
public static class BattleGeometry {
 public static Rectangle Search(Point p,BattleSettings c,int? anchor=null){if(c.mode=="stationary"){var l=Attack(p,c,true);var r=Attack(p,c,false);return l.IsEmpty?r:r.IsEmpty?l:Rectangle.Union(l,r);}if(c.mode=="patrol"){int center=anchor??p.X,range=(int)Math.Round(c.patrolRadius*c.screenPerMini);return Rectangle.FromLTRB(center-range-c.attackExtension,p.Y+c.searchOffsetY-c.searchY,center+range+c.attackExtension,p.Y+c.searchOffsetY+c.searchY);}return Rectangle.FromLTRB(p.X+c.searchOffsetX-c.searchX,p.Y+c.searchOffsetY-c.searchY,p.X+c.searchOffsetX+c.searchX,p.Y+c.searchOffsetY+c.searchY);}
 public static Rectangle Attack(Point p,BattleSettings c,bool left){var a=c.Profile;var side=left?c.left:c.right;int reach=c.mode=="stationary"?(side.enabled?side.reach:0):a.rangeX;int half=c.mode=="stationary"?side.halfHeight:a.rangeY,offset=c.mode=="stationary"?side.offsetY:a.offsetY;if(reach<10||half<1)return Rectangle.Empty;return Rectangle.FromLTRB(left?p.X-reach:p.X,p.Y+offset-half,left?p.X:p.X+reach,p.Y+offset+half);}
 public static bool Hit(Rectangle box,Rectangle target){return !box.IsEmpty&&Rectangle.Intersect(box,target).Width>0&&Rectangle.Intersect(box,target).Height>0;}
 public static SideArea Side(Point p,Rectangle box,bool left){return new SideArea{enabled=left?box.Left<p.X:box.Right>p.X,reach=Math.Max(0,left?p.X-box.Left:box.Right-p.X),halfHeight=Math.Max(1,box.Height/2),offsetY=box.Top+box.Height/2-p.Y};}
 public static void SetAttack(BattleSettings c,Point p,Rectangle box){if(box.Left<p.X)c.left=Side(p,box,true);if(box.Right>p.X)c.right=Side(p,box,false);if(c.mode!="stationary"){var a=c.Profile;a.rangeX=Math.Max(Math.Abs(box.Left-p.X),Math.Abs(box.Right-p.X));a.rangeY=Math.Max(1,box.Height/2);a.offsetY=box.Top+box.Height/2-p.Y;}}
 public static void SetSearch(BattleSettings c,Point p,Rectangle box){if(c.mode!="patrol"){c.searchX=Math.Max(1,box.Width/2);c.searchOffsetX=box.Left+box.Width/2-p.X;}c.searchY=Math.Max(1,box.Height/2);c.searchOffsetY=box.Top+box.Height/2-p.Y;}
 public static void SetMovement(BattleSettings c,Point p,Rectangle box){c.moveLeft=Math.Max(0,p.X-box.Left);c.moveRight=Math.Max(0,box.Right-p.X);if(c.mode=="patrol"){c.patrolRadius=Math.Max(c.moveLeft,c.moveRight)/Math.Max(.01,c.screenPerMini);c.searchY=Math.Max(1,box.Height/2);c.searchOffsetY=box.Top+box.Height/2-p.Y;}}
 public static double? Meter(byte[] pixels,int width,int height,MeterArea region,bool hp){if(!region.Valid(width,height))return null;int filled=0;for(int x=region.x;x<region.x+region.width;x++){int good=0;for(int y=region.y;y<region.y+region.height;y++){int i=(y*width+x)*3,b=pixels[i],g=pixels[i+1],r=pixels[i+2];if(hp?r>70&&r>g*1.18&&r>b*1.12:b>70&&b>r*1.18&&b>g*1.05)good++;}if(good>=Math.Max(1,region.height/5))filled++;}return 100.0*filled/region.width;}
}
public sealed class CombatObservation {
 public const int MaxAgeMs=600;
 public long sequence,window;public double now,age;public bool fresh,combatReady=true;public Point body;public string facing="unknown";public bool facingFresh;public List<Rectangle> monsters=new List<Rectangle>();public PointF? dot;public double? hp,mp;
 public static CombatObservation Parse(Dictionary<string,object> d,double at){var o=new CombatObservation{sequence=Wire.Long(d,"sequence"),window=Wire.Long(d,"window"),now=at,fresh=Wire.Bool(d,"found")&&Wire.Bool(d,"position_fresh"),facing=Wire.Str(d,"direction"),facingFresh=Wire.Bool(d,"direction_fresh"),combatReady=!d.ContainsKey("navigation_combat_ready")||Wire.Bool(d,"navigation_combat_ready")};object value;if(d.TryGetValue("body",out value)&&value!=null){var p=Wire.IntArray(value);o.body=new Point(p[0],p[1]);}if(d.TryGetValue("monsters",out value)&&value is System.Collections.IEnumerable)foreach(object item in (System.Collections.IEnumerable)value){var m=item as Dictionary<string,object>;if(m!=null){var p=Wire.IntArray(m["position"]);var s=Wire.IntArray(m["size"]);o.monsters.Add(new Rectangle(p[0],p[1],s[1],s[0]));}}if(d.TryGetValue("minimap_dot",out value)&&value!=null){var p=Wire.IntArray(value);o.dot=new PointF(p[0],p[1]);}if(d.TryGetValue("hp",out value)&&value!=null)o.hp=Convert.ToDouble(value);if(d.TryGetValue("mp",out value)&&value!=null)o.mp=Convert.ToDouble(value);if(d.TryGetValue("capture_age_ms",out value))o.age=Convert.ToDouble(value);return o;}
}
}
