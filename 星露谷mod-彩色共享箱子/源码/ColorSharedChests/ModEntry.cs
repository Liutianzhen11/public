using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;

namespace ColorSharedChests
{
    public sealed class ModEntry : Mod
    {
        internal const string Prefix = "Local.ColorSharedChests/";
        internal const string IdKey = Prefix + "Id";
        internal const string CapacityKey = Prefix + "Capacity";
        internal const string OrderKey = Prefix + "Order";
        internal const string NameKey = "Pathoschild.ChestsAnywhere/Name";
        internal static ModEntry Instance;
        internal static bool ReadingBaseCapacity;
        internal static Chest ConstructingMenu;
        internal readonly Dictionary<string,int> Pages = new Dictionary<string,int>();
        private ModConfig Config;
        internal ModConfig Effective;
        private readonly Dictionary<string,Placed> known = new Dictionary<string,Placed>();
        private readonly Dictionary<string,long> actors = new Dictionary<string,long>();
        private readonly Queue<Tuple<ActionRequest,long>> requests = new Queue<Tuple<ActionRequest,long>>();
        private long sequence;
        private bool reconciling;
        private bool recoveryLogged;
        private IGenericModConfigMenuApi gmcm;
        private static string T(string zh,string en) => LocalizedContentManager.CurrentLanguageCode==LocalizedContentManager.LanguageCode.zh?zh:en;

        private sealed class Placed
        {
            public Chest Chest;
            public GameLocation Location;
            public Vector2 Tile;
        }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            Config = helper.ReadConfig<ModConfig>();
            Normalize(Config);
            Effective = Copy(Config);
            new Harmony(ModManifest.UniqueID).PatchAll(typeof(ModEntry).Assembly);
            helper.Events.GameLoop.GameLaunched += OnLaunched;
            helper.Events.GameLoop.SaveLoaded += OnLoaded;
            helper.Events.GameLoop.ReturnedToTitle += (s,e) => { known.Clear(); Pages.Clear(); actors.Clear(); requests.Clear(); sequence=0; Effective=Copy(Config); };
            helper.Events.GameLoop.UpdateTicked += OnTick;
            helper.Events.Multiplayer.PeerConnected += (s,e) => { if(Context.IsMainPlayer) SendSettings(new[]{e.Peer.PlayerID}); };
            helper.Events.Multiplayer.ModMessageReceived += OnMessage;
            helper.Events.Display.MenuChanged += OnMenu;
            helper.Events.Input.ButtonPressed += OnButton;
            helper.Events.Input.MouseWheelScrolled += (s,e)=>{
                if(Context.IsWorldReady && Game1.activeClickableMenu is ItemGrabMenu menu)Scroll(menu,e.Delta);
            };
            helper.Events.Display.RenderedActiveMenu += OnDrawMenu;
            helper.ConsoleCommands.Add("colorchests_status", T("显示共享组、容量和物品数量。","Show shared inventories and capacity settings."), (c,a)=>Status());
            helper.ConsoleCommands.Add("colorchests_export", T("房主将所有共享组库存导出到普通箱，供卸载前使用。","Host: export shared items to chests before uninstalling."), (c,a)=>{
                if(!Context.IsWorldReady || !Context.IsMainPlayer) return;
                Config.EnableSharing=false; Effective=Copy(Config); Helper.WriteConfig(Config); SendSettings(); Reconcile();
            });
            Monitor.Log(T("彩色共享箱子 ","Color Shared Chests ")+ModManifest.Version+T(" 已加载：名称栏右侧按钮重命名；滚轮浏览箱子；Ctrl+右键搬箱。"," loaded: Rename beside the name bar; mouse wheel to scroll; Ctrl+right-click pick up."), LogLevel.Info);
        }

