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

        // One spacing scale for the whole window, on a 4 px grid: Gap between
        // things inside a block, Block between blocks, Gutter at the edges.
        internal const float Gap = 4f;
        internal const float Block = 8f;
        internal const float Gutter = 8f;

        const float RowHeight = 22f;
        const float Indent = 16f;
        const float ButtonHeight = 22f;

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
            window.minSize = new Vector2(640, 420);
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

        void OnEnable()
        {
            wantsMouseMove = true;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

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

            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.DragExited) dragConfig = null;

            using (new EditorGUILayout.VerticalScope(Styles.Top))
            {
                YukiGUI.Header(L["ui.title"], Version);
                EditorGUILayout.Space(Gap);
                DrawAvatarBar();

                if (avatar == null)
                {
                    EditorGUILayout.Space(Block);
                    EditorGUILayout.HelpBox(L["ui.pick_avatar"], MessageType.Info);
                    return;
                }
                if (set == null) return;

                DrawProblems();
            }

            Styles.HorizontalRule();

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                // The list sits on a slightly different ground from the
                // detail, so the two read as two places rather than one long
                // run of controls.
                using (new EditorGUILayout.VerticalScope(Styles.TreePane, GUILayout.Width(TreeWidth), GUILayout.ExpandHeight(true)))
                {
                    treeScroll = EditorGUILayout.BeginScrollView(treeScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
                    using (new EditorGUILayout.VerticalScope(Styles.TreeInner))
                        DrawTree();
                    EditorGUILayout.EndScrollView();
                }

                Styles.VerticalRule();

                using (new EditorGUILayout.VerticalScope())
                {
                    detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                    using (new EditorGUILayout.VerticalScope(Styles.DetailPane))
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
                    if (fromSelection != null && fromSelection != avatar)
                    {
                        GUILayout.Space(Gap);
                        if (GUILayout.Button(L["ui.window.use_selection"], GUILayout.ExpandWidth(false)))
                        { avatar = fromSelection; pane = Pane.None; Refresh(); }
                    }
                }
            }

            if (set != null)
            {
                EditorGUILayout.Space(2);
                GUILayout.Label(L.Tr("ui.window.summary", set.Menus.Count, set.Slots.Count, set.TotalBits), Styles.Subtitle);
            }
        }

        void DrawProblems()
        {
            if (set == null || set.Warnings.Count() + set.Errors.Count() == 0) return;
            EditorGUILayout.Space(Block);
            foreach (var warning in set.Warnings) EditorGUILayout.HelpBox(warning.Message, MessageType.Warning);
            foreach (var error in set.Errors) EditorGUILayout.HelpBox(error.Message, MessageType.Error);
        }

        // ------------------------------------------------------------- tree

        void DrawTree()
        {
            TreeHeading(L["ui.tree.menus"], true);
            if (set.MenuComponents.Count == 0)
            {
                EditorGUILayout.LabelField(L["ui.tree.no_menus"], YukiGUI.WrapMini);
                EditorGUILayout.Space(Gap);
            }

            foreach (var menu in set.MenuComponents)
            {
                if (menu == null) continue;
                bool selected = pane == Pane.Menu && focusMenu == menu;
                var row = TreeRow(selected);
                var count = TreeCount(row, L.Tr("ui.menu.states_n", menu.states.Count));
                GUI.Label(new Rect(row.x + Block, row.y, Mathf.Max(0, count.x - Gap - row.x - Block), row.height),
                          MaterialSet.NameOf(menu), selected ? Styles.RowSelected : Styles.Row);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) Select(menu);
                EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
            }

            EditorGUILayout.Space(Gap);
            if (GUILayout.Button(L["ui.tree.new_menu"], GUILayout.Height(ButtonHeight)))
            {
                var menu = MaterialActions.CreateMenu(avatar.gameObject, "");
                Select(menu);
                Dirty();
                GUIUtility.ExitGUI();
            }

            TreeHeading(L["ui.tree.objects"], false);
            if (set.Catalogues.Count == 0)
            {
                EditorGUILayout.LabelField(L["ui.tree.no_objects"], YukiGUI.WrapMini);
                EditorGUILayout.Space(Gap);
            }

            foreach (var config in set.Catalogues)
            {
                if (config == null) continue;
                int key = config.GetInstanceID();
                bool expanded = open.Contains(key);

                var row = TreeRow(false);
                var count = TreeCount(row, config.targets.Count.ToString());
                var fold = new Rect(row.x + Gap, row.y, Mathf.Max(0, count.x - Gap - row.x - Gap), row.height);
                var now = EditorGUI.Foldout(fold, expanded, config.gameObject.name, true, Styles.Foldout);
                if (now != expanded) { if (now) open.Add(key); else open.Remove(key); }
                if (!open.Contains(key)) continue;

                foreach (var target in config.targets.ToList())
                {
                    if (target == null) continue;
                    bool selected = (pane == Pane.Slot || pane == Pane.Look) &&
                                    focusConfig == config && focusTargetId == target.id;
                    var slot = TreeRow(selected);
                    var looks = target.variants.Count > 0 ? TreeCount(slot, target.variants.Count.ToString())
                                                          : new Rect(slot.xMax - Block, slot.y, 0, slot.height);
                    var x = slot.x + Block + Indent;
                    GUI.Label(new Rect(x, slot.y, Mathf.Max(0, looks.x - Gap - x), slot.height),
                              MaterialSet.NameOf(target), selected ? Styles.RowSelected : Styles.Row);
                    SlotRow(slot, config, target);
                }
            }

            EditorGUILayout.Space(Gap);
            var go = Selection.activeGameObject;
            bool usable = go != null && go.GetComponentInParent<VRCAvatarDescriptor>() == avatar;
            using (new EditorGUI.DisabledScope(!usable))
                if (GUILayout.Button(usable ? L.Tr("ui.tree.add_object", go.name) : L["ui.tree.add_hint"], GUILayout.Height(ButtonHeight)))
                {
                    Catalogue(go);
                    Dirty();
                    GUIUtility.ExitGUI();
                }

            // Dragging the object in from the Hierarchy says the same thing as
            // selecting it and pressing the button, with one gesture instead of
            // two — and it works for an object you have not selected.
            EditorGUILayout.Space(Gap);
            System.Action dropped;
            MaterialDrag.Bar(L["ui.tree.drop"], DraggedObjects().Count > 0, CaptureObjects, out dropped);
            if (dropped != null) { dropped(); Dirty(); Repaint(); }
        }

        static void TreeHeading(string title, bool first)
        {
            if (!first) EditorGUILayout.Space(Block * 2);
            GUILayout.Label(title, Styles.SectionTitle);
            EditorGUILayout.Space(Gap);
        }

        /// <summary>One row of the list, with its selection and hover drawn.
        /// Every row is the same height and shape whether or not it is
        /// selected, so selecting one moves nothing.</summary>
        static Rect TreeRow(bool selected)
        {
            var row = GUILayoutUtility.GetRect(10, RowHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(row, Styles.Selected);
                    EditorGUI.DrawRect(new Rect(row.x, row.y, 2, row.height), Styles.Accent);
                }
                else if (row.Contains(Event.current.mousePosition) && GUIUtility.hotControl == 0)
                    EditorGUI.DrawRect(row, Styles.Hover);
            }
            return row;
        }

        /// <summary>A count at the right end of a row; returns where it went so
        /// the label can stop short of it.</summary>
        static Rect TreeCount(Rect row, string text)
        {
            var width = Styles.Count.CalcSize(new GUIContent(text)).x;
            var rect = new Rect(row.xMax - Block - width, row.y, width, row.height);
            GUI.Label(rect, text, Styles.Count);
            return rect;
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

            GUILayout.Space(Block * 6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(L["ui.nothing_selected"], Styles.EmptyNote, GUILayout.MaxWidth(360));
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>The title of the right-hand side: what is open, a line
        /// saying where it lives, and the actions that belong to the whole of
        /// it, kept to the right so they are always in the same place.</summary>
        static void PaneHeader(string title, string subtitle, System.Action actions)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Label(title, Styles.PaneTitle);
                    if (!string.IsNullOrEmpty(subtitle)) GUILayout.Label(subtitle, Styles.Subtitle);
                }
                if (actions != null)
                {
                    // Straight into the row, not wrapped in a group of their
                    // own: a nested group takes a share of the spare width
                    // and leaves the buttons short of the edge.
                    GUILayout.Space(Block);
                    actions();
                }
            }
            EditorGUILayout.Space(Block);
        }

        internal static void Heading(string title, string help)
        {
            EditorGUILayout.Space(Block * 2);
            GUILayout.Label(title, Styles.SectionTitle);
            if (!string.IsNullOrEmpty(help))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(help, YukiGUI.WrapMini);
            }
            EditorGUILayout.Space(Block);
        }

        /// <summary>
        /// An icon as a small picture that opens the picker when clicked and
        /// takes a texture dropped on it. Unity's own texture field at this
        /// size prints its "Select" caption outside its box, and at one line
        /// high it is a name too narrow to read.
        /// </summary>
        static Texture2D IconField(Texture2D value)
        {
            const float size = 20f;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            var e = Event.current;
            bool hover = rect.Contains(e.mousePosition);

            if (e.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rect, false, false, false, false);
                if (value != null)
                    GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), value, ScaleMode.ScaleToFit, true);
                else
                    GUI.Label(rect, "+", Styles.IconPlaceholder);
                if (hover) EditorGUI.DrawRect(rect, Styles.Hover);
            }
            GUI.Label(rect, new GUIContent("", value != null ? value.name : L["ui.icon"]), GUIStyle.none);

            switch (e.type)
            {
                case EventType.MouseDown when hover && e.button == 0:
                    EditorGUIUtility.ShowObjectPicker<Texture2D>(value, false, "", id);
                    e.Use();
                    break;
                case EventType.ExecuteCommand when e.commandName == "ObjectSelectorUpdated" &&
                                                   EditorGUIUtility.GetObjectPickerControlID() == id:
                    GUI.changed = true;
                    e.Use();
                    return EditorGUIUtility.GetObjectPickerObject() as Texture2D;
                case EventType.DragUpdated when hover && DragAndDrop.objectReferences.OfType<Texture2D>().Any():
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    e.Use();
                    break;
                case EventType.DragPerform when hover && DragAndDrop.objectReferences.OfType<Texture2D>().Any():
                    DragAndDrop.AcceptDrag();
                    GUI.changed = true;
                    e.Use();
                    return DragAndDrop.objectReferences.OfType<Texture2D>().First();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return value;
        }

        static bool HeaderButton(string label, float width = 0f)
        {
            return width > 0f
                ? GUILayout.Button(label, GUILayout.Height(ButtonHeight), GUILayout.Width(width))
                : GUILayout.Button(label, GUILayout.Height(ButtonHeight), GUILayout.ExpandWidth(false));
        }

        // ------------------------------------------------------------- menu

        void DrawMenu(YukiMaterialMenu menu)
        {
            PaneHeader(MaterialSet.NameOf(menu), L["ui.menu.cost"], () =>
            {
                if (HeaderButton(L["ui.select_object"]))
                    EditorGUIUtility.PingObject(Selection.activeObject = menu.gameObject);
                GUILayout.Space(Gap);
                if (HeaderButton(L["ui.remove"]) &&
                    EditorUtility.DisplayDialog(L["ui.title"], L.Tr("ui.menu.remove.confirm", MaterialSet.NameOf(menu)), L["ui.ok"], L["ui.cancel"]))
                {
                    Undo.DestroyObjectImmediate(menu);
                    pane = Pane.None;
                    Dirty();
                    GUIUtility.ExitGUI();
                }
            });

            using (new EditorGUILayout.VerticalScope(Styles.Card))
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField(new GUIContent(L["ui.menu_name"], L["ui.menu_name.tip"]), menu.displayName);
                var icon = (Texture2D)EditorGUILayout.ObjectField(L["ui.icon"], menu.icon, typeof(Texture2D), false,
                                                                  GUILayout.Height(EditorGUIUtility.singleLineHeight));
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

                EditorGUILayout.Space(Gap);
                advancedOpen = EditorGUILayout.Foldout(advancedOpen, L["ui.menu.advanced"], true);
                if (advancedOpen)
                {
                    using (new EditorGUI.IndentLevelScope())
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
                }
            }

            DrawTable(menu);
        }

        /// <summary>
        /// The states down the side, the slots across the top, and in every cell
        /// what that slot wears in that state. One row is one thing the menu can
        /// be set to, whatever it takes across however many objects.
        /// </summary>
        void DrawTable(YukiMaterialMenu menu)
        {
            Heading(L["ui.table.title"], L["ui.table.help"]);

            if (menu.slots.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.table.empty"], MessageType.Info);
                EditorGUILayout.Space(Block);
                DrawTableButtons(menu);
                return;
            }

            // Measured off the drawn table: the header is two lines, a state
            // row two controls high plus its padding.
            const float headerHeight = 48f, rowHeight = 58f;
            tableScroll = EditorGUILayout.BeginScrollView(tableScroll,
                GUILayout.Height(Mathf.Min(480, headerHeight + menu.states.Count * rowHeight + Block * 2)));

            // ---- the slots across the top
            using (new EditorGUILayout.HorizontalScope(Styles.TableHeader))
            {
                GUILayout.Label(L["ui.table.state"], Styles.ColumnTitle, GUILayout.Width(StateWidth));
                foreach (var slot in menu.slots.ToList())
                {
                    if (slot == null) continue;
                    GUILayout.Space(Block);
                    var resolved = set.Find(slot.source, slot.targetId);
                    var target = slot.source != null ? slot.source.FindTarget(slot.targetId) : null;
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(SlotWidth)))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var label = new GUIContent(slot.source != null ? slot.source.gameObject.name : "?",
                                                       L["ui.column.tip"]);
                            if (GUILayout.Button(label, Styles.ColumnTitle, GUILayout.Width(SlotWidth - 24)) && target != null)
                                Select(slot.source, target);
                            if (GUILayout.Button("⋯", EditorStyles.miniButton, GUILayout.Width(20)))
                                ColumnMenu(menu, slot);
                        }
                        var detail = target == null ? L["ui.column.missing"]
                                   : resolved == null ? MaterialSet.NameOf(target) + " · " + L["ui.column.unresolved"]
                                   : MaterialSet.NameOf(target);
                        GUILayout.Label(detail, Styles.Subtitle, GUILayout.Width(SlotWidth - 4));
                    }
                }
            }

            // ---- one row per state
            MaterialState remove = null, makeDefault = null, moving = null;
            int move = 0;
            System.Action pending = null;

            int index = 0;
            foreach (var state in menu.states.ToList())
            {
                if (state == null) continue;
                bool isDefault = state.id == menu.Default?.id;
                using (new EditorGUILayout.HorizontalScope((index++ & 1) == 0 ? Styles.TableRow : Styles.TableRowAlt))
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
                                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) remove = state;
                        }

                        EditorGUILayout.Space(2);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var icon = IconField(state.icon);
                            GUILayout.Space(Gap);
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
                        GUILayout.Space(Block);
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

            EditorGUILayout.Space(Block);
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
            int picked = EditorGUILayout.Popup(current, labels.ToArray(), GUILayout.Width(SlotWidth));
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
                if (GUILayout.Button(L["ui.table.add_state"], GUILayout.Width(140), GUILayout.Height(MaterialDrag.BarHeight)))
                {
                    MaterialActions.AddState(menu, null);
                    Dirty();
                    GUIUtility.ExitGUI();
                }
                GUILayout.Space(Block);

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
            PaneHeader(MaterialSet.NameOf(target), config.gameObject.name + " · " + L.Tr("ui.slot", target.slot), () =>
            {
                if (HeaderButton(L["ui.select_object"]))
                    EditorGUIUtility.PingObject(Selection.activeObject = config.gameObject);
                GUILayout.Space(Gap);
                if (HeaderButton(L["ui.remove_slot"]))
                {
                    MaterialActions.RemoveSlot(config, target);
                    pane = Pane.None;
                    Dirty();
                    GUIUtility.ExitGUI();
                }
            });

            // What drives it, if anything.
            var resolved = set.Find(config, target.id);
            var driver = resolved != null && resolved.Menu != null ? resolved.Menu.Config
                         : set.MenuComponents.FirstOrDefault(m => m.Drives(config, target.id));

            using (new EditorGUILayout.VerticalScope(Styles.Card))
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField(new GUIContent(L["ui.display_name"], L["ui.display_name.tip"]), target.displayName);
                if (EditorGUI.EndChangeCheck())
                {
                    UndoEdit.Begin(config, "Rename material slot");
                    target.displayName = name;
                    UndoEdit.End(config);
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
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(driver != null ? L["ui.slot.base.driven"] : L["ui.slot.base.help"], YukiGUI.WrapMini);

                EditorGUILayout.Space(Block);
                using (new EditorGUILayout.HorizontalScope())
                {
                    YukiGUI.StatusLabel(driver != null ? YukiStatus.Ok : YukiStatus.None,
                        driver != null ? L.Tr("ui.slot.driven_by", MaterialSet.NameOf(driver)) : L["ui.slot.not_driven"]);
                    GUILayout.FlexibleSpace();
                    if (driver != null && GUILayout.Button(L["ui.slot.open_menu"], EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    { Select(driver); GUIUtility.ExitGUI(); }
                }
            }

            Heading(L["ui.looks"], L["ui.looks.help"]);

            // A slot with no looks has nothing to edit, and a small button at
            // the bottom of an empty list does not look like the way in. This
            // is the way in, so it says so.
            if (target.variants.Count == 0)
            {
                EditorGUILayout.HelpBox(L["ui.no_looks"], MessageType.Info);
                EditorGUILayout.Space(Block);
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
                using (new EditorGUILayout.HorizontalScope(Styles.ListRow))
                {
                    var icon = IconField(variant.icon);
                    if (icon != variant.icon)
                    {
                        UndoEdit.Begin(config, "Set look icon");
                        variant.icon = icon;
                        UndoEdit.End(config);
                    }
                    GUILayout.Space(Gap);

                    EditorGUI.BeginChangeCheck();
                    var label = EditorGUILayout.TextField(variant.displayName, Styles.RowField, GUILayout.MinWidth(100));
                    if (EditorGUI.EndChangeCheck())
                    {
                        UndoEdit.Begin(config, "Rename look");
                        variant.displayName = label;
                        UndoEdit.End(config);
                    }
                    GUILayout.Space(Block);

                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(100)))
                    {
                        GUILayout.Space(2);
                        using (new EditorGUILayout.HorizontalScope())
                            YukiGUI.StatusLabel(variant.Changes ? YukiStatus.Approximate : YukiStatus.Ok,
                                variant.Changes ? L.Tr("ui.changes", variant.edit.Count) : L["ui.unchanged"]);
                    }
                    GUILayout.Space(Block);

                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(144)))
                    {
                        GUILayout.Space(1);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(L["ui.edit"], EditorStyles.miniButtonLeft, GUILayout.Width(60)))
                            { Select(config, target, variant); GUIUtility.ExitGUI(); }
                            if (GUILayout.Button(L["ui.duplicate"], EditorStyles.miniButtonMid, GUILayout.Width(60))) duplicate = variant;
                            if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(24))) remove = variant;
                        }
                    }
                }
                EditorGUILayout.Space(Gap);
            }

            EditorGUILayout.Space(Gap);
            if (GUILayout.Button(L["ui.add_look"], GUILayout.Width(140), GUILayout.Height(ButtonHeight)))
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
            // Where this is, and the way back up, on one line above the title.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L["ui.back_to_slot"], EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                { Select(config, target); GUIUtility.ExitGUI(); }
                GUILayout.Space(Block);
                GUILayout.Label(config.gameObject.name + "  ›  " + MaterialSet.NameOf(target), Styles.Breadcrumb);
            }
            EditorGUILayout.Space(Block);

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

            PaneHeader(MaterialSet.NameOf(target, look), wearers.Count > 0
                ? L.Tr("ui.look.worn_in", string.Join(", ", wearers.ToArray()))
                : L["ui.look.worn_nowhere"], null);

            using (new EditorGUILayout.VerticalScope(Styles.Card))
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField(new GUIContent(L["ui.look.name"], L["ui.look.name.tip"]), look.displayName);
                var icon = (Texture2D)EditorGUILayout.ObjectField(L["ui.icon"], look.icon, typeof(Texture2D), false,
                                                                  GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (EditorGUI.EndChangeCheck())
                {
                    UndoEdit.Begin(config, "Rename look");
                    look.displayName = name;
                    look.icon = icon;
                    UndoEdit.End(config);
                }
            }

            drawer.Bind(config, target, look);
            drawer.Draw();
        }

        // ------------------------------------------------------ look and feel

        internal static class Styles
        {
            static bool Pro => EditorGUIUtility.isProSkin;

            public static Color Accent => Pro ? new Color(0.36f, 0.62f, 1f) : new Color(0.15f, 0.42f, 0.86f);
            public static Color Selected => Pro ? new Color(0.36f, 0.62f, 1f, 0.22f) : new Color(0.15f, 0.42f, 0.86f, 0.18f);
            public static Color Hover => Pro ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.06f);
            public static Color Rule => Pro ? new Color(0f, 0f, 0f, 0.4f) : new Color(0f, 0f, 0f, 0.18f);
            static Color TreeGround => Pro ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.74f, 0.74f, 0.74f);
            static Color HeaderGround => Pro ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.7f, 0.7f, 0.7f);
            static Color RowGround => Pro ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.8f, 0.8f, 0.8f);
            static Color RowGroundAlt => Pro ? new Color(0.235f, 0.235f, 0.235f) : new Color(0.78f, 0.78f, 0.78f);

            public static void HorizontalRule()
            {
                var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, Rule);
            }

            public static void VerticalRule()
            {
                var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, Rule);
            }

            static RectOffset Pad(float left, float right, float top, float bottom) =>
                new RectOffset((int)left, (int)right, (int)top, (int)bottom);

            // Solid grounds, remade when the skin changes or a domain reload
            // drops them.
            static readonly Dictionary<Color, Texture2D> Fills = new Dictionary<Color, Texture2D>();
            static Texture2D Fill(Color color)
            {
                Texture2D texture;
                if (Fills.TryGetValue(color, out texture) && texture != null) return texture;
                texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                texture.SetPixel(0, 0, color);
                texture.Apply();
                return Fills[color] = texture;
            }

            static GUIStyle Ground(GUIStyle style, Color color)
            {
                style.normal.background = Fill(color);
                return style;
            }

            static bool _pro;
            static GUIStyle _top, _treePane, _treeInner, _detailPane, _card, _listRow, _tableHeader, _tableRow, _tableRowAlt;

            /// <summary>Anything that carries a colour is rebuilt when the
            /// skin changes, or it would keep the old one.</summary>
            static void CheckSkin()
            {
                if (_pro == Pro && _treePane != null && _treePane.normal.background != null) return;
                _pro = Pro;
                _treePane = _tableHeader = _tableRow = _tableRowAlt = null;
            }

            public static GUIStyle Top => _top ??= new GUIStyle { padding = Pad(Gutter, Gutter, Block, Block) };
            public static GUIStyle TreePane { get { CheckSkin(); return _treePane ??= Ground(new GUIStyle(), TreeGround); } }
            public static GUIStyle TreeInner => _treeInner ??= new GUIStyle { padding = Pad(Block, Block, Block + Gap, Block * 2) };
            public static GUIStyle DetailPane => _detailPane ??= new GUIStyle { padding = Pad(Block * 2, Block * 2, Block + Gap, Block * 3) };
            public static GUIStyle Card => _card ??= new GUIStyle(EditorStyles.helpBox) { padding = Pad(Block, Block, Block, Block), margin = Pad(0, 0, 0, Gap) };
            public static GUIStyle ListRow => _listRow ??= new GUIStyle(EditorStyles.helpBox) { padding = Pad(Gap + 2, Gap + 2, Gap, Gap), margin = Pad(0, 0, 0, 0) };
            public static GUIStyle TableHeader { get { CheckSkin(); return _tableHeader ??= Ground(new GUIStyle { padding = Pad(Block, Block, Gap + 2, Gap + 2) }, HeaderGround); } }
            public static GUIStyle TableRow { get { CheckSkin(); return _tableRow ??= Ground(new GUIStyle { padding = Pad(Block, Block, Gap + 2, Gap + 2) }, RowGround); } }
            public static GUIStyle TableRowAlt { get { CheckSkin(); return _tableRowAlt ??= Ground(new GUIStyle { padding = Pad(Block, Block, Gap + 2, Gap + 2) }, RowGroundAlt); } }

            static GUIStyle _paneTitle, _sectionTitle, _subtitle, _row, _rowSelected, _count, _foldout, _breadcrumb,
                            _columnTitle, _emptyNote, _rowField;

            public static GUIStyle PaneTitle => _paneTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, padding = Pad(0, 0, 0, 2) };
            public static GUIStyle SectionTitle => _sectionTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, padding = Pad(0, 0, 0, 0) };
            public static GUIStyle Subtitle => _subtitle ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, padding = Pad(0, 0, 0, 0) };
            public static GUIStyle Row => _row ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = Pad(0, 0, 0, 0) };
            public static GUIStyle RowSelected => _rowSelected ??= new GUIStyle(Row) { fontStyle = FontStyle.Bold };
            public static GUIStyle Count => _count ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, padding = Pad(0, 0, 0, 0) };
            public static GUIStyle Foldout => _foldout ??= new GUIStyle(EditorStyles.foldout) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            public static GUIStyle Breadcrumb => _breadcrumb ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, fixedHeight = 18 };
            public static GUIStyle ColumnTitle => _columnTitle ??= new GUIStyle(EditorStyles.miniBoldLabel) { clipping = TextClipping.Clip };
            public static GUIStyle EmptyNote => _emptyNote ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true, fontSize = 11 };
            public static GUIStyle RowField => _rowField ??= new GUIStyle(EditorStyles.textField) { fixedHeight = 20, alignment = TextAnchor.MiddleLeft };
            static GUIStyle _iconPlaceholder;
            public static GUIStyle IconPlaceholder => _iconPlaceholder ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { padding = Pad(0, 0, 0, 0) };
        }
    }
}
