using System;using System.Collections.Generic;using System.Drawing;using System.Globalization;using System.IO;using System.Linq;using System.Windows.Forms;
namespace SshScreen {
public abstract class SettingsPanel:UserControl {
 protected readonly Func<RolePreset> current;protected readonly Action changed;protected readonly Action<string> selectArea;protected TableLayoutPanel rows,rootRows;readonly Dictionary<string,bool> expanded=new Dictionary<string,bool>();
 bool rebuildPosted;
 protected SettingsPanel(Func<RolePreset> role,Action update,Action<string> capture){current=role;changed=update;selectArea=capture;Dock=DockStyle.Fill;AutoScroll=true;BackColor=AppUi.Background;ForeColor=AppUi.Text;Font=new Font("Microsoft YaHei UI",10);}
 protected void Begin(){SuspendLayout();var old=Controls.Cast<Control>().ToArray();Controls.Clear();foreach(Control c in old)c.Dispose();rows=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(18,0,18,18),MaximumSize=new Size(670,0)};rootRows=rows;Controls.Add(rootRows);}
 protected void End(){AppUi.ApplyInputs(this);ResumeLayout(true);}
 protected void Heading(string text){
  bool open;if(!expanded.TryGetValue(text,out open))open=!(text=="稳定参数"||text=="活动与休息"||text=="补药");
  var group=new AppleCard{AutoSize=true,Dock=DockStyle.Top,ColumnCount=1,
   Margin=new Padding(0,0,0,14),Padding=new Padding(16,9,16,12),BackColor=AppUi.Surface};
  var header=new AppleButton{Text=(open?"⌄  ":"›  ")+text,Width=470,Height=38,Anchor=AnchorStyles.Left,
   TextAlign=ContentAlignment.MiddleLeft,ForeColor=AppUi.Text,Font=new Font(Font,FontStyle.Bold)};
  AppUi.Button(header);
  var content=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,ColumnCount=1,Visible=open,Margin=new Padding(10,0,0,0)};
  header.Click+=delegate{expanded[text]=!content.Visible;content.Visible=expanded[text];header.Text=(content.Visible?"⌄  ":"›  ")+text;};
  group.Controls.Add(header);group.Controls.Add(content);rootRows.Controls.Add(group);rows=content;
 }

 protected void Row(string text,Control value){var p=new FlowLayoutPanel{Width=472,Height=39,WrapContents=false,Margin=new Padding(0,0,0,4)};p.Controls.Add(new Label{Text=text,Width=240,Height=34,Margin=new Padding(0),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,ForeColor=AppUi.Text,BackColor=AppUi.Surface});value.Dock=DockStyle.None;value.Width=200;value.Margin=new Padding(0,4,0,0);var textBox=value as TextBox;if(textBox!=null){var inset=new Panel{Width=200,Height=34,Margin=Padding.Empty,BackColor=AppUi.Background,Padding=new Padding(10,7,10,4)};textBox.Dock=DockStyle.Fill;textBox.BorderStyle=BorderStyle.None;textBox.BackColor=inset.BackColor;inset.Controls.Add(textBox);AppUi.Round(inset,6);p.Controls.Add(inset);}else p.Controls.Add(value);rows.Controls.Add(p);}

 protected void Number(string label,Func<double> get,Action<double> set,double min=0,double max=100000){var box=new TextBox{Text=get().ToString("0.###",CultureInfo.InvariantCulture),BorderStyle=BorderStyle.FixedSingle};box.Validated+=delegate{double value;if(Double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!Double.IsNaN(value)&&!Double.IsInfinity(value)&&value>=min&&value<=max){box.BackColor=AppUi.Field;if(Math.Abs(get()-value)>0.0000001){set(value);changed();}}else{box.BackColor=Color.FromArgb(98,48,53);box.Text=get().ToString("0.###",CultureInfo.InvariantCulture);}};Row(label,box);}
 protected void Toggle(string label,Func<bool> get,Action<bool> set){var box=new CheckBox{Text=label,Checked=get(),AutoSize=true,ForeColor=AppUi.Text,Margin=new Padding(0,5,0,8)};box.CheckedChanged+=delegate{set(box.Checked);changed();};rows.Controls.Add(box);}
 protected void PostRebuild(){
  if(rebuildPosted||IsDisposed||Disposing||!IsHandleCreated)return;
  rebuildPosted=true;
  BeginInvoke(new Action(delegate{rebuildPosted=false;if(!IsDisposed&&!Disposing)Build();}));
 }
 protected void Choice(string label,string[] labels,string[] values,Func<string> get,Action<string> set,bool rebuild=false){
  var box=new WorkspaceComboBox{AccessibleName=label};
  box.Items.AddRange(labels);box.SelectedIndex=Math.Max(0,Array.IndexOf(values,get()));
  bool rebuildWhenClosed=false;
  box.SelectedIndexChanged+=delegate{
   int index=box.SelectedIndex;
   if(index<0||index>=values.Length||get()==values[index])return;
   set(values[index]);changed();
   if(rebuild){if(box.DroppedDown)rebuildWhenClosed=true;else PostRebuild();}
  };
  box.DropDownClosed+=delegate{if(rebuildWhenClosed){rebuildWhenClosed=false;PostRebuild();}};
  Row(label,box);
 }
 protected void Button(string label,string area){var b=new AppleButton{Text=label,Width=160,Height=32,Margin=new Padding(0,4,0,8)};AppUi.Button(b);b.Click+=delegate{selectArea(area);};rows.Controls.Add(b);}
 public abstract void Build();
}
public sealed class BattlePanel:SettingsPanel {
 public BattlePanel(Func<RolePreset> role,Action update,Action<string> capture):base(role,update,capture){Build();}
 public override void Build(){Begin();var c=current().battle;Heading("战斗方式");Choice("模式",new[]{"定点","巡逻","路线"},new[]{"stationary","patrol","route"},()=>c.mode,v=>c.mode=v,true);Choice("参数档",new[]{"近战","远程"},new[]{"melee","ranged"},()=>c.style,v=>c.style=v,true);var a=c.Profile;
 Toggle("跳跃攻击",()=>a.jumpAttack,v=>a.jumpAttack=v);if(c.mode=="patrol"){Choice("战斗触发",new[]{"识别怪物","定时攻击"},new[]{"detect","timed"},()=>c.detectMonsters?"detect":"timed",v=>c.detectMonsters=v=="detect",true);if(!c.detectMonsters)Number("定时攻击间隔（秒）",()=>c.periodicAttack,v=>c.periodicAttack=v,.1,3600);}else Toggle(c.mode=="route"?"路线中启用战斗":"启用怪物战斗",()=>c.detectMonsters,v=>c.detectMonsters=v);
 Heading("群攻");Number("攻击范围内至少几只怪",()=>a.aoeCount,v=>a.aoeCount=(int)v,1,1000);Number("群攻间隔（秒）",()=>a.aoeCooldown,v=>a.aoeCooldown=v,0,3600);Toggle("群攻不转身",()=>a.aoeNoTurn,v=>a.aoeNoTurn=v);
 Heading("攻击范围 · 原图像素");Button("框选攻击区","attack_box");
 if(c.mode=="stationary"){Toggle("启用左侧",()=>c.left.enabled,v=>c.left.enabled=v);Number("左侧距离",()=>c.left.reach,v=>c.left.reach=(int)v,0,4096);Number("左侧垂直半高",()=>c.left.halfHeight,v=>c.left.halfHeight=(int)v,1,4096);Number("左侧垂直偏移",()=>c.left.offsetY,v=>c.left.offsetY=(int)v,-4096,4096);Toggle("启用右侧",()=>c.right.enabled,v=>c.right.enabled=v);Number("右侧距离",()=>c.right.reach,v=>c.right.reach=(int)v,0,4096);Number("右侧垂直半高",()=>c.right.halfHeight,v=>c.right.halfHeight=(int)v,1,4096);Number("右侧垂直偏移",()=>c.right.offsetY,v=>c.right.offsetY=(int)v,-4096,4096);Toggle("原地攻击（不转身）",()=>c.inPlace,v=>c.inPlace=v);}
 else{Number("水平距离",()=>a.rangeX,v=>a.rangeX=(int)v,10,4096);Number("垂直半高",()=>a.rangeY,v=>a.rangeY=(int)v,1,4096);Number("垂直偏移",()=>a.offsetY,v=>a.offsetY=(int)v,-4096,4096);Heading("搜怪范围 · 原图像素");Button(c.mode=="patrol"?"框选搜怪高度":"框选搜怪区","search_box");if(c.mode=="patrol")Number("边界搜怪延伸",()=>c.attackExtension,v=>c.attackExtension=(int)v,0,4096);else{Number("水平半宽",()=>c.searchX,v=>c.searchX=(int)v,10,4096);Number("水平偏移",()=>c.searchOffsetX,v=>c.searchOffsetX=(int)v,-4096,4096);}Number("垂直半高",()=>c.searchY,v=>c.searchY=(int)v,1,4096);Number("垂直偏移",()=>c.searchOffsetY,v=>c.searchOffsetY=(int)v,-4096,4096);}
 
 Heading("施放节奏");Number("普通攻击间隔（秒）",()=>a.cooldown,v=>a.cooldown=v,0,3600);Number("转身延迟（秒）",()=>a.turnDelay,v=>a.turnDelay=v,0,10);Number("转身按住（毫秒）",()=>a.turnHoldMs,v=>a.turnHoldMs=(int)v,1,2000);Number("动画保护（毫秒）",()=>a.animationMs,v=>a.animationMs=(int)v,0,10000);
 Heading("活动与休息");Toggle("移动时使用传送",()=>c.teleportWhileWalking,v=>c.teleportWhileWalking=v);Number("传送间隔（秒）",()=>c.teleportCooldown,v=>c.teleportCooldown=v,.1,60);if(c.mode!="route"){Button(c.mode=="patrol"?"框选巡逻范围":"框选活动范围","movement_box");if(c.mode=="patrol"){Number("巡逻半径（小地图）",()=>c.patrolRadius,v=>c.patrolRadius=v,0,1000);Toggle("无怪返回锚点",()=>c.returnToAnchor,v=>c.returnToAnchor=v);Number("回锚等待（秒）",()=>c.returnDelay,v=>c.returnDelay=v,0,3600);}else{Number("左侧活动距离",()=>c.moveLeft,v=>c.moveLeft=(int)v,0,4096);Number("右侧活动距离",()=>c.moveRight,v=>c.moveRight=(int)v,0,4096);}Number("屏幕 / 小地图比例",()=>c.screenPerMini,v=>c.screenPerMini=v,.1,100);Number("边缘缓冲比例",()=>c.softMargin,v=>c.softMargin=v,0,.9);}
 Number("连续运行（分钟）",()=>c.restEveryMinutes,v=>c.restEveryMinutes=v,0,1440);Number("休息（分钟）",()=>c.restMinutes,v=>c.restMinutes=v,0,1440);if(c.mode=="stationary"){Toggle("定时小幅位移",()=>c.antiStuck,v=>c.antiStuck=v);Toggle("位移使用上下方向",()=>c.antiStuckVertical,v=>c.antiStuckVertical=v);Number("位移周期（秒）",()=>c.antiStuckInterval,v=>c.antiStuckInterval=v,1,3600);Number("位移按住（毫秒）",()=>c.antiStuckPulseMs,v=>c.antiStuckPulseMs=(int)v,10,500);}Heading("稳定参数");Number("丢检容忍（毫秒）",()=>c.lossToleranceMs,v=>c.lossToleranceMs=(int)v,0,1000);Number("换侧最短（毫秒）",()=>c.sideHoldMs,v=>c.sideHoldMs=(int)v,0,5000);Number("空框确认（毫秒）",()=>c.clearHoldMs,v=>c.clearHoldMs=(int)v,0,5000);Number("同侧距离加权",()=>c.faceBonus,v=>c.faceBonus=(int)v,0,1000);End();}
}
public sealed class SkillsPanel:SettingsPanel {
 public SkillsPanel(Func<RolePreset> role,Action update,Action<string> capture):base(role,update,capture){Build();}
 static string KeyName(int key){return new KeysConverter().ConvertToString((Keys)key);}
 Control KeyBox(Func<int> get,Action<int> set){var box=new TextBox{Text=KeyName(get()),ShortcutsEnabled=false,TextAlign=HorizontalAlignment.Center};box.KeyDown+=delegate(object s,KeyEventArgs e){e.SuppressKeyPress=true;e.Handled=true;if(e.KeyValue>=8&&e.KeyValue<=254&&e.KeyValue!=123){set(e.KeyValue);box.Text=KeyName(e.KeyValue);changed();}};return box;}
 public override void Build(){Begin();var r=current();Heading("按键绑定");foreach(var bind in r.keys){var row=new FlowLayoutPanel{Width=324,Height=36,WrapContents=false,Margin=new Padding(0,0,0,4)};var check=new CheckBox{Checked=bind.enabled,Width=26,Height=28};var key=KeyBox(()=>bind.key,v=>bind.key=v);key.Width=102;key.Enabled=bind.enabled;check.CheckedChanged+=delegate{bind.enabled=check.Checked;key.Enabled=bind.enabled;if(bind.action=="hp")r.hp.enabled=bind.enabled;if(bind.action=="mp")r.mp.enabled=bind.enabled;changed();};row.Controls.Add(check);row.Controls.Add(new Label{Text=bind.label,Width=160,Height=28,TextAlign=ContentAlignment.MiddleLeft});row.Controls.Add(key);rows.Controls.Add(row);}
 Heading("补药");Number("HP低于（%）",()=>r.hp.threshold,v=>r.hp.threshold=v,0,100);Number("HP间隔（秒）",()=>r.hp.cooldown,v=>r.hp.cooldown=v,.05,3600);Button(r.hp.width>0?"重新框选HP条":"框选HP条","hp_bar");Number("MP低于（%）",()=>r.mp.threshold,v=>r.mp.threshold=v,0,100);Number("MP间隔（秒）",()=>r.mp.cooldown,v=>r.mp.cooldown=v,.05,3600);Button(r.mp.width>0?"重新框选MP条":"框选MP条","mp_bar");Toggle("低HP持续后回城",()=>r.battle.returnHomeOnLowHp,v=>r.battle.returnHomeOnLowHp=v);Number("低HP持续（秒）",()=>r.battle.lowHpTimeout,v=>r.battle.lowHpTimeout=v,1,3600);Heading("自动增益");foreach(var buff in r.buffs.ToArray()){var bar=new FlowLayoutPanel{Width=324,Height=34,WrapContents=false,Margin=new Padding(0,8,0,0)};var check=new CheckBox{Checked=buff.enabled,Width=26,Height=28};check.CheckedChanged+=delegate{buff.enabled=check.Checked;changed();};var name=new TextBox{Text=buff.name,Width=130};name.Validated+=delegate{if(buff.name!=name.Text){buff.name=name.Text;changed();}};var key=KeyBox(()=>buff.key,v=>buff.key=v);key.Width=76;var del=new AppleButton{Text="删除",Width=56,Height=28};AppUi.Button(del);del.Click+=delegate{r.buffs.Remove(buff);changed();Build();};bar.Controls.AddRange(new Control[]{check,name,key,del});rows.Controls.Add(bar);Number("周期（秒）",()=>buff.interval,v=>buff.interval=v,.1,86400);Number("动作间隔（秒）",()=>buff.delay,v=>buff.delay=v,0,60);}
 var add=new AppleButton{Text="添加增益",Width=120,Height=32};AppUi.Button(add);add.Click+=delegate{r.buffs.Add(new BuffBinding());changed();Build();};rows.Controls.Add(add);End();}
}
sealed class ConnectionPanel:UserControl {
 readonly ComboBox mode=new WorkspaceComboBox{Name="SshDestination",Width=248};
 readonly TextBox host=new TextBox{Name="SshHost",MaxLength=253},user=new TextBox{Name="SshUser",MaxLength=64},
  port=new TextBox{Name="SshPort",MaxLength=5};
 readonly TableLayoutPanel details=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty};
 readonly Label editHint=new Label{Text="断开后可更改被控端",AutoSize=true,Visible=false,ForeColor=AppUi.Muted,Margin=new Padding(12,13,0,0)};
 bool loading;
 public bool Dirty {get;private set;}
 public ConnectionPanel(Action changed,Button connect,ComboBox targets,Button refresh,Button typeText){
  Dock=DockStyle.Fill;AutoScroll=true;BackColor=AppUi.Background;
  var page=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(18,0,18,18),MaximumSize=new Size(670,0)};
  var devices=Group("设备");
  Row(devices,"控制端",new Label{Text=Environment.MachineName,TextAlign=ContentAlignment.MiddleLeft});
  mode.Items.AddRange(new object[]{"电脑1 · 已配置","自定义 SSH"});
  Row(devices,"被控端",mode);
  Row(details,"主机名或 IP 地址",host);
  Row(details,"SSH 用户名",user);
  Row(details,"SSH 端口",port);
  details.Controls.Add(new Label{Text="用户名和端口可留空，沿用本机 OpenSSH 配置。",AutoSize=true,
   ForeColor=AppUi.Muted,Margin=new Padding(0,6,0,9)});
  devices.Controls.Add(details);
  var connectionActions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};
  connectionActions.Controls.AddRange(new Control[]{connect,editHint});devices.Controls.Add(connectionActions);
  page.Controls.Add(devices);
  var source=Group("画面与输入");
  Row(source,"预览窗口",targets);
  var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=Padding.Empty};
  actions.Controls.AddRange(new Control[]{refresh,typeText});source.Controls.Add(actions);
  page.Controls.Add(source);Controls.Add(page);AppUi.ApplyInputs(page);
  mode.SelectedIndexChanged+=delegate{details.Visible=mode.SelectedIndex==1;Edited(changed);};
  host.TextChanged+=delegate{Edited(changed);};user.TextChanged+=delegate{Edited(changed);};port.TextChanged+=delegate{Edited(changed);};
 }
 static AppleCard Group(string title){
  var group=new AppleCard{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(17,10,17,14),
   Margin=new Padding(0,0,0,14),BackColor=AppUi.Surface};
  group.Controls.Add(new Label{Text=title,Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold),
   ForeColor=AppUi.Text,Height=36,AutoSize=false,Dock=DockStyle.Top,TextAlign=ContentAlignment.MiddleLeft});
  return group;
 }
 static void Row(TableLayoutPanel group,string title,Control value){
  var row=new TableLayoutPanel{Width=490,Height=38,ColumnCount=2,Margin=Padding.Empty};
  row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,192));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  row.Controls.Add(new Label{Text=title,ForeColor=AppUi.Text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
  value.AccessibleName=title;value.Dock=DockStyle.Fill;value.Margin=new Padding(0,4,0,5);
  row.Controls.Add(value,1,0);group.Controls.Add(row);
 }
 void Edited(Action changed){if(!loading){Dirty=true;changed();}}
 public void LoadDestination(SshDestination destination){
  loading=true;
  mode.SelectedIndex=destination.custom?1:0;
  host.Text=destination.host??"";user.Text=destination.user??"";port.Text=destination.port==0?"":destination.port.ToString(CultureInfo.InvariantCulture);
  details.Visible=destination.custom;
  loading=Dirty=false;
 }
 public SshDestination ReadDestination(){
  int number=0;
  if(!String.IsNullOrWhiteSpace(port.Text)&&
   (!Int32.TryParse(port.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out number)||number<1||number>65535)){
   if(mode.SelectedIndex==1)throw new IOException("SSH 端口应为 1 到 65535，或留空");
   number=0;
  }
  var value=new SshDestination{custom=mode.SelectedIndex==1,host=host.Text.Trim(),user=user.Text.Trim(),port=number};
  value.Validate();
  return value;
 }
 public void AllowDeviceEdit(bool allowed){mode.Enabled=details.Enabled=allowed;editHint.Visible=!allowed;}
}
public sealed class RoleSelector:UserControl {
 public readonly ComboBox List=new WorkspaceComboBox{Width=206};public Action<string> Selected;public Action<string> Operation;bool updating;
 public RoleSelector(){Dock=DockStyle.Top;Height=58;Padding=new Padding(18,9,18,8);var rows=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};rows.Controls.Add(new Label{Text="角色预设",Width=88,Height=32,TextAlign=ContentAlignment.MiddleLeft,ForeColor=AppUi.Text});rows.Controls.Add(List);List.SelectedIndexChanged+=delegate{if(!updating&&Selected!=null&&List.SelectedItem is RoleItem)Selected(((RoleItem)List.SelectedItem).Id);};foreach(string label in new[]{"新建","复制","改名","删除"}){var b=new AppleButton{Text=label,Width=72,Height=30};AppUi.Button(b);b.Click+=delegate{if(Operation!=null)Operation(label);};rows.Controls.Add(b);}Controls.Add(rows);}
 public void Refresh(WorkspaceSettings settings){updating=true;List.Items.Clear();foreach(var r in settings.roles){var item=new RoleItem{Id=r.id,Name=r.name};List.Items.Add(item);if(r.id==settings.active)List.SelectedItem=item;}updating=false;}
 sealed class RoleItem {public string Id,Name;public override string ToString(){return Name;}}
 public static string Ask(Control owner,string title,string initial){using(var form=new AppleForm{Text=title,Size=new Size(360,186),MinimumSize=new Size(360,186),MaximumSize=new Size(360,186),AllowResize=false,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,Font=owner.Font}){form.InstallCaption(title,false);var input=new TextBox{Text=initial,Left=22,Top=62,Width=316,MaxLength=64};var okay=new AppleButton{Text="保存",DialogResult=DialogResult.OK,Left=250,Top=120,Width=88,Height=32};var cancel=new AppleButton{Text="取消",DialogResult=DialogResult.Cancel,Left=154,Top=120,Width=88,Height=32};AppUi.Button(okay,true);AppUi.Button(cancel);form.Controls.AddRange(new Control[]{input,okay,cancel});form.AcceptButton=okay;form.CancelButton=cancel;return form.ShowDialog(owner)==DialogResult.OK&&!String.IsNullOrWhiteSpace(input.Text)?input.Text.Trim():null;}}
 public static DialogResult ConfirmSave(Control owner,string role){using(var form=new AppleForm{Text="保存更改",Size=new Size(380,206),MinimumSize=new Size(380,206),MaximumSize=new Size(380,206),AllowResize=false,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,Font=owner.Font}){form.InstallCaption("保存更改",false);var text=new Label{Text="要保存对“"+role+"”的更改吗？",Left=22,Top=58,Width=336,Height=56,ForeColor=AppUi.Text};var discard=new AppleButton{Text="不保存",DialogResult=DialogResult.No,Left=22,Top=140,Width=94,Height=32};var cancel=new AppleButton{Text="取消",DialogResult=DialogResult.Cancel,Left=166,Top=140,Width=88,Height=32};var save=new AppleButton{Text="保存",DialogResult=DialogResult.Yes,Left=264,Top=140,Width=94,Height=32};AppUi.Button(discard);AppUi.Button(cancel);AppUi.Button(save,true);form.Controls.AddRange(new Control[]{text,discard,cancel,save});form.AcceptButton=save;form.CancelButton=cancel;return form.ShowDialog(owner);}}

}
}
