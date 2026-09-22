using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 任务3「任务道具拾取与最小物品栏」的场景配置工具（增量、幂等，不重建场景）：
/// - 在 Player 上挂载 Inventory（容量 3）；
/// - 创建/复用测试物件 Pickup_SmallBox（立方体小盒，Interactable Layer + Collider + PickupItem），
///   并保存为 Assets/Game/Prefabs/Pickup_SmallBox.prefab；
/// - 创建/复用 UI_Inventory 画布（3 个槽位 + InventoryHUD），并与 Inventory 接线。
/// 菜单：Tools/交互系统/配置物品拾取与物品栏（任务3）。
/// </summary>
public static class InventorySystemSetup
{
    private const string PlayerName = "Player";
    private const string BoxName = "Pickup_SmallBox";
    private const string CanvasName = "UI_Inventory";
    private const string PanelName = "SlotsPanel";

    private const string BoxMaterialPath = "Assets/Game/Prefabs/M_SmallBox.mat";
    private const string BoxPrefabPath = "Assets/Game/Prefabs/Pickup_SmallBox.prefab";

    private const int Capacity = 3;
    private const float SlotWidth = 190f;
    private const float SlotHeight = 64f;

    [MenuItem("Tools/交互系统/配置物品拾取与物品栏（任务3）")]
    public static void SetupViaMenu()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[物品拾取] 配置完成并已保存场景。");
    }

    /// <summary>执行全部配置，返回玩家身上的 Inventory。</summary>
    public static Inventory Setup()
    {
        int interactableLayer = InteractionSystemSetup.EnsureInteractableLayer();
        InteractionSystemSetup.EnsureTMPEssentials();

        Inventory inventory = EnsureInventory();
        EnsureSmallBox(interactableLayer);
        EnsureInventoryCanvas(inventory);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return inventory;
    }

    // ---------- 1. Player + Inventory ----------

    private static Inventory EnsureInventory()
    {
        GameObject player = GameObject.Find(PlayerName);
        if (player == null)
        {
            throw new System.InvalidOperationException("场景中找不到名为 " + PlayerName + " 的物体。");
        }

        Inventory inventory = player.GetComponent<Inventory>();
        if (inventory == null)
        {
            inventory = Undo.AddComponent<Inventory>(player);
        }

        var serialized = new SerializedObject(inventory);
        serialized.FindProperty("capacity").intValue = Capacity;
        serialized.ApplyModifiedProperties();
        return inventory;
    }

    // ---------- 2. 小盒测试物件 ----------

    private static void EnsureSmallBox(int interactableLayer)
    {
        GameObject box = GameObject.Find(BoxName);
        bool created = false;

        if (box == null)
        {
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = BoxName;
            created = true;
            Undo.RegisterCreatedObjectUndo(box, "配置物品拾取");

            // 0.25m 见方，放在玩家初始位置前方地面上（玩家位于 (-3.36, 0, 1.016)，面朝 -Z）
            box.transform.localScale = Vector3.one * 0.25f;
            box.transform.position = new Vector3(-3.0f, 0.125f, 0.5f);
        }

        SetLayerRecursively(box, interactableLayer);

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = EnsureBoxMaterial();
        }

        PickupItem pickup = box.GetComponent<PickupItem>();
        if (pickup == null)
        {
            pickup = Undo.AddComponent<PickupItem>(box);
        }

        var pickupSerialized = new SerializedObject(pickup);
        pickupSerialized.FindProperty("itemId").enumValueIndex = (int)ItemId.SmallBox;
        pickupSerialized.ApplyModifiedProperties();

        // 保存/更新预制体，方便复制出“两个相同 Id 物件”的测试场景
        PrefabUtility.SaveAsPrefabAsset(box, BoxPrefabPath);

        if (created)
        {
            Debug.Log("[物品拾取] 已创建测试物件 " + BoxName + " 并保存预制体：" + BoxPrefabPath);
        }
    }

    private static Material EnsureBoxMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(BoxMaterialPath);
        if (material != null)
        {
            return material;
        }

        // URP 项目使用 Universal Lit；找不到时回退 Standard，保证不报错
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        material = new Material(shader)
        {
            name = "M_SmallBox"
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", new Color(0.72f, 0.48f, 0.28f, 1f)); // 木盒棕
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", new Color(0.72f, 0.48f, 0.28f, 1f));
        }

        AssetDatabase.CreateAsset(material, BoxMaterialPath);
        return material;
    }

    // ---------- 3. 物品栏 UI ----------

    private static void EnsureInventoryCanvas(Inventory inventory)
    {
        GameObject canvasObject = GameObject.Find(CanvasName);
        bool created = false;

        if (canvasObject == null)
        {
            canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "配置物品栏 UI");
            created = true;

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        Transform panel = EnsureSlotsPanel(canvasObject.transform);
        List<TMP_Text> slotLabels = EnsureSlots(panel);

        InventoryHUD hud = canvasObject.GetComponent<InventoryHUD>();
        if (hud == null)
        {
            hud = Undo.AddComponent<InventoryHUD>(canvasObject);
        }

        var hudSerialized = new SerializedObject(hud);
        hudSerialized.FindProperty("inventory").objectReferenceValue = inventory;

        SerializedProperty slotsProperty = hudSerialized.FindProperty("slotTexts");
        slotsProperty.arraySize = slotLabels.Count;
        for (int i = 0; i < slotLabels.Count; i++)
        {
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slotLabels[i];
        }

        hudSerialized.ApplyModifiedProperties();

        if (created)
        {
            Debug.Log("[物品拾取] 已创建物品栏 UI " + CanvasName + "（" + slotLabels.Count + " 个槽位）。");
        }
    }

    private static Transform EnsureSlotsPanel(Transform canvasTransform)
    {
        Transform panel = canvasTransform.Find(PanelName);
        if (panel == null)
        {
            var panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(panelObject, "配置物品栏 UI");
            panel = panelObject.transform;
            panel.SetParent(canvasTransform, false);

            var rect = (RectTransform)panel;
            // 固定在屏幕右上角
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(Capacity * SlotWidth + 40f, SlotHeight + 16f);

            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        return panel;
    }

    private static List<TMP_Text> EnsureSlots(Transform panel)
    {
        var labels = new List<TMP_Text>();
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;

        for (int i = 0; i < Capacity; i++)
        {
            string slotName = "Slot_" + i;
            Transform slot = panel.Find(slotName);

            if (slot == null)
            {
                // 槽位根：半透明黑底 + 固定布局尺寸
                var slotObject = new GameObject(slotName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                Undo.RegisterCreatedObjectUndo(slotObject, "配置物品栏 UI");
                slot = slotObject.transform;
                slot.SetParent(panel, false);

                slot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

                var layoutElement = slot.GetComponent<LayoutElement>();
                layoutElement.minWidth = SlotWidth;
                layoutElement.minHeight = SlotHeight;
                layoutElement.preferredWidth = SlotWidth;
                layoutElement.preferredHeight = SlotHeight;

                // 槽位文本：铺满槽位、居中
                var labelObject = new GameObject("Label", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(labelObject, "配置物品栏 UI");
                var labelRect = (RectTransform)labelObject.transform;
                labelRect.SetParent(slot, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var label = labelObject.AddComponent<TextMeshProUGUI>();
                label.font = font;
                label.fontSize = 26f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.text = "—";
            }

            TMP_Text slotLabel = slot.GetComponentInChildren<TMP_Text>();
            labels.Add(slotLabel);
        }

        return labels;
    }

    // ---------- 辅助 ----------

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
