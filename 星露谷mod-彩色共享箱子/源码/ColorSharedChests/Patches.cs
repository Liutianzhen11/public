using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Menus;
using Microsoft.Xna.Framework;
using StardewModdingAPI;

namespace ColorSharedChests
{
    internal static class AutomateIdentityPatch
    {
        internal static FieldInfo ChestField;
        public static void Postfix(object __instance,ref object __result)
        {
            if(ChestField?.GetValue(__instance) is Chest c&&ModEntry.Supported(c))
                __result=c.GetItemsForPlayer();
        }
    }
    [HarmonyPatch(typeof(Chest),nameof(Chest.performToolAction))]
    internal static class MoveToolPatch
    {
        private static bool Prefix(Chest __instance,Tool t,ref bool __result)
        {
            if(!Context.IsWorldReady || !ModEntry.Supported(__instance) ||
                !ModEntry.OwnGroup(__instance.GlobalInventoryId) || t==null ||
                t is StardewValley.Tools.MeleeWeapon || !t.isHeavyHitter())return true;
            if(t.getLastFarmerToUse()==Game1.player)ModEntry.Instance.QueueMove(__instance);
            __result=false;return false;
        }
    }
    [HarmonyPatch(typeof(Chest),nameof(Chest.placementAction))]
    internal static class PlaceCarriedChestPatch
    {
        private static void Prefix(Chest __instance,out List<Item> __state)
        {
            __state=null;
            if(!ModEntry.Supported(__instance)||!string.IsNullOrEmpty(__instance.GlobalInventoryId)||!__instance.Items.HasAny())return;
            __state=__instance.Items.Where(i=>i!=null).ToList();
            __instance.Items.Clear();
        }
        private static void Postfix(Chest __instance,GameLocation location,int x,int y,bool __result,List<Item> __state)
        {
            if(__state==null)return;
            if(__result&&location.Objects.TryGetValue(new Vector2(x/64,y/64),out var placed)&&placed is Chest c)
            {
                // Vanilla clones the carried object during placement. The payload
                // was removed before cloning and restored exactly once here.
                c.Items.AddRange(__state);
                foreach(var key in new[]{ModEntry.IdKey,ModEntry.OrderKey,ModEntry.NameKey})
                    if(__instance.modData.TryGetValue(key,out string value))c.modData[key]=value;
            }
            else __instance.Items.AddRange(__state);
        }
        private static Exception Finalizer(Chest __instance,Exception __exception,List<Item> __state)
        {
            if(__exception!=null&&__state!=null&&!__instance.Items.HasAny())__instance.Items.AddRange(__state);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Chest),nameof(Chest.GetActualCapacity))]
    internal static class CapacityPatch
    {
        private static void Postfix(Chest __instance,ref int __result)
        {
            if(!ModEntry.ReadingBaseCapacity)__result=ModEntry.Instance.Capacity(__instance,__result);
        }
    }
    [HarmonyPatch]
    internal static class MenuPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()=>typeof(ItemGrabMenu).GetConstructors().Where(c=>c.GetParameters().Any(p=>p.Name=="context"));
        private static void Prefix(object context,out Chest __state)
        {
            __state=ModEntry.ConstructingMenu;
            ModEntry.ConstructingMenu=context is Chest c&&ModEntry.Supported(c)?c:null;
        }
        private static void Postfix(ItemGrabMenu __instance,Chest __state)
        {
            ModEntry.ConstructingMenu=__state;
            if(__instance.context is Chest c)ModEntry.Instance.ConfigurePage(__instance,c);
        }
        private static Exception Finalizer(Exception __exception,Chest __state)
        {ModEntry.ConstructingMenu=__state;return __exception;}
    }
    // A view onto the same inventory, never a copied item list. Bounds on the
    // last page prevent adding items to slots beyond the authoritative capacity.
    internal sealed class InventoryWindow : IList<Item>
    {
        private readonly IList<Item> source;
        private readonly int offset;
        public int Count {get;}
        public bool IsReadOnly=>false;
        public InventoryWindow(IList<Item> source,int offset,int count){this.source=source;this.offset=offset;Count=count;}
        public Item this[int index]
        {
            get{if(index<0||index>=Count)throw new ArgumentOutOfRangeException();return offset+index<source.Count?source[offset+index]:null;}
            set{if(index<0||index>=Count)throw new ArgumentOutOfRangeException();while(source.Count<=offset+index)source.Add(null);source[offset+index]=value;}
        }
        public void Add(Item item){for(int i=0;i<Count;i++)if(this[i]==null){this[i]=item;return;}throw new InvalidOperationException("Page is full");}
        public void Clear(){for(int i=0;i<Count;i++)this[i]=null;}
        public bool Contains(Item item)=>IndexOf(item)>=0;
        public int IndexOf(Item item){for(int i=0;i<Count;i++)if(ReferenceEquals(this[i],item))return i;return -1;}
        public void CopyTo(Item[] array,int start){for(int i=0;i<Count;i++)array[start+i]=this[i];}
        public IEnumerator<Item> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        public bool Remove(Item item){int i=IndexOf(item);if(i<0)return false;this[i]=null;return true;}
        public void RemoveAt(int index){this[index]=null;}
        public void Insert(int index,Item item){if(this[index]!=null)throw new InvalidOperationException("Slot occupied");this[index]=item;}
    }
}
