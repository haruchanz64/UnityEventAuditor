# Unity Event Auditor

Unity Editor scanning, diagnostics, cleanup, and reporting package for Unity 6 (`6000.0+`). It locates serialized `UnityEvent` and `UnityEvent<T>` fields across loaded scenes, Prefab Stages, project prefabs, and `ScriptableObject` assets, flags broken or unused persistent listeners, removes invalid bindings with Undo support, and exports CSV or JSON reports.

## Features

- **Deep serialized discovery (`UnityEventScanner`):** Walks `SerializedObject` properties via `SerializedProperty` to find top-level `UnityEvent` / `UnityEvent<T>` fields as well as nested events inside `[Serializable]` structs, classes, arrays, and lists.
- **Reflection-backed listener validation (`ListenerIssue`):** Flags missing target object references (`MissingTarget`), unset method names (`MissingMethodName`), and renamed, deleted, or signature-incompatible callbacks (`MethodNotFound`) across `Void`, `Int`, `Float`, `String`, `Bool`, `Object`, and `EventDefined` persistent listener modes.
- **Multi-scope auditing (`AuditScanScope`):** Scans loaded scenes (or the active `PrefabStage` when editing a prefab in isolation) and optionally includes project `Prefab` (`t:Prefab`) and `ScriptableObject` (`t:ScriptableObject`) assets under a configurable search folder.
- **Interactive Editor window (`UnityEventAuditorWindow`):** Split-pane object browser and inspector (`Tools > HaruChanZ64 > Analysis > UnityEventAuditor > Unity Event Auditor`) with live summary counts, search across object names, field names, and property paths, status filters (`All`, `Valid`, `Broken`, `Empty`), multi-object selection (`Ctrl`/`Cmd` and `Shift`), and inline editable `SerializedProperty` drawers.
- **Undo-safe broken listener cleanup (`EditorUtils`):** Removes invalid persistent calls on a single event (`RemoveBrokenListeners`) or across an entire scan in one collapsed Undo group (`RemoveAllBrokenListeners`) while keeping valid listeners intact.
- **CSV and JSON report export (`UnityEventAuditExporter`):** Serializes scan entries and `AuditSummary` totals into RFC 4180 CSV or `JsonUtility`-formatted JSON for QA reviews and batch-mode CI checks.

## Requirements

- Unity `6000.0` or newer
- Zero required external dependencies
- Editor-only assembly (`UnityEventAuditor.Editor`, included in the `Editor` platform only; adds zero runtime overhead to builds)

## Installation

### Unity Package Manager (Git URL)

1. Open **Window > Package Manager** in Unity 6 (`6000.0+`).
2. Click **+** (top-left) and choose **Install package from git URL...**.
3. Enter:

```text
https://github.com/haruchanz64/UnityEventAuditor.git
```

### Local package

