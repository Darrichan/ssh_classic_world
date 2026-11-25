using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Reflection;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace SshScreen {
public static class RoutePreviewChecks {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Need(bool ok,string reason){if(!ok)throw new IOException(reason);}
 [STAThread]public static void Main(string[] args){var report=Wire.Map("ok",false,"game_input",false,"ssh_started",false);try{
  var root=Path.Combine(args[0],"preview-fixture");Directory.CreateDirectory(root);byte[] bytes;string png;
  using(var bitmap=new Bitmap(160,100)){var random=new Random(13);for(int y=0;y<48;y++)for(int x=0;x<64;x++)bitmap.SetPixel(x,y,Color.FromArgb(random.Next(30,100),random.Next(30,100),random.Next(30,100)));using(var g=Graphics.FromImage(bitmap))g.FillRectangle(new SolidBrush(Color.FromArgb(255,255,136)),15,20,3,3);bytes=Wire.Pixels(bitmap);using(var roi=bitmap.Clone(new Rectangle(0,0,64,48),bitmap.PixelFormat))png=RouteFiles.EncodePng(roi);}
  var d=new RouteDocument{window=42,sourceWidth=160,sourceHeight=100,roiWidth=64,roiHeight=48,lastMiniPng=png,dotSeedX=16,dotSeedY=21};
  long window=15796042,seq=0;string source="";bool cursorInGame=false;int[] cursorSamples=null;
  Func<Dictionary<string,object>> frame=()=>Wire.Map("window",window,"source_host",source,"width",160,"height",100,"age_ms",0.0,"cursor_in_game",cursorInGame,"cursor_samples",cursorSamples,"sequence",++seq,"bgr24",bytes);
  using(var studio=new RouteStudio(root,frame,(layout,reference)=>layout.Extract(frame(),reference),(enabled,document)=>{})){
   ((System.Threading.Timer)typeof(RouteStudio).GetField("worker",F).GetValue(studio)).Change(-1,-1);
   var engine=new RouteEngine(d);typeof(RouteStudio).GetField("engine",F).SetValue(studio,engine);
   studio.ValidateRecordingSource=runtime=>{runtime.window=window;runtime.sourceHost=source;};
   var poll=typeof(RouteStudio).GetMethod("PollPosition",F);
   poll.Invoke(studio,new object[]{engine});
   var live=(Point?)typeof(RouteStudio).GetField("livePosition",F).GetValue(studio);
   Need(live.HasValue&&live.Value==new Point(16,21),"A reopened game did not produce the live canvas marker");
   Need(d.window==42&&d.sourceHost=="","Preview rebound rewrote the saved route identity");
   report["live_marker_survives_saved_window_handle_change"]=true;
   window=5705596;poll.Invoke(studio,new object[]{engine});
   var cal=(RouteDocument)typeof(RouteStudio).GetField("previewCalibration",F).GetValue(studio);
   Need(cal.window==window&&d.window==42,"Second window change reused a stale preview binding");
   report["subsequent_window_change_refreshes_only_runtime_binding"]=true;
   source="another-device";bool rejected=false;try{poll.Invoke(studio,new object[]{engine});}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}
   Need(rejected,"Preview accepted the saved map on another source device");report["source_device_guard_preserved"]=true;
   source="";
   studio.SetHomeMapVisible(true);
   typeof(RouteStudio).GetField("previewAt",F).SetValue(studio,120000L);
   typeof(RouteStudio).GetMethod("UseDocument",F).Invoke(studio,new object[]{RouteFiles.Clone(d),null,true});
   typeof(RouteStudio).GetMethod("Poll",F).Invoke(studio,new object[]{null});
   var fresh=(Point?)typeof(RouteStudio).GetMethod("FreshMapPosition",F).Invoke(studio,null);
   Need(fresh==new Point(16,21),"New route waited on the previous route's preview timestamp");
   report["new_route_polls_immediately_after_restarting_its_clock"]=true;
   var current=(RouteEngine)typeof(RouteStudio).GetField("engine",F).GetValue(studio);
   var latest=typeof(RouteStudio).GetMethod("FreshMapPosition",F);
   cursorInGame=true;
   cursorSamples=new[]{20,20,120,80};
   poll.Invoke(studio,new object[]{current});
   Need(latest.Invoke(studio,null)==null,"A pointer over the minimap left a cached position usable");
   cursorSamples=new[]{120,80,125,80};
   poll.Invoke(studio,new object[]{current});
   Need((Point?)latest.Invoke(studio,null)==new Point(16,21),"A pointer elsewhere in the game prevented positioning");
   cursorSamples=null;
   poll.Invoke(studio,new object[]{current});
   Need(latest.Invoke(studio,null)==null,"An old capture without a precise pointer location passed the overlap check");
   report["pointer_outside_minimap_allows_points_and_overlap_releases_on_next_frame"]=true;
  }
  report["ok"]=true;
 }catch(Exception e){report["error"]=e.GetBaseException().Message;Environment.ExitCode=1;}
 Wire.Save(Path.Combine(args[0],"route-preview-checks.json"),report);Console.WriteLine(new JavaScriptSerializer().Serialize(report));}
}}
