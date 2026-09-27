# 🚛 CabSpan — Easy Multi-Monitor Setup & 1-Click Screen Switcher for ATS & ETS2

[![Release](https://img.shields.io/badge/version-v1.0.0%20(Experimental%20Preview)-C8A017?style=for-the-badge&logo=windows)](https://github.com/itsjuztin/CabSpan/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20(64--bit)-252228?style=for-the-badge&logo=windows11)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20Desktop%20Runtime-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
[![GitHub](https://img.shields.io/badge/GitHub-itsjuztin%2FCabSpan-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/itsjuztin/CabSpan)
[![Discord](https://img.shields.io/badge/Discord-Community%20%26%20Help-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/JjUFYu3Jff)
[![Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20Project-FF5E5B?style=for-the-badge&logo=kofi&logoColor=white)](https://ko-fi.com/itsjuztin)

Welcome to **CabSpan**! 👋

If you have ever tried to stretch **American Truck Simulator (ATS)** or **Euro Truck Simulator 2 (ETS2)** across 2, 3, or 4 monitors, you probably ran into a wall of confusing text files (`multimon_config.sii`), weird camera math, or graphics card settings (*NVIDIA Surround / AMD Eyefinity*) that mess up your Windows taskbar and lock up your extra monitors.

**CabSpan fixes all of that in 4 easy steps — with zero coding, zero math, and zero risk to your game saves.**

> 🧪 **Experimental Community Preview — Safe to Try (`↺ 1-Click Undo Backup`):**
> **CabSpan is an active experimental release!** Because every sim-rig's combination of monitor resolutions, Windows scaling, and AMD Eyefinity / NVIDIA Surround driver settings is unique, you may occasionally run into quirks or edge cases.
> **You can try it with 100% peace of mind:** Before CabSpan makes a single change, it automatically saves a backup copy of your untouched original game configuration (`config.single_monitor.bak`). Clicking **`↺ Undo / Restore Default`** puts your game right back to normal in one click — and if you spot a bug on your setup, click **`🐞 Report Bug`** inside the app or let us know on [Discord](https://discord.gg/JjUFYu3Jff) so we can dial it in!

---

## 🌟 Why Truckers Love CabSpan

- **🛞 Fixes the "Steering Wheel in the Crack" Problem (2-Monitor & 3-Monitor Rigs):**
  Using 2 monitors? Normally, the game puts your steering wheel right in the plastic gap between your two screens! With CabSpan, you just click **`★ Put Steering Wheel Here`** on the monitor in front of your chair. Your steering wheel, dashboard gauges, GPS map, and pause menus stay centered on that screen while your second screen becomes a natural side window.
- **🖥️ Keep Extra Monitors Free for Your Desktop:**
  Have 3 or 4 monitors connected, but only want to drive on 2 of them while keeping the others free for Discord, YouTube, Spotify, or Trucky/SimHub? Just click the monitors you want **`USED FOR DRIVING`** and leave the others **`KEPT FREE FOR DESKTOP`**.
- **⚡ 1-Click Switch Back to 1 Screen Anytime:**
  Don't feel like using all your monitors today? Open CabSpan and click **`🖥️ Switch Back to 1 Screen`**. Your game goes right back to normal single-monitor play. Whichever mode is currently active lights up in **bright Gold (`✓ Active`)** so you always know which mode your game is in at a glance.
- **🪟 Works With OR Without NVIDIA Surround / AMD Eyefinity:**
  You don't have to merge your Windows desktop into one giant screen if you don't want to! CabSpan works great with Surround/Eyefinity, *and* includes an optional **Borderless Window Spanner** for regular Windows desktops.

---

## 🚀 Getting Started in 4 Simple Steps

1. **Download & Open:** Grab `CabSpan-v1.0.0.zip` (or `CabSpan.exe`) from the [**Releases Page**](https://github.com/itsjuztin/CabSpan/releases/latest), put it in a folder on your PC, and double-click **`CabSpan.exe`**.
   *(On your very first launch, a friendly **Guided Setup Wizard** will walk you through these 4 steps automatically!)*
   *(💡 **Windows SmartScreen Note:** Because CabSpan is a free, community-built open-source tool without a corporate code-signing certificate, Windows may show a blue **"Windows protected your PC"** prompt the very first time you open it. Simply click **More info** → **Run anyway**.)*
2. **Follow the 4 Steps on the Dashboard:**
   - **STEP 1 — Pick Your Game(s):** Check the box for **American Truck Simulator**, **Euro Truck Simulator 2**, or **Both Games**. The status pills next to each game show whether it is currently in `🚛 Multi-Screen` or `🖥️ 1-Screen (Normal)` mode.
   - **STEP 2 — Select Your Driving Screens & Steering Wheel Position:** Click any monitor on the visual map to turn it **ON** (`USED FOR DRIVING`) or **OFF** (`KEPT FREE FOR DESKTOP`). Then click **`★ Put Steering Wheel Here`** on the screen directly in front of your seat.
   - **STEP 3 — Fine-Tune Monitor Borders & Desk Angle *(Optional)*:**
     - *Screen Border Gap (`2.5° Standard Frame` default):* Hides a tiny slice of the road behind your plastic monitor borders so highway lines and mirrors line up straight across the gap instead of looking bent.
     - *Side Screen Angle (`0.0° Flat Line` default):* Leave at `0.0°` if your monitors sit flat side-by-side, or slide higher (`15°–45°`) if your side monitors are angled inward toward your seat.
   - **STEP 4 — Save Changes to Your Game:** Click **`⚡ Apply Multi-Screen Setup`** (while your game is closed). The button will turn **Gold (`⚡ Multi-Screen Setup ✓ Active`)** and confirm at the bottom of the window that your setup is saved!

---

## ⏱️ Timeline FAQ: When Do I Need to Open CabSpan?

If you are wondering where CabSpan fits into your everyday gaming routine, here is everything you need to know:

### ❓ 1. Do I need to open CabSpan every single time I launch the game?
**No!** When you click **`⚡ Apply Multi-Screen Setup`**, CabSpan writes your multi-monitor camera angles and screen settings directly into your game's configuration folder (`Documents\American Truck Simulator` and/or `Documents\Euro Truck Simulator 2`).
**Those settings stay saved permanently on your PC.** Once you click Apply, you can close CabSpan and launch your game tomorrow, next week, or next month — your multi-screen setup will still be waiting for you!

### ❓ 2. When DO I need to open CabSpan?
You only need to open CabSpan when you want to **change** something:
- When you want to take a break from multi-screen driving and click **`🖥️ Switch Back to 1 Screen`**.
- When you want to switch back to **`⚡ Apply Multi-Screen Setup`**.
- When you want to tweak your **Screen Border Gap** or **Side Screen Angle** sliders.

### ❓ 3. Should I click Apply before or after starting the game?
**Always click Apply or Switch *before* starting the game** (while ATS/ETS2 is closed). The game only reads its display configuration files at the exact moment it boots up.

### ❓ 4. How does the `📋 Optional: Steam Auto-Launch` command work?
In Step 4, you will see an optional button labeled **`📋 Optional: Steam Auto-Launch`**.
You **do not** have to use this if you just switch modes manually in CabSpan. However, if you copy that command (`"C:\Path\To\CabSpan.exe" --auto-launch %command%`) and paste it once into Steam (*Right-click Game in Steam → Properties → Launch Options*), here is what happens every time you click the green **▶ Play** button in Steam:
1. **Steam hands the baton to CabSpan for 1 second** invisibly in the background right before the game window opens.
2. **If you use NVIDIA Surround or AMD Eyefinity:** CabSpan checks whether you currently have Surround/Eyefinity turned **ON** or **OFF** in Windows.
   - If Surround is **ON**, CabSpan automatically ensures your game is in **Multi-Screen Mode**.
   - If you turned Surround **OFF** for regular desktop work and hit Play in Steam, CabSpan automatically switches the game to **1-Screen Mode** so the game never launches squished into a tiny strip on one screen!
3. **If you DO NOT use NVIDIA Surround or AMD Eyefinity (Borderless Window Mode):** CabSpan starts the game, waits a couple of seconds for the game window to appear, automatically stretches the borderless game window across all your selected driving screens, and then **immediately closes itself** so it uses **0% CPU and 0% RAM** while you drive!

---

## 🔄 Hands-Free Updates & Built-In Safety

- **One-Click In-App Updates:** Whenever a new version of CabSpan is released on GitHub, a gold notification bar appears at the top of the app with three simple options: **`⚡ Update & Restart Now`**, **`Always Update`**, or **`Silence 1 Week`**. It downloads and updates `CabSpan.exe` automatically without making you open a browser or unzip files.
- **Original Config Backup (`↺ Undo / Restore Default`):** Your untouched original `config.cfg` is safely backed up as `config.single_monitor.bak`. Clicking **`↺ Undo / Restore Default`** restores your original game configuration at any time.

---

## 🔬 Under the Hood (Technical Details for Sim-Rig Builders)

For fellow developers and sim-rig builders curious about what CabSpan does behind the scenes:

1. **Win32 & CCD Hardware Display Scanning (`MonitorScanner.cs`):**
   Enumerates connected displays via `EnumDisplayMonitors`, `GetMonitorInfo`, and `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo` to read real monitor model names (EDID) and exact virtual desktop pixel coordinates `(X, Y, Width, Height)`.
2. **SCS Prism3D `multimon_config.sii` Frustum Generator (`SiiGenerator.cs`):**
   Computes normalized `[0.0, 1.0]` viewport coordinates (`normalized_x`, `normalized_width`), pins the Route Advisor HUD and main menus (`normalized_ui_x`, `normalized_ui_width`) to the monitor marked **`★ Steering Wheel & Menus Here`**, and calculates `horizontal_fov_relative_offset` and `heading_offset` using your physical bezel gap (`0.0°–6.0°`) and side monitor angle (`0.0°–45.0°`).
3. **Automatic `config.cfg` Cvar Management (`ConfigManager.cs`):**
   Safely toggles `uset r_multimon_mode` between `"4"` (custom multi-monitor frustum mode) and `"0"` (single-monitor mode), updates `uset r_mode_width`, and sets `uset g_interior_camera_zero_pitch "1"` in multi-screen mode so side-window horizons stay level.
4. **Win32 Borderless Window Spanner (`BorderlessSpanner.cs`):**
   Strips `WS_CAPTION` and `WS_THICKFRAME` via `SetWindowLong` and positions the `prism3d` (`amtrucks` / `eurotrucks2`) window across `(CabLeft, CabTop, TotalCabWidth, TotalCabHeight)` via `SetWindowPos`.

---

## 💜 Community, Help & Support

Built by **Justin (`@itsjuztin`)** for the American Truck Simulator & Euro Truck Simulator 2 community.
Have a question about your monitor layout, found a bug, or want to hang out with other truckers?

- 💬 **Join our Discord Community:** [discord.gg/JjUFYu3Jff](https://discord.gg/JjUFYu3Jff)
- 💻 **GitHub Repository & Releases:** [github.com/itsjuztin/CabSpan](https://github.com/itsjuztin/CabSpan)
- ☕ **Support the Project on Ko-fi:** [ko-fi.com/itsjuztin](https://ko-fi.com/itsjuztin)

---

## 📄 License

Released under the [MIT License](LICENSE). Free and open-source forever. Happy trucking! 🚛💨