        private static ModConfig Copy(ModConfig c) => new ModConfig { EnableCapacity=c.EnableCapacity, EnableSharing=c.EnableSharing, ExtraRows=c.ExtraRows, SharedColors=new List<int>(c.SharedColors) };
        private static void Normalize(ModConfig c)
        {
            c.ExtraRows=Math.Clamp(c.ExtraRows,0,10);
            c.SharedColors=(c.SharedColors ?? new List<int>()).Where(i=>i>=0&&i<=20).Distinct().ToList();
        }
        private bool CanConfigure => !Context.IsWorldReady || Context.IsMainPlayer;

        private void OnLaunched(object sender, GameLaunchedEventArgs e)
        {
            Type automate=AccessTools.TypeByName("Pathoschild.Stardew.Automate.Framework.Storage.ChestContainer");
            if(automate!=null)
            {
                new Harmony(ModManifest.UniqueID).Patch(AccessTools.PropertyGetter(automate,"InventoryReferenceId"),
                    postfix:new HarmonyMethod(typeof(AutomateIdentityPatch),nameof(AutomateIdentityPatch.Postfix)));
                AutomateIdentityPatch.ChestField=AccessTools.Field(automate,"Chest");
                Monitor.Log(T("Automate 共享库存标识适配已注册。","Automate shared inventory identity integration registered."),LogLevel.Info);
            }
            gmcm=Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if(gmcm==null) { Monitor.Log(T("通用模组配置菜单未安装，可编辑 config.json 设置。","Generic Mod Config Menu is not installed; edit config.json instead."),LogLevel.Info); return; }
            gmcm.Register(ModManifest, ()=>{if(CanConfigure) Config=new ModConfig();}, ()=>{
                if(!CanConfigure) { Game1.addHUDMessage(new HUDMessage(T("仅房主可以修改全局设置。","Only the host can change global settings."),3)); return; }
                Normalize(Config); Helper.WriteConfig(Config); Effective=Copy(Config);
                if(Context.IsWorldReady) { SendSettings(); Reconcile(); }
            });
            gmcm.AddSectionTitle(ModManifest, ()=>T("容量与共享（联机由房主管理）","Capacity and sharing (host-managed)"),()=>T("更改后会等待所有箱子菜单关闭，再由房主迁移物品。额外排数每排增加12格。默认0排。","Changes wait for all chest menus to close. Each extra row adds 12 slots. Default: 0 rows."));
            gmcm.AddBoolOption(ModManifest,()=>Config.EnableCapacity,v=>{if(CanConfigure)Config.EnableCapacity=v;},()=>T("启用容量修改","Enable capacity changes"),()=>T("关闭时使用箱子原版容量；开启时增加下面配置的排数。","Off: original capacity. On: add the extra rows configured below."));
            gmcm.AddBoolOption(ModManifest,()=>Config.EnableSharing,v=>{if(CanConfigure)Config.EnableSharing=v;},()=>T("启用物品共享","Enable item sharing"),()=>T("同色组容量取最大单箱容量。关闭后优先导出至该组最早加入的现存箱子。","A color group uses its largest single chest capacity. Disabling exports items to its oldest remaining chest."));
            gmcm.AddNumberOption(ModManifest,()=>Config.ExtraRows,v=>{if(CanConfigure)Config.ExtraRows=Math.Clamp(v,0,10);},()=>T("额外排数（每排12格，最多10排）","Extra rows (12 slots per row, max 10)"),()=>T("所有箱型增加相同排数，最多10排，即额外120格。鼠标放在箱子物品区滚动查看。","All chest types add the same rows, up to 10 (120 extra slots). Hover the chest inventory and scroll."),0,10,1);
            gmcm.AddSectionTitle(ModManifest,()=>T("参与共享的原版颜色","Colors enabled for sharing"),()=>T("每行一个颜色，勾选表示参与共享；默认未染色不共享。","One color per row. Check the box to enable sharing; undyed chests are independent by default."));
            for(int colorIndex=0;colorIndex<=20;colorIndex++)
            {
            int selection=colorIndex;
            bool colorMouseDown=false;
            gmcm.AddComplexOption(ModManifest,()=>"",(batch,pos)=>{
                bool down=Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton==Microsoft.Xna.Framework.Input.ButtonState.Pressed;
                int mx=Game1.getMouseX(),my=Game1.getMouseY();
                var swatch=new Rectangle((int)pos.X,(int)pos.Y+4,48,36);
                batch.Draw(Game1.staminaRect,swatch,new Color(90,55,25));
                batch.Draw(Game1.staminaRect,new Rectangle(swatch.X+3,swatch.Y+3,42,30),selection==0?new Color(170,110,55):DiscreteColorPicker.getColorFromSelection(selection));
                if(selection==0)batch.DrawString(Game1.smallFont,T("未染色","Undyed"),new Vector2(pos.X+64,pos.Y+8),Game1.textColor);
                var check=new Rectangle((int)pos.X+300,(int)pos.Y+4,36,36);
                batch.Draw(Game1.mouseCursors,check,Config.SharedColors.Contains(selection)?OptionsCheckbox.sourceRectChecked:OptionsCheckbox.sourceRectUnchecked,Color.White);
                if(check.Contains(mx,my)&&down&&!colorMouseDown&&CanConfigure)
                {if(!Config.SharedColors.Remove(selection))Config.SharedColors.Add(selection);Game1.playSound("drumkit6");}
                colorMouseDown=down;
            },()=>T("勾选：该颜色参与共享；未勾选：该颜色不共享。","Checked: this color shares items. Unchecked: independent inventories."),beforeMenuOpened:()=>colorMouseDown=false,height:()=>52,fieldId:"color-"+selection);
            }
            Monitor.Log(T("中英文配置菜单已注册，跟随游戏语言。","Bilingual configuration menu registered; follows game language."),LogLevel.Info);
        }

