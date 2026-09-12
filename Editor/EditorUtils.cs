using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEventAuditor.Editor
{
    public static class EditorUtils
    {
        public static int RemoveBrokenListeners(UnityEventScanner.EventEntry entry)
        {
            if (entry == null || !entry.OwnerObject)
                return 0;

            var path = !string.IsNullOrEmpty(entry.PropertyPath) ? entry.PropertyPath : entry.FieldName;
            if (string.IsNullOrEmpty(path))
                return 0;

            using var serializedObject = new SerializedObject(entry.OwnerObject);
            serializedObject.Update();

            var eventProp = serializedObject.FindProperty(path);
            if (eventProp == null)
                return 0;

            var callsProp = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (callsProp == null || !callsProp.isArray)
                return 0;

            var removedCount = 0;
            for (var i = callsProp.arraySize - 1; i >= 0; i--)
            {
                var callProp = callsProp.GetArrayElementAtIndex(i);
                var target = callProp.FindPropertyRelative("m_Target")?.objectReferenceValue;
                var method = callProp.FindPropertyRelative("m_MethodName")?.stringValue;
                var modeProp = callProp.FindPropertyRelative("m_Mode");
                var mode = modeProp != null
                    ? (PersistentListenerMode)modeProp.enumValueIndex
                    : PersistentListenerMode.EventDefined;
                var objArgTypeName = callProp.FindPropertyRelative("m_Arguments.m_ObjectArgumentAssemblyTypeName")?.stringValue;

                var issue = UnityEventScanner.ValidateListener(target, method, mode, objArgTypeName);
                if (issue != ListenerIssue.None)
                {
                    callsProp.DeleteArrayElementAtIndex(i);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                Undo.RecordObject(entry.OwnerObject, "Remove Broken UnityEvent Listeners");
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry.OwnerObject);
                entry.Listeners.RemoveAll(l => l.IsBroken);
                Debug.Log($"[UnityEventAuditor] Removed {removedCount} broken listener(s) from: {entry.OwnerName}.{path}");
            }

            return removedCount;
        }

        public static int RemoveAllBrokenListeners(IEnumerable<UnityEventScanner.EventEntry> entries)
        {
            if (entries == null)
                return 0;

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove All Broken UnityEvent Listeners");

            var totalRemoved = 0;
            foreach (var entry in entries)
            {
                if (entry == null || !entry.HasBrokenListeners) continue;
                totalRemoved += RemoveBrokenListeners(entry);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return totalRemoved;
        }
    }
}
