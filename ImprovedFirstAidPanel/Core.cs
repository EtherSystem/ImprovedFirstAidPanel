using Il2CppInterop.Runtime.Injection;
using Il2CppTLD.IntBackedUnit;

[assembly: MelonInfo(typeof(ImprovedFirstAidPanel.Core), "Improved First Aid Panel", "1.0.1", "EtherSystem", null)]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace ImprovedFirstAidPanel
{
    public class Core : MelonMod
    {
        public override void OnInitializeMelon()
        {
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<BodyAreaClickProxy>();
                ClassInjector.RegisterTypeInIl2Cpp<TreatmentClickProxy>();
            }
            catch
            {
            }

            LoggerInstance.Msg("Initialized.");
        }
    }

    internal static class FirstAidBodyFilter
    {
        private static bool s_HasSelectedArea;
        private static AfflictionBodyArea s_SelectedArea;

        internal static bool HasSelectedArea => s_HasSelectedArea;
        internal static AfflictionBodyArea SelectedArea => s_SelectedArea;

        internal static bool Matches(AfflictionBodyArea area)
        {
            if (!s_HasSelectedArea) return true;
            return area == s_SelectedArea;
        }

        internal static void Clear()
        {
            s_HasSelectedArea = false;
        }

        internal static void Toggle(AfflictionBodyArea area)
        {
            if (s_HasSelectedArea && s_SelectedArea == area)
            {
                Clear();
                return;
            }

            s_SelectedArea = area;
            s_HasSelectedArea = true;
        }

        internal static void ToggleAndRefresh(Panel_FirstAid panel, AfflictionBodyArea area)
        {
            if (panel == null) return;

            bool wasActive = s_HasSelectedArea;
            AfflictionBodyArea previousArea = s_SelectedArea;

            Toggle(area);

            bool filterWasCleared = wasActive && !s_HasSelectedArea && previousArea == area;
            bool filterChanged = !filterWasCleared && s_HasSelectedArea;

            if (filterWasCleared || filterChanged)
                panel.m_SelectedAffButton = null;

            panel.RefreshScrollList();

            PanelFirstAidFilterPatches.SelectFirstVisibleAffliction(panel);

            panel.RefreshRightPage();
            panel.UpdateScrollbar();

            FirstAidTreatmentButtonManager.Refresh(panel);
        }
    }

    public sealed class BodyAreaClickProxy(IntPtr ptr) : MonoBehaviour(ptr)
    {
        private Panel_FirstAid _panel;
        private AfflictionBodyArea _area;

        public void Configure(Panel_FirstAid panel, AfflictionBodyArea area)
        {
            _panel = panel;
            _area = area;
        }

        public void OnClick()
        {
            if (_panel == null) return;

            FirstAidBodyFilter.ToggleAndRefresh(_panel, _area);
        }
    }

    internal static class FirstAidBodyClickTargets
    {
        private const float ClickTargetMinSize = 16f;
        private const float ClickTargetMaxSize = 24f;

        internal static void Refresh(Panel_FirstAid panel)
        {
            if (panel == null) return;
            if (panel.m_BodyIconList == null) return;

            for (int i = 0; i < panel.m_BodyIconList.Count; i++)
            {
                UISprite bodyIcon = panel.m_BodyIconList[i];
                if (bodyIcon == null) continue;

                ConfigureBodyIcon(panel, bodyIcon, (AfflictionBodyArea)i);
            }
        }

        private static void ConfigureBodyIcon(Panel_FirstAid panel, UISprite bodyIcon, AfflictionBodyArea area)
        {
            GameObject iconObject = bodyIcon.gameObject;
            if (iconObject == null) return;

            BodyAreaClickProxy proxy = iconObject.GetComponent<BodyAreaClickProxy>() ?? iconObject.AddComponent<BodyAreaClickProxy>();
            proxy.Configure(panel, area);

            SphereCollider collider = iconObject.GetComponent<SphereCollider>() ?? iconObject.AddComponent<SphereCollider>();

            float diameter = Mathf.Clamp(Mathf.Min(bodyIcon.width, bodyIcon.height), ClickTargetMinSize, ClickTargetMaxSize);
            float radius = diameter * 0.5f;

            collider.enabled = iconObject.activeInHierarchy && bodyIcon.alpha > 0.01f;
            collider.isTrigger = true;
            collider.center = Vector3.zero;
            collider.radius = radius;
        }
    }

    public sealed class TreatmentClickProxy(IntPtr ptr) : MonoBehaviour(ptr)
    {
        private static readonly Color s_NormalTextColor = new(0.68f, 0.68f, 0.64f, 1f);
        private static readonly Color s_HoverTextColor = new(0.86f, 0.86f, 0.80f, 1f);
        private static readonly Color s_PressedTextColor = new(0.55f, 0.55f, 0.52f, 1f);
        private static readonly Color s_DisabledTextColor = new(0.42f, 0.42f, 0.40f, 1f);

        private Panel_FirstAid _panel;
        private bool _altTreatment;
        private bool _hovered;
        private bool _pressed;
        private UILabel _label;
        private Vector3 _normalScale = Vector3.one;

        public void Configure(Panel_FirstAid panel, bool altTreatment)
        {
            _panel = panel;
            _altTreatment = altTreatment;
            _label = GetComponent<UILabel>();
            _normalScale = Vector3.one;

            ApplyVisualState();
        }

        public void Update()
        {
            bool hovered = UICamera.hoveredObject == gameObject;
            if (_hovered == hovered) return;

            _hovered = hovered;
            ApplyVisualState();
        }

        public void OnHover(bool isOver)
        {
            _hovered = isOver;
            ApplyVisualState();
        }

        public void OnPress(bool isDown)
        {
            _pressed = isDown;
            transform.localScale = isDown ? _normalScale * 0.96f : _normalScale;

            ApplyVisualState();
        }

        public void OnClick()
        {
            if (_panel == null) return;
            if (!_panel.IsEnabled()) return;
            if (FirstAidTreatmentRouter.IsTreatmentActive) return;

            FirstAidTreatmentRouter.StartTreatment(_panel, _altTreatment);
        }

        private void ApplyVisualState()
        {
            if (_label == null) return;

            if (FirstAidTreatmentRouter.IsTreatmentActive)
            {
                _label.color = s_DisabledTextColor;
                return;
            }

            if (_pressed)
            {
                _label.color = s_PressedTextColor;
                return;
            }

            _label.color = _hovered ? s_HoverTextColor : s_NormalTextColor;
        }
    }

    internal static class FirstAidTreatmentButtonManager
    {
        private const string MainButtonName = "FAO_TreatMainButton";
        private const string AltButtonName = "FAO_TreatAltButton";

        private const float ButtonWidth = 110f;
        private const float ButtonHeight = 28f;
        private const float ButtonYOffset = -45f;

        private static readonly Color s_ButtonTextColor = new(0.68f, 0.68f, 0.64f, 1f);
        private static readonly Color s_ButtonDisabledTextColor = new(0.42f, 0.42f, 0.40f, 1f);
        private static readonly Color s_ButtonOutlineColor = new(0.04f, 0.04f, 0.035f, 0.95f);

        internal static void Refresh(Panel_FirstAid panel)
        {
            if (panel == null) return;

            if (panel.m_SelectedAffButton == null)
            {
                HideAllButtons(panel);
                return;
            }

            bool hasMainTreatment = HasMainTreatment(panel);
            bool hasAltTreatment = HasAltTreatment(panel);

            bool showSingle = hasMainTreatment && !hasAltTreatment && IsSingleTreatmentVisible(panel);
            bool showMain = hasMainTreatment && hasAltTreatment && IsMainTreatmentVisible(panel);
            bool showAlt = hasAltTreatment && IsAltTreatmentVisible(panel);

            RefreshTreatmentButton(panel, panel.m_TreatmentWidgetSingle, MainButtonName, false, showSingle);
            RefreshTreatmentButton(panel, panel.m_TreatmentWidgetMultiLeft, MainButtonName, false, showMain);
            RefreshTreatmentButton(panel, panel.m_TreatmentWidgetMultiRight, AltButtonName, true, showAlt);
        }

        private static bool HasMainTreatment(Panel_FirstAid panel)
        {
            return panel.m_MainTreatmentItems != null && panel.m_MainTreatmentItems.Count > 0;
        }

        private static bool HasAltTreatment(Panel_FirstAid panel)
        {
            return panel.m_AltTreatmentItems != null && panel.m_AltTreatmentItems.Count > 0;
        }

        private static bool IsSingleTreatmentVisible(Panel_FirstAid panel)
        {
            if (panel.m_TreatmentWidgetSingle == null) return false;
            return panel.m_TreatmentWidgetSingle.alpha > 0.1f;
        }

        private static bool IsMainTreatmentVisible(Panel_FirstAid panel)
        {
            if (panel.m_TreatmentWidgetMultiLeft == null) return false;
            return panel.m_TreatmentWidgetMultiLeft.alpha > 0.1f;
        }

        private static bool IsAltTreatmentVisible(Panel_FirstAid panel)
        {
            if (panel.m_TreatmentWidgetMultiRight == null) return false;
            return panel.m_TreatmentWidgetMultiRight.alpha > 0.1f;
        }

        private static void RefreshTreatmentButton(Panel_FirstAid panel, UIWidget anchorWidget, string buttonName, bool altTreatment, bool shouldShow)
        {
            if (anchorWidget == null) return;

            GameObject buttonObject = FindOrCreateButton(panel, anchorWidget, buttonName, altTreatment);
            if (buttonObject == null) return;

            buttonObject.SetActive(shouldShow);

            if (!shouldShow) return;

            buttonObject.transform.localPosition = new Vector3(0f, ButtonYOffset, -1f);
            buttonObject.transform.localScale = Vector3.one;

            UpdateButton(buttonObject, panel, altTreatment);
        }

        private static GameObject FindOrCreateButton(Panel_FirstAid panel, UIWidget anchorWidget, string buttonName, bool altTreatment)
        {
            Transform existing = anchorWidget.transform.Find(buttonName);
            if (existing != null)
            {
                UpdateButton(existing.gameObject, panel, altTreatment);
                return existing.gameObject;
            }

            UILabel template = panel.m_LabelSpecialTreatment ?? panel.m_LabelAfflictionName;
            if (template == null) return null;

            GameObject buttonObject = UnityEngine.Object.Instantiate(template.gameObject);
            buttonObject.name = buttonName;
            buttonObject.SetActive(true);
            buttonObject.layer = anchorWidget.gameObject.layer;
            buttonObject.transform.SetParent(anchorWidget.transform, false);
            buttonObject.transform.localPosition = new Vector3(0f, ButtonYOffset, -1f);
            buttonObject.transform.localRotation = Quaternion.identity;
            buttonObject.transform.localScale = Vector3.one;

            UILabel label = buttonObject.GetComponent<UILabel>();
            if (label != null)
                ConfigureLabel(label, panel, altTreatment);

            BoxCollider collider = buttonObject.GetComponent<BoxCollider>() ?? buttonObject.AddComponent<BoxCollider>();
            collider.enabled = !FirstAidTreatmentRouter.IsTreatmentActive;
            collider.isTrigger = true;
            collider.center = Vector3.zero;
            collider.size = new Vector3(ButtonWidth, ButtonHeight, 1f);

            TreatmentClickProxy proxy = buttonObject.GetComponent<TreatmentClickProxy>() ?? buttonObject.AddComponent<TreatmentClickProxy>();
            proxy.Configure(panel, altTreatment);

            return buttonObject;
        }

        private static void UpdateButton(GameObject buttonObject, Panel_FirstAid panel, bool altTreatment)
        {
            if (buttonObject == null) return;

            UILabel label = buttonObject.GetComponent<UILabel>();
            if (label != null)
                ConfigureLabel(label, panel, altTreatment);

            BoxCollider collider = buttonObject.GetComponent<BoxCollider>() ?? buttonObject.AddComponent<BoxCollider>();
            collider.enabled = !FirstAidTreatmentRouter.IsTreatmentActive;
            collider.isTrigger = true;
            collider.center = Vector3.zero;
            collider.size = new Vector3(ButtonWidth, ButtonHeight, 1f);

            TreatmentClickProxy proxy = buttonObject.GetComponent<TreatmentClickProxy>() ?? buttonObject.AddComponent<TreatmentClickProxy>();
            proxy.Configure(panel, altTreatment);
        }

        private static void ConfigureLabel(UILabel label, Panel_FirstAid panel, bool altTreatment)
        {
            label.enabled = true;
            label.text = altTreatment ? "USE ALT" : "USE";
            label.color = FirstAidTreatmentRouter.IsTreatmentActive ? s_ButtonDisabledTextColor : s_ButtonTextColor;
            label.alpha = 1f;
            label.width = (int)ButtonWidth;
            label.height = (int)ButtonHeight;
            label.depth = GetButtonDepth(panel);
            label.alignment = NGUIText.Alignment.Center;
            label.effectStyle = UILabel.Effect.Outline;
            label.effectColor = s_ButtonOutlineColor;
            label.effectDistance = new Vector2(1f, 1f);
        }

        private static void HideAllButtons(Panel_FirstAid panel)
        {
            HideButton(panel.m_TreatmentWidgetSingle, MainButtonName);
            HideButton(panel.m_TreatmentWidgetMultiLeft, MainButtonName);
            HideButton(panel.m_TreatmentWidgetMultiRight, AltButtonName);
        }

        private static void HideButton(UIWidget anchorWidget, string buttonName)
        {
            if (anchorWidget == null) return;

            Transform existing = anchorWidget.transform.Find(buttonName);
            if (existing == null) return;

            existing.gameObject.SetActive(false);
        }

        private static int GetButtonDepth(Panel_FirstAid panel)
        {
            if (panel.m_LabelSpecialTreatment != null) return panel.m_LabelSpecialTreatment.depth + 100;
            if (panel.m_LabelAfflictionName != null) return panel.m_LabelAfflictionName.depth + 100;

            return 400;
        }
    }

    internal static class FirstAidTreatmentRouter
    {
        private const float BeginDebounceSeconds = 0.35f;

        private static float s_LastBeginTime;
        private static bool s_OverhaulTreatmentActive;
        private static int s_PendingAfflictionIndex;
        private static AfflictionType s_PendingAfflictionType;

        internal static bool IsTreatmentActive => s_OverhaulTreatmentActive;

        private static FieldInfo s_SelectedCustomAfflictionField;
        private static FieldInfo s_CustomAfflictionListField;
        private static MethodInfo s_GetAfflictionManagerInstanceMethod;
        private static bool s_ReflectionCached;

        internal static bool StartTreatment(Panel_FirstAid panel, bool altTreatment)
        {
            if (!Begin(panel)) return false;
            if (!CopyTreatmentItems(panel, altTreatment)) return false;

            return UseNextTreatmentItem(panel);
        }

        internal static bool TryHandleFirstAidItemCallback(Panel_FirstAid panel)
        {
            if (!s_OverhaulTreatmentActive) return true;

            if (panel == null)
            {
                s_OverhaulTreatmentActive = false;
                return false;
            }

            if (panel.m_TreatmentItemsToUse != null && panel.m_TreatmentItemsToUse.Count > 0)
            {
                panel.RefreshCheckmarks();
                panel.RefreshKit();

                UseNextTreatmentItem(panel);
                return false;
            }

            FinishOverhaulTreatment(panel);
            return false;
        }

        private static bool Begin(Panel_FirstAid panel)
        {
            if (panel == null) return false;
            if (panel.m_SelectedAffButton == null) return false;

            float now = Time.realtimeSinceStartup;
            if (now - s_LastBeginTime < BeginDebounceSeconds) return false;

            s_LastBeginTime = now;

            s_OverhaulTreatmentActive = true;
            s_PendingAfflictionType = panel.GetSelectedAfflictionType();
            s_PendingAfflictionIndex = panel.GetSelectedAfflictionIndex();

            if (s_PendingAfflictionType == AfflictionType.Generic)
                PrepareSelectedCustomAffliction(s_PendingAfflictionIndex);

            return true;
        }

        private static bool CopyTreatmentItems(Panel_FirstAid panel, bool altTreatment)
        {
            Il2CppSystem.Collections.Generic.List<string> source = altTreatment ? panel.m_AltTreatmentItems : panel.m_MainTreatmentItems;
            if (source == null || source.Count <= 0) return false;

            panel.m_TreatmentItemsToUse = new Il2CppSystem.Collections.Generic.List<string>();

            for (int i = 0; i < source.Count; i++)
                panel.m_TreatmentItemsToUse.Add(source[i]);

            return true;
        }

        private static bool UseNextTreatmentItem(Panel_FirstAid panel)
        {
            if (panel == null) return false;

            if (panel.m_TreatmentItemsToUse == null || panel.m_TreatmentItemsToUse.Count <= 0)
            {
                FinishOverhaulTreatment(panel);
                return false;
            }

            if (s_PendingAfflictionType == AfflictionType.IntestinalParasites && GameManager.GetIntestinalParasitesComponent().HasTakenDoseToday())
            {
                GameAudioManager.PlayGUIError();
                HUDMessage.AddMessage(Localization.Get("GAMEPLAY_IntestinalParasitesAlreadyTakenDose"));
                return false;
            }

            string itemName = panel.m_TreatmentItemsToUse[0];
            panel.m_TreatmentItemsToUse.RemoveAt(0);
            panel.m_ItemJustUsed = itemName;

            GearItem gearItem = GetTreatmentGearItem(itemName);
            if (gearItem == null)
            {
                GameAudioManager.PlayGUIError();
                HUDMessage.AddMessage(Localization.Get("GAMEPLAY_NoItemToUse"));
                return false;
            }

            if (gearItem.m_FirstAidItem == null)
            {
                GameManager.GetPlayerManagerComponent().UseInventoryItem(gearItem, ItemLiquidVolume.FromLiters(-1f), false);
                GameManager.GetPlayerManagerComponent().m_UsedItemFromFirstAidPanel = true;
                return true;
            }

            return UseFirstAidItemDirectly(panel, gearItem);
        }

        private static GearItem GetTreatmentGearItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return null;

            if (itemName.Contains("Water"))
                return GameManager.GetInventoryComponent().GetPotableWaterSupply();

            return GameManager.GetInventoryComponent().GearInInventory(itemName, 1);
        }

        private static bool UseFirstAidItemDirectly(Panel_FirstAid panel, GearItem gearItem)
        {
            if (panel == null) return false;
            if (gearItem == null || gearItem.m_FirstAidItem == null) return false;
            if (!CanUseFirstAidItem(gearItem)) return false;

            PlayerManager playerManager = GameManager.GetPlayerManagerComponent();
            if (playerManager == null) return false;

            bool result;

            if (s_PendingAfflictionType == AfflictionType.Generic)
            {
                if (!PrepareSelectedCustomAffliction(s_PendingAfflictionIndex)) return false;

                result = playerManager.TreatAfflictionWithFirstAid(gearItem.m_FirstAidItem, Affliction.InvalidAffliction);
                playerManager.m_UsedItemFromFirstAidPanel = true;
                return result;
            }

            if (!TryGetSelectedVanillaAffliction(panel, s_PendingAfflictionType, s_PendingAfflictionIndex, out Affliction selectedAffliction)) return false;

            result = playerManager.TreatAfflictionWithFirstAid(gearItem.m_FirstAidItem, selectedAffliction);
            playerManager.m_UsedItemFromFirstAidPanel = true;
            return result;
        }

        private static void FinishOverhaulTreatment(Panel_FirstAid panel)
        {
            s_OverhaulTreatmentActive = false;

            PlayerManager playerManager = GameManager.GetPlayerManagerComponent();
            if (playerManager != null)
                playerManager.m_UsedItemFromFirstAidPanel = false;

            if (panel == null) return;

            panel.m_TreatmentItemsToUse?.Clear();

            panel.m_ItemJustUsed = string.Empty;
            panel.m_SelectedFAKButton = null;
            panel.m_SelectedAffButton = null;

            UICamera.selectedObject = null;

            panel.RefreshScrollList();
            ForceRightPageStateFromVisibleList(panel);

            if (HasVisibleFirstAidEntries(panel))
            {
                PanelFirstAidFilterPatches.SelectFirstVisibleAffliction(panel);
                panel.RefreshRightPage();
            }
            else
            {
                panel.HideRightPage();
            }

            panel.RefreshKit();
            panel.RefreshCheckmarks();
            panel.UpdateScrollbar();

            FirstAidTreatmentButtonManager.Refresh(panel);
            FirstAidBodyClickTargets.Refresh(panel);
        }

        private static bool HasVisibleFirstAidEntries(Panel_FirstAid panel)
        {
            if (panel == null) return false;
            if (panel.m_ScrollListEffects == null) return false;
            if (panel.m_ScrollListEffects.m_ScrollObjects == null) return false;

            return panel.m_ScrollListEffects.m_ScrollObjects.Count > 0;
        }

        private static void ForceRightPageStateFromVisibleList(Panel_FirstAid panel)
        {
            if (panel == null) return;

            bool hasVisibleEntries = HasVisibleFirstAidEntries(panel);

            panel.m_RightPageObject?.SetActive(hasVisibleEntries);

            panel.m_RightPageHealthyObject?.SetActive(!hasVisibleEntries);
        }

        private static bool CanUseFirstAidItem(GearItem gearItem)
        {
            if (gearItem == null || gearItem.m_FirstAidItem == null) return false;

            StackableItem stackableItem = gearItem.m_StackableItem;
            if (stackableItem == null) return true;

            int available = GameManager.GetInventoryComponent().NumGearInInventory(gearItem.name);
            int required = gearItem.m_FirstAidItem.m_UnitsPerUse;

            if (available >= required) return true;

            GameAudioManager.PlayGUIError();
            HUDMessage.AddMessage(Localization.Get("GAMEPLAY_PillsRequiredValue").Replace("{num-pills}", required.ToString()));

            return false;
        }

        private static bool TryGetSelectedVanillaAffliction(Panel_FirstAid panel, AfflictionType selectedType, int selectedIndex, out Affliction selectedAffliction)
        {
            selectedAffliction = Affliction.InvalidAffliction;

            if (panel == null) return false;
            if (panel.m_ScrollListAfflictions == null) return false;

            int typeIndex = 0;

            for (int i = 0; i < panel.m_ScrollListAfflictions.Count; i++)
            {
                Affliction affliction = panel.m_ScrollListAfflictions[i];
                if (affliction.m_AfflictionType != selectedType) continue;

                if (typeIndex == selectedIndex)
                {
                    selectedAffliction = affliction;
                    return true;
                }

                typeIndex++;
            }

            return false;
        }

        private static bool PrepareSelectedCustomAffliction(int selectedIndex)
        {
            ClearSelectedCustomAffliction();

            if (!CacheAfflictionComponentReflection()) return false;

            object manager = s_GetAfflictionManagerInstanceMethod.Invoke(null, null);
            if (manager == null) return false;

            if (s_CustomAfflictionListField.GetValue(manager) is not IList afflictions) return false;
            if (selectedIndex < 0 || selectedIndex >= afflictions.Count) return false;

            object selectedCustomAffliction = afflictions[selectedIndex];
            if (selectedCustomAffliction == null) return false;

            s_SelectedCustomAfflictionField.SetValue(null, selectedCustomAffliction);
            return true;
        }

        private static void ClearSelectedCustomAffliction()
        {
            if (!s_ReflectionCached) return;
            if (s_SelectedCustomAfflictionField == null) return;

            s_SelectedCustomAfflictionField.SetValue(null, null);
        }

        private static bool CacheAfflictionComponentReflection()
        {
            if (s_ReflectionCached)
                return s_SelectedCustomAfflictionField != null
                       && s_CustomAfflictionListField != null
                       && s_GetAfflictionManagerInstanceMethod != null;

            s_ReflectionCached = true;

            Assembly afflictionComponentAssembly = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "AfflictionComponent") continue;

                afflictionComponentAssembly = assembly;
                break;
            }

            if (afflictionComponentAssembly == null) return false;

            Type globalFieldsType = afflictionComponentAssembly.GetType("AfflictionComponent.Patches.PanelAfflictionPatches.GlobalFields", false);
            Type managerType = afflictionComponentAssembly.GetType("AfflictionComponent.Components.AfflictionManager", false);

            if (globalFieldsType == null || managerType == null) return false;

            s_SelectedCustomAfflictionField = AccessTools.Field(globalFieldsType, "selectedCustomAffliction");
            s_CustomAfflictionListField = AccessTools.Field(managerType, "m_Afflictions");
            s_GetAfflictionManagerInstanceMethod = AccessTools.Method(managerType, "GetAfflictionManagerInstance");

            return s_SelectedCustomAfflictionField != null
                   && s_CustomAfflictionListField != null
                   && s_GetAfflictionManagerInstanceMethod != null;
        }
    }

    internal static class PanelFirstAidTreatmentPatches
    {
        [HarmonyPatch(typeof(Panel_FirstAid), nameof(Panel_FirstAid.Enable))]
        private static class EnablePatch
        {
            private static void Postfix(Panel_FirstAid __instance, bool enable)
            {
                if (__instance == null) return;
                if (!enable) return;

                FirstAidTreatmentButtonManager.Refresh(__instance);
            }
        }

        [HarmonyPatch(typeof(Panel_FirstAid), nameof(Panel_FirstAid.RefreshRightPage))]
        private static class RefreshRightPagePatch
        {
            private static void Postfix(Panel_FirstAid __instance)
            {
                if (__instance == null) return;

                FirstAidTreatmentButtonManager.Refresh(__instance);
            }
        }

        [HarmonyPatch(typeof(Panel_FirstAid), nameof(Panel_FirstAid.RefreshScrollList))]
        private static class RefreshScrollListPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Panel_FirstAid __instance)
            {
                if (__instance == null) return;

                FirstAidTreatmentButtonManager.Refresh(__instance);
            }
        }

        [HarmonyPatch(typeof(Panel_FirstAid), nameof(Panel_FirstAid.FirstAidItemCallback))]
        private static class FirstAidItemCallbackPatch
        {
            private static bool Prefix(Panel_FirstAid __instance)
            {
                return FirstAidTreatmentRouter.TryHandleFirstAidItemCallback(__instance);
            }
        }
    }

    internal static class PanelFirstAidFilterPatches
    {
        private static bool s_ApplyingFilter;

        private static readonly Vector3 s_NormalBodyIconScale = Vector3.one;
        private static readonly Vector3 s_SelectedBodyIconScale = new(1.35f, 1.35f, 1f);

        private const float ActiveBodyIconAlpha = 1f;
        private const float FadedBodyIconAlpha = 0.35f;

        [HarmonyPatch(typeof(Panel_FirstAid), nameof(Panel_FirstAid.RefreshScrollList))]
        private static class RefreshScrollListPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Panel_FirstAid __instance)
            {
                if (s_ApplyingFilter) return;
                if (__instance == null) return;
                if (__instance.m_ScrollListEffects == null) return;

                try
                {
                    s_ApplyingFilter = true;

                    if (FirstAidBodyFilter.HasSelectedArea)
                        ApplyBodyAreaFilter(__instance);

                    FirstAidBodyClickTargets.Refresh(__instance);
                    RefreshBodyAreaVisuals(__instance);
                }
                finally
                {
                    s_ApplyingFilter = false;
                }
            }
        }

        [HarmonyPatch(typeof(Panel_FirstAid), nameof(Panel_FirstAid.Enable))]
        private static class EnablePatch
        {
            private static void Postfix(Panel_FirstAid __instance, bool enable)
            {
                if (__instance == null) return;

                if (!enable)
                {
                    FirstAidBodyFilter.Clear();
                    return;
                }

                FirstAidBodyClickTargets.Refresh(__instance);
                RefreshBodyAreaVisuals(__instance);
            }
        }

        internal static void SelectFirstVisibleAffliction(Panel_FirstAid panel)
        {
            if (panel == null) return;
            if (panel.m_ScrollListEffects == null) return;

            panel.m_SelectedAffButton = null;

            int count = panel.m_ScrollListEffects.m_ScrollObjects.Count;
            for (int i = 0; i < count; i++)
            {
                AfflictionButton button = GetAfflictionButton(panel, i);
                if (button == null) continue;

                button.SetSelected(false);
            }

            if (count <= 0)
            {
                panel.HideRightPage();
                return;
            }

            AfflictionButton firstButton = GetAfflictionButton(panel, 0);
            if (firstButton == null)
            {
                panel.HideRightPage();
                return;
            }

            firstButton.SetSelected(true);
            panel.m_SelectedAffButton = firstButton;

            panel.UpdateBodyIconColors(firstButton, true, (int)firstButton.m_AfflictionLocation);
            panel.UpdateAllButSelectedBodyIconColors();
        }

        private static void ApplyBodyAreaFilter(Panel_FirstAid panel)
        {
            FirstAidEntrySnapshot selectedEntry = FirstAidEntrySnapshot.From(panel.m_SelectedAffButton);
            List<FirstAidEntrySnapshot> entries = CollectMatchingEntries(panel);

            panel.m_ScrollListEffects.CleanUp();

            if (entries.Count <= 0)
            {
                panel.m_ScrollListEffects.CreateList(0);
                panel.m_SelectedAffButton = null;
                panel.HideRightPage();
                panel.UpdateScrollbar();
                return;
            }

            int selectedIndex = GetSelectedEntryIndex(entries, selectedEntry);
            if (selectedIndex < 0) selectedIndex = 0;

            panel.m_ScrollListEffects.CreateList(entries.Count);
            panel.m_ScrollListEffects.SetTargetIndex(selectedIndex, false);

            for (int i = 0; i < entries.Count; i++)
            {
                AfflictionButton button = GetAfflictionButton(panel, i);
                if (button == null) continue;

                entries[i].ApplyTo(button);

                bool selected = i == selectedIndex;
                button.SetSelected(selected);

                if (!selected) continue;

                panel.m_SelectedAffButton = button;
                panel.UpdateBodyIconColors(button, true, (int)button.m_AfflictionLocation);
            }

            panel.UpdateAllButSelectedBodyIconColors();
            panel.UpdateScrollbar();
        }

        private static void RefreshBodyAreaVisuals(Panel_FirstAid panel)
        {
            if (panel == null) return;
            if (panel.m_BodyIconList == null) return;

            for (int i = 0; i < panel.m_BodyIconList.Count; i++)
            {
                UISprite bodyIcon = panel.m_BodyIconList[i];
                if (bodyIcon == null) continue;

                bodyIcon.transform.localScale = s_NormalBodyIconScale;
                bodyIcon.alpha = ActiveBodyIconAlpha;
            }

            if (!FirstAidBodyFilter.HasSelectedArea)
            {
                if (panel.m_SelectedAffButton != null)
                    panel.UpdateAllButSelectedBodyIconColors();

                return;
            }

            int selectedIndex = (int)FirstAidBodyFilter.SelectedArea;
            if (selectedIndex < 0 || selectedIndex >= panel.m_BodyIconList.Count) return;

            for (int i = 0; i < panel.m_BodyIconList.Count; i++)
            {
                UISprite bodyIcon = panel.m_BodyIconList[i];
                if (bodyIcon == null) continue;
                if (!bodyIcon.gameObject.activeSelf) continue;

                if (i == selectedIndex)
                {
                    bodyIcon.transform.localScale = s_SelectedBodyIconScale;
                    bodyIcon.alpha = ActiveBodyIconAlpha;
                    bodyIcon.color = new Color(panel.m_ColorAffliction.r, panel.m_ColorAffliction.g, panel.m_ColorAffliction.b, 1f);
                    continue;
                }

                bodyIcon.transform.localScale = s_NormalBodyIconScale;
                bodyIcon.alpha = FadedBodyIconAlpha;
            }
        }

        private static List<FirstAidEntrySnapshot> CollectMatchingEntries(Panel_FirstAid panel)
        {
            List<FirstAidEntrySnapshot> entries = [];

            int count = panel.m_ScrollListEffects.m_ScrollObjects.Count;
            for (int i = 0; i < count; i++)
            {
                AfflictionButton button = GetAfflictionButton(panel, i);
                if (button == null) continue;
                if (!FirstAidBodyFilter.Matches(button.m_AfflictionLocation)) continue;

                FirstAidEntrySnapshot entry = FirstAidEntrySnapshot.From(button);
                if (entry == null) continue;

                entries.Add(entry);
            }

            return entries;
        }

        private static AfflictionButton GetAfflictionButton(Panel_FirstAid panel, int index)
        {
            if (panel == null) return null;
            if (panel.m_ScrollListEffects == null) return null;
            if (index < 0 || index >= panel.m_ScrollListEffects.m_ScrollObjects.Count) return null;

            GameObject scrollObject = panel.m_ScrollListEffects.m_ScrollObjects[index];
            if (scrollObject == null) return null;
            if (scrollObject.transform.childCount <= 0) return null;

            return scrollObject.transform.GetChild(0).GetComponent<AfflictionButton>();
        }

        private static int GetSelectedEntryIndex(List<FirstAidEntrySnapshot> entries, FirstAidEntrySnapshot selectedEntry)
        {
            if (selectedEntry == null) return -1;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Matches(selectedEntry)) return i;
            }

            return -1;
        }

        private sealed class FirstAidEntrySnapshot
        {
            internal string Cause = string.Empty;
            internal AfflictionType Type;
            internal AfflictionBodyArea Location;
            internal int Index;
            internal string EffectName = string.Empty;
            internal string SpriteName = string.Empty;
            internal UIAtlas SpriteAtlas;

            internal static FirstAidEntrySnapshot From(AfflictionButton button)
            {
                if (button == null) return null;

                return new FirstAidEntrySnapshot
                {
                    Cause = button.m_LabelCause != null ? button.m_LabelCause.text : string.Empty,
                    Type = button.m_AfflictionType,
                    Location = button.m_AfflictionLocation,
                    Index = button.GetAfflictionIndex(),
                    EffectName = button.m_LabelEffect != null ? button.m_LabelEffect.text : string.Empty,
                    SpriteName = button.m_SpriteEffect != null ? button.m_SpriteEffect.spriteName : string.Empty,
                    SpriteAtlas = button.m_SpriteEffect?.atlas
                };
            }

            internal bool Matches(FirstAidEntrySnapshot other)
            {
                if (other == null) return false;
                if (Type != other.Type) return false;
                if (Location != other.Location) return false;

                return Index == other.Index;
            }

            internal void ApplyTo(AfflictionButton button)
            {
                if (button == null) return;

                button.SetCauseAndEffect(Cause, Type, Location, Index, EffectName, SpriteName);

                if (SpriteAtlas != null && button.m_SpriteEffect != null)
                    button.m_SpriteEffect.atlas = SpriteAtlas;
            }
        }
    }
}