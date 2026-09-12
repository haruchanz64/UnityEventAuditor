using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEventAuditor.Editor;
using Object = UnityEngine.Object;

namespace UnityEventAuditor.Tests
{
    [Serializable]
    internal struct NestedEventContainer
    {
        public UnityEvent onNestedTrigger;
    }

    internal sealed class TestEventHost : MonoBehaviour
    {
        public UnityEvent onPrimary = new();
        public UnityEvent<int> onScoreChanged = new();
        public UnityEvent onUnused = new();
        public NestedEventContainer nested;

        public void HandleValidVoid() { }
        public void HandleValidInt(int value) { }
    }

    internal sealed class TestEventScriptableObject : ScriptableObject
    {
        public UnityEvent onAssetSignal = new();
    }

    public sealed class UnityEventScannerAndCleanupTests
    {
        private GameObject _hostGo;
        private TestEventHost _host;
        private TestEventScriptableObject _so;

        [SetUp]
        public void SetUp()
        {
            _hostGo = new GameObject("AuditTestHost");
            _host = _hostGo.AddComponent<TestEventHost>();
            _so = ScriptableObject.CreateInstance<TestEventScriptableObject>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGo) Object.DestroyImmediate(_hostGo);
            if (_so) Object.DestroyImmediate(_so);
        }

        [Test]
        public void ScanGameObject_FindsTopLevelNestedEmptyAndAllBrokenListenerTypes()
        {
            // Valid Void listener on onPrimary
            UnityEventTools.AddPersistentListener(_host.onPrimary, _host.HandleValidVoid);

            // Inject two broken listeners onto onPrimary: missing target and renamed/nonexistent method
            AddRawPersistentCall(_host, "onPrimary", target: null, methodName: "MissingTargetMethod", mode: PersistentListenerMode.Void);
            AddRawPersistentCall(_host, "onPrimary", target: _host, methodName: "RenamedOrDeletedMethod", mode: PersistentListenerMode.Void);

            // Inject a broken listener on nested struct UnityEvent
            AddRawPersistentCall(_host, "nested.onNestedTrigger", target: _host, methodName: string.Empty, mode: PersistentListenerMode.Void);

            // Valid int listener on onScoreChanged
            UnityEventTools.AddIntPersistentListener(_host.onScoreChanged, _host.HandleValidInt, 42);

            var entries = UnityEventScanner.ScanGameObject(_hostGo);
            Assert.AreEqual(4, entries.Count);

            var primary = entries.Find(e => e.PropertyPath == "onPrimary");
            Assert.IsNotNull(primary);
            Assert.AreEqual(3, primary.Listeners.Count);
            Assert.IsFalse(primary.Listeners[0].IsBroken);
            Assert.AreEqual(ListenerIssue.MissingTarget, primary.Listeners[1].Issue);
            Assert.AreEqual(ListenerIssue.MethodNotFound, primary.Listeners[2].Issue);

            var nested = entries.Find(e => e.PropertyPath == "nested.onNestedTrigger");
            Assert.IsNotNull(nested);
            Assert.AreEqual(1, nested.Listeners.Count);
            Assert.AreEqual(ListenerIssue.MissingMethodName, nested.Listeners[0].Issue);

            var unused = entries.Find(e => e.PropertyPath == "onUnused");
            Assert.IsNotNull(unused);
            Assert.IsTrue(unused.IsEmpty);

            var summary = UnityEventScanner.ComputeSummary(entries);
            Assert.AreEqual(4, summary.TotalEvents);
            Assert.AreEqual(5, summary.TotalListeners);
            Assert.AreEqual(3, summary.BrokenListeners);
            Assert.AreEqual(1, summary.EmptyEvents);
            Assert.AreEqual(2, summary.EventsWithBrokenListeners);

            // Bulk remove all broken listeners and confirm valid ones remain untouched.
            var removed = EditorUtils.RemoveAllBrokenListeners(entries);
            Assert.AreEqual(3, removed);

            var afterCleanup = UnityEventScanner.ScanGameObject(_hostGo);
            var afterSummary = UnityEventScanner.ComputeSummary(afterCleanup);
            Assert.AreEqual(0, afterSummary.BrokenListeners);
            Assert.AreEqual(2, afterSummary.TotalListeners);
        }

        [Test]
        public void ScanObject_ScansScriptableObjectAndExportsCsvAndJson()
        {
            AddRawPersistentCall(_so, "onAssetSignal", target: _host, methodName: "NonExistentCallback", mode: PersistentListenerMode.Void);

            var entries = UnityEventScanner.ScanObject(_so, "Assets/TestSignal.asset");
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(ListenerIssue.MethodNotFound, entries[0].Listeners[0].Issue);

            var csv = UnityEventAuditExporter.ToCsv(entries);
            Assert.That(csv, Does.Contain("onAssetSignal"));
            Assert.That(csv, Does.Contain("MethodNotFound"));

            var json = UnityEventAuditExporter.ToJson(entries, prettyPrint: false);
            Assert.That(json, Does.Contain("\"brokenListeners\":1"));
            Assert.That(json, Does.Contain("NonExistentCallback"));
        }

        private static void AddRawPersistentCall(Object owner, string propertyPath, Object target, string methodName, PersistentListenerMode mode)
        {
            using var so = new SerializedObject(owner);
            so.Update();

            var eventProp = so.FindProperty(propertyPath);
            Assert.IsNotNull(eventProp, $"Expected serialized property '{propertyPath}' on '{owner.GetType().Name}'.");

            var callsProp = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
            var index = callsProp.arraySize;
            callsProp.InsertArrayElementAtIndex(index);
            var call = callsProp.GetArrayElementAtIndex(index);

            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            call.FindPropertyRelative("m_MethodName").stringValue = methodName;
            call.FindPropertyRelative("m_Mode").enumValueIndex = (int)mode;
            call.FindPropertyRelative("m_CallState").enumValueIndex = (int)UnityEventCallState.RuntimeOnly;

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
