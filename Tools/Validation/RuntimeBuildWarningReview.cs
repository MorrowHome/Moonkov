using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Campaign;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

public static class RuntimeBuildWarningReview
{
    public static async Task<object> Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use the existing idle Editor.");
        // Exercise the actual JSON serializer and legacy field names without opening a profile.
        var jsonType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json")
            .GetType("Newtonsoft.Json.JsonConvert");
        var read = jsonType.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        var write = jsonType.GetMethod("SerializeObject", new[] { typeof(object) });
        const string oldSave = "{\"SchemaVersion\":1,\"ContentVersion\":1,\"Revision\":3,\"PreferenceRevision\":2," +
            "\"NodeMasks\":{\"CM01\":3},\"CompletedTasks\":[\"CM01\"],\"NodeItemIds\":{\"CM03/recover\":\"fixture-id\"}," +
            "\"ProcessedEvents\":[\"fixture-event\"],\"PrimaryTracked\":\"CM02\",\"SecondaryTracked\":[\"CM03\"],\"TrackingSuppressed\":true}";
        var saved = (CampaignProgress)read.Invoke(null, new object[] { oldSave, typeof(CampaignProgress) });
        var json = (string)write.Invoke(null, new object[] { saved });
        var restored = (CampaignProgress)read.Invoke(null, new object[] { json, typeof(CampaignProgress) });
        if (restored.SchemaVersion != 1 || restored.ContentVersion != 1 || restored.Revision != 3 || restored.PreferenceRevision != 2 ||
            restored.NodeMasks["CM01"] != 3 || !restored.CompletedTasks.SetEquals(new[] { "CM01" }) ||
            restored.NodeItemIds["CM03/recover"] != "fixture-id" || !restored.ProcessedEvents.SetEquals(new[] { "fixture-event" }) ||
            restored.PrimaryTracked != "CM02" || restored.SecondaryTracked.Single() != "CM03" || !restored.TrackingSuppressed)
            throw new InvalidOperationException("Legacy campaign JSON did not survive a round trip.");
        var link = NavMesh.AddLink(new NavMeshLinkData { startPosition = Vector3.zero, endPosition = Vector3.right,
            width = 1, bidirectional = true, agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID });
        try
        {
            if (!NavMesh.IsLinkValid(link)) throw new InvalidOperationException("Test link was not created.");
        }
        finally { NavMesh.RemoveLink(link); }
        if (NavMesh.IsLinkValid(link)) throw new InvalidOperationException("Test link was not released.");

        var window = ScriptableObject.CreateInstance<RuntimeBuildWarningWindow>();
        window.position = new Rect(80, 80, 800, 600);
        window.ShowUtility();
        RaidChatView chat = null;
        try
        {
            int sent = 0, cancelled = 0;
            chat = new RaidChatView(window.rootVisualElement, () => sent++, () => cancelled++);
            var state = new RaidChatClientState { Session = 1, TeamAvailable = true };
            chat.Present(state, true);
            chat.SetOpen(true);
            chat.Input.Focus();
            // Resume outside the panel's update/dispatch gate before sending input.
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.rootVisualElement.schedule.Execute(() => ready.TrySetResult(true)).StartingIn(200);
            if (await Task.WhenAny(ready.Task, Task.Delay(5000)) != ready.Task)
                throw new InvalidOperationException("Native chat UI did not update.");
            chat.Input.value = "warning cleanup fixture";
            using (var tab = KeyDownEvent.GetPooled('\t', KeyCode.Tab, EventModifiers.None))
            {
                chat.Input.SendEvent(tab);
                if (!tab.isImmediatePropagationStopped) throw new InvalidOperationException("Tab escaped the composer: open=" + chat.IsOpen +
                    ", attached=" + (chat.Input.panel != null) + ", propagation=" + tab.isPropagationStopped + ", target=" + (tab.target as VisualElement)?.name);
            }
            var focused = chat.Input.focusController.focusedElement as VisualElement;
            if (focused != chat.Input && !chat.Input.Contains(focused))
                throw new InvalidOperationException("Tab moved focus out of the composer.");
            using (var enter = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None))
            {
                chat.Input.SendEvent(enter);
                if (!enter.isImmediatePropagationStopped || sent != 1) throw new InvalidOperationException("Enter was not consumed exactly once.");
            }
            using (var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
                chat.Input.SendEvent(escape);
            if (cancelled != 1 || state.Draft != "warning cleanup fixture")
                throw new InvalidOperationException("Escape lost the draft or failed to cancel.");
            return new { campaignJsonRoundTrip = true, navLinkCreatedAndReleased = true, chatTabFocus = true, sent, cancelled, draftPreserved = true };
        }
        finally { chat?.Dispose(); window.Close(); }
    }
}

public sealed class RuntimeBuildWarningWindow : EditorWindow { }