Add the package path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.haruchanz64.unity-event-auditor": "file:../Plugins/UnityEventAuditor"
  }
}
```

## Quick start

1. Open **Tools > HaruChanZ64 > Analysis > UnityEventAuditor > Unity Event Auditor**.
2. The window immediately scans loaded scenes (or the open Prefab Stage) and displays aggregate counts in the summary bar (`Events`, `Listeners`, `Broken`, `Empty/Unused`).
3. *(Optional)* Toggle **Prefabs** and/or **ScriptableObjects** in the toolbar to expand the scan across project assets under `Assets/`.
4. Use the **Search** box to match object names, field names, or nested property paths, and narrow results with the **Filter** dropdown (`All`, `Valid`, `Broken`, or `Empty`).
5. Select one or more GameObjects (with `Shift` or `Ctrl`/`Cmd`) or a `ScriptableObject` in the left pane to inspect and edit its live `UnityEvent` drawers on the right.
6. Click **Remove Broken Listeners** on an individual event card, or click **Fix All Broken** in the toolbar to clean every broken listener in the active scan as a single Undoable operation.
7. Click **Export CSV** or **Export JSON** to write the currently filtered results to disk.

## Core concepts

### Serialized discovery and listener validation

`UnityEventScanner.Scan(AuditScanScope, string)` collects `UnityEventScanner.EventEntry` records in three optional stages:

1. **Active scenes (`AuditScanScope.ActiveScenes`):** If a Prefab Stage is currently open (`PrefabStageUtility.GetCurrentPrefabStage()`), only that prefab root is scanned; otherwise every loaded scene's root GameObjects are traversed for `MonoBehaviour` components (including inactive children, skipping objects with `HideFlags.HideInHierarchy`).
2. **Project prefabs (`AuditScanScope.ProjectPrefabs`):** Queries `AssetDatabase.FindAssets("t:Prefab", ...)` under the specified folder (default `"Assets"`) and scans every prefab hierarchy.
3. **ScriptableObjects (`AuditScanScope.ScriptableObjects`):** Queries `AssetDatabase.FindAssets("t:ScriptableObject", ...)` under the specified folder and scans each asset directly.

For every inspected `UnityEngine.Object`, the scanner walks `SerializedObject.GetIterator()` and detects any `SerializedPropertyType.Generic` property containing a `m_PersistentCalls.m_Calls` array (skipping `m_Script` and avoiding recursion into internal call sub-properties). Each element of `m_Calls` is validated via `UnityEventScanner.ValidateListener`:

- Null or destroyed `m_Target` returns `ListenerIssue.MissingTarget`.
- Empty `m_MethodName` returns `ListenerIssue.MissingMethodName`.
- Static-argument modes (`Void`, `Int`, `Float`, `String`, `Bool`) check `UnityEventBase.GetValidMethodInfo` with the exact parameter list.
- `PersistentListenerMode.Object` resolves `m_Arguments.m_ObjectArgumentAssemblyTypeName` with `GetValidMethodInfo`, falling back to any single-parameter instance method accepting a `UnityEngine.Object`-assignable parameter.
- `PersistentListenerMode.EventDefined` (dynamic bindings) checks whether any instance method with the given name exists along the target's type hierarchy.

### Undo-grouped cleanup and reporting

`EditorUtils.RemoveBrokenListeners` re-validates every persistent call in `m_PersistentCalls.m_Calls` in reverse index order so array deletions do not shift subsequent indices. When at least one call is removed, it calls `Undo.RecordObject`, `SerializedObject.ApplyModifiedProperties`, and `EditorUtility.SetDirty`, and removes broken items from `entry.Listeners`. `EditorUtils.RemoveAllBrokenListeners` wraps multi-entry cleanup in a single `Undo.CollapseUndoOperations` group so one Undo reverts the entire batch.

`UnityEventAuditExporter` converts any `IReadOnlyList<UnityEventScanner.EventEntry>` into either a structured JSON report (summary counts plus per-event and per-listener records via `JsonUtility`) or an RFC 4180 CSV table with columns `Owner,Component,PropertyPath,AssetPath,ListenerTarget,MethodName,Status,Issue,Reason`.

## Code examples

### 1. Run a project-wide audit and fail CI on broken listeners

Use `UnityEventScanner.Scan`, `ComputeSummary`, and `UnityEventAuditExporter` from an Editor script or Unity batch-mode CI command:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEventAuditor.Editor;

public static class UnityEventAuditCiGate
{
    public static void RunProjectAudit()
    {
        var entries = UnityEventScanner.Scan(AuditScanScope.All, assetSearchFolder: "Assets");
        var summary = UnityEventScanner.ComputeSummary(entries);

        var json = UnityEventAuditExporter.ToJson(entries, prettyPrint: true);
        File.WriteAllText("unity_event_audit.json", json);

        var csv = UnityEventAuditExporter.ToCsv(entries);
        File.WriteAllText("unity_event_audit.csv", csv);

        Debug.Log(
            $"[Audit] Events: {summary.TotalEvents}, Listeners: {summary.TotalListeners}, " +
            $"Broken: {summary.BrokenListeners}, Empty: {summary.EmptyEvents}");

        if (summary.BrokenListeners > 0)
        {
            throw new System.InvalidOperationException(
                $"UnityEvent audit failed with {summary.BrokenListeners} broken listener(s) across {summary.EventsWithBrokenListeners} event(s).");
        }
    }
}
```

### 2. Scan a specific GameObject or ScriptableObject and clean broken bindings

Target individual assets or hierarchies programmatically and remove invalid persistent listeners in a single Undo group:

```csharp
using UnityEditor;
using UnityEngine;
using UnityEventAuditor.Editor;

public static class SelectedObjectEventCleaner
{
    [MenuItem("Tools/Custom/Clean Broken UnityEvents On Selection")]
    public static void CleanSelection()
    {
        var selected = Selection.activeGameObject;
        if (!selected)
            return;

        var entries = UnityEventScanner.ScanGameObject(selected);
        var removed = EditorUtils.RemoveAllBrokenListeners(entries);
        Debug.Log($"Removed {removed} broken persistent listener(s) under '{selected.name}'.");
    }
}
```

To inspect a `ScriptableObject` or single `Component` directly, call `UnityEventScanner.ScanObject(asset, AssetDatabase.GetAssetPath(asset))`.

### 3. Validate a single listener binding directly

Call `UnityEventScanner.ValidateListener` directly when building custom importers, validators, or migration scripts:

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEventAuditor.Editor;

