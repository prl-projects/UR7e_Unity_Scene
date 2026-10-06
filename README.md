# UR7e Unity Scene

Unity scene that mirrors a **Universal Robots UR7e** (real robot or URSim) and a
**DH-Robotics PGC-140-50** electric gripper. The arm model follows the robot's joint
angles live, and the gripper model follows robot **digital output 4 (DO4)** or DH PGC
programs sent from Unity.

| Item | Value |
|---|---|
| Unity version | **2022.3.30f1** |
| Scene | `Assets/Scenes/UR7e_Unity_Scene.unity` |
| Robot | UR7e (e-Series) or URSim UR7e |
| Gripper | DH-Robotics PGC-140-50 (model only; real gripper needs the DH PGC URCap) |
| Robot connection | UR Primary Client Interface, TCP port **30001** |
| Gripper callback | Robot → Unity, TCP port **5000** |

---

## ⚠️ Set the IP addresses first

The scene still contains the IP addresses of the original lab. **Nothing will connect
until you replace them with your own.** There are two kinds of IP address:

| Field | What to enter | Where it is |
|---|---|---|
| **Robot IP** | The IP address of the UR7e / URSim, read from **PolyScope** or **URSim IP** | `UR7e_RobotArm` and **both** `PGCCommandConsole` components |
| **Unity Listen IP** | The IP address of **your own computer** (the PC running Unity) on the same network as the robot | **Both** `PGCCommandConsole` components |

### All fields to change

| GameObject | Component | Field |
|---|---|---|
| `RobotOrigin/UR7e` | `UR7e_RobotArm` | Robot IP |
| `RobotOrigin/UR7e` | `PGCCommandConsole` | Robot IP |
| `RobotOrigin/UR7e` | `PGCCommandConsole` | Unity Listen IP |

`...` = `RobotOrigin/UR7e/base_link/base_link_inertia/shoulder_link/upper_arm_link/forearm_link/wrist_1_link/wrist_2_link/wrist_3_link/flange/tool0`

> Edit these fields **outside Play mode** (changes made in Play mode are lost), then save the scene (Ctrl+S).
> The second console comes from the `PGC-140-50` prefab. If you want the new values to become the prefab
> default, use **Overrides → Apply All** on that prefab instance.

### 1. Robot IP — read it from PolyScope

On the teach pendant (or the URSim PolyScope window):

1. Tap the **≡ (hamburger) menu → Settings → System → Network**.
2. Copy the **IP address** shown there (for example `192.168.0.121`).
3. Paste it into **Robot IP** on `UR7e_RobotArm` and on both `PGCCommandConsole` components.

All three Robot IP fields must be the same address.

### 2. Unity Listen IP — your computer's IP

This is where the robot sends gripper status back to Unity, so it must be **your PC's address
on the robot's network**:

1. On the PC running Unity, open **Command Prompt** and run `ipconfig`.
2. Find the network adapter that is on the **same subnet as the robot**
   (robot `192.168.0.231` → look for an IPv4 address `192.168.0.x`, subnet mask `255.255.255.0`).
   If URSim runs in a VirtualBox/VMware VM on the same PC, this is usually the *Host-Only* / *VMnet* adapter.
3. Enter that **IPv4 Address** in **Unity Listen IP** on both `PGCCommandConsole` components.

---

## Quick start

1. Open this folder in **Unity Hub** with Unity **2022.3.30f1**.
2. Open `Assets/Scenes/UR7e_Unity_Scene.unity`.
3. **Set all IP addresses** (section above) and save the scene.
4. Start the robot / URSim, power on, release brakes, and switch to **Remote Control**.
5. Press **Play**. The UR7e model should follow the robot's joints.
6. Toggle **DO4** in PolyScope (**I/O** tab). The PGC-140-50 model closes when DO4 is ON and opens when it is OFF.

### Two ways to drive the gripper model

| Mode | Component | Needs | Notes |
|---|---|---|---|
| DO4 mirror | `PGCDigitalOutputSync` | Nothing extra (works in URSim) | Updates every frame. |
| DH programs | `PGCCommandConsole` | DH PGC URCap on the robot, correct **Unity Listen IP**, firewall open | Model moves when the program reaches each `dh_pgc_set_position`. |

Use one at a time. While `PGCDigitalOutputSync` is enabled it overrides the console every frame, so disable it before running DH programs.

---

## Scene hierarchy and required components

If a component shows **"Missing (Mono Script)"** or was removed, re-add it here.
Select the GameObject → **Add Component** → type the script name. Then fill the fields as listed.

```
Main Camera                         ← CameraControl
Directional Light (inactive)
RobotOrigin
└── UR7e                            ← UR7e_RobotArm, PGCCommandConsole
    └── base_link/base_link_inertia/shoulder_link/upper_arm_link/forearm_link/
        wrist_1_link/wrist_2_link/wrist_3_link/flange/tool0
        └── PGC-140-50              ← PGCGripper, PGCDigitalOutputSync
            ├── GripperX+
            ├── GripperX-
            ├── GripperCenter
            ├── PGCCommandConsole   ← PGCCommandConsole
            └── PGCDemoProgram      ← PGCDemoProgram
```

### 1. `RobotOrigin/UR7e` → `UR7e_RobotArm` (required)

Connects to the robot on port 30001 and rotates the six joints of the model.

| Field | Value |
|---|---|
| Robot IP | Your robot IP from PolyScope |
| Transforms (size 6) | `shoulder_link`, `upper_arm_link`, `forearm_link`, `wrist_1_link`, `wrist_2_link`, `wrist_3_link` (drag them in this order from the UR7e hierarchy) |

