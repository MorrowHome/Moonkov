namespace Unity.MP_FPS.Client
{
    public sealed class QuickSlotPreferenceStore : IQuickSlotStore
    {
        #if UNITY_EDITOR
        // Installed before profile/HUD initialization by an isolated validation fixture.
        public static IQuickSlotStore ValidationStore { get; set; }
        public static string ValidationScope { get; set; }
#endif
        // Bindings live only in this raid. Never read or write legacy PlayerPrefs.
        public QuickSlotSave Load(string scope)
        {
            #if UNITY_EDITOR
            if (ValidationStore != null) return ValidationStore.Load(scope);
#endif
            return null;
        }
        public void Save(string scope, QuickSlotSave bindings)
        {
#if UNITY_EDITOR
            if (ValidationStore != null) { ValidationStore.Save(scope, bindings); return; }
#endif

        }
    }
}
