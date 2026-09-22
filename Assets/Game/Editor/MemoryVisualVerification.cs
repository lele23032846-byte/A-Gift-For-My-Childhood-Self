using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Play-mode integration checks. Run from CLI eval, then poll the report.</summary>
[InitializeOnLoad]
public static class MemoryVisualVerification
{
    private static readonly List<string> checks = new List<string>();
    private static double deadline;
    private static int phase;
    private static string report;
    private static bool errors;
    private static double startupDeadline;
    private static bool previousBackground;
    private const string Pending = "MemoryVisualVerification.Pending";
    static MemoryVisualVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if(state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))
            { SessionState.SetBool(Pending,false);Begin(); }
        };
    }
    public static void RunStage1Batch()
    {
        const string scenePath = "Assets/Game/Scenes/SC_Bedroom.unity";
        if(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != scenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-stage1Output");
        string output=i>=0?args[i+1]:Path.GetFullPath("MemoryVerification/stage1-verification.md");
        Run(output);
    }
    public static void Run(string reportPath)
    {
        SessionState.SetString("MemoryVerification.Report",reportPath);
        if(EditorApplication.isPlaying) Begin();
        else {SessionState.SetBool(Pending,true);EditorApplication.isPlaying=true;}
    }
    private static void Begin()
    {
        report=SessionState.GetString("MemoryVerification.Report","MemoryVerification.md");
        checks.Clear();phase=0;errors=false;deadline=EditorApplication.timeSinceStartup+2;
        startupDeadline=EditorApplication.timeSinceStartup+30;previousBackground=Application.runInBackground;Application.runInBackground=true;EditorApplication.isPaused=false;
        Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
    }
    private static void OnLog(string message,string trace,LogType type)
    { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert){ errors=true; checks.Add("FAIL / Unity log: "+message); } }
    private static void Check(string name,bool pass)
    { checks.Add((pass?"PASS":"FAIL")+" / "+name); }
    private static void Tick()
    {
        if(EditorApplication.timeSinceStartup<deadline) return;
        try
        {
            var manager=UnityEngine.Object.FindAnyObjectByType<ChapterManager>();
            var menu=UnityEngine.Object.FindAnyObjectByType<MemoryMenu>();
            var interactor=UnityEngine.Object.FindAnyObjectByType<PlayerInteractor>();
            if(phase==0 && (menu==null || GameObject.Find("MemoryMenuCanvas")==null))
            {
                if(EditorApplication.timeSinceStartup>startupDeadline) throw new InvalidOperationException("Gameplay startup timeout. playing="+EditorApplication.isPlaying+" paused="+EditorApplication.isPaused+" frame="+Time.frameCount+" presentation="+(UnityEngine.Object.FindAnyObjectByType<MemoryPresentationController>()!=null)+" menu="+(menu!=null));
                EditorApplication.QueuePlayerLoopUpdate();return;
            }
            if(phase==0)
            {
                Check("Title menu visible, gameplay paused",menu.IsOpen && Time.timeScale==0 && !interactor.enabled);
                Check("Chinese font is available",MemoryUI.Font!=null && MemoryUI.Font.HasCharacter('迟',true,true));
                Check("Camera post processing enabled",interactor.GetComponentInChildren<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing);
                Check("Exactly one EventSystem",UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>().Length==1);
                var promptUi=UnityEngine.Object.FindAnyObjectByType<InteractionPromptUI>();
                Check("Stage 1 exploration HUD created",promptUi!=null && GameObject.Find("Reticle")!=null && GameObject.Find("FocusRing")!=null && GameObject.Find("InteractionPrompt")!=null);
                Check("Chapter 2 and 3 demo objects removed",GameObject.Find("MemoryDeskLamp")==null && GameObject.Find("MemoryBirthdayGift")==null);
                var sceneRoot=GameObject.Find("MemoryArtDirection");
                Check("Presentation is explicitly attached to scene",sceneRoot.GetComponent<MemoryPresentationController>()!=null);
                Check("Ceilings generated",sceneRoot.GetComponentsInChildren<Transform>().Any(x=>x.name.StartsWith("Ceiling_")));
                Capture("01-title");
                // Exercise the actual menu button listener.
                GameObject.Find("Return").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Check("Start button restores gameplay input",!menu.IsOpen && Time.timeScale>0 && interactor.enabled);
                menu.Open();Check("Pause captures input",!interactor.enabled && Time.timeScale==0);menu.Resume();
                var placement=UnityEngine.Object.FindAnyObjectByType<ItemPlacement>();
                var binding=new SerializedObject(manager).FindProperty("lostPetPlacement").objectReferenceValue;
                Check("Chapter 1 references the actual placement",binding==placement);
                Check("Empty inventory rejects placement",!placement.TryInteract(interactor) && manager.IsChapterActive(ChapterId.LostPet));
                UnityEngine.Object.FindAnyObjectByType<PickupItem>().TryInteract(interactor);
                Check("Pickup enters inventory",interactor.GetComponent<Inventory>().HasItem(ItemId.SmallBox));
                Check("Pickup notification is available",GameObject.Find("PickupNotification")!=null);
                Check("Owning box does not finish memory",manager.IsChapterActive(ChapterId.LostPet));
                phase=1;deadline=EditorApplication.timeSinceStartup+1;
            }
            else if(phase==1)
            {
                Capture("02-faded");
                var placement=UnityEngine.Object.FindAnyObjectByType<ItemPlacement>();
                placement.TryInteract(interactor);
                Check("Placement consumes the box",placement.IsPlaced && !interactor.GetComponent<Inventory>().HasItem(ItemId.SmallBox));
                Check("Real placement completes first memory",manager.IsChapterCompleted(ChapterId.LostPet));
                Check("Repeated placement is rejected",!placement.TryInteract(interactor));
                phase=2;deadline=EditorApplication.timeSinceStartup+6;
            }
            else
            {
                Check("One completed memory restores 100 percent colour",Restoration()>.99f);
                var label=GameObject.Find("ChapterLabel").GetComponent<TMPro.TMP_Text>();
                Check("HUD ends first memory instead of requesting chapter 2",label.text.Contains("已归还"));
                Check("Permanent restoration progress removed",GameObject.Find("RestorationLabel")==null && GameObject.Find("RestorationBar")==null);
                Capture("03-restored");
                Check("No errors during interaction and restoration",!errors);
                Finish();
            }
        }
        catch(Exception e){checks.Add("FAIL / "+e);Finish();}
    }
    private static float Restoration()
    {
        var p=UnityEngine.Object.FindAnyObjectByType<MemoryPresentationController>();
        return (float)typeof(MemoryPresentationController).GetField("restoration",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(p);
    }
    private static void Finish()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;Application.runInBackground=previousBackground;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
        File.WriteAllText(report,"# Unity Play Mode verification\n\n"+string.Join("\n",checks.Select(x=>"- "+x))+"\n");
        Debug.Log("[Memory verification] "+checks.Count(x=>x.StartsWith("PASS"))+" passed; "+checks.Count(x=>x.StartsWith("FAIL"))+" failed. Report: "+report);
        if(Application.isBatchMode) EditorApplication.Exit(checks.Any(x=>x.StartsWith("FAIL"))?1:0);
        else EditorApplication.isPlaying=false;
    }
    private static void Capture(string name)
    {
        var player=UnityEngine.Object.FindAnyObjectByType<FirstPersonController>();
        var cam=player!=null?player.GetComponentInChildren<Camera>():Camera.main;
        if(cam==null) return;
        var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);
        var oldTarget=cam.targetTexture;var oldActive=RenderTexture.active;
        var oldPosition=cam.transform.position;var oldRotation=cam.transform.rotation;
        var review=GameObject.Find("MemoryTitleView");
        if(name!="01-title" && review!=null) cam.transform.SetPositionAndRotation(review.transform.position,review.transform.rotation);
        var canvases=UnityEngine.Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        try
        {
            foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=cam;c.planeDistance=.3f;}
            Canvas.ForceUpdateCanvases();cam.targetTexture=target;cam.Render();RenderTexture.active=target;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report),name+".png"),image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceOverlay;c.worldCamera=null;}
            cam.transform.SetPositionAndRotation(oldPosition,oldRotation);cam.targetTexture=oldTarget;RenderTexture.active=oldActive;target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