        internal static bool Supported(Chest c) => c!=null && c.playerChest.Value && !c.fridge.Value && !c.giftbox.Value &&
            (c.ItemId=="130" || c.ItemId=="232" || c.ItemId=="BigChest" || c.ItemId=="BigStoneChest");
        private string Desired(Chest c)
        {
            if(!Effective.EnableSharing)return null;
            int i=DiscreteColorPicker.getSelectionFromColor(c.playerChoiceColor.Value);
            return Effective.SharedColors.Contains(i)?Prefix+"Color/"+c.playerChoiceColor.Value.PackedValue.ToString("X8"):null;
        }
        internal static bool OwnGroup(string id) => id!=null && id.StartsWith(Prefix+"Color/",StringComparison.Ordinal);
        private int BaseCapacity(Chest c)
        {
            ReadingBaseCapacity=true;
            try{return c.GetActualCapacity();}finally{ReadingBaseCapacity=false;}
        }
        private int SingleCapacity(Chest c) => BaseCapacity(c)+(Effective.EnableCapacity?Effective.ExtraRows*12:0);
        internal int Capacity(Chest c, int vanilla)
        {
            if(!Context.IsWorldReady || !Supported(c))return vanilla;
            int cap=vanilla+(Effective.EnableCapacity?Effective.ExtraRows*12:0);
            if(c.modData.TryGetValue(CapacityKey,out string value)&&int.TryParse(value,out int shared))cap=shared;
            if(ConstructingMenu!=null && ReferenceEquals(ConstructingMenu,c))cap=Math.Min(cap,36);
            return cap;
        }
        private List<Placed> Scan()
        {
            var result=new List<Placed>();
            Utility.ForEachLocation(location=>{
                foreach(var pair in location.Objects.Pairs.ToArray())
                    if(pair.Value is Chest c && Supported(c))result.Add(new Placed{Chest=c,Location=location,Tile=pair.Key});
                return true;
            },true,true);
            return result;
        }
        private string EnsureId(Chest c)
        {
            if(!c.modData.TryGetValue(IdKey,out string id)||string.IsNullOrEmpty(id))
            { id=Guid.NewGuid().ToString("N"); c.modData[IdKey]=id; }
            if(c.modData.TryGetValue(OrderKey,out string order)&&long.TryParse(order,out long n))sequence=Math.Max(sequence,n);
            else c.modData[OrderKey]=(++sequence).ToString();
            return id;
        }
        private void OnLoaded(object sender,SaveLoadedEventArgs e)
        {
            known.Clear(); Pages.Clear(); actors.Clear(); requests.Clear(); recoveryLogged=false;
            if(Context.IsMainPlayer){Effective=Copy(Config); Reconcile(); SendSettings();}
            else Helper.Multiplayer.SendMessage(new ActionRequest{Action="settings"},"request",new[]{ModManifest.UniqueID},new[]{Game1.MasterPlayer.UniqueMultiplayerID});
            Monitor.Log(T("存档已加载；共享库存存放在游戏原生联机库存中。","Save loaded; shared items use the game's native multiplayer inventory."),LogLevel.Info);
        }
        private void OnTick(object sender,UpdateTickedEventArgs e)
        {
            if(!Context.IsWorldReady||!Context.IsMainPlayer||!e.IsMultipleOf(60))return;
            try
            {
                if(requests.Count>0)
                {
                    var r=requests.Peek(); if(ApplyRequest(r.Item1,r.Item2))requests.Dequeue();
                }
                Reconcile();
            }
            catch(Exception ex){Monitor.Log(T("库存迁移发生错误，请检查日志：","Inventory migration failed; check the log: ")+ex,LogLevel.Error);}
        }
        private void SendSettings(long[] players=null)=>Helper.Multiplayer.SendMessage(Effective,"settings",new[]{ModManifest.UniqueID},players);
        private void OnMessage(object sender,ModMessageReceivedEventArgs e)
        {
            if(e.FromModID!=ModManifest.UniqueID)return;
            if(e.Type=="settings"&&!Context.IsMainPlayer&&e.FromPlayerID==Game1.MasterPlayer.UniqueMultiplayerID)
            { Effective=e.ReadAs<ModConfig>();Normalize(Effective);return; }
            if(e.Type=="request"&&Context.IsMainPlayer)
            {
                ActionRequest r=e.ReadAs<ActionRequest>();
                if(r.Action=="settings"){SendSettings(new[]{e.FromPlayerID});return;}
                if(r.ChestId!=null) {actors[r.ChestId]=e.FromPlayerID;requests.Enqueue(Tuple.Create(r,e.FromPlayerID));}
            }
        }
        private void Request(string action,Chest c,string name=null)
        {
            if(!c.modData.TryGetValue(IdKey,out string id)) { Game1.addHUDMessage(new HUDMessage(T("等待房主同步箱子后再操作。","Wait for the host to synchronize this chest."),3)); return; }
            var r=new ActionRequest{Action=action,ChestId=id,Name=name};
            if(Context.IsMainPlayer){actors[id]=Game1.player.UniqueMultiplayerID;requests.Enqueue(Tuple.Create(r,Game1.player.UniqueMultiplayerID));}
            else Helper.Multiplayer.SendMessage(r,"request",new[]{ModManifest.UniqueID},new[]{Game1.MasterPlayer.UniqueMultiplayerID});
        }
        private Farmer Actor(Chest c)
        {
            if(c.modData.TryGetValue(IdKey,out string id)&&actors.TryGetValue(id,out long player))return Game1.GetPlayer(player,onlyOnline:true)??Game1.MasterPlayer;
            return Game1.MasterPlayer;
        }
        private bool ApplyRequest(ActionRequest r,long playerId)
        {
            Placed p=Scan().FirstOrDefault(x=>x.Chest.modData.TryGetValue(IdKey,out string id)&&id==r.ChestId);
            if(p==null)return true;
            if(r.Action=="hint")return true;
            if(r.Action=="rename")
            {
                string name=(r.Name??"").Trim();if(name.Length>80)name=name.Substring(0,80);
                if(name.Length==0)p.Chest.modData.Remove(NameKey);else p.Chest.modData[NameKey]=name;
                return true;
            }
            if(r.Action=="move")
            {
                var farmer=Game1.GetPlayer(playerId,onlyOnline:true);
                if(farmer==null||farmer.currentLocation!=p.Location||Vector2.Distance(farmer.Tile,p.Tile)>3f)return true;
                if(p.Chest.GetMutex().IsLocked())return false;
                var rest=Scan().Where(x=>!ReferenceEquals(x.Chest,p.Chest)&&x.Chest.GlobalInventoryId==p.Chest.GlobalInventoryId).ToList();
                string group=p.Chest.GlobalInventoryId;
                if(OwnGroup(group))
                {
                    p.Chest.GlobalInventoryId=null;p.Chest.modData.Remove(CapacityKey);
                    if(rest.Count==0)TransferAll(Game1.player.team.GetOrCreateGlobalInventory(group),p.Chest.Items,SingleCapacity(p.Chest),p,farmer);
                }
                p.Location.Objects.Remove(p.Tile);
                if(!farmer.addItemToInventoryBool(p.Chest))Game1.createItemDebris(p.Chest,p.Tile*64f+new Vector2(32,32),-1,p.Location);
                known.Remove(r.ChestId);
                if(OwnGroup(group)&&rest.Count>0)
                {
                    int cap=rest.Max(x=>SingleCapacity(x.Chest));
                    foreach(var x in rest)x.Chest.modData[CapacityKey]=cap.ToString();
                    Trim(Game1.player.team.GetOrCreateGlobalInventory(group),cap,p,farmer);
                }
                return true;
            }
            return true;
        }

