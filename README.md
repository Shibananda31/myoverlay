# MyOverlay — Floating Desktop Browser & Notes Overlay

A lightweight, high-performance Windows desktop application built with **C# .NET 8** and **Windows Forms (WinForms)** embedding an Edge Chromium browser (**Microsoft Edge WebView2**).

---

## Key Features

### 🌐 Embedded Chromium Browser (WebView2)
- **Multi-Tab Browsing**:
  - Independent tabs with individual navigation history, titles, and isolated page state.
  - Tab strip with page titles, active tab highlights, and close (`✕`) buttons on each tab.
  - Middle-click to close tabs.
  - Add new tab button (`+`).
- **Comprehensive Navigation Bar**:
  - **Back** (`◀`), **Forward** (`▶`), **Reload/Stop** (`↻`/`✕`), and **Home** (`🏠`).
  - **Omnibox / Address Bar**:
    - Accepts standard URLs (`https://example.com`, `http://...`, `file://`).
    - Smart domain detection (`www.example.com`, `github.com`, `localhost:3000` -> automatically prepends `https://`).
    - Search query support: automatically queries configurable search engine (default: Google).
  - **Go** button (`➜`) and **Close Tab** button (`✕`).
- **Full Chromium Web Features**:
  - HTTPS, modern JavaScript (ESNext), cookies, local storage, sessions, and redirects.
  - PDF viewing, file uploads, external links, context menus (Inspect / DevTools).
  - Popups and new window requests open neatly as new tabs inside the overlay.
- **Find on Page (`Ctrl + F`)**:
  - Built-in find bar with Next, Previous, and close buttons using in-engine search.
- **Persistent User Data Directory**:
  - Cookies, local storage, and browsing state persist across app restarts in `%LOCALAPPDATA%\MyOverlay\WebView2Data`.
- **Evergreen WebView2 Runtime Check**:
  - Gracefully detects if WebView2 Runtime is missing and displays a friendly install guide and download button.

---

### 🪟 Floating Overlay Window
- **Frameless & Borderless**: Sleek borderless window (`FormBorderStyle = FormBorderStyle.None`).
- **Always On Top**: Stays above other open applications (`TopMost = true`).
- **Draggable**: Drag the top header bar, empty space in the tab strip, or navigation toolbar to reposition.
- **Resizable**: Drag any border or corner (8px resize margin handled via `WM_NCHITTEST`).
- **Show / Hide Browser Panel**:
  - Toggle between Browser view and Notes view via the header button (`[🌐 Browser]` / `[📝 Notes]`), hotkey (`Ctrl + B`), or system tray menu.
  - Toggling does **NOT** destroy the browser session—active tabs, videos, and logins remain alive.
- **Click-Through Mode**:
  - When enabled (`WS_EX_TRANSPARENT`), mouse clicks pass directly through to background applications.
  - Shortcut: `Ctrl + Alt + C`.
- **System Tray Integration**:
  - Notification area tray icon with quick access menu to open Settings, toggle browser, center window, and exit.

---

### 🛡️ Non-Capturable Anti-Capture Shield (Screen Share & Recording Invisibility)
- **Full App Screen Capture Exclusion**:
  - Entire application (Overlay, Settings, Context Menus, and child controls) is completely excluded from desktop captures, window captures, and screen recordings.
- **Hardware-Level Display Affinity**:
  - Utilizes `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE` (`0x00000011`) on Windows 10 (2004+) & Windows 11.
  - Automatically falls back to `WDA_MONITOR` (`0x00000001`) if required by older display drivers.
- **Verified Screen Share & Recording Bypass**:
  - **Zoom**: Invisible during screen share and window share.
  - **Microsoft Teams**: Excluded from desktop and window sharing.
  - **Google Meet**: Invisible across desktop and application capture.
  - **OBS Studio**: Completely omitted from Display Capture and Window Capture.
  - **Windows Snipping Tool & PrintScreen**: Screenshots capture the content behind the overlay.
  - **Windows Game Bar & Screen Recorders**: Excluded from recorded video stream.
- **DWM Cloaking & Extended ToolWindow Attributes**:
  - `WS_EX_TOOLWINDOW` to bypass window enumeration in screen-sharing app pickers.
  - `DWMWA_EXCLUDED_FROM_PEEK` and `DWMWA_FLIP3D_POLICY` to prevent preview leakage during Aero Peek or Alt+Tab.
- **CBT Thread Hooking**:
  - Automatically monitors and enforces capture protection on any dynamically spawned windows, dialogs (`FontDialog`, `ColorDialog`), and popup context menus.

---

### 🔊 System Audio & Computer Audio Capture (WASAPI Loopback)
- **Zero-Microphone Audio Loopback**:
  - Captures computer/system audio directly from the Windows audio render mixing engine using NAudio's `WasapiLoopbackCapture`.
  - Does **NOT** record the physical microphone or rely on speaker acoustic leakage.
