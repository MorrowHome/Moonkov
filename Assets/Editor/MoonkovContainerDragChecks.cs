using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;

public sealed partial class MoonkovUIPreview
{
    [MenuItem("Tools/Moonkov/Check Container Drag")]
    public static void CheckContainerDrag()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Open(); var window=GetWindow<MoonkovUIPreview>();
        var scope=new VisualElement(); scope.style.position=Position.Absolute;
        scope.style.left=scope.style.top=0; scope.style.right=scope.style.bottom=0;
        window.rootVisualElement.Add(scope);
        var graph=InventoryGraph.Create(); var pack=graph.Equipped("Backpack");
        Require(graph.AddSupply("dust",3,pack.Id)==InventoryError.None,"Container fixture failed.");
        int commands=0; ContainerInventoryView view=null;
        view=new ContainerInventoryView(scope,true,command =>
        {
            Require(graph.TryApply(command)==InventoryError.None,"Pointer gesture sent an invalid move.");
            commands++; view.Present(graph);
        });
        view.Present(graph);
        scope.schedule.Execute(() =>
        {
            try
            {
                var root=view.Element; var stash=root.Q<VisualElement>(className:"inventory-stash").Q<VisualElement>(className:"inventory-grid");
                float cell=stash.resolvedStyle.width/10;
                Drag(root.Q<Button>("inventorySlotBackpack"),stash.LocalToWorld(new Vector2(3.5f*cell,3.5f*cell)));
                Require(commands==1 && graph.Find(pack.Id).Parent=="stash" && graph.Count("dust")==3,"Equipped bag failed to return with its contents.");
                var rig=root.Q<Button>("inventorySlotChestRig"); var pocket=root.Q<VisualElement>(className:"inventory-containers").Q<VisualElement>(className:"inventory-grid");
                using(var down=PointerDownEvent.GetPooled(new Event {type=EventType.MouseDown,button=0,mousePosition=rig.worldBound.center,clickCount=2})) rig.SendEvent(down);
                Move(rig,pocket.worldBound.center); Up(rig,pocket.worldBound.center);
                Require(commands==1 && graph.Equipped("ChestRig")!=null,"Invalid drop changed equipment.");
                Require(root.Query<VisualElement>(className:"terminal-context").ToList().Count==0 && root.Query<VisualElement>(className:"terminal-window").ToList().Count==0,"Drag opened a click window.");
                Debug.Log("Container drag checks passed: equipped bag returns with contents; invalid double-click drag preserves equipment and opens no window.");
            }
            finally { view.Dispose(); scope.RemoveFromHierarchy(); }
        }).StartingIn(150);
    }
}
