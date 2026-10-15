using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace ColorSharedChests
{
    public sealed class ModConfig
    {
        public bool EnableCapacity { get; set; } = false;
        public bool EnableSharing { get; set; } = true;
        public int ExtraRows { get; set; } = 0;
        public List<int> SharedColors { get; set; } = new List<int> {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20};
    }
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddComplexOption(IManifest mod, Func<string> name, Action<Microsoft.Xna.Framework.Graphics.SpriteBatch,Microsoft.Xna.Framework.Vector2> draw, Func<string> tooltip = null, Action beforeMenuOpened = null, Action beforeSave = null, Action afterSave = null, Action beforeReset = null, Action afterReset = null, Action beforeMenuClosed = null, Func<int> height = null, string fieldId = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int,string> formatValue = null, string fieldId = null);
    }
    public sealed class ActionRequest
    {
        public string Action { get; set; }
        public string ChestId { get; set; }
        public string Name { get; set; }
    }
}
