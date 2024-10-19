# ScreamRouter Windows Desktop

ScreamRouter Windows Desktop is a C# application that provides a desktop interface for controlling ScreamRouter, an audio management system.

![ScreamRouter Windows Desktop Screenshot](/screenshot.png)


## Features

- Web interface that opens the ScreamRouter URL in a web browser
- Notification Area Icon with context menu
- Global media key support
- Start Menu pinning support
- Transparent background with blur effect

## Requirements

- Windows 10 or later
- .NET 8.0 or later
- Microsoft Edge WebView2 Runtime

## Building and Running

1. Clone the repository:
   ```
   git clone https://github.com/yourusername/screamrouter-windows-desktop.git
   ```

2. Open the solution in Visual Studio 2022 or later.

3. Restore NuGet packages.

4. Build the solution.

5. Run the application.

## Usage

- The application runs in the system tray.
- Left-click the tray icon to open the web interface.
- Right-click the tray icon to access settings or exit the application.
- Use global media keys to control playback.

## Configuration

- Set the ScreamRouter Desktop Menu URL in the configuration window. Example: https://screamrouter.netham45.org/site/desktopmenu