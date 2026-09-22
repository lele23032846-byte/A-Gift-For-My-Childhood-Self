using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Idempotent authored scene pass. All geometry is made through Unity APIs.</summary>
public static class MemoryVisualSetup
{
    private const string ScenePath = "Assets/Game/Scenes/SC_Bedroom.unity";
    private const string RootName = "MemoryArtDirection";
    [MenuItem("Tools/迟到的礼物/应用视觉与章节演示")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play mode before editing the scene.");
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        EnsureFont();
        PrewarmFont();
        var root = GameObject.Find(RootName);
        if (root == null) { root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root,"Memory visual pass"); }
        if(root.GetComponent<MemoryPresentationController>()==null) root.AddComponent<MemoryPresentationController>();
        var ceilingMat = Material("MemoryPlaster",new Color(.76f,.74f,.68f),.08f);
        var walls=UnityEngine.Object.FindObjectsByType<Renderer>().Where(r=>
            r.bounds.size.y>1.5f && (r.name.IndexOf("wall",StringComparison.OrdinalIgnoreCase)>=0 ||
            r.GetComponentsInParent<Transform>().Any(t=>t.name.Equals("Walls",StringComparison.OrdinalIgnoreCase)))).ToArray();
        if(walls.Length==0) throw new InvalidOperationException("Cannot align ceiling without real wall geometry.");
        File.WriteAllLines("MemoryVerification/wall-bounds.txt",walls.Select(r=>r.name+": "+r.bounds));
        foreach (var floorName in new[]{"MyBedroomFloor","LivingroomFloor (1)","LivingroomFloor (2)"})
        {
            var floor = GameObject.Find(floorName); if(floor == null) continue;
            var b = BoundsOf(floor);
            if(b.size.x<.5f || b.size.z<.5f) continue;
            var adjacent=walls.Where(r=>r.bounds.max.x>=b.min.x-.25f && r.bounds.min.x<=b.max.x+.25f &&
                r.bounds.max.z>=b.min.z-.25f && r.bounds.min.z<=b.max.z+.25f).Select(r=>r.bounds.max.y).OrderBy(v=>v).ToArray();
            if(adjacent.Length==0) continue;
            float top=adjacent[adjacent.Length/2];
            Primitive("Ceiling_"+floorName,root.transform,PrimitiveType.Cube,new Vector3(b.center.x,top-.055f,b.center.z),new Vector3(b.size.x+.36f,.12f,b.size.z+.36f),ceilingMat,false);
            var light = Light("RoomBounce_"+floorName,root.transform,new Vector3(b.center.x,top-.45f,b.center.z),new Color(1,.91f,.78f),1.6f,Mathf.Max(b.size.x,b.size.z)*1.15f);
            light.shadows = LightShadows.None;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.36f,.39f,.43f);
        RenderSettings.ambientEquatorColor = new Color(.25f,.25f,.23f);
        RenderSettings.ambientGroundColor = new Color(.15f,.13f,.10f);
        RenderSettings.fog = false;
        var sun = UnityEngine.Object.FindObjectsByType<Light>().FirstOrDefault(x=>x.type==LightType.Directional);
        if(sun != null) { Undo.RecordObject(sun,"Memory daylight"); sun.intensity=.65f; sun.color=new Color(1,.9f,.76f); sun.shadows=LightShadows.Soft; sun.shadowStrength=.65f; }
        var player = UnityEngine.Object.FindAnyObjectByType<FirstPersonController>();
        var cam = player != null ? player.GetComponentInChildren<Camera>() : Camera.main;
        if(cam != null) { cam.GetUniversalAdditionalCameraData().renderPostProcessing=true; cam.GetUniversalAdditionalCameraData().volumeLayerMask |= 1; cam.allowHDR=true; }
        int layer = LayerMask.NameToLayer("Interactable");
        if(layer<0) throw new InvalidOperationException("Existing Interactable layer is required.");
        // The demo ends after the first memory. Remove only objects authored by this pass.
        foreach(var name in new[]{"MemoryDeskLamp","MemoryBirthdayGift"})
        {
            var owned=root.transform.Find(name);if(owned!=null) Undo.DestroyObjectImmediate(owned.gameObject);
        }
        var manager=UnityEngine.Object.FindAnyObjectByType<ChapterManager>();
        var placementObject=GameObject.Find("SmallBoxPlacement");
        var placement=placementObject!=null?placementObject.GetComponent<ItemPlacement>():null;
        if(manager==null || placement==null) throw new InvalidOperationException("Chapter 1 manager and fishbowl placement are required.");
        var managerData=new SerializedObject(manager);
        managerData.FindProperty("lostPetPlacement").objectReferenceValue=placement;
        managerData.FindProperty("lostPetRequiredItem").enumValueIndex=(int)ItemId.SmallBox;
        managerData.ApplyModifiedProperties();
        var titleView=Child(root.transform,"MemoryTitleView");
        var bedroom=BoundsOf(GameObject.Find("MyBedroomFloor"));
        titleView.transform.position=new Vector3(bedroom.max.x-.55f,bedroom.max.y+1.5f,bedroom.max.z-.55f);
        titleView.transform.rotation=Quaternion.LookRotation(new Vector3(-3.05f,1.0f,-2.2f)-titleView.transform.position);
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Resources/MemoryFontSerif.asset");
        foreach(var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include)) { Undo.RecordObject(t,"Chinese typography");t.font=font; }
        Localize();
        var scenes=EditorBuildSettings.scenes.ToList();
        if(!scenes.Any(x=>x.path==ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath,true));
        else scenes.First(x=>x.path==ScenePath).enabled=true;
        EditorBuildSettings.scenes=scenes.ToArray();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();
        foreach(var obsolete in new[]{"Assets/Game/Resources/MemoryFont.asset","Assets/Game/Art/Fonts/ZCOOLXiaoWei-Regular.ttf","Assets/Game/Art/Fonts/OFL.txt"})
            if(AssetDatabase.LoadMainAssetAtPath(obsolete)!=null) AssetDatabase.DeleteAsset(obsolete);
        Debug.Log("[MemoryVisualSetup] First memory: aligned ceilings, Chinese UI, room lighting and fishbowl placement ready.");
    }
    private static void EnsureFont()
    {
        Directory.CreateDirectory("Assets/Game/Resources");
        if(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Resources/MemoryFontSerif.asset")!=null) return;
        var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/Art/Fonts/NotoSerifSC-Memory.ttf");
        if(source==null) throw new InvalidOperationException("Open-source Chinese font has not imported.");
        var f=TMP_FontAsset.CreateFontAsset(source,48,6,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
        f.name="MemoryFontSerif";f.isMultiAtlasTexturesEnabled=true;
        AssetDatabase.CreateAsset(f,"Assets/Game/Resources/MemoryFontSerif.asset");
        AssetDatabase.AddObjectToAsset(f.material,f);
        foreach(var tex in f.atlasTextures) { tex.name="MemoryFont Atlas";AssetDatabase.AddObjectToAsset(tex,f); }
        AssetDatabase.SaveAssets();
    }
    private static void PrewarmFont()
    {
        var f=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Resources/MemoryFontSerif.asset");
        string characters=new string(Directory.GetFiles("Assets/Game/Scripts/UI","*.cs").SelectMany(File.ReadAllText).Concat("小盒子开门关门记忆回到旧家迟到的礼物").Where(c=>c>=32).Distinct().ToArray());
        f.TryAddCharacters(characters,out string missing);
        f.material.EnableKeyword("UNDERLAY_ON");
        f.material.SetColor("_UnderlayColor",new Color(.04f,.035f,.03f,.5f));
        f.material.SetFloat("_UnderlayOffsetY",-.3f);f.material.SetFloat("_UnderlaySoftness",.15f);
        EditorUtility.SetDirty(f);EditorUtility.SetDirty(f.material);AssetDatabase.SaveAssets();
    }
    private static void Localize()
    {
        foreach(var door in UnityEngine.Object.FindObjectsByType<DoorInteractable>())
        {
            var so=new SerializedObject(door);so.FindProperty("closedPrompt").stringValue="E  /  推开门";
            so.FindProperty("openPrompt").stringValue="E  /  轻轻关上";so.ApplyModifiedProperties();
        }
        foreach(var p in UnityEngine.Object.FindObjectsByType<PickupItem>())
        {
            var so=new SerializedObject(p);var prop=so.FindProperty("promptFormat");if(prop!=null) prop.stringValue="E  /  收起{0}";so.ApplyModifiedProperties();
        }
        foreach(var p in UnityEngine.Object.FindObjectsByType<ItemPlacement>())
        {
            var so=new SerializedObject(p);so.FindProperty("promptFormat").stringValue="E  /  把{0}放回这里";
            so.FindProperty("lockedPromptFormat").stringValue="这里，似乎少了一只{0}";so.ApplyModifiedProperties();
        }
    }
    public static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>();if(renderers.Length==0) throw new InvalidOperationException("No geometry: "+go.name);
        var b=renderers[0].bounds;foreach(var r in renderers.Skip(1)) b.Encapsulate(r.bounds);return b;
    }
    private static GameObject Child(Transform parent,string name)
    {
        var t=parent.Find(name);if(t!=null) return t.gameObject;
        var go=new GameObject(name);go.transform.SetParent(parent,false);return go;
    }
    private static GameObject Primitive(string name,Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Material material,bool collision)
    {
        var t=parent.Find(name);var go=t!=null?t.gameObject:GameObject.CreatePrimitive(type);
        go.name=name;go.transform.SetParent(parent,true);go.transform.position=position;
        // Author world dimensions even when a primitive is nested under the scaled gift lid.
        var ps=parent.lossyScale;go.transform.localScale=new Vector3(scale.x/ps.x,scale.y/ps.y,scale.z/ps.z);
        go.GetComponent<Renderer>().sharedMaterial=material;
        if(!collision && go.TryGetComponent<Collider>(out var c)) UnityEngine.Object.DestroyImmediate(c);
        return go;
    }
    private static Light Light(string name,Transform parent,Vector3 pos,Color color,float intensity,float range)
    {
        var go=Child(parent,name);go.transform.position=pos;
        var l=go.GetComponent<Light>(); if(l==null) l=go.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;return l;
    }
    private static Material Material(string name,Color color,float smoothness)
    {
        Directory.CreateDirectory("Assets/Game/Art/Materials");string path="Assets/Game/Art/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smoothness);return m;
    }
}