- **Wired Earphones / Headphones Automatic Switching**:
  - Automatically captures from whichever playback device Windows sends sound to:
    - Laptop / Desktop Speakers
    - Wired 3.5mm Earphones & Headphones
    - USB Headphones / Headsets / DACs
    - Bluetooth Headphones & Soundbars
  - Integrates `IMMNotificationClient` to listen for Windows default device change notifications (`OnDefaultDeviceChanged`, `OnDeviceStateChanged`). When wired earphones are plugged in or unplugged, the capture switches seamlessly to the active output without restarting the application.
- **Dedicated Independent Sources**:
  - Dedicated `SystemAudioCapture` component kept strictly separate from microphone capture.
  - Dedicated `MicrophoneAudioCapture` component for separate voice input.
  - Unified `AudioCaptureManager` pipeline with selectable speech/transcription sources:
    1. **Computer / System Audio** (WASAPI Loopback)
    2. **Microphone**
    3. **Both** (Tagged independent streams)
- **Settings UI & Live VU Meters**:
  - Checkbox: `[x] Capture computer/system audio`.
  - Dropdown of all active Windows playback endpoints with instant `🔄 Refresh`.
  - Live real-time audio output VU level meter to visually confirm loopback sound flow.
  - Graceful handling of unplugged/disabled devices and invalid formats.

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **Ctrl + B** | Toggle Browser Panel (Show / Hide without destroying session) |
| **Ctrl + T** | Open New Tab |
| **Ctrl + W** | Close Active Tab |
| **Ctrl + L** | Focus and select Address Bar |
| **Ctrl + R** / **F5** | Reload Current Page |
| **Ctrl + Tab** | Next Tab |
| **Ctrl + Shift + Tab** | Previous Tab |
| **Ctrl + F** | Find on Page |
| **F12** | Open Chromium Developer Tools |
| **Ctrl + Alt + C** | Toggle Click-Through mode |
| **Ctrl + Alt + S** | Open Settings dialog |
| **Ctrl + Alt + H** | Toggle Overlay Visibility (Hide / Show) |

---

## VS Code Terminal Commands

Open the integrated terminal in VS Code (`Ctrl + \`` or **Terminal -> New Terminal**) within `C:\myFolder\Projects\Windows APPS\browser\MyOverlay`:

### 1. Restore Dependencies
```powershell
dotnet restore
```

### 2. Build the Application (Debug)
```powershell
dotnet build
```

### 3. Run the Application
```powershell
dotnet run
```

### 4. Build Optimized Release
```powershell
dotnet build -c Release
```

### 5. Publish Standalone Single-File `.exe` (Self-Contained)
Bundles the .NET 8 runtime and native dependencies into a single `.exe` file that runs on any 64-bit Windows 10/11 machine without needing .NET preinstalled:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
The resulting executable will be located at:
```
bin\Release\net8.0-windows\win-x64\publish\MyOverlay.exe
```

### 6. Publish Lightweight Single-File `.exe` (Framework-Dependent)
Creates a small (~250 KB) standalone executable for machines that already have the .NET 8 Desktop Runtime installed:
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---

## Project Structure

```
MyOverlay/
├── MyOverlay.csproj          # Project file (.NET 8 WinForms + Microsoft.Web.WebView2 + NAudio)
├── app.manifest              # Windows 10/11 execution & DPI awareness manifest
├── Program.cs                # Application entry point & self-test suite
├── OverlayForm.cs            # Floating overlay window with header, dragging, hotkeys, and tray
├── BrowserPanel.cs           # Multi-tab browser container, toolbar, omnibox, and search
├── BrowserTab.cs             # Individual tab controller and WebView2 instance
├── SettingsForm.cs           # Settings interface (browser defaults, audio devices, notes, colors, opacity)
├── AppSettings.cs            # Configuration model and JSON persistence
├── SystemAudioCapture.cs     # WASAPI Loopback system audio capture (speakers, earphones, headphones)
├── MicrophoneAudioCapture.cs # Independent microphone capture component
├── AudioCaptureManager.cs    # Pipeline coordinator connecting audio sources to voice/speech consumers
├── AudioDeviceInfo.cs        # Audio endpoint data model (ID, friendly name, default status)
├── AudioDataEventArgs.cs     # PCM audio buffer event arguments & source enums
├── ScreenCaptureProtection.cs # Capture exclusion engine (WDA_EXCLUDEFROMCAPTURE, CBT hooks, DWM)
├── NativeMethods.cs          # Win32 P/Invoke declarations (display affinity, hotkeys, dragging)
└── README.md                 # Documentation and terminal commands
```
