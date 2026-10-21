using System;
using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed class QuickSlotPreferenceStore : IQuickSlotStore
    {
        #if UNITY_EDITOR
        // Installed before profile/HUD initialization by an isolated validation fixture.
        public static IQuickSlotStore ValidationStore { get; set; }
        public static string ValidationScope { get; set; }
#endif
        private const string Prefix = "Moonkov.QuickSlots.v2.";
        public QuickSlotSave Load(string scope)
        {
            #if UNITY_EDITOR
            if (ValidationStore != null) return ValidationStore.Load(scope);
#endif
            var json = PlayerPrefs.GetString(Prefix + scope, "");
            if (json.Length == 0 || json.Length > 4096) return null;
            try { return JsonUtility.FromJson<QuickSlotSave>(json); }
            catch (ArgumentException) { return null; }
        }
        public void Save(string scope, QuickSlotSave bindings)
        {
#if UNITY_EDITOR
            if (ValidationStore != null) { ValidationStore.Save(scope, bindings); return; }
#endif
            PlayerPrefs.SetString(Prefix + scope, JsonUtility.ToJson(bindings)); PlayerPrefs.Save();
        }
    }
}
