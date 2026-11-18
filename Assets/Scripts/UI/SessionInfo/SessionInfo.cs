// 会话调试面板。从客户端 World 取网络状态，用 ProfilerRecorder 采集帧时间、内存和绘制等数据。离开面板时释放采集器。
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Profiling;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Unity.MP_FPS.Client {
    [RequireComponent(typeof(UIDocument))]
    public class SessionInfo : MonoBehaviour {
        private const float k_UpdateSessionInfoDelay = 2f;
        private const string k_RedColor = "#ff5555";
        private const string k_OrangeColor = "#ffb86c";
        private const string k_GreenColor = "#50fa7b";

        private static class UIElementNames {
            public const string SessionInfoContainer = "SessionInfoContainer";
            public const string BinaryInfoLabel = "BinaryInfo";
            public const string ArgsInfoLabel = "ArgsInfo";
            public const string RightInfoLabel = "RightInfo";
            public const string HardwareInfoLabel = "HardwareInfo";
            public const string ConnectionInfoLabel = "ConnectionInfo";
            public const string ShowSessionInfo = "ShowSessionInfo";
        }

        private VisualElement m_SessionInfoBanner;
        private VisualElement m_SessionInfoContainer;
        private Label m_BinaryInfoLabel;
        private Label m_RightInfoLabel;
        private Label m_HardwareInfoLabel;
        private Label m_ConnectionInfoLabel;
        private Label m_ArgsInfoLabel;
        private Label m_ShowSessionInfo;
        private string m_RightInfo;
        private string m_ConnectionInfo;
        private string m_MachineInfo;
        private ProfilerRecorder m_SystemMemoryRecorder;
        private ProfilerRecorder m_GcMemoryRecorder;
        private ProfilerRecorder m_CpuRecorder;
        private ProfilerRecorder m_CpuToGpuRecorder;
        private ProfilerRecorder m_GpuRecorder;
        private ProfilerRecorder m_GcCountRecorder;
        private ProfilerRecorder m_DrawCallsRecorder;
        private ProfilerRecorder m_FilesOpenRecorder;
        private ProfilerRecorder m_FilesBytesReadRecorder;
        private readonly StringBuilder m_StringBuilder = new(256);
        private float m_Timer;

        private void OnEnable() {
            m_SessionInfoBanner = GetComponent<UIDocument>().rootVisualElement;
            m_SessionInfoContainer = m_SessionInfoBanner.Q<VisualElement>(UIElementNames.SessionInfoContainer);
            m_BinaryInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.BinaryInfoLabel);
            m_RightInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.RightInfoLabel);
            m_HardwareInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.HardwareInfoLabel);
            m_ConnectionInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.ConnectionInfoLabel);
            m_ConnectionInfoLabel.RegisterCallback<ClickEvent>(CopySessionCode);
            m_ArgsInfoLabel = m_SessionInfoBanner.Q<Label>(UIElementNames.ArgsInfoLabel);
            m_ShowSessionInfo = m_SessionInfoBanner.Q<Label>(UIElementNames.ShowSessionInfo);

            // https://docs.unity3d.com/6000.1/Documentation/Manual/frame-timing-manager.html
            // 诊断面板打开后采样 CPU、内存及绘制数据。下面的记录器各自设采样窗口，显示时从它们读近期平均。
            m_SystemMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            m_GcMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            m_CpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time", 15);
            m_CpuToGpuRecorder =
                ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time", 15);
            m_GpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time", 15);
            m_GcCountRecorder =
                ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC Allocation In Frame Count", 15);
            m_DrawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Draw Calls Count", 15);
            m_FilesOpenRecorder = ProfilerRecorder.StartNew(ProfilerCategory.FileIO, "File Handles Open", 15);
            m_FilesBytesReadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.FileIO, "File Bytes Read", 60);
            m_RightInfo = $"{{0}} | {{1}}";
            bool hasArgs = HasArgs(out string args);
            m_ArgsInfoLabel.style.display = hasArgs ? DisplayStyle.Flex : DisplayStyle.None;
            m_ArgsInfoLabel.text = args;
            m_ConnectionInfo = $"Player: \"{{0}}\" on \"{Environment.MachineName}\" | {{1}}";
            string burst = BurstCompiler.IsEnabled ? " +BC" : "";
            string qualityPreset = $"{QualitySettings.names[QualitySettings.GetQualityLevel()]}";
            string targetFrameRate = Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "OFF";
            string vSync = QualitySettings.vSyncCount > 0
                ? $"{QualitySettings.vSyncCount}th @ {Screen.currentResolution.refreshRateRatio.value}hz"
                : "OFF";
            m_MachineInfo =
                $"<color=#8be9fd>{SystemInfo.operatingSystem} | {SystemInfo.deviceModel} | {SystemInfo.processorType} | {SystemInfo.graphicsDeviceName}</color>";
            m_BinaryInfoLabel.text =
                $"{Application.productName} by {Application.companyName} | Ver {Application.version}{burst} | {GetBuildType()} | QSetting:{qualityPreset} | TargetFPS:{targetFrameRate} | VSync:{vSync}";
            if (ConnectionSettings.Instance != null)
                ConnectionSettings.Instance.propertyChanged += OnConnectionStateChanged;
            UpdateSessionInfo();
        }

        private void CopySessionCode(ClickEvent _) {
            if (ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Disconnected &&
                !string.IsNullOrEmpty(ConnectionSettings.Instance.SessionCode)) {
                GUIUtility.systemCopyBuffer = ConnectionSettings.Instance.SessionCode;
                Debug.Log($"Session code {ConnectionSettings.Instance.SessionCode} was copied to clipboard.");
            }
        }

        private void ToggleSessionInfoVisibility() {
            m_SessionInfoContainer.style.display = m_SessionInfoContainer.style.display == DisplayStyle.None
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void OnDisable() {
            m_ConnectionInfoLabel.UnregisterCallback<ClickEvent>(CopySessionCode);
            if (ConnectionSettings.Instance != null)
                ConnectionSettings.Instance.propertyChanged -= OnConnectionStateChanged;
        }

        private void OnConnectionStateChanged(object sender, BindablePropertyChangedEventArgs e) {
            if (e.propertyName == ConnectionSettings.ConnectionStatusStylePropertyName) UpdateSessionInfo();
        }

        private void OnDestroy() {
            m_SystemMemoryRecorder.Dispose();
            m_GcMemoryRecorder.Dispose();
            m_CpuToGpuRecorder.Dispose();
            m_CpuRecorder.Dispose();
            m_GpuRecorder.Dispose();
            m_GcCountRecorder.Dispose();
            m_DrawCallsRecorder.Dispose();
            m_FilesOpenRecorder.Dispose();
            m_FilesBytesReadRecorder.Dispose();
        }

        private void LateUpdate() {
            // 模板会话面板在游戏内让位给局内自己的诊断信息。隐藏时也不定时重写标签。
            bool inGame = GameSettings.Instance.GameState == GlobalGameState.InGame;
            m_SessionInfoBanner.style.display = inGame ? DisplayStyle.None : DisplayStyle.Flex;
            if (inGame) return;
            if (m_SessionInfoContainer.style.display == DisplayStyle.None) return;
            m_Timer += Time.deltaTime;
            // 读数按间隔更新。性能统计不需要像操控角色那样每帧变化。
            if (m_Timer >= k_UpdateSessionInfoDelay) {
                m_Timer -= k_UpdateSessionInfoDelay;
                UpdateSessionInfo();
            }
        }

        private static string GetBuildType() {
            if (Application.isEditor) return "EDITOR";
            return Debug.isDebugBuild ? "DEVELOP" : "RELEASE";
        }

        private static bool HasArgs(out string args) {
            args = "";
            if (Application.isEditor) return false;
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            var sb = new StringBuilder();
            sb.Append("Args: ");

            // Ignore the first, as it's just the full path.
            for (int i = 1; i < commandLineArgs.Length; i++) {
                sb.Append(commandLineArgs[i]);
                if (i < commandLineArgs.Length - 1) sb.Append(' ');
            }
            args = sb.ToString();
            return commandLineArgs.Length > 1;
        }

        private void UpdateSessionInfo() {
            string netcodeInfo = GetNetcodeInfoFromSystem();
            string playerName = GameSettings.Instance.PlayerName;
            // 同机还有服务器 World 时显示主机，否则显示客户端。连接状态文字另由 ECS 系统提供。
            var networkRole = ClientServerBootstrap.HasServerWorld ? NetworkRole.Host : NetworkRole.Client;
            string interactionTypeText = "click";
#if UNITY_ANDROID || UNITY_IOS
            interactionTypeText = "tap";
#endif
            string sessionState =
                ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Disconnected &&
                ConnectionSettings.Instance.SessionCode != null
                    ? $"Session-Code: <b>{ConnectionSettings.Instance.SessionCode}</b> ({interactionTypeText} to copy) | Role: <b>{networkRole.ToString()}</b>"
                    : "No Session";
            m_ConnectionInfoLabel.text = string.Format(m_ConnectionInfo, playerName, sessionState);
            m_RightInfoLabel.text = string.Format(m_RightInfo, netcodeInfo, GetFps());
            m_HardwareInfoLabel.text = GetCurrentParameters();
        }

        private string GetFps() {
            int avgFps = Mathf.CeilToInt(1f / Time.smoothDeltaTime);
            if (avgFps <= 0) avgFps = Mathf.CeilToInt(1f / Time.deltaTime);
            double targetFps = GetImpliedTargetFps();
            int targetFPSInt = (int)targetFps;
            return
                $"<color={GetPingColor()}>FPS {avgFps.ToString()} / {targetFPSInt.ToString(CultureInfo.InvariantCulture)}</color>";

            double GetImpliedTargetFps() {
                double screenRefreshRate = Screen.currentResolution.refreshRateRatio.value;
                if (QualitySettings.vSyncCount <= 0) {
                    if (Application.targetFrameRate > 0) return Application.targetFrameRate;
                    return screenRefreshRate;
                }
                return screenRefreshRate / QualitySettings.vSyncCount;
            }

            string GetPingColor() {
                double currentFpsVsTargetFpsRatio = avgFps / targetFps;
                if (currentFpsVsTargetFpsRatio >= 0.99f) return k_GreenColor;
                if (currentFpsVsTargetFpsRatio >= 0.5f || avgFps >= 60) return k_OrangeColor;
                return k_RedColor;
            }
        }

        private string GetCurrentParameters() {
            // Clear the cached StringBuilder instead of creating a new one.
            m_StringBuilder.Clear();
            bool needsSeparator = false;

            void AppendSeparator() {
                if (needsSeparator) m_StringBuilder.Append(" | ");
                needsSeparator = true;
            }

            // 平台不一定支持所有统计项。逐项检查记录器有效后再显示，缺一项不影响其他数据。
            if (m_CpuRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("CPU:").Append((GetRecorderFrameAverage(m_CpuRecorder) * 1e-6f).ToString("F1"))
                    .Append("ms");
            }
            if (m_CpuToGpuRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("Render:")
                    .Append((GetRecorderFrameAverage(m_CpuToGpuRecorder) * 1e-6f).ToString("F1")).Append("ms");
            }
            if (m_GpuRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("GPU:").Append((GetRecorderFrameAverage(m_GpuRecorder) * 1e-6f).ToString("F1"))
                    .Append("ms");
            }
            if (m_DrawCallsRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("Draw Calls:")
                    .Append(GetRecorderFrameAverage(m_DrawCallsRecorder).ToString("0"));
            }
            if (m_GcMemoryRecorder.Valid) {
                AppendSeparator();
                // For integers/longs, ToString() is efficient and doesn't box.
                m_StringBuilder.Append("Managed:").Append(m_GcMemoryRecorder.LastValue / (1024 * 1024)).Append("MB");
            }
            if (m_SystemMemoryRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("Native:").Append(m_SystemMemoryRecorder.LastValue / (1024 * 1024)).Append("MB");
            }
            if (m_GcCountRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("GC/Frame:").Append(GetRecorderFrameAverage(m_GcCountRecorder).ToString("0"));
            }
            if (m_FilesOpenRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("Files Open:").Append(m_FilesOpenRecorder.LastValue.ToString("0"));
            }
            if (m_FilesBytesReadRecorder.Valid) {
                AppendSeparator();
                m_StringBuilder.Append("File Read Bytes:")
                    .Append((GetRecorderFrameAverage(m_FilesBytesReadRecorder, false) / (1024 * 1024)).ToString("0.0"))
                    .Append("MB");
            }
            if (!string.IsNullOrEmpty(m_MachineInfo)) {
                AppendSeparator();
                m_StringBuilder.Append(m_MachineInfo);
            }
            return m_StringBuilder.ToString();
        }

        private static double GetRecorderFrameAverage(ProfilerRecorder recorder, bool avg = true) {
            int samplesCount = recorder.Capacity;
            if (samplesCount == 0) return 0;
            double r = 0;
            // unsafe
            // {
            //     var samples = stackalloc ProfilerRecorderSample[samplesCount];
            //     recorder.CopyTo(samples, samplesCount);
            //     for (var i = 0; i < samplesCount; ++i)
            //         r += samples[i].Value;
            //     if (avg) r /= samplesCount;
            // }
            return r;
        }

        /// <summary>
        /// Reads the cached network status from the NetworkStatusSystem.
        /// </summary>
        /// <returns>A formatted string with network status, or a default message if not available.</returns>
        private string GetNetcodeInfoFromSystem() {
            var clientWorld = ClientServerBootstrap.ClientWorld;
            // World 未创建或已离开、系统未提供状态时直接返回等候文字。避免继续读无效的实体句柄。
            if (clientWorld == null || !clientWorld.IsCreated || NetworkStatusSystem.StatusEntity == Entity.Null)
                return $"<color={k_RedColor}>No client world!</color>";
            var entityManager = clientWorld.EntityManager;
            var status = entityManager.GetComponentData<NetworkStatusSingleton>(NetworkStatusSystem.StatusEntity);
            return status.Status.ToString();
        }
    }
}