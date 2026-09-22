using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Consumes one explicit setup request after a normal editor launch.</summary>
[InitializeOnLoad]
public static class MemoryVisualAutoSetup
{
    private const string Request="MemoryVisualPass.request";
    static MemoryVisualAutoSetup(){ EditorApplication.update += CheckRequest; }
    private static void CheckRequest()
    {
        if(Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if(!File.Exists(Request)) return;
        if(EditorApplication.isPlaying) { EditorApplication.isPlaying=false; return; }
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        // This explicit request also resumes a partially generated visual pass.
        var assembly=typeof(MemoryVisualSetup).Assembly.Location;
        var compiled=File.GetLastWriteTimeUtc(assembly);
        var runtimeCompiled=File.GetLastWriteTimeUtc(typeof(MemoryPresentationController).Assembly.Location);
        bool stale=false;
        foreach(var source in Directory.GetFiles("Assets/Game","*.cs",SearchOption.AllDirectories))
            if(File.GetLastWriteTimeUtc(source)>(source.Replace('\\','/').Contains("/Editor/")?compiled:runtimeCompiled)) {stale=true;break;}
        if(stale) {AssetDatabase.Refresh();return;}
        File.Delete(Request);
        MemoryBatch.Run();
    }
}


