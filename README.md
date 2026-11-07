# Unity Event Auditor

Unity Editor tool for Unity 6 (`6000.0+`) that locates, validates, reports, and cleans up broken or unused `UnityEvent` listeners across loaded scenes, project prefabs, and `ScriptableObject` assets.

## Features

- Deep serialized scanning discovers top-level `UnityEvent` fields as well as nested `UnityEvent`s inside `[Serializable]` structs, classes, arrays, and lists.
- Broken-listener detection flags missing targets (`MissingTarget`), unset methods (`MissingMethodName`), and renamed, deleted, or signature-incompatible callbacks (`MethodNotFound`).
- Multi-scope auditing scans loaded scenes (or the active Prefab Stage) and optionally includes all project `Prefab` and `ScriptableObject` assets.
- Filter by `All`, `Valid`, `Broken`, or `Empty` (unused) events, inspect live `SerializedProperty` widgets, and remove broken listeners individually or across the entire scan with `Fix All Broken` (Undo-supported).
- Export filtered audit results to CSV or JSON via `UnityEventAuditExporter`.

## Installation

### Option 1: Unity Package Manager (Git URL)
1. Open `Window > Package Manager` in Unity 6.
2. Click `+` > `Install package from git URL...`
3. Enter:
   ```text
   https://github.com/haruchanz64/UnityEventAuditor.git
   ```

### Option 2: Manual Installation
Clone or extract the repository into your project's `Assets/` directory.

## Usage

1. Open `Tools > HaruChanZ64 > Analysis > UnityEventAuditor > Unity Event Auditor`.
2. Toggle `Prefabs` or `ScriptableObjects` in the toolbar to scan project assets in addition to loaded scenes.
3. Search by object name or property path, filter by `Broken` or `Empty`, and use `Remove Broken Listeners` or `Fix All Broken` to clean up invalid bindings.
4. Click `Export CSV` or `Export JSON` to generate an audit report.

## License
[MIT License](LICENSE)