        private void Reconcile()
        {
            if(reconciling||!Context.IsWorldReady||!Context.IsMainPlayer)return;
            var live=Scan();
            // Migrations wait until every affected native mutex is free. Game
            // update and Automate execute on this same thread, so a migration is
            // one uninterrupted operation between their update callbacks.
            if(live.Any(p=>p.Chest.GetMutex().IsLocked())||known.Values.Any(p=>p.Chest.GetMutex().IsLocked()))return;
            reconciling=true;
            try
            {
                var ids=new HashSet<string>();
                foreach(var p in live)
                {
                    string id=EnsureId(p.Chest);
                    if(!ids.Add(id)){p.Chest.modData.Remove(IdKey);id=EnsureId(p.Chest);ids.Add(id);}
                }
                // First calculate the target groups. All joins have a known final
                // capacity before local inventories are merged into a shared one.
                var destinations=live.Where(p=>string.IsNullOrEmpty(p.Chest.GlobalInventoryId)||OwnGroup(p.Chest.GlobalInventoryId))
                    .Select(p=>new{Place=p,Group=Desired(p.Chest)}).ToList();
                var groups=destinations.Where(x=>x.Group!=null).GroupBy(x=>x.Group)
                    .ToDictionary(g=>g.Key,g=>g.Select(x=>x.Place).ToList());
                var oldGroups=live.Where(p=>OwnGroup(p.Chest.GlobalInventoryId)).GroupBy(p=>p.Chest.GlobalInventoryId)
                    .ToDictionary(g=>g.Key,g=>g.ToList());
                foreach(var old in known.Values)
                    if(OwnGroup(old.Chest.GlobalInventoryId)&&!oldGroups.ContainsKey(old.Chest.GlobalInventoryId))oldGroups[old.Chest.GlobalInventoryId]=new List<Placed>{old};
                foreach(var old in oldGroups)
                {
                    var inventory=Game1.player.team.GetOrCreateGlobalInventory(old.Key);
                    if(groups.TryGetValue(old.Key,out var members))
                    {
                        int cap=members.Max(p=>SingleCapacity(p.Chest));
                        Placed source=old.Value.FirstOrDefault(p=>Desired(p.Chest)!=old.Key)??members[0];
                        Trim(inventory,cap,source,Actor(source.Chest));
                    }
                    else
                    {
                        bool recolored=Effective.EnableSharing && old.Value.Any(p=>live.Any(x=>ReferenceEquals(x.Chest,p.Chest)) && Desired(p.Chest)!=old.Key)
                            && Effective.SharedColors.Any(i=>Prefix+"Color/"+DiscreteColorPicker.getColorFromSelection(i).PackedValue.ToString("X8")==old.Key);
                        if(recolored)
                        {
                            Game1.MasterPlayer.modData[Prefix+"Reserved/"+old.Key.Substring((Prefix+"Color/").Length)]=old.Value[0].Chest.modData[IdKey];
                            continue;
                        }
                        // With no remaining member, retain the items in the moved
                        // chest or the oldest surviving anchor when sharing is off.
                        Placed target=old.Value.OrderBy(p=>long.Parse(p.Chest.modData[OrderKey])).First();
                        var actor=Actor(target.Chest);
                        target.Chest.GlobalInventoryId=null;target.Chest.modData.Remove(CapacityKey);
                        TransferAll(inventory,target.Chest.Items,SingleCapacity(target.Chest),target,actor);
                        if(!live.Any(p=>ReferenceEquals(p.Chest,target.Chest)))
                        {
                            // A third-party removal must not leave inaccessible
                            // local items on a destroyed in-memory chest.
                            bool exists=false;
                            Utility.ForEachItem(item=>{if(ReferenceEquals(item,target.Chest))exists=true;return true;});
                            if(!exists)TransferAll(target.Chest.Items,new Inventory(),0,target,actor);
                        }
                    }
                }
                foreach(var group in groups)
                {
                    var place=group.Value[0];
                    Trim(Game1.player.team.GetOrCreateGlobalInventory(group.Key),group.Value.Max(p=>SingleCapacity(p.Chest)),place,Actor(place.Chest));
                }
                foreach(var d in destinations)
                {
                    Chest c=d.Place.Chest;
                    if(d.Group==null)
                    {
                        if(OwnGroup(c.GlobalInventoryId))c.GlobalInventoryId=null;
                        c.modData.Remove(CapacityKey);
                        Trim(c.Items,SingleCapacity(c),d.Place,Actor(c));
                        continue;
                    }
                    int cap=groups[d.Group].Max(p=>SingleCapacity(p.Chest));
                    Game1.MasterPlayer.modData.Remove(Prefix+"Reserved/"+d.Group.Substring((Prefix+"Color/").Length));
                    if(c.GlobalInventoryId!=d.Group)
                    {
                        c.GlobalInventoryId=d.Group;
                        c.modData[OrderKey]=(++sequence).ToString();
                    }
                    c.modData[CapacityKey]=cap.ToString();
                    if(c.Items.HasAny())TransferAll(c.Items,Game1.player.team.GetOrCreateGlobalInventory(d.Group),cap,d.Place,Actor(c));
                }
                known.Clear(); foreach(var p in live)known[p.Chest.modData[IdKey]]=p;
                // Recover an orphaned saved group rather than silently abandoning
                // it after a third-party chest deletion between game sessions.
                foreach(var key in Game1.player.team.globalInventories.Keys.ToArray())
                    if(OwnGroup(key)&&!groups.ContainsKey(key))
                    {
                        var inv=Game1.player.team.GetOrCreateGlobalInventory(key);
                        if(!inv.HasAny())continue;
                        string reserveKey=Prefix+"Reserved/"+key.Substring((Prefix+"Color/").Length);
                        bool reserved=Game1.MasterPlayer.modData.TryGetValue(reserveKey,out string anchor);
                        if(reserved && Effective.EnableSharing && Effective.SharedColors.Any(i=>Prefix+"Color/"+DiscreteColorPicker.getColorFromSelection(i).PackedValue.ToString("X8")==key))continue;
                        var target=live.FirstOrDefault(p=>p.Chest.modData[IdKey]==anchor)??live.OrderBy(p=>long.Parse(p.Chest.modData[OrderKey])).FirstOrDefault();
                        if(target!=null)
                        {
                            TransferAll(inv,target.Chest.GetItemsForPlayer(),target.Chest.GetActualCapacity(),target,Game1.MasterPlayer);
                            Game1.MasterPlayer.modData.Remove(reserveKey);
                        }
                        else if(!recoveryLogged){Monitor.Log(T("发现没有箱子入口的保留库存。放置一个普通箱后自动恢复。","Reserved inventory has no chest access. Place a regular chest to recover it."),LogLevel.Warn);recoveryLogged=true;}
                    }
            }
            finally{reconciling=false;}
        }

