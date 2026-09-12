using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEventAuditor.Editor
{
    public class UnityEventAuditorWindow : EditorWindow
    {
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        private string _searchQuery = string.Empty;
        private readonly string[] _filterOptions = { "All", "Valid", "Broken", "Empty" };
        private string _selectedFilter = "All";

        private bool _includeProjectPrefabs;
        private bool _includeScriptableObjects;

        private List<UnityEventScanner.EventEntry> _allEventEntries = new();
        private List<UnityEventScanner.EventEntry> _filteredEntries = new();
        private AuditSummary _summary;

        private readonly HashSet<GameObject> _gameObjectsWithEvents = new();
        private readonly HashSet<GameObject> _selectedGameObjects = new();
        private readonly List<ScriptableObject> _scriptableObjectsWithEvents = new();
        private ScriptableObject _selectedScriptableObject;

        private readonly Dictionary<GameObject, bool> _expandedStates = new();

        private bool _isDirty;

        private GameObject _lastClickedGameObject;
        private readonly List<GameObject> _flatGameObjectList = new();

        [MenuItem("Tools/Unity Event Auditor")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityEventAuditorWindow>("Unity Event Auditor");
            window.minSize = new Vector2(860, 440);
        }

        private void OnEnable()
        {
            RefreshEventList();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.update -= OnUpdate;
        }

        private void OnHierarchyChanged() => _isDirty = true;

        private void OnPrefabStageChanged(PrefabStage _) => _isDirty = true;

        private void OnUndoRedoPerformed() => _isDirty = true;

        private void OnUpdate()
        {
            if (!_isDirty) return;
            _isDirty = false;
            RefreshEventList();
            Repaint();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => RefreshEventList();

        private void OnSceneClosed(Scene scene) => RefreshEventList();

        private AuditScanScope GetActiveScope()
        {
            var scope = AuditScanScope.ActiveScenes;
            if (_includeProjectPrefabs) scope |= AuditScanScope.ProjectPrefabs;
            if (_includeScriptableObjects) scope |= AuditScanScope.ScriptableObjects;
            return scope;
        }

        private void RefreshEventList()
        {
            _allEventEntries = UnityEventScanner.Scan(GetActiveScope());
            _summary = UnityEventScanner.ComputeSummary(_allEventEntries);
            RebuildFilteredView();
        }

        private void RebuildFilteredView()
        {
            ApplyFilterAndSearch();

            _gameObjectsWithEvents.Clear();
            _scriptableObjectsWithEvents.Clear();
            var matchedGameObjects = new HashSet<GameObject>();
            var seenScriptableObjects = new HashSet<ScriptableObject>();

            foreach (var entry in _filteredEntries)
            {
                var go = GetEntryGameObject(entry);
                if (go)
                {
                    matchedGameObjects.Add(go);

                    var t = go.transform;
                    while (t)
                    {
                        _gameObjectsWithEvents.Add(t.gameObject);
                        t = t.parent;
                    }
                }
                else if (entry.OwnerObject is ScriptableObject so && seenScriptableObjects.Add(so))
                {
                    _scriptableObjectsWithEvents.Add(so);
                }
            }

            _scriptableObjectsWithEvents.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            var staleKeys = _expandedStates.Keys.Where(go => !go || !_gameObjectsWithEvents.Contains(go)).ToList();
            foreach (var key in staleKeys)
                _expandedStates.Remove(key);

            _selectedGameObjects.RemoveWhere(go => !go || !_gameObjectsWithEvents.Contains(go));
            if (!_lastClickedGameObject || !_gameObjectsWithEvents.Contains(_lastClickedGameObject))
                _lastClickedGameObject = null;

            if (_selectedScriptableObject && !seenScriptableObjects.Contains(_selectedScriptableObject))
                _selectedScriptableObject = null;

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                foreach (var go in matchedGameObjects)
                {
                    foreach (var parentTransform in GetParents(go.transform))
                        _expandedStates[parentTransform.gameObject] = true;
                }
            }

            return;

            static IEnumerable<Transform> GetParents(Transform transform)
            {
                var parent = transform.parent;
                while (parent)
                {
                    yield return parent;
                    parent = parent.parent;
                }
            }
        }

        private static GameObject GetEntryGameObject(UnityEventScanner.EventEntry entry)
        {
            if (entry == null || !entry.OwnerObject)
                return null;

            return entry.OwnerObject switch
            {
                GameObject obj => obj,
                Component comp => comp.gameObject,
                _ => null
            };
        }

        private void ApplyFilterAndSearch()
        {
            _filteredEntries = string.IsNullOrEmpty(_searchQuery)
                ? new List<UnityEventScanner.EventEntry>(_allEventEntries)
                : _allEventEntries.FindAll(e =>
                    e.OwnerName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.FieldName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(e.PropertyPath) && e.PropertyPath.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0));

            switch (_selectedFilter)
            {
                case "Valid":
                    _filteredEntries.RemoveAll(e => e.IsEmpty || e.HasBrokenListeners);
                    break;
                case "Broken":
                    _filteredEntries.RemoveAll(e => !e.HasBrokenListeners);
                    break;
                case "Empty":
                    _filteredEntries.RemoveAll(e => !e.IsEmpty);
                    break;
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSummaryBar();
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65)))
                RefreshEventList();

            GUILayout.Space(6);
            GUILayout.Label("Search:", GUILayout.Width(48));
            var newQuery = GUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField, GUILayout.Width(180));
            if (newQuery != _searchQuery)
            {
                _searchQuery = newQuery;
                RebuildFilteredView();
            }

            GUILayout.Space(6);
            GUILayout.Label("Filter:", GUILayout.Width(38));
            var selectedIndex = EditorGUILayout.Popup(
                Array.IndexOf(_filterOptions, _selectedFilter),
                _filterOptions,
                EditorStyles.toolbarPopup,
                GUILayout.Width(80));
            if (selectedIndex >= 0 && _filterOptions[selectedIndex] != _selectedFilter)
            {
                _selectedFilter = _filterOptions[selectedIndex];
                RebuildFilteredView();
            }

            GUILayout.Space(6);
            var newIncludePrefabs = GUILayout.Toggle(_includeProjectPrefabs, "Prefabs", EditorStyles.toolbarButton, GUILayout.Width(64));
            var newIncludeSOs = GUILayout.Toggle(_includeScriptableObjects, "ScriptableObjects", EditorStyles.toolbarButton, GUILayout.Width(118));
            if (newIncludePrefabs != _includeProjectPrefabs || newIncludeSOs != _includeScriptableObjects)
            {
                _includeProjectPrefabs = newIncludePrefabs;
                _includeScriptableObjects = newIncludeSOs;
                RefreshEventList();
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_summary.BrokenListeners == 0))
            {
                if (GUILayout.Button("Fix All Broken", EditorStyles.toolbarButton, GUILayout.Width(96)))
                {
                    var removed = EditorUtils.RemoveAllBrokenListeners(_allEventEntries);
                    if (removed > 0)
                    {
                        RefreshEventList();
                    }
                }
            }

            if (GUILayout.Button("Export CSV", EditorStyles.toolbarButton, GUILayout.Width(78)))
                ExportReport(asJson: false);

            if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton, GUILayout.Width(84)))
                ExportReport(asJson: true);

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                _searchQuery = string.Empty;
                _selectedFilter = "All";
                RebuildFilteredView();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummaryBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label($"Events: {_summary.TotalEvents}", EditorStyles.miniBoldLabel);
            GUILayout.Space(12);
            GUILayout.Label($"Listeners: {_summary.TotalListeners}", EditorStyles.miniLabel);
            GUILayout.Space(12);
            GUILayout.Label($"Broken: {_summary.BrokenListeners} (in {_summary.EventsWithBrokenListeners} event(s))",
                _summary.BrokenListeners > 0 ? EditorStyles.boldLabel : EditorStyles.miniLabel);
            GUILayout.Space(12);
            GUILayout.Label($"Empty/Unused: {_summary.EmptyEvents}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void ExportReport(bool asJson)
        {
            var ext = asJson ? "json" : "csv";
            var path = EditorUtility.SaveFilePanel("Export UnityEvent Audit Report", Application.dataPath, $"unity_event_audit.{ext}", ext);
            if (string.IsNullOrEmpty(path)) return;

            var content = asJson
                ? UnityEventAuditExporter.ToJson(_filteredEntries)
                : UnityEventAuditExporter.ToCsv(_filteredEntries);

            File.WriteAllText(path, content);
            Debug.Log($"[UnityEventAuditor] Exported {_filteredEntries.Count} event(s) to '{path}'.");
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(310));
            GUILayout.Label("Objects With Events", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Expand All", GUILayout.Width(95)))
                SetAllExpanded(true);
            if (GUILayout.Button("Collapse All", GUILayout.Width(95)))
                SetAllExpanded(false);
            EditorGUILayout.EndHorizontal();

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            _flatGameObjectList.Clear();

            var roots = GetRootGameObjects(_gameObjectsWithEvents);
            if (roots.Count == 0 && _scriptableObjectsWithEvents.Count == 0)
            {
                EditorGUILayout.LabelField("No matching UnityEvents found.");
            }
            else
            {
                foreach (var root in roots)
                {
                    DrawGameObjectHierarchy(root, 0);
                }

                if (_scriptableObjectsWithEvents.Count > 0)
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("ScriptableObjects", EditorStyles.miniBoldLabel);
                    foreach (var so in _scriptableObjectsWithEvents)
                    {
                        if (!so) continue;
                        var rect = GUILayoutUtility.GetRect(new GUIContent(so.name), EditorStyles.label);
                        if (_selectedScriptableObject == so)
                            EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.3f));

                        if (GUI.Button(rect, so.name, EditorStyles.label))
                        {
                            _selectedGameObjects.Clear();
                            _selectedScriptableObject = so;
                            EditorGUIUtility.PingObject(so);
                            Repaint();
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGameObjectHierarchy(GameObject go, int indent)
        {
            if (!go) return;

            _flatGameObjectList.Add(go);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);

            var hasChildrenInEvents = go.transform.Cast<Transform>()
                .Any(child => child && _gameObjectsWithEvents.Contains(child.gameObject));

            _expandedStates.TryAdd(go, true);

            if (hasChildrenInEvents)
            {
                var rowRect = GUILayoutUtility.GetRect(new GUIContent(go.name), EditorStyles.foldout);
                const float foldoutWidth = 15f;
                var foldRect = new Rect(rowRect.x, rowRect.y, foldoutWidth, rowRect.height);
                var labelRect = new Rect(rowRect.x + foldoutWidth, rowRect.y, Mathf.Max(0f, rowRect.width - foldoutWidth), rowRect.height);

                _expandedStates[go] = EditorGUI.Foldout(foldRect, _expandedStates[go], GUIContent.none, false);

                if (_selectedGameObjects.Contains(go))
                    EditorGUI.DrawRect(labelRect, new Color(0.24f, 0.48f, 0.90f, 0.3f));

                if (GUI.Button(labelRect, go.name, EditorStyles.label))
                    HandleSelection(go, Event.current);

                EditorGUILayout.EndHorizontal();

                if (!_expandedStates[go]) return;
                var children = go.transform.Cast<Transform>()
                    .Where(child => child && _gameObjectsWithEvents.Contains(child.gameObject))
                    .ToList();
                children.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
                foreach (var child in children)
                {
                    DrawGameObjectHierarchy(child.gameObject, indent + 1);
                }
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(new GUIContent(go.name), EditorStyles.label);
                if (_selectedGameObjects.Contains(go))
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.3f));

                if (GUI.Button(rect, go.name, EditorStyles.label))
                    HandleSelection(go, Event.current);

                EditorGUILayout.EndHorizontal();
            }
        }

        private void SetAllExpanded(bool expanded)
        {
            foreach (var go in _gameObjectsWithEvents)
            {
                if (go)
                    _expandedStates[go] = expanded;
            }
        }

        private void HandleSelection(GameObject go, Event e)
        {
            _selectedScriptableObject = null;

            if (e.shift && _lastClickedGameObject && _flatGameObjectList.Contains(_lastClickedGameObject))
            {
                var lastIndex = _flatGameObjectList.IndexOf(_lastClickedGameObject);
                var newIndex = _flatGameObjectList.IndexOf(go);

                if (lastIndex != -1 && newIndex != -1)
                {
                    var min = Math.Min(lastIndex, newIndex);
                    var max = Math.Max(lastIndex, newIndex);

                    for (var i = min; i <= max; i++)
                    {
                        _selectedGameObjects.Add(_flatGameObjectList[i]);
                        EditorGUIUtility.PingObject(_flatGameObjectList[i]);
                    }
                }
            }
            else if (e.control || e.command)
            {
                if (!_selectedGameObjects.Add(go))
                    _selectedGameObjects.Remove(go);

                _lastClickedGameObject = go;
                EditorGUIUtility.PingObject(go);
            }
            else
            {
                _selectedGameObjects.Clear();
                _selectedGameObjects.Add(go);
                _lastClickedGameObject = go;
                EditorGUIUtility.PingObject(go);
            }

            Repaint();
        }

        private static List<GameObject> GetRootGameObjects(HashSet<GameObject> gameObjects)
        {
            var roots = gameObjects
                .Where(go => go && (!go.transform.parent || !gameObjects.Contains(go.transform.parent.gameObject)))
                .ToList();

            roots.Sort((a, b) =>
            {
                var sceneCompare = string.Compare(a.scene.path, b.scene.path, StringComparison.Ordinal);
                if (sceneCompare != 0)
                    return sceneCompare;

                var siblingCompare = a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
                return siblingCompare != 0
                    ? siblingCompare
                    : string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            return roots;
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Events for Selected Object(s)", EditorStyles.boldLabel);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            if (_selectedGameObjects.Count == 0 && !_selectedScriptableObject)
            {
                EditorGUILayout.LabelField("Select a GameObject or ScriptableObject to inspect its UnityEvents.", EditorStyles.helpBox);
            }
            else if (_selectedScriptableObject)
            {
                EditorGUILayout.LabelField(_selectedScriptableObject.name, EditorStyles.boldLabel);
                var events = _filteredEntries.FindAll(e => e.OwnerObject == _selectedScriptableObject);
                foreach (var entry in events)
                    DrawEventEntry(entry);
            }
            else
            {
                foreach (var selected in _selectedGameObjects)
                {
                    if (!selected) continue;

                    EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
                    var events = _filteredEntries.FindAll(e => GetEntryGameObject(e) == selected);

                    if (events.Count == 0)
                    {
                        EditorGUILayout.LabelField("No matching events on this object.", EditorStyles.helpBox);
                    }
                    else
                    {
                        foreach (var entry in events)
                            DrawEventEntry(entry);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEventEntry(UnityEventScanner.EventEntry entry)
        {
            if (entry == null || !entry.OwnerObject)
                return;

            EditorGUILayout.BeginVertical("box");

            var displayPath = !string.IsNullOrEmpty(entry.PropertyPath) ? entry.PropertyPath : entry.FieldName;
            var title = entry.Component
                ? $"Event: {entry.Component.GetType().Name}.{displayPath}"
                : $"Event: {entry.OwnerObject.GetType().Name}.{displayPath}";
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(entry.AssetPath))
            {
                EditorGUILayout.LabelField(entry.AssetPath, EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);

            using (var so = new SerializedObject(entry.OwnerObject))
            {
                so.Update();
                var eventProp = so.FindProperty(displayPath);
                if (eventProp != null)
                {
                    EditorGUILayout.PropertyField(eventProp, true);
                    if (so.ApplyModifiedProperties())
                        _isDirty = true;
                }
            }

            EditorGUILayout.Space(4);
            DrawListeners(entry);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void DrawListeners(UnityEventScanner.EventEntry entry)
        {
            if (entry.Listeners.Count == 0)
            {
                EditorGUILayout.HelpBox("No listeners attached (Empty / Unused).", MessageType.Info);
            }
            else
            {
                foreach (var listener in entry.Listeners)
                {
                    var label = $" - {(listener.Target ? listener.Target.name : "NULL")}::{listener.MethodName} ({listener.Mode})";
                    if (listener.IsBroken)
                        EditorGUILayout.HelpBox($"Broken Listener: {label}\nReason: {listener.BrokenReason}", MessageType.Warning);
                    else
                        EditorGUILayout.LabelField(label, EditorStyles.helpBox);
                }
            }

            var hasBroken = entry.HasBrokenListeners;
            using (new EditorGUI.DisabledScope(!hasBroken))
            {
                if (GUILayout.Button("Remove Broken Listeners", GUILayout.Width(200)))
                {
                    if (EditorUtils.RemoveBrokenListeners(entry) > 0)
                    {
                        _isDirty = true;
                        Repaint();
                    }
                }
            }
        }
    }
}
