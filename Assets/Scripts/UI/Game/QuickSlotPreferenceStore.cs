// 给快捷栏的存取接口。正式运行时不保存到 PlayerPrefs，每局的绑定只放在内存。编辑器测试可注入一份临时存储。
namespace Unity.MP_FPS.Client {
    public sealed class QuickSlotPreferenceStore : IQuickSlotStore {
#if UNITY_EDITOR
        // Installed before profile/HUD initialization by an isolated validation fixture.
        public static IQuickSlotStore ValidationStore { get; set; }
        public static string ValidationScope { get; set; }
#endif
        // Bindings live only in this raid. Never read or write legacy PlayerPrefs.
        public QuickSlotSave Load(string scope) {
#if UNITY_EDITOR
            // 编辑器隔离检查可替换读写接口，方便用假的档案范围验证快捷栏逻辑。正式运行走下面的空初始状态。
            if (ValidationStore != null) return ValidationStore.Load(scope);
#endif
            return null;
        }

        // 这里的保存入口在正式环境不写磁盘。当前会话的绑定保存在 QuickSlotBindings，切局后由 HUD 重置。
        public void Save(string scope, QuickSlotSave bindings) {
#if UNITY_EDITOR
            if (ValidationStore != null) {
                ValidationStore.Save(scope, bindings);
                return;
            }
#endif
        }
    }
}