        private void TransferAll(IInventory source,IInventory target,int cap,Placed where,Farmer actor)
        {
            if(ReferenceEquals(source,target))return;
            for(int i=source.Count-1;i>=0;i--)
            {
                Item item=source[i];if(item==null)continue;
                source[i]=null;
                try
                {
                    Item excess=Insert(target,item,cap);
                    if(excess!=null)Overflow(excess,where,actor);
                }
                catch
                {
                    if(item.Stack>0&&!target.Contains(item))source[i]=item;
                    throw;
                }
            }
            source.RemoveEmptySlots();
        }
        internal static Item Insert(IList<Item> target,Item item,int capacity)
        {
            for(int i=0;i<Math.Min(target.Count,capacity);i++)
                if(target[i]!=null && target[i].canStackWith(item))
                { item.Stack=target[i].addToStack(item);if(item.Stack<=0)return null; }
            for(int i=0;i<capacity;i++)
            {
                if(i>=target.Count){target.Add(item);return null;}
                if(target[i]==null){target[i]=item;return null;}
            }
            return item;
        }
        private void Trim(IInventory inv,int cap,Placed where,Farmer actor)
        {
            inv.RemoveEmptySlots();
            while(inv.Count>cap)
            {
                Item item=inv[inv.Count-1];inv.RemoveAt(inv.Count-1);
                if(item!=null)
                {
                    try{Overflow(item,where,actor);}
                    catch{if(item.Stack>0)inv.Add(item);throw;}
                }
            }
        }
        private void Overflow(Item item,Placed p,Farmer farmer)
        {
            Item remainder=farmer.addItemToInventory(item);
            if(remainder!=null)Game1.createItemDebris(remainder,p.Tile*64f+new Vector2(32,32),-1,p.Location);
        }
        private Chest CurrentChest() => (Game1.activeClickableMenu as ItemGrabMenu)?.context as Chest;
        private void OnMenu(object sender,MenuChangedEventArgs e)
        {
            if(e.OldMenu is ItemGrabMenu old && old.context is Chest c && Supported(c))Request("hint",c);
            if(e.NewMenu is ItemGrabMenu menu && menu.context is Chest chest && Supported(chest))ConfigurePage(menu,chest);
        }
        private void OnButton(object sender,ButtonPressedEventArgs e)
        {
            if(!Context.IsWorldReady)return;
            Chest c=CurrentChest();
            bool ctrl=Helper.Input.IsDown(SButton.LeftControl)||Helper.Input.IsDown(SButton.RightControl);
            if(Supported(c)&&Game1.activeClickableMenu is ItemGrabMenu grab&&grab.heldItem==null &&
                ((ctrl&&e.Button==SButton.N)||(e.Button==SButton.MouseLeft&&RenameBounds(grab).Contains(Game1.getMouseX(),Game1.getMouseY()))))
            {
                Helper.Input.Suppress(e.Button);
                string name=c.modData.TryGetValue(NameKey,out string existing)?existing:"";
                Game1.exitActiveMenu();
                Game1.activeClickableMenu=new NamingMenu(n=>{Request("rename",c,n);Game1.exitActiveMenu();},T("箱子名称（留空恢复默认名称）","Chest name (leave blank for default)"),name);
                return;
            }
            if(ctrl&&e.Button==SButton.MouseRight&&Game1.activeClickableMenu==null)
            {
                if(Game1.currentLocation.Objects.TryGetValue(e.Cursor.GrabTile,out var o)&&o is Chest chest&&Supported(chest))
                {Helper.Input.Suppress(e.Button);Request("move",chest);}
            }
        }
        internal void ConfigurePage(ItemGrabMenu menu,Chest c)
        {
            if(menu.ItemsToGrabMenu==null||!Supported(c))return;
            int cap=c.GetActualCapacity(),slots=menu.ItemsToGrabMenu.inventory.Count;
            if(slots<1)return;
            string id=c.modData.TryGetValue(IdKey,out string s)?s:"local";
            int columns=Math.Max(1,slots/Math.Max(1,menu.ItemsToGrabMenu.rows));
            int max=Math.Max(0,(cap-slots+columns-1)/columns);
            int page=Pages.TryGetValue(id,out int n)?Math.Clamp(n,0,max):0;
            Pages[id]=page;
            menu.ItemsToGrabMenu.actualInventory=new InventoryWindow(c.GetItemsForPlayer(),page*columns,Math.Min(slots,cap-page*columns));
        }
        internal bool Scroll(ItemGrabMenu menu,int direction)
        {
            if(!(menu.context is Chest c)||!Supported(c))return false;
            if(menu.heldItem!=null)return true;
            var grid=menu.ItemsToGrabMenu;
            if(grid==null || grid.inventory.Count==0)return false;
            var first=grid.inventory[0].bounds;
            var last=grid.inventory[grid.inventory.Count-1].bounds;
            if(!new Rectangle(first.X,first.Y,last.Right-first.X,last.Bottom-first.Y).Contains(Game1.getMouseX(),Game1.getMouseY()))return false;
            string id=c.modData.TryGetValue(IdKey,out string s)?s:"local";
            int row=Pages.TryGetValue(id,out int n)?n:0;
            Pages[id]=Math.Max(0,row+(direction<0?1:-1));
            ConfigurePage(menu,c);
            return true;
        }
        private static Rectangle RenameBounds(ItemGrabMenu menu)
        {
            var first=menu.ItemsToGrabMenu.inventory[0].bounds;
            int x=first.X+220;
            int y=first.Y-76;
            return new Rectangle(Math.Clamp(x,0,Math.Max(0,Game1.uiViewport.Width-136)),Math.Max(0,y),128,40);
        }
        internal void QueueMove(Chest c)=>Request("move",c);
        private void OnDrawMenu(object sender,RenderedActiveMenuEventArgs e)
        {
            Chest c=CurrentChest(); if(!Supported(c))return;
            var menu=(ItemGrabMenu)Game1.activeClickableMenu;
            // Other menu overlays can replace the inventory after construction.
            // Restore the authoritative scrolling view without copying any items.
            ConfigurePage(menu,c);
            var rect=RenameBounds(menu);
            bool hover=rect.Contains(Game1.getMouseX(),Game1.getMouseY());
            IClickableMenu.drawTextureBox(e.SpriteBatch,Game1.mouseCursors,new Rectangle(432,439,9,9),rect.X,rect.Y,rect.Width,rect.Height,hover?new Color(255,245,210):Color.White,4f,false);
            string label=T("重命名","Rename");
            var size=Game1.smallFont.MeasureString(label);
            e.SpriteBatch.DrawString(Game1.smallFont,label,new Vector2(rect.Center.X-size.X/2,rect.Center.Y-size.Y/2),new Color(70,40,20));
        }
        private void Status()
        {
            if(!Context.IsWorldReady){Monitor.Log(T("请先进入存档。","Load a save first."),LogLevel.Info);return;}
            foreach(var key in Game1.player.team.globalInventories.Keys.Where(OwnGroup))
                Monitor.Log(key+": "+Game1.player.team.GetOrCreateGlobalInventory(key).Count+T("个库存槽（含可能的空槽）"," inventory slots (including empty slots)"),LogLevel.Info);
            Monitor.Log(T("容量开关=","Capacity enabled=")+Effective.EnableCapacity+T("，额外排数=",", extra rows=")+Effective.ExtraRows+T("，共享开关=",", sharing enabled=")+Effective.EnableSharing,LogLevel.Info);
        }
    }
}
