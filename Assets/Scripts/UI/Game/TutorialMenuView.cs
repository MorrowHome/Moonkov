using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    public sealed class TutorialMenuView : IDisposable
    {
        private const int Ready = TutorialRules.Equipped | TutorialRules.Cells | TutorialRules.Medicine;
        private readonly VisualElement m_Root, m_Card, m_Storage;
        private readonly Label m_Title, m_Copy;
        private readonly Button m_Next, m_Brief, m_Skip, m_Supply, m_Return;
        private readonly Action m_OpenStorage, m_CheckDeparture;
        private readonly Action<string> m_RevealStorageItem;
        private readonly IVisualElementScheduledItem m_Timer;
        private bool m_Busy, m_Inline, m_Observing, m_ShowBrief, m_Disposed;
        private string m_Message;
        private OfflineProfileStore m_MessageStore;
        private int m_MessageInventoryVersion;
        public TutorialMenuView(VisualElement root, Action openStorage, Action checkDeparture, Action<string> revealStorageItem)
        {
            m_Root = root; m_OpenStorage = openStorage; m_CheckDeparture = checkDeparture;
            m_RevealStorageItem = revealStorageItem;
            m_Storage = root.Q<VisualElement>(className: "stash-body");
            var style = Resources.Load<StyleSheet>("Moonkov/UI/Tutorial"); if (style) root.styleSheets.Add(style);
            m_Card = new VisualElement { name = "tutorialMenu" }; m_Card.AddToClassList("tutorial-card");
            m_Title = new Label(); m_Title.AddToClassList("tutorial-title");
            m_Copy = new Label(); m_Copy.AddToClassList("tutorial-copy");
            var actions = new VisualElement(); actions.AddToClassList("tutorial-actions");
            m_Next = new Button(Next) { name = "tutorialNext" }; m_Next.AddToClassList("tutorial-primary");
            m_Brief = new Button(ToggleBrief) { name = "tutorialBrief" };
            m_Skip = new Button(Skip) { name = "tutorialSkip" };
            m_Supply = new Button(Supply) { name = "tutorialSupply" };
            m_Return = new Button(Return) { name = "tutorialReturn" };
            actions.Add(m_Next); actions.Add(m_Brief); actions.Add(m_Supply); actions.Add(m_Skip); actions.Add(m_Return);
            m_Card.Add(m_Title); m_Card.Add(m_Copy); m_Card.Add(actions); root.Add(m_Card);
            m_Timer = root.schedule.Execute(Refresh).Every(250); Refresh();
        }
        internal void Refresh()
        {
            if (m_Disposed) return;
            var state = PlayerProfileClient.Tutorial;
            bool active = state?.Status == TutorialStatus.Active;
            bool practice = PlayerProfileClient.IsTutorialPractice;
            bool onShip = m_Root.ClassListContains("terminal-is-ship");
            bool inStorage = m_Root.ClassListContains("terminal-is-loadout");
            bool visible = (onShip || inStorage) && state != null && (active || practice) &&
                !PlayerProfileClient.IsLan && !PlayerProfileClient.RaidActive && GameSettings.Instance?.GameState == GlobalGameState.MainMenu;
            m_Card.EnableInClassList("tutorial-off", !visible);
            m_Root.EnableInClassList("tutorial-guiding", visible && active);
            if (!visible || m_Busy || m_Root.resolvedStyle.display == DisplayStyle.None) return;
            bool hasBrief = active && onShip && state.IntroPage < TutorialRules.Introduction.Length;
            if (!hasBrief) m_ShowBrief = false;
            if (inStorage != m_Inline)
            {
                m_Inline = inStorage; m_Card.EnableInClassList("tutorial-inline", inStorage);
                if (inStorage) m_Root.Insert(m_Root.IndexOf(m_Storage), m_Card);
                else m_Root.Add(m_Card);
            }
            var inventory = PlayerProfileClient.Inventory;
            if (m_Message != null && (m_MessageStore != PlayerProfileClient.OfflineStore || m_MessageInventoryVersion != (inventory?.Version ?? -1)))
                m_Message = null;
            int prep = TutorialRules.Preparation(inventory);
            bool ready = prep == Ready;
            bool canLocate = inStorage && m_RevealStorageItem != null && FindStorageTarget(inventory, prep) != null;
            if (active && !m_Observing && (state.CompletedSteps & prep) != prep) ObservePreparation(prep);

            MoonkovLocalization.Set(m_Title, m_ShowBrief ? "BRIEFING" : practice ? "TRAINING PRACTICE" : "NEW PLAYER TRAINING");
            string copy = !active ? "Practice complete. Return to your main profile." :
                m_ShowBrief ? TutorialRules.Introduction[state.IntroPage] :
                inventory == null ? "Loading equipment..." :
                ready ? "Your kit is ready. Register at the MC00 entrance." :
                onShip && prep == 0 ? "Equip a halo and pack a battery and an injector." :
                (prep & TutorialRules.Equipped) == 0 ? "Drag a halo from storage to its weapon slot." :
                (prep & TutorialRules.Cells) == 0 ? "Move a charged battery into a pocket." :
                "Move a medical injector into a pocket.";
            MoonkovLocalization.Set(m_Copy, m_Message ?? copy);
            MoonkovLocalization.Set(m_Next, m_ShowBrief ? "CONTINUE" : ready ? "CHECK BEFORE DEPLOYING" :
                inStorage ? "LOCATE ITEM" : "GEAR UP");
            MoonkovLocalization.Set(m_Brief, m_ShowBrief ? "CLOSE BRIEFING" : "BRIEFING");
            MoonkovLocalization.Set(m_Skip, "SKIP TUTORIAL");
            MoonkovLocalization.Set(m_Supply, "CLAIM TRAINING SUPPLIES");
            MoonkovLocalization.Set(m_Return, "END PRACTICE");

            m_Next.style.display = active && (onShip || ready || canLocate) ? DisplayStyle.Flex : DisplayStyle.None;
            m_Next.SetEnabled(m_ShowBrief || inventory != null);
            m_Brief.style.display = hasBrief ? DisplayStyle.Flex : DisplayStyle.None;
            m_Skip.style.display = active && !m_ShowBrief ? DisplayStyle.Flex : DisplayStyle.None;
            bool missingSupplies = active && inStorage && inventory != null && !ready &&
                (inventory.Count("cells") < 3 || inventory.Count("medkit") == 0 || !inventory.Items.Any(i => TutorialRules.IsWeapon(i.Code)));
            m_Supply.style.display = missingSupplies ? DisplayStyle.Flex : DisplayStyle.None;
            m_Return.style.display = practice ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private async void ObservePreparation(int steps)
        {
            m_Observing = true;
            try { await PlayerProfileClient.UpdateTutorialAsync(steps: steps); }
            catch (Exception e) { ShowMessage(e.Message); }
            finally { m_Observing = false; }
        }
        private async void Next()
        {
            if (m_Busy || PlayerProfileClient.Tutorial?.Status != TutorialStatus.Active) return;
            m_Message = null;
            if (m_ShowBrief)
            {
                await Run(() => PlayerProfileClient.UpdateTutorialAsync(nextIntro: true));
                return;
            }
            var inventory = PlayerProfileClient.Inventory;
            if (inventory == null) return;
            int prep = TutorialRules.Preparation(inventory);
            if (prep == Ready) m_CheckDeparture();
            else if (!m_Inline) m_OpenStorage();
            else if (FindStorageTarget(inventory, prep) is string target) m_RevealStorageItem?.Invoke(target);
        }

        private static string FindStorageTarget(InventoryGraph inventory, int prep)
        {
            if (inventory == null || prep == Ready) return null;
            return inventory.Items.Find(item => item.Parent == InventoryCatalog.Stash &&
                ((prep & TutorialRules.Equipped) == 0 ? TutorialRules.IsWeapon(item.Code) :
                (prep & TutorialRules.Cells) == 0 ? BatteryEnergy.Stored(item) > 0 : item.Code == "medkit"))?.Id;
        }

        private void ToggleBrief() { if (m_Busy) return; m_Message = null; m_ShowBrief = !m_ShowBrief; Refresh(); }
        private async void Skip() => await Run(() => PlayerProfileClient.UpdateTutorialAsync(skip: true));
        private async void Supply() => await Run(async () => ShowMessage(await PlayerProfileClient.ClaimTutorialSuppliesAsync()));
        private void Return() { if (m_Busy) return; try { PlayerProfileClient.EndTutorialPractice(); } catch (Exception e) { ShowMessage(e.Message); } Refresh(); }
        private void ShowMessage(string message)
        {
            m_Message = message;
            m_MessageStore = PlayerProfileClient.IsOffline ? PlayerProfileClient.OfflineStore : null;
            m_MessageInventoryVersion = PlayerProfileClient.Inventory?.Version ?? -1;
        }
        private async System.Threading.Tasks.Task Run(Func<System.Threading.Tasks.Task> action)
        {
            if (m_Busy) return; m_Busy = true; m_Card.SetEnabled(false);
            try { await action(); } catch (Exception e) { ShowMessage(e.Message); }
            finally { m_Busy = false; m_Card.SetEnabled(true); Refresh(); }
        }
        public void Dispose()
        {
            m_Disposed = true;
            m_Timer.Pause();
            m_Root.RemoveFromClassList("tutorial-guiding");
            m_Card.RemoveFromHierarchy();
        }
    }
}
