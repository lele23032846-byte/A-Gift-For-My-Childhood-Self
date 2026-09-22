using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MemoryBatch
{
    public static void Run()
    {
        try
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-memoryOutput");
            string output=i>=0?args[i+1]:Path.GetFullPath("MemoryVerification");Directory.CreateDirectory(output);
            MemoryVisualSetup.Apply();
            var names=new[]{"MyBedroomFloor","LivingroomFloor (1)","LivingroomFloor (2)","MyBedroomDesk","CoffeeTable","MyBedroomBookshelf","Player","MemoryDeskLamp","MemoryBirthdayGift"};
            File.WriteAllLines(Path.Combine(output,"scene-inspection.txt"),names.Select(n=>
            {
                var go=GameObject.Find(n);if(go==null)return n+": MISSING";
                return n+": position="+go.transform.position+" rotation="+go.transform.eulerAngles+" bounds="+(go.GetComponentsInChildren<Renderer>().Length>0?MemoryVisualSetup.BoundsOf(go).ToString():"none");
            }));
            MemoryVisualVerification.Run(Path.Combine(output,"verification.md"));
        }
        catch(Exception e){Debug.LogException(e);if(Application.isBatchMode) EditorApplication.Exit(1);}
    }
}