public static class ListenerCheckExample
{
    public static bool IsVoidCallbackValid(Object target, string methodName)
    {
        var issue = UnityEventScanner.ValidateListener(target, methodName, PersistentListenerMode.Void);
        return issue == ListenerIssue.None;
    }
}
```

## Core API

| Type | Namespace | Purpose | Notes |
| :--- | :--- | :--- | :--- |
| `UnityEventScanner` | `UnityEventAuditor.Editor` | Static scanner for scenes, Prefab Stages, prefabs, GameObjects, and `UnityEngine.Object`s, plus summary computation and listener validation | Entry points: `Scan`, `ScanAllUnityEvents`, `ScanGameObject`, `ScanObject`, `ComputeSummary`, `ValidateListener` |
| `UnityEventScanner.EventEntry` | `UnityEventAuditor.Editor` | Discovered serialized event record containing owner metadata, `PropertyPath`, `AssetPath`, optional `EventInstance`, and `Listeners` | Exposes `IsEmpty`, `HasBrokenListeners`, and `BrokenListenerCount` |
| `UnityEventScanner.ListenerInfo` | `UnityEventAuditor.Editor` | Per-call listener snapshot with `CallIndex`, `Target`, `MethodName`, `Mode`, and `Issue` | Exposes `IsBroken` and human-readable `BrokenReason` |
| `AuditScanScope` | `UnityEventAuditor.Editor` | `[Flags]` enum selecting `ActiveScenes`, `ProjectPrefabs`, `ScriptableObjects`, or `All` | Passed to `UnityEventScanner.Scan` |
| `ListenerIssue` | `UnityEventAuditor.Editor` | Validation classification: `None`, `MissingTarget`, `MissingMethodName`, `MethodNotFound` | Returned by `UnityEventScanner.ValidateListener` |
| `AuditSummary` | `UnityEventAuditor.Editor` | Immutable `readonly struct` holding `TotalEvents`, `TotalListeners`, `BrokenListeners`, `EmptyEvents`, and `EventsWithBrokenListeners` | Returned by `UnityEventScanner.ComputeSummary` |
| `EditorUtils` | `UnityEventAuditor.Editor` | Undo-safe serialized cleanup utilities (`RemoveBrokenListeners`, `RemoveAllBrokenListeners`) | Marks modified owners dirty via `EditorUtility.SetDirty` |
| `UnityEventAuditExporter` | `UnityEventAuditor.Editor` | Serializes scanned entries to JSON (`ToJson`) or RFC 4180 CSV (`ToCsv`) | Uses `UnityEngine.JsonUtility` for JSON output |
| `UnityEventAuditorWindow` | `UnityEventAuditor.Editor` | Split-pane `EditorWindow` for interactive auditing, inline editing, cleanup, and export | Opened via `UnityEventAuditorWindow.ShowWindow()` or menu item |

## Included samples

Import **Event Audit Demo** (`Samples~/EventAuditDemo`) from the Package Manager **Samples** tab:

- `SampleEventEmitter` (`UnityEventAuditor.Samples`): example `MonoBehaviour` declaring `onInteracted` (`UnityEvent`), `onDamaged` (`UnityEvent<int>`), and `onQuestUpdated` (`UnityEvent<string>`), along with a `Trigger Sample Events` Inspector context menu (`TriggerAll`) and sample callback methods (`LogInteraction`, `LogDamage`) for testing scanning, filtering, and broken-listener cleanup.

## Testing

The package includes EditMode NUnit tests under `Tests/Editor/` runnable from **Window > General > Test Runner** (EditMode tab):

- `Tests/Editor/UnityEventScannerAndCleanupTests.cs`: verifies top-level, nested (`[Serializable]` struct), and empty `UnityEvent` discovery, detection of all `ListenerIssue` variants (`MissingTarget`, `MissingMethodName`, `MethodNotFound`), `AuditSummary` totals, `EditorUtils.RemoveAllBrokenListeners` preserving valid listeners, `ScriptableObject` scanning via `ScanObject`, and CSV/JSON report export.

## Troubleshooting and limitations

- **Active Prefab Stage overrides loaded scene scanning:** When a prefab is open in Prefab Mode, `AuditScanScope.ActiveScenes` scans only that `PrefabStage` root instead of loaded scenes. Exit Prefab Mode to scan loaded scenes.
- **Only serialized persistent listeners are audited:** `UnityEventScanner` inspects `m_PersistentCalls.m_Calls` via `SerializedObject`. Runtime listeners added in code with `AddListener(...)` are not serialized and are not included in audit results.
- **Dynamic (`EventDefined`) bindings validate method names only:** For `PersistentListenerMode.EventDefined`, `ValidateListener` checks whether any instance method with the serialized name exists on the target's type hierarchy rather than verifying the closed generic `UnityEvent<T...>` argument signature.
- **`EventEntry.EventInstance` is `null` for nested events:** Direct reflection lookup only resolves top-level fields whose `PropertyPath` has no `.` separator. Nested `UnityEvent` fields inside structs, classes, arrays, and lists are still scanned, displayed, and cleaned up through `PropertyPath`.
- **Hidden GameObjects and non-`MonoBehaviour` components:** `ScanGameObjectHierarchy` inspects `MonoBehaviour` components (including inactive children) and skips GameObjects flagged with `HideFlags.HideInHierarchy`.
- **Window export scope vs. bulk fix scope:** In `UnityEventAuditorWindow`, **Export CSV** and **Export JSON** write the currently filtered and searched entries, whereas **Fix All Broken** cleans all broken listeners across the entire active scan.
- **Assets outside `Assets/`:** `UnityEventScanner.Scan` defaults `assetSearchFolder` to `"Assets"`. Pass an explicit folder path if you need to scan prefabs or `ScriptableObject` assets located under a specific subfolder or package path.

## License

[MIT](LICENSE)
