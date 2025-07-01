# Unity Event Auditor

Unity Event Auditor is a Unity Editor tool for Unity 6 (6000.0+) to find, inspect, and clean up `UnityEvent` bindings in your scenes and prefabs.

---

## Features

- Scan All UnityEvents: automatically find all UnityEvent fields across your scene(s).
- Search & Filter: search by GameObject name or event field name, filter for All, Valid, or Broken listeners.
- Hierarchy View with Icons: browse GameObjects in a collapsible tree view with foldouts and parent/child relationships.
- Auto-expand parents when searching: keep matches visible in context.
- Multi-select support: hold Shift to select multiple GameObjects in the tree.
- Auto-ping selected objects: selecting a GameObject highlights it in the Unity Hierarchy.
- Event Inspector Panel: inspect UnityEvents, see listeners, and remove broken ones.
- Expand/Collapse All: expand or collapse the entire hierarchy tree.

---

## Installation

**Option 1: Unity Package Manager (Recommended)**
1. Open `Window > Package Manager` in Unity 6.
2. Click `+` > **Install package from git URL...**
3. Enter:
   ```text
   https://github.com/haruchanz64/UnityEventAuditor.git
   ```

**Option 2: Download ZIP**
1. Download the repository as a **ZIP** (`Code > Download ZIP`).
2. Extract and drop the folder into your Unity project's `Assets` directory.

**Option 3: Git Clone**
1. Clone this repository directly into your Unity project's `Assets` directory:
   ```bash
   git clone https://github.com/haruchanz64/UnityEventAuditor.git Assets/UnityEventAuditor
   ```

---

## Usage

- Open the auditor: `Tools > Unity Event Auditor`.
- Use the left panel to navigate your hierarchy.
- Click a `GameObject` to see its UnityEvents on the right.
- Search, filter, and clean up broken listeners.

---

## License

[MIT](https://github.com/haruchanz64/UnityEventAuditor/blob/main/LICENSE). You can use, modify, and share this tool.

---
