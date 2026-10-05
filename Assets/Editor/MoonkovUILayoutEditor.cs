using UnityEditor;
using UnityEngine.UIElements;

public static class MoonkovUILayoutEditor
{
    [MenuItem("Tools/Moonkov/Edit Layout/Ship")]
    public static void Ship() => Open("Assets/Resources/Moonkov/UI/Ship.uxml");

    [MenuItem("Tools/Moonkov/Edit Layout/Character Inventory")]
    public static void Inventory() => Open("Assets/Resources/Moonkov/UI/ContainerInventory.uxml");

    [MenuItem("Tools/Moonkov/Edit Layout/Operation Operator")]
    public static void Operator() => Open("Assets/Resources/Moonkov/UI/OperationOperator.uxml");

    [MenuItem("Tools/Moonkov/Edit Layout/Navigation")]
    public static void Navigation() => Open("Assets/UI Toolkit/GameUI/StashScreen.uxml");

    private static void Open(string path) => AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path));
}
