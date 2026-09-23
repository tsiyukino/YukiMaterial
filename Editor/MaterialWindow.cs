using System.Collections.Generic;
using System.Linq;
using TsiYuki.Core.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace TsiYuki.Materials.Editor
{
    /// <summary>
    /// Where Yuki Material is set up.
    ///
    /// The components are a catalogue and a wiring diagram; this is the place
    /// they are written from. On the left is everything on the avatar — the
    /// menus, and the objects whose material slots they can reach. On the right
    /// is whatever is selected: a menu with its table of states, a slot with its
    /// looks, or one look with the material editor open on it.
    ///
    /// The table is the point of the window. A menu's states run down it and the
    /// slots it drives run across, so "pink" is one row that says what the hair
    /// wears and what the ears wear — which no inspector can show, because the
    /// two live on different objects.
    /// </summary>
    public class MaterialWindow : EditorWindow
    {
        static YukiLocalizer L => MaterialText.L;

        enum Pane { None, Menu, Slot, Look }

        [SerializeField] VRCAvatarDescriptor avatar;
        [SerializeField] Pane pane = Pane.None;
        [SerializeField] YukiMaterialMenu focusMenu;
        [SerializeField] YukiMaterial focusConfig;
        [SerializeField] string focusTargetId = "";
        [SerializeField] string focusLookId = "";

        Vector2 treeScroll, detailScroll, tableScroll;
        [SerializeField] bool advancedOpen;
        MaterialSet set;
        double lastRefresh;
        readonly HashSet<int> open = new HashSet<int>();

        // A row is only dragged once the mouse has meant it; until then it is
        // just a click that selects.
        YukiMaterial dragConfig;
        string dragTargetId;
        Vector2 dragStart;
        readonly MaterialEditDrawer drawer = new MaterialEditDrawer();

        const float TreeWidth = 250f;
        const float StateWidth = 190f;
        const float SlotWidth = 150f;

        static string _version;
        static string Version
        {
            get
            {
                if (_version != null) return _version;
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MaterialWindow).Assembly);
                return _version = info != null ? "v" + info.version : "";
            }
        }

        [MenuItem(YukiMenu.Root + "Material Editor")]
        public static void ShowWindow() => Open((Component)null);

        public static MaterialWindow Open(Component context)
        {
            var window = GetWindow<MaterialWindow>();
            window.titleContent = new GUIContent("Yuki Material");
            if (context != null)
            {
                var found = context.GetComponentInParent<VRCAvatarDescriptor>();
                if (found != null) window.avatar = found;
                var config = context as YukiMaterial;
                if (config != null) window.Select(config, config.targets.FirstOrDefault());
                var menu = context as YukiMaterialMenu;
                if (menu != null) window.Select(menu);
            }
            window.Refresh();
            window.Show();
            return window;
        }

        void OnEnable() => EditorApplication.hierarchyChanged += OnHierarchyChanged;

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            drawer.Release();
        }

        void OnHierarchyChanged() { set = null; Repaint(); }

        void Refresh()
        {
            set = avatar != null ? MaterialSet.Resolve(avatar.transform) : null;
            lastRefresh = EditorApplication.timeSinceStartup;
        }

        void Dirty() { set = null; Repaint(); }

        // -------------------------------------------------------- selection

        void Select(YukiMaterialMenu menu)
        {
            pane = Pane.Menu;
            focusMenu = menu;
            drawer.Release();
        }

        void Select(YukiMaterial config, MaterialTarget target)
        {
            pane = Pane.Slot;
            focusConfig = config;
            focusTargetId = target != null ? target.id : "";
            focusLookId = "";
            drawer.Release();
        }

        void Select(YukiMaterial config, MaterialTarget target, MaterialVariant look)
        {
            pane = Pane.Look;
            focusConfig = config;
            focusTargetId = target != null ? target.id : "";
            focusLookId = look != null ? look.id : "";
        }

        MaterialTarget FocusedTarget => focusConfig != null ? focusConfig.FindTarget(focusTargetId) : null;

        // ------------------------------------------------------------- draw

        void OnGUI()
        {
            // Resolving is cheap, but it happens before a frame is drawn rather
            // than during one: IMGUI lays out and repaints in two passes, and a
            // pass that finds a different answer from the one before it counts
            // as a broken layout.
            if (Event.current.type == EventType.Layout &&
                (set == null || EditorApplication.timeSinceStartup - lastRefresh > 1.0)) Refresh();

            if (Event.current.type == EventType.DragExited) dragConfig = null;

            YukiGUI.Header(L["ui.title"], Version);
            DrawAvatarBar();

            if (avatar == null)
            {
                EditorGUILayout.HelpBox(L["ui.pick_avatar"], MessageType.Info);
                return;
            }
            if (set == null) return;

            DrawProblems();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(TreeWidth)))
                {
                    treeScroll = EditorGUILayout.BeginScrollView(treeScroll, GUILayout.Width(TreeWidth));
                    DrawTree();
                    EditorGUILayout.EndScrollView();
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                    DrawDetail();
                    EditorGUILayout.EndScrollView();
                }
            }

            // A press that ended somewhere else was neither a click nor a drag.
            if (Event.current.type == EventType.MouseUp) dragConfig = null;
        }

        void DrawAvatarBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var picked = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(L["ui.avatar"], avatar, typeof(VRCAvatarDescriptor), true);
                if (EditorGUI.EndChangeCheck()) { avatar = picked; pane = Pane.None; Refresh(); }

                // Following the selection is what you want nine times out of ten.
                if (Selection.activeGameObject != null)
                {
                    var fromSelection = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();
                    if (fromSelection != null && fromSelection != avatar &&
                        GUILayout.Button(L["ui.window.use_selection"], EditorStyles.miniButton, GUILayout.Width(110)))
                    { avatar = fromSelection; pane = Pane.None; Refresh(); }
                }
            }

            EditorGUILayout.LabelField(set != null
                    ? L.Tr("ui.window.summary", set.Menus.Count, set.Slots.Count, set.TotalBits)
                    : " ",
                EditorStyles.miniLabel);
        }

        void DrawProblems()
        {
            if (set == null) return;
            foreach (var warning in set.Warnings) EditorGUILayout.HelpBox(warning.Message, MessageType.Warning);
            foreach (var error in set.Errors) EditorGUILayout.HelpBox(error.Message, MessageType.Error);
        }

        // ------------------------------------------------------------- tree

        void DrawTree()
        {
            YukiGUI.Section(L["ui.tree.menus"]);
            if (set.MenuComponents.Count == 0)
                EditorGUILayout.LabelField(L["ui.tree.no_menus"], YukiGUI.WrapMini);

            foreach (var menu in set.MenuComponents)
            {
                if (menu == null) continue;
                bool selected = pane == Pane.Menu && focusMenu == menu;
                using (new EditorGUILayout.HorizontalScope(selected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (GUILayout.Button(MaterialSet.NameOf(menu), selected ? EditorStyles.boldLabel : EditorStyles.label))
                        Select(menu);
                    GUILayout.Label(L.Tr("ui.menu.states_n", menu.states.Count), EditorStyles.miniLabel, GUILayout.Width(52));
                }
            }

            if (GUILayout.Button(L["ui.tree.new_menu"]))
            {
                var menu = MaterialActions.CreateMenu(avatar.gameObject, "");
                Select(menu);
                Dirty();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(6);
            YukiGUI.Section(L["ui.tree.objects"]);
            if (set.Catalogues.Count == 0)
                EditorGUILayout.LabelField(L["ui.tree.no_objects"], YukiGUI.WrapMini);

            foreach (var config in set.Catalogues)
            {
                if (config == null) continue;
                int key = config.GetInstanceID();
                bool expanded = open.Contains(key);

                using (new EditorGUILayout.HorizontalScope())
                {
                    var now = EditorGUILayout.Foldout(expanded, config.gameObject.name, true);
                    if (now != expanded) { if (now) open.Add(key); else open.Remove(key); }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(config.targets.Count.ToString(), EditorStyles.miniLabel, GUILayout.Width(18));
                }
                if (!open.Contains(key)) continue;

                foreach (var target in config.targets.ToList())
                {
                    if (target == null) continue;
                    bool selected = (pane == Pane.Slot || pane == Pane.Look) &&
                                    focusConfig == config && focusTargetId == target.id;
                    using (new EditorGUILayout.HorizontalScope(selected ? EditorStyles.helpBox : GUIStyle.none))
                    {
                        GUILayout.Space(12);
                        GUILayout.Label(MaterialSet.NameOf(target), selected ? EditorStyles.boldLabel : EditorStyles.label);
                        GUILayout.FlexibleSpace();
                        if (target.variants.Count > 0)
                            GUILayout.Label(target.variants.Count.ToString(), EditorStyles.miniLabel, GUILayout.Width(18));
                    }
                    SlotRow(GUILayoutUtility.GetLastRect(), config, target);
                }
            }

            var go = Selection.activeGameObject;
            bool usable = go != null && go.GetComponentInParent<VRCAvatarDescriptor>() == avatar;
            using (new EditorGUI.DisabledScope(!usable))
                if (GUILayout.Button(usable ? L.Tr("ui.tree.add_object", go.name) : L["ui.tree.add_hint"]))
                {
                    Catalogue(go);
                    Dirty();
                    GUIUtility.ExitGUI();
                }

            // Dragging the object in from the Hierarchy says the same thing as
            // selecting it and pressing the button, with one gesture instead of
            // two — and it works for an object you have not selected.
            System.Action dropped;
            MaterialDrag.Bar(L["ui.tree.drop"], DraggedObjects().Count > 0, CaptureObjects, out dropped);
            if (dropped != null) { dropped(); Dirty(); Repaint(); }
        }

        /// <summary>A row that selects when clicked and can be dragged into a
        /// menu's table.</summary>
        void SlotRow(Rect row, YukiMaterial config, MaterialTarget target)
        {
            var e = Event.current;
            bool candidate = dragConfig == config && dragTargetId == target.id;

            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                // Pressing only says which row is under the mouse. Selecting it
                // here would swap the right-hand side for this slot's settings
                // before the drag has begun — pulling the table the slot was
                // being dragged into out from under it.
                dragConfig = config;
                dragTargetId = target.id;
                dragStart = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && candidate &&
                     (e.mousePosition - dragStart).sqrMagnitude > 25f)
            {
                MaterialDrag.Begin(config, target, MaterialSet.NameOf(target));
                dragConfig = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && candidate)
            {
                // Released without going anywhere: that was a click.
                dragConfig = null;
                if (row.Contains(e.mousePosition)) Select(config, target);
                e.Use();
            }
            EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
        }

        /// <summary>Sets an object up and selects its first slot.</summary>
        YukiMaterial Catalogue(GameObject go)
        {
            var config = MaterialActions.CatalogueFor(go);
            MaterialActions.FillFrom(config, go, go.GetComponent<Renderer>() == null);
            open.Add(config.GetInstanceID());
            Select(config, config.targets.FirstOrDefault());
            return config;
        }

        List<GameObject> DraggedObjects()
        {
            var objects = avatar != null ? MaterialDrag.Objects(avatar.transform) : new List<GameObject>();
            return objects.Where(MaterialDrag.HasSlots).ToList();
        }

        System.Action CaptureObjects()
        {
            var objects = DraggedObjects();
            return () => { foreach (var go in objects) Catalogue(go); };
        }

        // ----------------------------------------------------------- detail

        void DrawDetail()
        {
            switch (pane)
            {
                case Pane.Menu:
                    if (focusMenu != null) { DrawMenu(focusMenu); return; }
                    break;
                case Pane.Slot:
                    if (FocusedTarget != null) { DrawSlot(focusConfig, FocusedTarget); return; }
                    break;
                case Pane.Look:
                    var target = FocusedTarget;
                    var look = target != null ? target.Find(focusLookId) : null;
                    if (look != null) { DrawLook(focusConfig, target, look); return; }
                    break;
            }
            EditorGUILayout.LabelField(L["ui.nothing_selected"], YukiGUI.WrapMini);
        }

        // ------------------------------------------------------------- menu

        void DrawMenu(YukiMaterialMenu menu)
        {
            YukiGUI.Section(MaterialSet.NameOf(menu));

            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField(new GUIContent(L["ui.menu_name"], L["ui.menu_name.tip"]), menu.displayName);
            var icon = (Texture2D)EditorGUILayout.ObjectField(L["ui.icon"], menu.icon, typeof(Texture2D), false, GUILayout.Height(16));
            var saved = EditorGUILayout.Toggle(new GUIContent(L["ui.saved"], L["ui.saved.tip"]), menu.saved);
            var parent = EditorGUILayout.ObjectField(new GUIContent(L["ui.menu_parent"], L["ui.menu_parent.tip"]), menu.menuParent, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                UndoEdit.Begin(menu, "Edit menu");
                menu.displayName = name;
                menu.icon = icon;
                menu.saved = saved;
                menu.menuParent = parent;
                UndoEdit.End(menu);
                Dirty();
            }

            advancedOpen = EditorGUILayout.Foldout(advancedOpen, L["ui.menu.advanced"], true);
            if (advancedOpen)
            {
                EditorGUI.BeginChangeCheck();
                // Two menus given the same name share one parameter, which is
                // the only reason to touch this.
                var parameter = EditorGUILayout.TextField(
                    new GUIContent(L["ui.menu.parameter"], L["ui.menu.parameter.tip"]), menu.parameterName);
                if (EditorGUI.EndChangeCheck())
                {
                    UndoEdit.Begin(menu, "Edit menu");
                    menu.parameterName = parameter;
                    UndoEdit.End(menu);
                    Dirty();
                }
                EditorGUILayout.LabelField(L.Tr("ui.menu.parameter.current",
                    set.Menus.FirstOrDefault(m => m.Config == menu)?.ParameterName ?? "Material/" + menu.id),
                    YukiGUI.WrapMini);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L["ui.menu.cost"], EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L["ui.select_object"], EditorStyles.miniButton, GUILayout.Width(60)))
                    EditorGUIUtility.PingObject(Selection.activeObject = menu.gameObject);
                if (GUILayout.Button(L["ui.remove"], EditorStyles.miniButton, GUILayout.Width(60)) &&
                    EditorUtility.DisplayDialog(L["ui.title"], L.Tr("ui.menu.remove.confirm", MaterialSet.NameOf(menu)), L["ui.ok"], L["ui.cancel"]))
                {
                    Undo.DestroyObjectImmediate(menu);
                    pane = Pane.None;
                    Dirty();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space(6);
            DrawTable(menu);
        }

        /// <summary>
        /// The states down the side, the slots across the top, and in every cell
        /// what that slot wears in that state. One row is one thing the menu can
        /// be set to, whatever it takes across however many objects.
        /// </summary>
        void DrawTable(YukiMaterialMenu menu)
        {
            YukiGUI.Section(L["ui.table.title"]);
            EditorGUILayout.LabelField(L["ui.table.help"], YukiGUI.WrapMini);

            if (menu.slots.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.table.empty"], MessageType.Info);
                DrawTableButtons(menu);
                return;
            }


            tableScroll = EditorGUILayout.BeginScrollView(tableScroll, GUILayout.Height(Mathf.Min(420, 80 + menu.states.Count * 24)));

            // ---- the slots across the top
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L["ui.table.state"], EditorStyles.miniBoldLabel, GUILayout.Width(StateWidth));
                foreach (var slot in menu.slots.ToList())
                {
                    if (slot == null) continue;
                    var resolved = set.Find(slot.source, slot.targetId);
                    var target = slot.source != null ? slot.source.FindTarget(slot.targetId) : null;
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(SlotWidth)))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var label = new GUIContent(slot.source != null ? slot.source.gameObject.name : "?",
                                                       L["ui.column.tip"]);
                            if (GUILayout.Button(label, EditorStyles.miniBoldLabel, GUILayout.Width(SlotWidth - 24)) && target != null)
                                Select(slot.source, target);
                            if (GUILayout.Button("⋯", EditorStyles.miniButton, GUILayout.Width(20)))
                                ColumnMenu(menu, slot);
                        }
                        var detail = target == null ? L["ui.column.missing"]
                                   : resolved == null ? MaterialSet.NameOf(target) + " · " + L["ui.column.unresolved"]
                                   : MaterialSet.NameOf(target);
                        GUILayout.Label(detail, EditorStyles.miniLabel, GUILayout.Width(SlotWidth - 4));
                    }
                }
            }

            // ---- one row per state
            MaterialState remove = null, makeDefault = null, moving = null;
            int move = 0;
            System.Action pending = null;

            foreach (var state in menu.states.ToList())
            {
                if (state == null) continue;
                bool isDefault = state.id == menu.Default?.id;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(StateWidth)))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUI.BeginChangeCheck();
                            var label = EditorGUILayout.TextField(state.displayName, GUILayout.Width(StateWidth - 52));
                            if (EditorGUI.EndChangeCheck())
                            {
                                UndoEdit.Begin(menu, "Rename state");
                                state.displayName = label;
                                UndoEdit.End(menu);
                            }
                            if (GUILayout.Button("⋯", EditorStyles.miniButton, GUILayout.Width(20)))
                                StateMenu(menu, state);
                            using (new EditorGUI.DisabledScope(menu.states.Count <= 1))
                                if (GUILayout.Button("×", GUILayout.Width(20))) remove = state;
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var icon = (Texture2D)EditorGUILayout.ObjectField(state.icon, typeof(Texture2D), false, GUILayout.Width(28), GUILayout.Height(16));
                            if (icon != state.icon)
                            {
                                UndoEdit.Begin(menu, "Set state icon");
                                state.icon = icon;
                                UndoEdit.End(menu);
                            }
                            using (new EditorGUI.DisabledScope(isDefault))
                                if (GUILayout.Button(isDefault ? L["ui.badge.default"] : L["ui.set_default"], EditorStyles.miniButton, GUILayout.Width(62)))
                                    makeDefault = state;

                            bool showing = MaterialPreviewState.IsShowing(menu, state);
                            if (GUILayout.Button(new GUIContent(showing ? L["ui.stop_preview"] : L["ui.try_on"], L["ui.try_on.tip"]),
                                                 EditorStyles.miniButton, GUILayout.Width(56)))
                                MaterialPreviewState.Show(menu, showing ? null : state);
                        }
                    }

                    foreach (var slot in menu.slots.ToList())
                    {
                        if (slot == null) continue;
                        var change = DrawCell(menu, state, slot);
                        if (change != null) pending = change;
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            if (pending != null) { pending(); Dirty(); GUIUtility.ExitGUI(); }
            if (makeDefault != null) { MaterialActions.SetDefaultState(menu, makeDefault); Dirty(); GUIUtility.ExitGUI(); }
            if (moving != null) { MaterialActions.MoveState(menu, moving, move); Dirty(); GUIUtility.ExitGUI(); }
            if (remove != null)
            {
                MaterialPreviewState.Show(menu, null);
                MaterialActions.RemoveState(menu, remove);
                Dirty();
                GUIUtility.ExitGUI();
            }

            DrawTableButtons(menu);
        }

        /// <summary>One cell: which look this slot wears in this state. The
        /// material it already has is always the first choice, because a state
        /// that is not about a slot has to be able to say so.</summary>
        System.Action DrawCell(YukiMaterialMenu menu, MaterialState state, MaterialSlotRef slot)
        {
            var target = slot.source != null ? slot.source.FindTarget(slot.targetId) : null;
            if (target == null)
            {
                GUILayout.Label("—", EditorStyles.miniLabel, GUILayout.Width(SlotWidth));
                return null;
            }

            var labels = new List<string> { L["ui.original"] };
            var ids = new List<string> { MaterialVariant.Original };
            foreach (var variant in target.variants)
            {
                if (variant == null) continue;
                labels.Add(MaterialSet.NameOf(target, variant));
                ids.Add(variant.id);
            }
            labels.Add(L["ui.table.new_look"]);

            var worn = state.Wears(slot.key);
            int current = Mathf.Max(0, ids.IndexOf(worn));
            int picked = EditorGUILayout.Popup(current, labels.ToArray(), GUILayout.Width(SlotWidth - 4));
            if (picked == current) return null;

            if (picked == labels.Count - 1)
                // Made here and worn here: building the table is how looks get
                // made, so making one should not mean leaving the table.
                return () =>
                {
                    var created = MaterialActions.AddVariant(slot.source, target,
                        MaterialSet.NameOf(state, menu.states.IndexOf(state) + 1));
                    MaterialActions.Wear(menu, state, slot.key, created.id);
                    Select(slot.source, target, created);
                };

            var chosen = ids[picked];
            return () => MaterialActions.Wear(menu, state, slot.key, chosen);
        }

        void DrawTableButtons(YukiMaterialMenu menu)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L["ui.table.add_state"], GUILayout.Width(140)))
                {
                    MaterialActions.AddState(menu, null);
                    Dirty();
                    GUIUtility.ExitGUI();
                }

                // Dragging a slot over from the list on the left is the short
                // way to say what this menu should move; the bar is also the
                // button, for when the list is long or nothing is open.
                System.Action dropped;
                bool clicked = MaterialDrag.Bar(L["ui.table.drop"], CanDropInto(menu), () => CaptureIntoMenu(menu),
                                                out dropped, GUILayout.MinWidth(200));
                if (dropped != null) { dropped(); Dirty(); Repaint(); }
                if (clicked) SlotPicker(menu);
            }
        }

        /// <summary>Whether what is being dragged is a slot this menu could
        /// drive, or an object with such slots on it.</summary>
        bool CanDropInto(YukiMaterialMenu menu)
        {
            YukiMaterial config;
            string targetId;
            if (MaterialDrag.Slot(out config, out targetId))
                return menu.FindSlot(config, targetId) == null && !Taken(menu, config, targetId);
            return DraggedObjects().Count > 0;
        }

        /// <summary>A slot already answering to another menu is not free; two
        /// menus on one slot is the conflict the panel reports, not something
        /// to make with a gesture.</summary>
        bool Taken(YukiMaterialMenu menu, YukiMaterial config, string targetId)
        {
            foreach (var other in set.MenuComponents)
                if (other != menu && other.Drives(config, targetId)) return true;
            return false;
        }

        System.Action CaptureIntoMenu(YukiMaterialMenu menu)
        {
            YukiMaterial config;
            string targetId;
            if (MaterialDrag.Slot(out config, out targetId))
            {
                var target = config.FindTarget(targetId);
                return () =>
                {
                    MaterialActions.AddSlotRef(menu, config, target);
                    Select(menu);
                };
            }

            // An object dropped on the table brings every slot it has that is
            // still free, which is what "the ears change too" means when the
            // ears have not been set up yet.
            var objects = DraggedObjects();
            return () =>
            {
                foreach (var go in objects)
                {
                    var owner = MaterialActions.CatalogueFor(go);
                    MaterialActions.FillFrom(owner, go, go.GetComponent<Renderer>() == null);
                    open.Add(owner.GetInstanceID());
                    foreach (var target in owner.targets.ToList())
                    {
                        if (target == null || Taken(menu, owner, target.id)) continue;
                        MaterialActions.AddSlotRef(menu, owner, target);
                    }
                }
                Select(menu);
            };
        }

        // ----------------------------------------------------------- pickers

        /// <summary>
        /// Every material slot on the avatar, whether or not it has been set up
        /// yet. Picking one that has not been catalogued makes its component on
        /// the spot: asking someone to go and prepare the ears first, then come
        /// back here, would be a step with no purpose.
        /// </summary>
        void SlotPicker(YukiMaterialMenu menu)
        {
            var picker = new GenericMenu();
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    int index = i;
                    var config = renderer.GetComponent<YukiMaterial>();
                    var existing = config != null
                        ? config.targets.FirstOrDefault(t => t != null && t.renderer == renderer && t.slot == index)
                        : null;

                    bool taken = existing != null && set.MenuComponents.Any(m => m.Drives(config, existing.id));
                    var label = new GUIContent(renderer.gameObject.name + "/" +
                                               (materials[index].name + " (" + L.Tr("ui.slot", index) + ")"));
                    if (taken) { picker.AddDisabledItem(label); continue; }

                    picker.AddItem(label, false, () =>
                    {
                        var owner = MaterialActions.CatalogueFor(renderer.gameObject);
                        var target = owner.targets.FirstOrDefault(t => t != null && t.renderer == renderer && t.slot == index)
                                     ?? MaterialActions.AddSlot(owner, renderer, index);
                        MaterialActions.AddSlotRef(menu, owner, target);
                        open.Add(owner.GetInstanceID());
                        Dirty();
                    });
                }
            }
            if (picker.GetItemCount() == 0) picker.AddDisabledItem(new GUIContent(L["ui.table.no_slots"]));
            picker.ShowAsContext();
        }

        void ColumnMenu(YukiMaterialMenu menu, MaterialSlotRef slot)
        {
            var context = new GenericMenu();
            context.AddItem(new GUIContent(L["ui.column.move_left"]), false, () => { MaterialActions.MoveSlotRef(menu, slot, -1); Dirty(); });
            context.AddItem(new GUIContent(L["ui.column.move_right"]), false, () => { MaterialActions.MoveSlotRef(menu, slot, 1); Dirty(); });
            context.AddSeparator("");
            context.AddItem(new GUIContent(L["ui.column.select"]), false, () =>
            {
                if (slot.source != null) EditorGUIUtility.PingObject(Selection.activeObject = slot.source.gameObject);
            });
            context.AddItem(new GUIContent(L["ui.column.remove"]), false, () => { MaterialActions.RemoveSlotRef(menu, slot); Dirty(); });
            context.ShowAsContext();
        }

        void StateMenu(YukiMaterialMenu menu, MaterialState state)
        {
            var context = new GenericMenu();
            context.AddItem(new GUIContent(L["ui.state.move_up"]), false, () => { MaterialActions.MoveState(menu, state, -1); Dirty(); });
            context.AddItem(new GUIContent(L["ui.state.move_down"]), false, () => { MaterialActions.MoveState(menu, state, 1); Dirty(); });
            context.AddSeparator("");
            context.AddItem(new GUIContent(L["ui.state.duplicate"]), false, () => { MaterialActions.DuplicateState(menu, state); Dirty(); });
            context.ShowAsContext();
        }

        // ------------------------------------------------------------- slot

        void DrawSlot(YukiMaterial config, MaterialTarget target)
        {
            YukiGUI.Section(MaterialSet.NameOf(target));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(config.gameObject.name + " · " + L.Tr("ui.slot", target.slot), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L["ui.select_object"], EditorStyles.miniButton, GUILayout.Width(60)))
                    EditorGUIUtility.PingObject(Selection.activeObject = config.gameObject);
                if (GUILayout.Button(L["ui.remove_slot"], EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    MaterialActions.RemoveSlot(config, target);
                    pane = Pane.None;
                    Dirty();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField(new GUIContent(L["ui.display_name"], L["ui.display_name.tip"]), target.displayName);
            if (EditorGUI.EndChangeCheck())
            {
                UndoEdit.Begin(config, "Rename material slot");
                target.displayName = name;
                UndoEdit.End(config);
            }

            // What drives it, if anything.
            var resolved = set.Find(config, target.id);
            var driver = resolved != null && resolved.Menu != null ? resolved.Menu.Config
                         : set.MenuComponents.FirstOrDefault(m => m.Drives(config, target.id));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (driver != null)
                {
                    GUILayout.Label(L.Tr("ui.slot.driven_by", MaterialSet.NameOf(driver)), EditorStyles.miniLabel);
                    if (GUILayout.Button(L["ui.slot.open_menu"], EditorStyles.miniButton, GUILayout.Width(90)))
                    { Select(driver); GUIUtility.ExitGUI(); }
                }
                else
                {
                    GUILayout.Label(L["ui.slot.not_driven"], EditorStyles.miniLabel);
                }
            }

            // The look it wears when nothing switches it.
            var labels = new List<string> { L["ui.original"] };
            var ids = new List<string> { MaterialVariant.Original };
            foreach (var variant in target.variants)
            {
                if (variant == null) continue;
                labels.Add(MaterialSet.NameOf(target, variant));
                ids.Add(variant.id);
            }
            using (new EditorGUI.DisabledScope(driver != null))
            {
                int current = Mathf.Max(0, ids.IndexOf(target.baseVariant));
                int picked = EditorGUILayout.Popup(new GUIContent(L["ui.slot.base"], L["ui.slot.base.tip"]), current, labels.ToArray());
                if (picked != current)
                {
                    MaterialActions.SetBaseVariant(config, target, ids[picked]);
                    Dirty();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.LabelField(driver != null ? L["ui.slot.base.driven"] : L["ui.slot.base.help"], YukiGUI.WrapMini);

            EditorGUILayout.Space(6);
            YukiGUI.Section(L["ui.looks"]);
            EditorGUILayout.LabelField(L["ui.looks.help"], YukiGUI.WrapMini);

            // A slot with no looks has nothing to edit, and a small button at
            // the bottom of an empty list does not look like the way in. This
            // is the way in, so it says so.
            if (target.variants.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.no_looks"], MessageType.Info);
                if (GUILayout.Button(L["ui.add_look.first"], GUILayout.Height(28)))
                {
                    var first = MaterialActions.AddVariant(config, target);
                    Select(config, target, first);
                    Dirty();
                    GUIUtility.ExitGUI();
                }
                return;
            }

            MaterialVariant remove = null, duplicate = null;
            foreach (var variant in target.variants.ToList())
            {
                if (variant == null) continue;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUI.BeginChangeCheck();
                    var label = EditorGUILayout.TextField(variant.displayName, GUILayout.Width(160));
                    if (EditorGUI.EndChangeCheck())
                    {
                        UndoEdit.Begin(config, "Rename look");
                        variant.displayName = label;
                        UndoEdit.End(config);
                    }
                    var icon = (Texture2D)EditorGUILayout.ObjectField(variant.icon, typeof(Texture2D), false, GUILayout.Width(28), GUILayout.Height(16));
                    if (icon != variant.icon)
                    {
                        UndoEdit.Begin(config, "Set look icon");
                        variant.icon = icon;
                        UndoEdit.End(config);
                    }

                    YukiGUI.StatusLabel(variant.Changes ? YukiStatus.Approximate : YukiStatus.Ok,
                        variant.Changes ? L.Tr("ui.changes", variant.edit.Count) : L["ui.unchanged"], GUILayout.Width(90));

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(L["ui.edit"], EditorStyles.miniButton, GUILayout.Width(60)))
                    { Select(config, target, variant); GUIUtility.ExitGUI(); }
                    if (GUILayout.Button(L["ui.duplicate"], EditorStyles.miniButton, GUILayout.Width(60))) duplicate = variant;
                    if (GUILayout.Button("×", GUILayout.Width(22))) remove = variant;
                }
            }

            if (GUILayout.Button(L["ui.add_look"], GUILayout.Width(140)))
            {
                var created = MaterialActions.AddVariant(config, target);
                Select(config, target, created);
                Dirty();
                GUIUtility.ExitGUI();
            }
            if (duplicate != null) { MaterialActions.DuplicateVariant(config, target, duplicate); Dirty(); GUIUtility.ExitGUI(); }
            if (remove != null) { MaterialActions.RemoveVariant(config, target, remove); Dirty(); GUIUtility.ExitGUI(); }
        }

        // ------------------------------------------------------------- look

        void DrawLook(YukiMaterial config, MaterialTarget target, MaterialVariant look)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L["ui.back_to_slot"], EditorStyles.miniButton, GUILayout.Width(110)))
                { Select(config, target); GUIUtility.ExitGUI(); }
                GUILayout.Label(config.gameObject.name + " · " + MaterialSet.NameOf(target), EditorStyles.miniLabel);
            }

            YukiGUI.Section(MaterialSet.NameOf(target, look));

            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField(new GUIContent(L["ui.look.name"], L["ui.look.name.tip"]), look.displayName);
            var icon = (Texture2D)EditorGUILayout.ObjectField(L["ui.icon"], look.icon, typeof(Texture2D), false, GUILayout.Height(16));
            if (EditorGUI.EndChangeCheck())
            {
                UndoEdit.Begin(config, "Rename look");
                look.displayName = name;
                look.icon = icon;
                UndoEdit.End(config);
            }

            // Where this look is worn, so it is never a mystery what editing it
            // will change in game.
            var wearers = new List<string>();
            foreach (var menu in set.MenuComponents)
            {
                var key = YukiMaterialMenu.KeyOf(config, target.id);
                if (menu.FindSlot(key) == null) continue;
                for (int i = 0; i < menu.states.Count; i++)
                    if (menu.states[i] != null && menu.states[i].Wears(key) == look.id)
                        wearers.Add(MaterialSet.NameOf(menu) + " · " + MaterialSet.NameOf(menu.states[i], i + 1));
            }
            if (target.baseVariant == look.id) wearers.Add(L["ui.slot.base"]);
            EditorGUILayout.LabelField(wearers.Count > 0
                ? L.Tr("ui.look.worn_in", string.Join(", ", wearers.ToArray()))
                : L["ui.look.worn_nowhere"], YukiGUI.WrapMini);

            EditorGUILayout.Space(4);
            drawer.Bind(config, target, look);
            drawer.Draw();
        }
    }
}
