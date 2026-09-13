# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-09-12
### Added
- Deep `SerializedProperty` traversal in `UnityEventScanner` so nested `UnityEvent` fields inside `[Serializable]` structs, classes, arrays, and lists are discovered alongside top-level fields.
- Reflection-backed method validation (`ListenerIssue.MethodNotFound`) that flags listeners whose bound method was renamed, deleted, or has an incompatible signature, not only null targets or empty method names.
- Configurable `AuditScanScope` supporting loaded scenes / active PrefabStage, Project Prefab assets (`t:Prefab`), and `ScriptableObject` assets.
- `Empty` (unused) filter, summary statistics banner, one-click **Fix All Broken** bulk cleanup with Undo grouping (`EditorUtils.RemoveAllBrokenListeners`), and **CSV / JSON** report export (`UnityEventAuditExporter`).
- EditMode NUnit test suite covering top-level & nested event scanning, missing-target/missing-method detection, ScriptableObject scanning, bulk cleanup, and CSV/JSON export.

## [1.0.0] - 2025-07-01
### Added
- Initial release with `UnityEventScanner`, `UnityEventAuditorWindow`, `EditorUtils.RemoveBrokenListeners`, and `Samples~/EventAuditDemo`.
