using System;
using Unity.MP_FPS.MoonAlien.Editor;

internal static class Program
{
    private static void Main()
    {
        int groups = AlienCombatContractChecks.RunAll();
        Console.WriteLine(groups + " alien combat contract groups passed against the actual source files.");
        Console.WriteLine("This does not validate Unity physics, real network authority, or production player damage.");
    }
}
