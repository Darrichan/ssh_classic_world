using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.IO;using System.Linq;
namespace SshScreen {
public static class NativeModelChecks {
 public static void Main(string[] args){string root=args[0];var report=Wire.Map("ok",false,"game_input",false,"ssh_started",false);try{
  var profile=WorkspaceFiles.Copy(PlayerFiles.Load(Path.Combine(root,"players","current.json")));profile.mode="nametag";profile.search_y_end=0;byte[] raw;
  using(var scene=new Bitmap(640,480))using(var tag=RouteFiles.Png(profile.nametag.png)){using(var g=Graphics.FromImage(scene))g.Clear(Color.FromArgb(55,55,55));for(int y=0;y<tag.Height;y++)for(int x=0;x<tag.Width;x++){Color c=tag.GetPixel(x,y);if(!(c.G>=180&&c.R<=80&&c.B<=80))scene.SetPixel(280+x,260+y,c);}raw=Wire.Pixels(scene);}
  var times=new List<double>();var watch=Stopwatch.StartNew();using(var runtime=new PlayerRuntime(root,profile)){report["load_ms"]=watch.Elapsed.TotalMilliseconds;for(int seq=1;seq<=6;seq++){var answer=runtime.Locate(Wire.Map("bgr24",raw,"width",640,"height",480,"sequence",seq,"window",42L));if(!Wire.Bool(answer,"found"))throw new IOException("Original model pipeline lost synthetic nametag");int[] body=Wire.IntArray(answer["body"]);if(body[0]!=280+(int)profile.nametag.body_x||body[1]!=260+(int)profile.nametag.body_y)throw new IOException("Native body coordinates changed");times.Add(Convert.ToDouble(answer["processing_ms"]));}}
  report["frames"]=times.Count;report["processing_median_ms"]=times.OrderBy(x=>x).ElementAt(times.Count/2);report["processing_max_ms"]=times.Max();report["native_body_exact"]=true;report["ok"]=true;
 }catch(Exception e){report["error"]=e.GetBaseException().Message;}Wire.Save(Path.Combine(root,"native-model-checks.json"),report);Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(report));if(!Wire.Bool(report,"ok"))Environment.ExitCode=1;}
}}
