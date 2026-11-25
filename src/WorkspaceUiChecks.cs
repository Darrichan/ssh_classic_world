using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;
namespace SshScreen {
public static class WorkspaceUiChecks {
 const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Get(object o,string field){return o.GetType().GetField(field,F).GetValue(o);}static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,F).Invoke(o,args);}
 static void Need(bool okay,string text){if(!okay)throw new IOException(text);}
 static IEnumerable<Control> All(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var n in All(c))yield return n;}}
 static Point At(RemoteView view,Point p){var r=view.ImageRect();return new Point(r.X+(int)Math.Round((p.X+.5)*r.Width/view.SourceSize.Width),r.Y+(int)Math.Round((p.Y+.5)*r.Height/view.SourceSize.Height));}
 static void Mouse(RemoteView view,string method,Point native,MouseButtons button){var p=At(view,native);typeof(Control).GetMethod(method,F).Invoke(view,new object[]{new MouseEventArgs(button,1,p.X,p.Y,0)});}
 static void DrawMinimapBox(RemoteView view){
    Mouse(view,"OnMouseDown",new Point(40,40),MouseButtons.Left);
    Mouse(view,"OnMouseMove",new Point(199,139),MouseButtons.Left);
    Mouse(view,"OnMouseUp",new Point(199,139),MouseButtons.Left);
 }
 static void CheckAutoMinimap(ControllerForm form,RemoteView view,string root){
    var zoom=(RemoteView)Get(form,"mapCalibrationImage");
    var zoomPanel=(Control)Get(form,"mapCalibrationZoom");
    var save=(Button)Get(form,"captureSave");
    var reselect=(Button)Get(form,"mapReselect");
    var hint=(Label)Get(form,"captureHint");
    var frame=(Dictionary<string,object>)Get(form,"captureFrame");
    var original=(byte[])frame["bgr24"];
    DrawMinimapBox(view);
    Need(zoomPanel.Visible&&view.MapCalibration&&zoom.Frame!=null,"The highlighted selection and zoom did not appear");
    Need(view.HasPick&&save.Enabled&&save.Text=="保存标定"&&hint.Text.Contains("已自动找到"),"A unique marker required a manual click");
    Need(view.Pick==new Point(100,100),"Automatic detection selected the wrong native pixel");
    Shot(form,Path.Combine(root,"workspace-map-calibration.png"));
    reselect.PerformClick();
    Need(!zoomPanel.Visible&&!view.HasPick&&!save.Enabled,"Reselect did not clear the marker and its zoom");

    int width=Wire.Int(frame,"width");
    var ambiguous=(byte[])original.Clone();
    for(int y=115;y<=117;y++)for(int x=152;x<=154;x++){
        int index=(y*width+x)*3;
        ambiguous[index]=136;ambiguous[index+1]=ambiguous[index+2]=255;
    }
    frame["bgr24"]=ambiguous;
    DrawMinimapBox(view);
    var bounds=view.Selections.Single();
    var exactCenter=new Point(bounds.X+(int)Math.Round(126.5-bounds.X),108);
    Need(save.Enabled&&view.HasPick&&view.Pick==exactCenter,"Automatic calibration did not use the original all-pixel center");
    reselect.PerformClick();
    for(int y=99;y<=101;y++)for(int x=99;x<=101;x++)ambiguous[(y*width+x)*3]=137;
    for(int y=115;y<=117;y++)for(int x=152;x<=154;x++)ambiguous[(y*width+x)*3]=137;
    DrawMinimapBox(view);
    Need(!save.Enabled&&!view.HasPick&&hint.Text.Contains("没有找到"),"Inexact pixels activated automatic save");
    var area=view.Selections.Single();
    Mouse(zoom,"OnMouseDown",new Point(100-area.X,100-area.Y),MouseButtons.Left);
    Need(save.Enabled&&view.HasPick&&view.Pick==new Point(100,100),"Clicking the zoom did not resolve the marker in native coordinates");
    reselect.PerformClick();
    frame["bgr24"]=original;
    DrawMinimapBox(view);
    Need(save.Enabled&&view.Pick==new Point(100,100),"Automatic calibration could not be retried");
    save.PerformClick();
 }
 static void DemoOverlay(ControllerForm form){((CheckBox)Get(form,"showGuides")).Checked=true;Call(form,"OnVision",Wire.Map("sequence",999L,"window",42L,"found",true,"position_fresh",true,"body",new[]{600,400},"direction","right","direction_fresh",true,"monsters",new object[]{Wire.Map("position",new[]{420,390},"size",new[]{44,42},"score",.95),Wire.Map("position",new[]{720,390},"size",new[]{44,42},"score",.93)}));}
 static void CheckScaledRoleMinimap(PlayerMinimap minimap){
    var calibration=minimap.Route();
    var region=MinimapLayout.ReferencePixels(calibration);
    int width=minimap.sourceWidth*2,height=minimap.sourceHeight*2;
    var pixels=new byte[width*height*3];
    for(int y=0;y<minimap.roiHeight*2;y++)for(int x=0;x<minimap.roiWidth*2;x++)
        Buffer.BlockCopy(region,(y/2*minimap.roiWidth+x/2)*3,pixels,((minimap.roiY*2+y)*width+minimap.roiX*2+x)*3,3);
    var frame=Wire.Map("window",minimap.window,"width",width,"height",height,"bgr24",pixels);
    Need(minimap.Fits(frame),"Saved role minimap did not fit the resized game");
    var tracker=new MiniTracker();tracker.Seed(region,minimap.roiWidth,minimap.roiHeight,0,0);
    var observation=minimap.Observe(frame,tracker,calibration,new MinimapLayout(calibration));
    Need(Wire.Bool(observation,"enabled")&&observation["dot"]!=null,"No role minimap position after resize");
    Need(Wire.IntArray(observation["dot"]).SequenceEqual(new[]{minimap.dotSeedX,minimap.dotSeedY}),"Resized role minimap changed location units");
    Need(Wire.IntArray(observation["roi"]).SequenceEqual(new[]{minimap.roiX*2,minimap.roiY*2,minimap.roiWidth*2,minimap.roiHeight*2}),"Resized role minimap returned old crop");
 }
 static void Shot(Form f,string file){f.PerformLayout();using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,f.Size));image.Save(file,System.Drawing.Imaging.ImageFormat.Png);}}
 [STAThread]public static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);string root=args[0];var report=Wire.Map("ok",false,"game_input",false,"ssh_started",false,"physical_input",false);ControllerForm form=null;try{
  var settings=WorkspaceFiles.Load(root);settings.Current.battle=new BattleSettings();settings.Current.Binding("attack").enabled=true;WorkspaceFiles.Save(root,settings);byte[] pixels;using(var scene=new Bitmap(1368,768)){using(var g=Graphics.FromImage(scene)){g.Clear(Color.FromArgb(30,39,53));var random=new Random(13);for(int y=45;y<135;y+=5)for(int x=45;x<195;x+=5)
        using(var color=new SolidBrush(Color.FromArgb(random.Next(45,155),random.Next(45,155),random.Next(45,155))))g.FillRectangle(color,x,y,5,5);
        using(var brush=new SolidBrush(Color.FromArgb(255,255,136)))g.FillRectangle(brush,99,99,3,3);using(var brush=new SolidBrush(Color.FromArgb(52,65,84)))g.FillRectangle(brush,0,450,1368,25);using(var brush=new SolidBrush(Color.FromArgb(50,180,110))){g.FillEllipse(brush,420,390,42,44);g.FillEllipse(brush,720,390,42,44);}using(var brush=new SolidBrush(Color.FromArgb(180,195,225)))g.FillRectangle(brush,584,360,34,72);}pixels=Wire.Pixels(scene);}
  long seq=0;form=new ControllerForm(root,false,false);form.FixtureSource=()=>Wire.Map("width",1368,"height",768,"native_width",1368,"native_height",768,"window",42L,"sequence",++seq,"age_ms",0.0,"cursor_in_game",false,"captured_utc",DateTime.UtcNow.ToString("o"),"bgr24",(byte[])pixels.Clone());typeof(ControllerForm).GetField("target",F).SetValue(form,42L);typeof(ControllerForm).GetField("displayedWindow",F).SetValue(form,42L);
  var view=(RemoteView)Get(form,"view");view.Frame=Wire.BitmapFrom(pixels,1368,768);view.SourceSize=view.Frame.Size;view.Sequence=1;var tabs=(TabControl)Get(form,"sideTabs");var timer=new Timer{Interval=220};int phase=0;bool inTick=false;ComboBox oldStyle=null;Point anchor=Point.Empty;Rectangle selection=Rectangle.Empty;
  form.Shown+=delegate{timer.Start();};timer.Tick+=delegate{if(inTick)return;inTick=true;try{switch(phase){
   case 0:
      Need(Application.OpenForms.Cast<Form>().Count(f=>f.TopLevel&&f.Visible)==1,"Extra top-level window");
      Need(form.FormBorderStyle==FormBorderStyle.Sizable,"The app was not using native Windows chrome");
      var mini=(Control)Get(form,"minimapPanel");
      Need(!tabs.Visible&&view.Visible&&mini.Visible&&view.Width+mini.Width>=form.ClientSize.Width-36,
         "Home preview and minimap did not fit beside each other");
      var calibrationEntry=All(form).OfType<Button>().Single(b=>b.Name=="CalibrateRouteMap");
      var mapSelector=All(form).OfType<ComboBox>().Single(c=>c.Name=="RouteMapSelector");
      Need(calibrationEntry.Visible&&calibrationEntry.Parent==mapSelector.Parent&&calibrationEntry.Left>=mapSelector.Right,
         "Calibration was not easy to find beside the map selector");
      var choices=All(form).OfType<ComboBox>().Where(c=>c.Visible).Select(c=>c.Name).OrderBy(n=>n).ToArray();
      Need(choices.SequenceEqual(new[]{"RouteMapSelector","RouteSelector"}),"Home did not leave just the map and route selectors");
      Need(!All(form).OfType<Button>().Any(b=>b.Visible&&new[]{"开始录制","保存设置","输入文字"}.Contains(b.Text)),
         "Editor or connection actions escaped to the home screen");
      var recognition=(CheckBox)Get(form,"showGuides");
      Need(recognition.Visible&&recognition.Checked&&recognition.Text=="识别预览","Recognition preview toggle is missing from home");
      DemoOverlay(form);Need(view.Person!=null&&view.Guides!=null,"Checked preview did not display recognition results");
      typeof(ControllerForm).GetField("autoRunning",F).SetValue(form,true);
      Call(view,"OnLostFocus",EventArgs.Empty);
      recognition.Checked=false;
      Need(view.Person==null&&view.Guides==null&&(bool)Get(form,"autoRunning")&&Get(form,"observation")!=null,
         "Hiding the preview interrupted automation or discarded its observation");
      typeof(ControllerForm).GetField("autoRunning",F).SetValue(form,false);
      report["recognition_preview_toggle_is_independent_from_running_script"]=true;
      var entry=(Button)Get(form,"settingsButton");
      Need(entry.PointToScreen(new Point(0,entry.Height)).Y<=view.PointToScreen(Point.Empty).Y,
         "The Settings button is clipped by the preview");
      var person=(PlayerPanel)Get(form,"person");
      int uiThread=System.Threading.Thread.CurrentThread.ManagedThreadId,receivedThread=0;
      var callback=new System.Threading.Thread(()=>Call(person,"SafeUi",
          (Action)(()=>receivedThread=System.Threading.Thread.CurrentThread.ManagedThreadId)));
      callback.Start();callback.Join();
      var dispatch=System.Diagnostics.Stopwatch.StartNew();
      while(receivedThread==0&&dispatch.ElapsedMilliseconds<500){Application.DoEvents();System.Threading.Thread.Sleep(5);}
      Need(receivedThread==uiThread,"Recognition callbacks were lost with Settings closed");
      report["recognition_callbacks_reach_home"]=true;
      Shot(form,Path.Combine(root,"workspace-home.png"));report["home_preview_and_route_selection"]=true;
      entry.PerformClick();
      Need(tabs.Visible&&!view.Visible&&((Control)Get(form,"settingsNavigation")).Visible,
         "Settings did not open as a full page in the same window");
      Need(!All(form).OfType<Button>().Any(b=>b.Text=="开始识别"),"Recognition action still in role settings");
      Need(tabs.TabPages.Cast<TabPage>().Select(p=>p.Text).SequenceEqual(new[]{"角色","战斗","技能","路线管理","连接","记录"}),"Settings tabs mismatch");
      Need(tabs.SelectedIndex==4&&All(tabs.TabPages[4]).OfType<Label>().Any(l=>l.Text=="被控端"),
         "The settings page did not expose the remote computer");
      Shot(form,Path.Combine(root,"workspace-connection.png"));
      var fields=All(tabs.TabPages[4]).ToDictionary(c=>String.IsNullOrEmpty(c.Name)?Guid.NewGuid().ToString("N"):c.Name);
      var destination=(ComboBox)fields["SshDestination"];
      destination.SelectedIndex=1;
      var host=(TextBox)fields["SshHost"];
      host.Text="-invalid";
      Need(!(bool)Call(form,"SaveSettings"),"An invalid destination was accepted");
      host.Text="workstation.example";
      ((TextBox)fields["SshUser"]).Text="operator";
      ((TextBox)fields["SshPort"]).Text="2203";
      Need((bool)Call(form,"SaveSettings"),"The custom destination did not save");
      var device=WorkspaceFiles.Load(root).ssh;
      Need(device.custom&&device.SshHost()=="workstation.example"&&
         device.SshOptions(root).Contains("-p 2203 -l operator")&&device.SshOptions(root).Contains("StrictHostKeyChecking=yes"),
         "The saved destination did not affect OpenSSH options");
      var legacy=new SshDestination();
      Need(legacy.SshHost()=="maple-screen-computer1"&&legacy.SshOptions(root).Contains("ssh_config"),"Legacy computer1 settings changed");
      var node=new RouteDocument{sourceHost=device.Identity(),window=42,sourceWidth=1368,sourceHeight=768,roiWidth=50,roiHeight=50};
      var source=Wire.Map("window",42L,"width",1368,"height",768,"source_host",device.Identity());
      var otherDeviceMap=PlayerMinimap.FromRoute(node);Need(otherDeviceMap.Fits(source),"Calibration lost its device identity");
      source["source_host"]="";Need(!otherDeviceMap.Fits(source),"Another computer could reuse the same window handle");
      var oldCalibration=new RouteDocument{window=42,sourceWidth=1366,sourceHeight=768,
          roiX=5,roiY=73,roiWidth=122,roiHeight=117,dotSeedX=10,dotSeedY=46};
      var roleMinimap=person.Profile.minimap;
      var selectedCalibration=person.Calibration;
      person.Profile.minimap=PlayerMinimap.FromRoute(new RouteDocument{window=42,roiWidth=80,roiHeight=80});
      person.Calibration=()=>oldCalibration;
      var rebound=person.MinimapForFrame(Wire.Map("window",15796042L,"width",1366,"height",768));
      Need(rebound!=null&&rebound.window==15796042&&rebound.roiX==5&&rebound.roiY==73&&
           rebound.roiWidth==122&&rebound.dotSeedY==46&&oldCalibration.window==42,
           "Startup did not use the selected map on a reopened window without matching pixels");
      person.Calibration=selectedCalibration;person.Profile.minimap=roleMinimap;
      report["selected_map_reused_on_current_game_window_without_startup_match"]=true;
      destination.SelectedIndex=0;Need((bool)Call(form,"SaveSettings"),"Could not restore the installed device");
      report["destination_persists_and_applies_to_ssh"]=true;
      report["single_settings_entry_with_inline_preview"]=true;
      report["role_is_preset_without_recognition_button"]=true;
      tabs.SelectedIndex=0;
      Shot(form,Path.Combine(root,"workspace-role.png"));tabs.SelectedIndex=1;phase++;break;
   case 1:
      var thresholdLabel=All(tabs.TabPages[1]).OfType<Label>().Single(l=>l.Text=="攻击范围内至少几只怪");
      var threshold=All(thresholdLabel.Parent).OfType<TextBox>().Single();
      threshold.Text="3";Call(threshold,"OnValidated",EventArgs.Empty);
      Need(((WorkspaceSettings)Get(form,"workspace")).Current.battle.ranged.aoeCount==3&&
         ((WorkspaceSettings)Get(form,"workspace")).Current.battle.melee.aoeCount==2,"Group threshold edited another attack profile");
      report["group_attack_threshold_is_editable_for_each_attack_profile"]=true;
      var check=All(tabs.TabPages[1]).OfType<CheckBox>().First(c=>c.Text=="跳跃攻击");check.Checked=!check.Checked;Need((bool)Get(form,"settingsDirty"),"Editing did not create unsaved state");Need(!(bool)Get(form,"autoRunning"),"Editing started a script");((Button)Get(form,"saveSettings")).PerformClick();Need(!(bool)Get(form,"settingsDirty")&&!(bool)Get(form,"autoRunning"),"Save started script or did not clear dirty state");report["settings_then_explicit_save_then_start"]=true;var label=All(tabs.TabPages[1]).OfType<Label>().First(l=>l.Text=="左侧距离");Need(label.Visible&&label.Width>=100&&label.Height>=20,"Numeric field caption not visible");report["numeric_captions_visible"]=true;report["numeric_caption_bounds"]=new[]{label.Left,label.Top,label.Width,label.Height};DemoOverlay(form);Shot(form,Path.Combine(root,"workspace-battle.png"));Call(form,"BeginTemplateCapture","attack_box");phase++;break;
   case 2:Mouse(view,"OnMouseDown",new Point(600,400),MouseButtons.Left);Mouse(view,"OnMouseUp",new Point(600,400),MouseButtons.Left);anchor=view.Pick;Mouse(view,"OnMouseDown",new Point(400,330),MouseButtons.Left);Mouse(view,"OnMouseMove",new Point(590,470),MouseButtons.Left);Mouse(view,"OnMouseUp",new Point(590,470),MouseButtons.Left);Need(view.Selections.Count==1,"Attack selection absent");selection=view.Selections[0];Shot(form,Path.Combine(root,"workspace-edit-range.png"));((Button)Get(form,"captureSave")).PerformClick();var role=((WorkspaceSettings)Get(form,"workspace")).Current;Need(role.battle.left.reach==anchor.X-selection.Left&&role.battle.right.reach==240,"Saving left area changed right or used preview pixels");Need(Get(form,"captureFrame")==null&&!(bool)Get(form,"settingsDirty"),"Range save did not finish/persist");report["native_range_selection_and_one_side_preservation"]=true;tabs.SelectedIndex=2;phase++;break;
   case 3:var keys=All(tabs.TabPages[2]).OfType<CheckBox>().First();keys.Checked=false;Need((bool)Get(form,"settingsDirty"),"Binding enable toggle was not saved as dirty");((Button)Get(form,"saveSettings")).PerformClick();Need(!WorkspaceFiles.Load(root).Current.Binding("attack").enabled,"Disabled binding did not persist");report["binding_enabled_checkbox_persists"]=true;keys.Checked=true;Call(form,"DiscardSettings");Need(!((WorkspaceSettings)Get(form,"workspace")).Current.Binding("attack").enabled,"Discard did not restore saved binding");var restoredKey=All(tabs.TabPages[2]).OfType<CheckBox>().First();Need(!restoredKey.Checked,"Discard left stale controls");restoredKey.Checked=true;Need(((WorkspaceSettings)Get(form,"workspace")).Current.Binding("attack").enabled,"Controls still write discarded model");Call(form,"SaveSettings");report["discard_restores_controls_and_live_model"]=true;DemoOverlay(form);Shot(form,Path.Combine(root,"workspace-skills.png"));var config=(WorkspaceSettings)Get(form,"workspace");var copy=WorkspaceFiles.Copy(config.Current);copy.id=Guid.NewGuid().ToString("N");copy.name="预设副本";config.roles.Add(copy);Call(form,"SelectRole",copy.id);Need(((WorkspaceSettings)Get(form,"workspace")).active==copy.id,"Preset activation failed");report["preset_switch_keeps_script_stopped"]=!(bool)Get(form,"autoRunning");var studio=(RouteStudio)Get(form,"routes");var engine=(RouteEngine)Get(studio,"engine");engine.Document.graphEdited=true;engine.Document.nodes.Clear();engine.Document.edges.Clear();engine.Document.nodes.Add(new RouteNode{id="A",x=1,y=1});engine.Document.nodes.Add(new RouteNode{id="B",x=20,y=1});engine.Document.edges.Add(new RouteEdge{id="W",from="A",to="B",kind="walk",both=true});((PlayerPanel)Get(form,"person")).RequestMinimap();phase++;break;
   case 4:CheckAutoMinimap(form,view,root);report["minimap_exact_dot_auto_manual_zoom_click_and_reselect"]=true;var minimap=((WorkspaceSettings)Get(form,"workspace")).Current.vision.minimap;Need(minimap!=null,"Role minimap calibration was not saved");Need(minimap.roiX+minimap.dotSeedX==100&&minimap.roiY+minimap.dotSeedY==100&&!String.IsNullOrEmpty(minimap.backgroundPng),"Rough click did not snap and persist the background");report["minimap_automatic_dot_saved_actual_native_position"]=true;CheckScaledRoleMinimap(WorkspaceFiles.Load(root).Current.vision.minimap);report["saved_role_minimap_adapts_to_resize"]=true;Need(((RouteStudio)Get(form,"routes")).RuntimeRoute().edges.Count==1,"Role calibration discarded the route graph");report["role_minimap_does_not_replace_route_workshop"]=true;tabs.SelectedIndex=1;form.Size=new Size(960,640);phase++;break;
   case 5:DemoOverlay(form);Need(form.Width==960&&form.Height==640,"Compact size failed");Shot(form,Path.Combine(root,"workspace-small.png"));Need(Wire.Hash((byte[])form.FixtureSource()["bgr24"])==Wire.Hash(pixels),"Overlay changed native pixels");report["preview_overlays_leave_native_pixels_unchanged"]=true;report["single_window"]=true;report["size"]=new[]{form.Width,form.Height};
      oldStyle=All(tabs.TabPages[1]).OfType<ComboBox>().Single(box=>box.AccessibleName=="参数档");
      for(int i=0;i<30;i++){
         oldStyle.SelectedIndex=i%2;Need(!oldStyle.IsDisposed,"The native combo was destroyed while processing selection");
      }
      phase++;break;
   case 6:
      Need(oldStyle.IsDisposed,"The battle settings did not rebuild after the selection message returned");
      var style=All(tabs.TabPages[1]).OfType<ComboBox>().Single(box=>box.AccessibleName=="参数档");
      Need(style.SelectedIndex==1,"Rapid style switching did not retain the latest selection");
      style.SelectedIndex=0;Need(!style.IsDisposed,"The replacement combo was destroyed inline");
      phase++;break;
   case 7:
      Need(All(tabs.TabPages[1]).OfType<ComboBox>().Single(box=>box.AccessibleName=="参数档").SelectedIndex==0,
         "Returning to melee failed");
      report["melee_ranged_rebuild_after_native_selection"]=true;
      var windows=(ComboBox)Get(form,"targets");
      var both=new object[]{Wire.Map("handle",99L,"title","其他游戏"),Wire.Map("handle",42L,"title","冒险岛怀旧服")};
      Call(form,"SetTargets",(object)both);
      Need((long)Get(form,"target")==42L&&windows.Text=="冒险岛怀旧服","Preferred game window was not selected");
      windows.SelectedIndex=0;
      Call(form,"SetTargets",(object)both);
      Need((long)Get(form,"target")==0L,"Refresh replaced the user's explicit whole-screen choice");
      typeof(ControllerForm).GetField("userSelectedTarget",F).SetValue(form,false);
      Call(form,"SetTargets",(object)new object[]{Wire.Map("handle",99L,"title","其他游戏")});
      Need((long)Get(form,"target")==0L&&windows.Text=="整个主屏幕","Missing game did not fall back to the main screen");
      Call(form,"SetTargets",(object)both);
      Need((long)Get(form,"target")==42L,"The game window appearing later did not become the default");
      report["preferred_game_window_with_main_screen_fallback"]=true;
      Call(form,"SaveSettings");
      ((Button)Get(form,"previewButton")).PerformClick();
      Need(!tabs.Visible&&view.Visible&&((Control)Get(form,"minimapPanel")).Visible,
         "Returning to the preview did not restore the home layout");
      report["returns_to_full_preview_after_saving"]=true;
      ((Button)Get(form,"routeWorkspaceButton")).PerformClick();
      Need(tabs.Visible&&tabs.SelectedIndex==3&&!((Control)Get(form,"settingsNavigation")).Visible,
         "The route workbench did not open as its own section");
      ((Button)Get(form,"settingsButton")).PerformClick();
      Need(tabs.SelectedIndex==4&&((Control)Get(form,"settingsNavigation")).Visible,
         "Settings did not open directly from the route workbench");
      report["direct_navigation_between_preview_route_and_settings"]=true;
      report["ok"]=true;timer.Stop();form.Close();break;
  }}catch(Exception e){report["error"]=e.GetBaseException().Message;report["stack"]=e.GetBaseException().StackTrace;timer.Stop();typeof(ControllerForm).GetField("settingsDirty",F).SetValue(form,false);form.Close();}finally{inTick=false;}};form.FormClosed+=delegate{timer.Stop();Wire.Save(Path.Combine(root,"workspace-ui-checks.json"),report);};Application.Run(form);timer.Dispose();
 }catch(Exception e){report["error"]=e.GetBaseException().Message;if(form!=null)form.Dispose();Wire.Save(Path.Combine(root,"workspace-ui-checks.json"),report);}}
}}