It connects automatically when you press Play.

### 2. `.../tool0/PGC-140-50` → `PGCGripper` (required)

Moves the two finger meshes. 0 % = closed, 100 % = open.

| Field | Value |
|---|---|
| Gripper X Plus | `GripperX+` (child of PGC-140-50) |
| Gripper X Minus | `GripperX-` (child of PGC-140-50) |
| Opening Percent | `100` |
| Max Opening | `50` (mm, PGC-140-50 stroke) |
| Plus Close Axis | `(0, 0, -1)` |

### 3. `.../tool0/PGC-140-50` → `PGCDigitalOutputSync` (required for DO4 sync)

Opens/closes the gripper model from a robot digital output.

| Field | Value |
|---|---|
| Arm | `UR7e` (the `UR7e_RobotArm` component) |
| Gripper | `PGC-140-50` (the `PGCGripper` component) |
| Output Index | `4` (standard digital output DO4) |
| On Means Closed | ✔ (DO4 ON = close). Untick to invert. |
| Full Stroke Seconds | `0.75` |

### 4. `PGCCommandConsole` (optional, for DH PGC programs)

There are two in the scene: one on `RobotOrigin/UR7e` and one on `PGC-140-50/PGCCommandConsole`.
`PGCDemoProgram` uses the second one.

| Field | Value |
|---|---|
| Robot IP | Your robot IP from PolyScope |
| Port | `30001` |
| Unity Listen IP | **Your PC's IP** (see above) |
| Unity Listen Port | `5000` |
| Template File Name | `dh_pgc_template.script` (must exist in `Assets/URScripts/`) |
| Unity Gripper | `PGC-140-50` (the `PGCGripper` component) |

### 5. `PGC-140-50/PGCDemoProgram` → `PGCDemoProgram` (optional)

| Field | Value |
|---|---|
| Pgc Console | `PGC-140-50/PGCCommandConsole` |

Right-click the component header → **Run Full DH PGC Function Test** (in Play mode).
**This moves the robot** through three `movej` poses — make sure the workspace is clear.

### 6. `Main Camera` → `CameraControl` (optional)

Hold the right mouse button to look around, **W/A/S/D** to move, **Shift** to move faster.

---

## Scripts

| File | Class | Depends on |
|---|---|---|
| `Assets/UR7_script/UR7e_RobotArm.cs` | `UR7e_RobotArm` (+ `Axis` enum) | `UR7ePackageListener` |
| `Assets/UR7_script/UR7ePackageListener.cs` | `UR7ePackageListener` (namespace `Assets.Scripts`) | — |
| `Assets/UR7_script/PGCGripper.cs` | `PGCGripper` | — |
| `Assets/PGCDigitalOutputSync.cs` | `PGCDigitalOutputSync` | `UR7e_RobotArm`, `PGCGripper` |
| `Assets/UR7_script/PGCCommandConsole.cs` | `PGCCommandConsole` (+ `ReadOnlyAttribute`) | `PGCGripper`, `Assets/URScripts/dh_pgc_template.script` |
| `Assets/UR7_script/PGCDemoProgram.cs` | `PGCDemoProgram` | `PGCCommandConsole` |
| `Assets/Scripts/CameraControl.cs` | `CameraControl` | — |
| `Assets/URScripts/dh_pgc_template.script` | URScript template with DH PGC functions and `{{PROGRAM_BODY}}` | DH PGC URCap on the robot |

Keep each file name identical to its class name, otherwise Unity cannot attach it as a component.
---

## Troubleshooting

| Symptom | Check |
|---|---|
| Arm model doesn't move | Robot IP on `UR7e_RobotArm`; `ping` the robot; Primary Client Interface enabled; port 30001 not blocked. |
| Gripper ignores DO4 | `PGCDigitalOutputSync` enabled and its **Arm/Gripper** fields set; Output Index = `4`; you toggled a **standard** DO (tool outputs are bits 16–17). |
| No `[PGC Sync] Received:` lines in the Console | **Unity Listen IP** must be your PC's IP on the robot network; firewall allows TCP 5000; robot is in Remote Control; DH PGC URCap installed. |
| Robot rejects programs | Pendant is in **Local** mode — switch to **Remote**. |
| "Missing (Mono Script)" or "can't add script" | Fix compile errors in the Console first; file name must equal class name; re-add the component using the tables above. |
|---|---|

#### Setup UR7e in URSim
- Situation：Can’t find UR7e URSim in VM, unlike UR3, 5, 10, 20.
- VM version：URSim_VIRTUAL-5.12.6.1102099 or newer version (5.9.x unable to find UR7e)
- Solution：Write a Exec manually
    1. Open terminal at the left below corner of URSim
    2. Create the Desktop File
       ```
       nano ~/Desktop/UR7-URSim.desktop
       ``` 
    3. Paste the config on leadpad
       ``` 
       [Desktop Entry]
       Version=1.0
       Type=Application
       Terminal=false
       Name=URsim UR7e
       Exec = bash -c "cd ~/ursim-current && ./start-ursim.sh UR7e"
       Icon=utilities-terminal
       Categories=Applicaiton;
       ```
       And press Ctrl+O to write out, then Enter to leave.
    4. (Optional) Make the file executable within Terminal
       ``` 
       chmod +x ~/Desktop/UR7-URSim.desktop
       ``` 
    5. The exec file will be able to open up UR7e

---

## Third-party content

- UR7e visual meshes: Universal Robots `ur_description` (UR5e visual meshes).
- PGC-140-50 model and `dh_pgc_template.script`: DH-Robotics.

Check the respective licenses before reusing these files.
