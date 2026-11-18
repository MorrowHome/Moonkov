// 加载界面的数据源。连接、加载场景、同步角色等各占一段进度，UpdateLoading 把单步进度换算成总进度并通知绑定的界面。
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public class LoadingData : INotifyBindablePropertyChanged {
        public enum LoadingSteps {
            StartLoading,
            InitializeConnection,
            LookingForMatch,
            CreateWorld,
            WaitingConnection,
            LoadGameScene,
            LoadResourcesScene,
            LoadServer,
            LoadClient,
            WorldReplication,
            WaitingOnPlayer,
            LoadingDone,
            UnloadingGame,
            DisconnectingClient,
            UnloadingWorld,
            UnloadingGameScene,
            UnloadingResourcesScene,
            BackToMainMenu,
            NotLoading
        }

        private struct LoadingStep {
            public readonly string Text;
            public readonly float Start;
            public readonly float End;

            public LoadingStep(string text, float start, float end) {
                Text = text;
                Start = start;
                End = end;
            }
        }

        // 每项保存显示文案和总进度条上的起止位置。入局与退局分别从自己的零点到一百。
        private static readonly Dictionary<LoadingSteps, LoadingStep> k_LoadingSteps = new() {
            { LoadingSteps.StartLoading, new LoadingStep("Loading game...", 0f, 0f) },
            { LoadingSteps.InitializeConnection, new LoadingStep("Initializing connection...", 0.1f, 0.1f) },
            { LoadingSteps.LookingForMatch, new LoadingStep("Looking for a match session...", 0.12f, 0.12f) },
            { LoadingSteps.CreateWorld, new LoadingStep("Creating entity worlds...", 0.15f, 0.15f) },
            { LoadingSteps.WaitingConnection, new LoadingStep("Waiting for Client connection...", 0.2f, 0.2f) },
            { LoadingSteps.LoadGameScene, new LoadingStep("Loading Gameplay scene...", 0.3f, 0.5f) },
            { LoadingSteps.LoadResourcesScene, new LoadingStep("Loading Resources scene...", 0.6f, 0.6f) },
            { LoadingSteps.LoadServer, new LoadingStep("Loading Server world...", 0.6f, 0.7f) },
            { LoadingSteps.LoadClient, new LoadingStep("Loading Client world...", 0.7f, 0.8f) },
            { LoadingSteps.WorldReplication, new LoadingStep("Replicating world...", 0.8f, 0.9f) },
            { LoadingSteps.WaitingOnPlayer, new LoadingStep("Waiting for Player spawn...", 0.9f, 0.9f) },
            { LoadingSteps.LoadingDone, new LoadingStep("Starting gameplay...", 1f, 1f) },
            { LoadingSteps.UnloadingGame, new LoadingStep("Leaving gameplay...", 0f, 0f) },
            { LoadingSteps.DisconnectingClient, new LoadingStep("Disconnecting Client...", 0.1f, 0.1f) },
            { LoadingSteps.UnloadingWorld, new LoadingStep("Disposing entity worlds...", 0.1f, 0.2f) },
            { LoadingSteps.UnloadingGameScene, new LoadingStep("Unloading Gameplay scene...", 0.2f, 0.5f) },
            { LoadingSteps.UnloadingResourcesScene, new LoadingStep("Unloading Resources scene...", 0.5f, 0.9f) },
            { LoadingSteps.BackToMainMenu, new LoadingStep("Opening Main menu...", 1f, 1f) },
            { LoadingSteps.NotLoading, new LoadingStep("_", 0f, 0f) }
        };

        public static LoadingData Instance { get; private set; } = null!;

        // 刚开始运行就建一份新的进度对象。使用关闭域重载的 Editor 试玩选项时，也会重新开始计进度。
        /// <summary>
        /// 编辑器重新进入 Play 时先初始化，免得继续使用上一轮的旧实例。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitializeOnLoad() { Instance = new LoadingData(); }

        private LoadingData() { m_LoadingProgress = 0.0f; }
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        private void Notify([CallerMemberName] string property = "") {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }

        public void UpdateLoading(LoadingSteps step, float stepProgress = 0f) {
            var currentStep = k_LoadingSteps[step];
            // 场景读了四分之一，只表示当前阶段完成四分之一。这里按这个阶段在总进度中占的范围换算。
            LoadingProgress = currentStep.Start + stepProgress * (currentStep.End - currentStep.Start);
            LoadingStatusText = currentStep.Text;
        }

        private float m_LoadingProgress;
        public const string LoadingProgressPropertyName = nameof(LoadingProgress);

        [CreateProperty]
        private float LoadingProgress {
            get => m_LoadingProgress;
            set {
                m_LoadingProgress = value;
                Notify();
            }
        }

        private string m_LoadingStatusText;
        public const string LoadingStatusTextPropertyName = nameof(LoadingStatusText);

        [CreateProperty]
        private string LoadingStatusText {
            // 内部先记住原始文案。界面真正取这条状态时再按当前语言查对应文字。
            get => MoonkovLocalization.Text(m_LoadingStatusText);
            set {
                if (m_LoadingStatusText == value) return;
                m_LoadingStatusText = value;
                Notify();
            }
        }
    }
}