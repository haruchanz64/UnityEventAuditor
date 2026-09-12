using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEventAuditor.Editor
{
    public enum ListenerIssue
    {
        None = 0,
        MissingTarget = 1,
        MissingMethodName = 2,
        MethodNotFound = 3
    }

    [Flags]
    public enum AuditScanScope
    {
        None = 0,
        ActiveScenes = 1 << 0,
        ProjectPrefabs = 1 << 1,
        ScriptableObjects = 1 << 2,
        All = ActiveScenes | ProjectPrefabs | ScriptableObjects
    }

    public readonly struct AuditSummary
    {
        public int TotalEvents { get; }
        public int TotalListeners { get; }
        public int BrokenListeners { get; }
        public int EmptyEvents { get; }
        public int EventsWithBrokenListeners { get; }

        public AuditSummary(int totalEvents, int totalListeners, int brokenListeners, int emptyEvents, int eventsWithBrokenListeners)
        {
            TotalEvents = totalEvents;
            TotalListeners = totalListeners;
            BrokenListeners = brokenListeners;
            EmptyEvents = emptyEvents;
            EventsWithBrokenListeners = eventsWithBrokenListeners;
        }
    }

    public static class UnityEventScanner
    {
        public class ListenerInfo
        {
            public int CallIndex;
            public Object Target;
            public string MethodName;
            public PersistentListenerMode Mode;
            public ListenerIssue Issue;

            public bool IsBroken => Issue != ListenerIssue.None;

            public string BrokenReason => Issue switch
            {
                ListenerIssue.MissingTarget => "Missing target object reference",
                ListenerIssue.MissingMethodName => "No method selected",
                ListenerIssue.MethodNotFound => $"Method '{MethodName}' not found or incompatible on '{(Target ? Target.GetType().Name : "null")}'",
                _ => string.Empty
            };
        }

        public class EventEntry
        {
            public Object OwnerObject;
            public string OwnerName;
            public Component Component;
            public string FieldName;
            public string PropertyPath;
            public string AssetPath;
            public UnityEventBase EventInstance;
            public readonly List<ListenerInfo> Listeners = new();

            public bool IsEmpty => Listeners.Count == 0;
            public bool HasBrokenListeners => Listeners.Exists(l => l.IsBroken);
            public int BrokenListenerCount
            {
                get
                {
                    var count = 0;
                    for (var i = 0; i < Listeners.Count; i++)
                    {
                        if (Listeners[i].IsBroken) count++;
                    }
                    return count;
                }
            }
        }

        public static List<EventEntry> ScanAllUnityEvents(bool includeScriptableObjects = false)
        {
            var scope = AuditScanScope.ActiveScenes;
            if (includeScriptableObjects)
            {
                scope |= AuditScanScope.ScriptableObjects;
            }

            return Scan(scope);
        }

        public static List<EventEntry> Scan(AuditScanScope scope, string assetSearchFolder = "Assets")
        {
            var events = new List<EventEntry>();

            if ((scope & AuditScanScope.ActiveScenes) != 0)
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null && prefabStage.prefabContentsRoot)
                {
                    ScanGameObjectHierarchy(prefabStage.prefabContentsRoot, prefabStage.assetPath, events);
                }
                else
                {
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (!scene.isLoaded) continue;

                        foreach (var root in scene.GetRootGameObjects())
                        {
                            ScanGameObjectHierarchy(root, scene.path, events);
                        }
                    }
                }
            }

            if ((scope & AuditScanScope.ProjectPrefabs) != 0 && AssetDatabase.IsValidFolder(assetSearchFolder))
            {
                var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { assetSearchFolder });
                foreach (var guid in prefabGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!prefab) continue;

                    ScanGameObjectHierarchy(prefab, path, events);
                }
            }

            if ((scope & AuditScanScope.ScriptableObjects) != 0 && AssetDatabase.IsValidFolder(assetSearchFolder))
            {
                var soGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { assetSearchFolder });
                foreach (var guid in soGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (!so) continue;

                    ScanObject(so, path, events);
                }
            }

            return events;
        }

        public static List<EventEntry> ScanGameObject(GameObject root, string assetPath = null)
        {
            var events = new List<EventEntry>();
            ScanGameObjectHierarchy(root, assetPath, events);
            return events;
        }

        public static List<EventEntry> ScanObject(Object owner, string assetPath = null)
        {
            var events = new List<EventEntry>();
            ScanObject(owner, assetPath, events);
            return events;
        }

        public static AuditSummary ComputeSummary(IReadOnlyList<EventEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return default;
            }

            var totalListeners = 0;
            var brokenListeners = 0;
            var emptyEvents = 0;
            var eventsWithBroken = 0;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;

                totalListeners += entry.Listeners.Count;
                if (entry.Listeners.Count == 0)
                {
                    emptyEvents++;
                }

                var broken = entry.BrokenListenerCount;
                brokenListeners += broken;
                if (broken > 0)
                {
                    eventsWithBroken++;
                }
            }

            return new AuditSummary(entries.Count, totalListeners, brokenListeners, emptyEvents, eventsWithBroken);
        }

        public static ListenerIssue ValidateListener(
            Object target,
            string methodName,
            PersistentListenerMode mode,
            string objectArgAssemblyTypeName = null)
        {
            if (!target)
                return ListenerIssue.MissingTarget;

            if (string.IsNullOrEmpty(methodName))
                return ListenerIssue.MissingMethodName;

            var targetType = target.GetType();
            var resolved = mode switch
            {
                PersistentListenerMode.Void => UnityEventBase.GetValidMethodInfo(targetType, methodName, Type.EmptyTypes) != null,
                PersistentListenerMode.Int => UnityEventBase.GetValidMethodInfo(targetType, methodName, new[] { typeof(int) }) != null,
                PersistentListenerMode.Float => UnityEventBase.GetValidMethodInfo(targetType, methodName, new[] { typeof(float) }) != null,
                PersistentListenerMode.String => UnityEventBase.GetValidMethodInfo(targetType, methodName, new[] { typeof(string) }) != null,
                PersistentListenerMode.Bool => UnityEventBase.GetValidMethodInfo(targetType, methodName, new[] { typeof(bool) }) != null,
                PersistentListenerMode.Object => ValidateObjectArgListener(targetType, methodName, objectArgAssemblyTypeName),
                _ => HasAnyMethodNamed(targetType, methodName)
            };

            return resolved ? ListenerIssue.None : ListenerIssue.MethodNotFound;
        }

        private static bool ValidateObjectArgListener(Type targetType, string methodName, string objectArgAssemblyTypeName)
        {
            var argType = !string.IsNullOrEmpty(objectArgAssemblyTypeName)
                ? Type.GetType(objectArgAssemblyTypeName, throwOnError: false)
                : typeof(Object);

            argType ??= typeof(Object);
            if (UnityEventBase.GetValidMethodInfo(targetType, methodName, new[] { argType }) != null)
            {
                return true;
            }

            // Fallback: check any 1-parameter method accepting an Object-compatible parameter.
            for (var type = targetType; type != null && type != typeof(object); type = type.BaseType)
            {
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < methods.Length; i++)
                {
                    if (!string.Equals(methods[i].Name, methodName, StringComparison.Ordinal)) continue;
                    var parameters = methods[i].GetParameters();
                    if (parameters.Length == 1 && typeof(Object).IsAssignableFrom(parameters[0].ParameterType))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAnyMethodNamed(Type targetType, string methodName)
        {
            for (var type = targetType; type != null && type != typeof(object); type = type.BaseType)
            {
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < methods.Length; i++)
                {
                    if (string.Equals(methods[i].Name, methodName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ScanGameObjectHierarchy(GameObject root, string assetPath, List<EventEntry> events)
        {
            if (!root) return;

            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component) continue;
                if ((component.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

                ScanObject(component, assetPath, events);
            }
        }

        private static void ScanObject(Object owner, string assetPath, List<EventEntry> events)
        {
            if (!owner) return;

            var component = owner as Component;
            var ownerName = component ? component.gameObject.name : owner.name;

            using var serializedObject = new SerializedObject(owner);
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                enterChildren = true;

                if (iterator.name == "m_Script")
                {
                    enterChildren = false;
                    continue;
                }

                if (iterator.propertyType != SerializedPropertyType.Generic)
                {
                    continue;
                }

                var callsProp = iterator.FindPropertyRelative("m_PersistentCalls.m_Calls");
                if (callsProp == null || !callsProp.isArray)
                {
                    continue;
                }

                // Do not descend into internal UnityEvent persistent call fields.
                enterChildren = false;

                var entry = new EventEntry
                {
                    OwnerObject = owner,
                    OwnerName = ownerName,
                    Component = component,
                    FieldName = iterator.name,
                    PropertyPath = iterator.propertyPath,
                    AssetPath = assetPath ?? string.Empty,
                    EventInstance = TryGetDirectEventInstance(owner, iterator.propertyPath)
                };

                for (var i = 0; i < callsProp.arraySize; i++)
                {
                    var callProp = callsProp.GetArrayElementAtIndex(i);
                    var target = callProp.FindPropertyRelative("m_Target")?.objectReferenceValue;
                    var methodName = callProp.FindPropertyRelative("m_MethodName")?.stringValue ?? string.Empty;
                    var modeProp = callProp.FindPropertyRelative("m_Mode");
                    var mode = modeProp != null
                        ? (PersistentListenerMode)modeProp.enumValueIndex
                        : PersistentListenerMode.EventDefined;
                    var objArgTypeName = callProp.FindPropertyRelative("m_Arguments.m_ObjectArgumentAssemblyTypeName")?.stringValue;

                    entry.Listeners.Add(new ListenerInfo
                    {
                        CallIndex = i,
                        Target = target,
                        MethodName = methodName,
                        Mode = mode,
                        Issue = ValidateListener(target, methodName, mode, objArgTypeName)
                    });
                }

                events.Add(entry);
            }
        }

        private static UnityEventBase TryGetDirectEventInstance(Object owner, string propertyPath)
        {
            if (!owner || string.IsNullOrEmpty(propertyPath) || propertyPath.IndexOf('.') >= 0)
            {
                return null;
            }

            for (var type = owner.GetType();
                 type != null && type != typeof(MonoBehaviour) && type != typeof(ScriptableObject) && type != typeof(Object);
                 type = type.BaseType)
            {
                var field = type.GetField(propertyPath, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                {
                    return field.GetValue(owner) as UnityEventBase;
                }
            }

            return null;
        }
    }
}
