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
    /// <summary>
    /// Classifies validation problems detected on a serialized persistent <see cref="UnityEventBase"/> listener.
    /// </summary>
    public enum ListenerIssue
    {
        /// <summary>The persistent listener has a valid target reference and matching callback method.</summary>
        None = 0,

        /// <summary>The persistent listener target object reference is null or destroyed.</summary>
        MissingTarget = 1,

        /// <summary>No callback method name is assigned on the persistent listener.</summary>
        MissingMethodName = 2,

        /// <summary>The assigned method name does not exist on the target type or its signature does not match the listener mode.</summary>
        MethodNotFound = 3
    }

    /// <summary>
    /// Bitmask defining which Editor targets <see cref="UnityEventScanner.Scan"/> inspects.
    /// </summary>
    [Flags]
    public enum AuditScanScope
    {
        /// <summary>Scan nothing.</summary>
        None = 0,

        /// <summary>Scan all loaded scenes, or only the active Prefab Stage root when Prefab Mode is open.</summary>
        ActiveScenes = 1 << 0,

        /// <summary>Scan all prefab assets (<c>t:Prefab</c>) under the configured asset search folder.</summary>
        ProjectPrefabs = 1 << 1,

        /// <summary>Scan all <see cref="ScriptableObject"/> assets (<c>t:ScriptableObject</c>) under the configured asset search folder.</summary>
        ScriptableObjects = 1 << 2,

        /// <summary>Scan loaded scenes (or active Prefab Stage), project prefabs, and <see cref="ScriptableObject"/> assets.</summary>
        All = ActiveScenes | ProjectPrefabs | ScriptableObjects
    }

    /// <summary>
    /// Immutable aggregate statistics computed over a collection of <see cref="UnityEventScanner.EventEntry"/> scan results.
    /// </summary>
    public readonly struct AuditSummary
    {
        /// <summary>Total number of scanned <see cref="UnityEventBase"/> fields.</summary>
        public int TotalEvents { get; }

        /// <summary>Total number of persistent listeners across all scanned events.</summary>
        public int TotalListeners { get; }

        /// <summary>Total number of persistent listeners flagged with a non-<see cref="ListenerIssue.None"/> issue.</summary>
        public int BrokenListeners { get; }

        /// <summary>Number of scanned events that have zero persistent listeners attached.</summary>
        public int EmptyEvents { get; }

        /// <summary>Number of scanned events containing at least one broken persistent listener.</summary>
        public int EventsWithBrokenListeners { get; }

        /// <summary>
        /// Initializes a new <see cref="AuditSummary"/> with precomputed audit counts.
        /// </summary>
        /// <param name="totalEvents">Total number of scanned event entries.</param>
        /// <param name="totalListeners">Total persistent listeners found across all entries.</param>
        /// <param name="brokenListeners">Number of broken persistent listeners found.</param>
        /// <param name="emptyEvents">Number of events with zero listeners.</param>
        /// <param name="eventsWithBrokenListeners">Number of events that have one or more broken listeners.</param>
        public AuditSummary(int totalEvents, int totalListeners, int brokenListeners, int emptyEvents, int eventsWithBrokenListeners)
        {
            TotalEvents = totalEvents;
            TotalListeners = totalListeners;
            BrokenListeners = brokenListeners;
            EmptyEvents = emptyEvents;
            EventsWithBrokenListeners = eventsWithBrokenListeners;
        }
    }

    /// <summary>
    /// Discovers serialized <see cref="UnityEventBase"/> fields across scenes, Prefab Stages, prefabs, and <see cref="ScriptableObject"/> assets, and validates their persistent listeners.
    /// </summary>
    public static class UnityEventScanner
    {
        /// <summary>
        /// Describes a single persistent listener entry read from a serialized <c>m_PersistentCalls.m_Calls</c> element.
        /// </summary>
        public class ListenerInfo
        {
            /// <summary>Zero-based index of this listener inside <c>m_PersistentCalls.m_Calls</c>.</summary>
            public int CallIndex;

            /// <summary>Serialized target object referenced by the persistent listener, or null if missing.</summary>
            public Object Target;

            /// <summary>Serialized method name invoked on <see cref="Target"/>.</summary>
            public string MethodName;

            /// <summary>Argument binding mode serialized on the persistent call.</summary>
            public PersistentListenerMode Mode;

            /// <summary>Validation result produced by <see cref="ValidateListener"/>.</summary>
            public ListenerIssue Issue;

            /// <summary>True when <see cref="Issue"/> is anything other than <see cref="ListenerIssue.None"/>.</summary>
            public bool IsBroken => Issue != ListenerIssue.None;

            /// <summary>Human-readable explanation of <see cref="Issue"/>, or an empty string when valid.</summary>
            public string BrokenReason => Issue switch
            {
                ListenerIssue.MissingTarget => "Missing target object reference",
                ListenerIssue.MissingMethodName => "No method selected",
                ListenerIssue.MethodNotFound => $"Method '{MethodName}' not found or incompatible on '{(Target ? Target.GetType().Name : "null")}'",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Represents a discovered serialized <see cref="UnityEventBase"/> property on a component or <see cref="ScriptableObject"/> along with its validated listeners.
        /// </summary>
        public class EventEntry
        {
            /// <summary>Owning <see cref="Component"/> or <see cref="ScriptableObject"/> that declares the serialized event.</summary>
            public Object OwnerObject;

            /// <summary>Display name of the owning <see cref="GameObject"/> (for components) or asset (for <see cref="ScriptableObject"/>s).</summary>
            public string OwnerName;

            /// <summary>Owning <see cref="Component"/> when the event belongs to a scene or prefab GameObject, or null for <see cref="ScriptableObject"/>s.</summary>
            public Component Component;

            /// <summary>Leaf serialized property name of the <see cref="UnityEventBase"/> field.</summary>
            public string FieldName;

            /// <summary>Full <see cref="SerializedProperty.propertyPath"/> used to locate top-level or nested events inside structs, classes, arrays, and lists.</summary>
            public string PropertyPath;

            /// <summary>Scene or project asset path of the owner, or an empty string when unsaved or unspecified.</summary>
            public string AssetPath;

            /// <summary>Direct managed reference for top-level event fields, or null for nested serialized event paths.</summary>
            public UnityEventBase EventInstance;

            /// <summary>Validated persistent listeners read from this event's <c>m_PersistentCalls.m_Calls</c> array.</summary>
            public readonly List<ListenerInfo> Listeners = new();

            /// <summary>True when the event has zero persistent listeners attached.</summary>
            public bool IsEmpty => Listeners.Count == 0;

            /// <summary>True when at least one listener in <see cref="Listeners"/> is marked broken.</summary>
            public bool HasBrokenListeners => Listeners.Exists(l => l.IsBroken);

            /// <summary>Number of broken listeners currently present in <see cref="Listeners"/>.</summary>
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

        /// <summary>
        /// Scans active loaded scenes (or the open Prefab Stage) and optionally all project <see cref="ScriptableObject"/> assets under <c>Assets</c>.
        /// </summary>
        /// <param name="includeScriptableObjects">When true, also scans all <see cref="ScriptableObject"/> assets in the project.</param>
        /// <returns>List of discovered event entries with validated listeners.</returns>
        public static List<EventEntry> ScanAllUnityEvents(bool includeScriptableObjects = false)
        {
            var scope = AuditScanScope.ActiveScenes;
            if (includeScriptableObjects)
            {
                scope |= AuditScanScope.ScriptableObjects;
            }

            return Scan(scope);
        }

        /// <summary>
        /// Scans all targets enabled in <paramref name="scope"/> and returns every discovered serialized <see cref="UnityEventBase"/> entry.
        /// </summary>
        /// <param name="scope">Bitmask selecting active scenes/Prefab Stage, project prefabs, and/or <see cref="ScriptableObject"/> assets.</param>
        /// <param name="assetSearchFolder">Project folder root passed to <see cref="AssetDatabase.FindAssets(string, string[])"/> for prefab and <see cref="ScriptableObject"/> scans.</param>
        /// <returns>List of discovered event entries with validated listeners.</returns>
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

        /// <summary>
        /// Scans all <see cref="MonoBehaviour"/> components on <paramref name="root"/> and its inactive or active descendants.
        /// </summary>
        /// <param name="root">Root <see cref="GameObject"/> to inspect; returns an empty list when null.</param>
        /// <param name="assetPath">Optional scene or prefab asset path recorded on each resulting <see cref="EventEntry"/>.</param>
        /// <returns>List of discovered event entries under <paramref name="root"/>.</returns>
        public static List<EventEntry> ScanGameObject(GameObject root, string assetPath = null)
        {
            var events = new List<EventEntry>();
            ScanGameObjectHierarchy(root, assetPath, events);
            return events;
        }

        /// <summary>
        /// Scans a single Unity <see cref="Object"/> (such as a <see cref="Component"/> or <see cref="ScriptableObject"/>) for serialized <see cref="UnityEventBase"/> properties.
        /// </summary>
        /// <param name="owner">Target Unity object to inspect; returns an empty list when null.</param>
        /// <param name="assetPath">Optional asset path recorded on each resulting <see cref="EventEntry"/>.</param>
        /// <returns>List of discovered event entries on <paramref name="owner"/>.</returns>
        public static List<EventEntry> ScanObject(Object owner, string assetPath = null)
        {
            var events = new List<EventEntry>();
            ScanObject(owner, assetPath, events);
            return events;
        }

        /// <summary>
        /// Computes aggregate event, listener, broken-listener, and empty-event totals over <paramref name="entries"/>.
        /// </summary>
        /// <param name="entries">Scanned entries to summarize; null or empty lists return a zeroed <see cref="AuditSummary"/>.</param>
        /// <returns>Aggregate <see cref="AuditSummary"/> counts.</returns>
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

        /// <summary>
        /// Validates a single serialized persistent listener against its target object type and binding mode.
        /// </summary>
        /// <param name="target">Target Unity object reference serialized on the listener.</param>
        /// <param name="methodName">Method name serialized on the listener.</param>
        /// <param name="mode">Argument passing mode used by the persistent call.</param>
        /// <param name="objectArgAssemblyTypeName">Optional assembly-qualified argument type name from <c>m_Arguments.m_ObjectArgumentAssemblyTypeName</c>, used when <paramref name="mode"/> is <see cref="PersistentListenerMode.Object"/>.</param>
        /// <returns><see cref="ListenerIssue.None"/> if a compatible target and callback exist; otherwise the specific failure reason.</returns>
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

        /// <summary>
        /// Validates a <see cref="PersistentListenerMode.Object"/> callback by resolving the serialized argument type first and falling back to any single <see cref="Object"/>-assignable parameter overload.
        /// </summary>
        /// <param name="targetType">Runtime type of the listener target object.</param>
        /// <param name="methodName">Expected callback method name.</param>
        /// <param name="objectArgAssemblyTypeName">Assembly-qualified type name of the serialized object argument, or null.</param>
        /// <returns>True when a matching object-parameter method exists on <paramref name="targetType"/>.</returns>
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

        /// <summary>
        /// Walks the inheritance chain of <paramref name="targetType"/> to check whether any instance method matches <paramref name="methodName"/> (used for dynamic <see cref="PersistentListenerMode.EventDefined"/> bindings).
        /// </summary>
        /// <param name="targetType">Runtime type of the listener target object.</param>
        /// <param name="methodName">Exact ordinal method name to search for.</param>
        /// <returns>True if at least one declared instance method with the given name exists.</returns>
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

        /// <summary>
        /// Collects events from every visible <see cref="MonoBehaviour"/> in the hierarchy rooted at <paramref name="root"/>, skipping objects with <see cref="HideFlags.HideInHierarchy"/>.
        /// </summary>
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

        /// <summary>
        /// Iterates all serialized properties on <paramref name="owner"/>, recording every generic property that contains a <c>m_PersistentCalls.m_Calls</c> array.
        /// </summary>
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

        /// <summary>
        /// Resolves a direct managed <see cref="UnityEventBase"/> reference for top-level serialized fields; returns null for nested paths containing a dot.
        /// </summary>
        /// <param name="owner">Owning Unity object whose type hierarchy is inspected via reflection.</param>
        /// <param name="propertyPath">Serialized property path of the event field.</param>
        /// <returns>The managed <see cref="UnityEventBase"/> instance if directly declared on <paramref name="owner"/>; otherwise null.</returns>
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
