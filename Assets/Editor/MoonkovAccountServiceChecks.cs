using System;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;

public static class MoonkovAccountServiceChecks
{
    [MenuItem("Tools/Moonkov/Check Account Service Addresses")]
    public static void Run()
    {
        Check("http://127.0.0.1:5080/", false, true);
        Check("https://accounts.example.com/", false, true);
        Check("http://10.249.60.168:5080/", false, false);
        Check("http://10.249.60.168:5080/", true, true);
        Check("http://172.16.0.1:5080/", true, true);
        Check("http://192.168.1.10:5080/", true, true);
        Check("http://172.32.0.1:5080/", true, false);
        Check("http://8.8.8.8:5080/", true, false);
        Check("http://accounts.example.com/", true, false);
        Check("https://user:secret@accounts.example.com/", true, false);
        Check("file:///tmp/account", true, false);
        Debug.Log("Account service address checks passed: 11 cases; LAN HTTP requires opt-in and a private IPv4 address.");
    }

    private static void Check(string address, bool allowLanHttp, bool expected)
    {
        bool accepted;
        try { AccountServiceSettings.ValidateAddress(address, allowLanHttp); accepted = true; }
        catch (InvalidOperationException) { accepted = false; }
        if (accepted != expected) throw new InvalidOperationException("Account address policy check failed: " + address);
    }
}
