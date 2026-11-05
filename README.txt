keyMouse for Windows
=========================

keyMouse lets you move the pointer, click, and scroll using the keyboard.

Install and start
-----------------
1. On a 64-bit Windows PC, run keyMouseSetup.exe.
2. Review the MIT license. On Choose Components, "Start keyMouse when
   I sign in" is selected by default. Clear it for manual startup only.
3. Select the launch option on the last page, or open keyMouse from Start.
   The app runs in the background without opening a window.

Automatic and manual startup
----------------------------
When enabled, a shortcut in your Windows Startup folder launches the app at
sign-in for your account. To start it yourself at any time, use the Start menu
or run %LOCALAPPDATA%\Programs\keyMouse\keyMouse.exe directly.
If it is already running, the second copy exits. F12 quits the current session;
you can launch it again immediately or let it start at your next sign-in.
To disable automatic startup later, press Win+R, enter shell:startup, and
delete the keyMouse shortcut. To enable it again, put a shortcut to the
installed EXE in that folder. Uninstalling removes the Startup shortcut.

Keyboard controls
-----------------
Hold the modifier key(s) first, then press Enter or an arrow key.

Ctrl + Enter             Left-click once at the pointer
Ctrl + Shift + Enter     Right-click once
Shift + Enter            Middle-click (press the mouse wheel)
Ctrl + arrow key(s)      Move while held; two directions move diagonally
Shift + Up / Down        Scroll up / down while held
F12                      Release F12 to quit

Holding Enter produces one click. Release and press Enter again for another.
Ctrl takes priority over Shift for arrow keys. The app blocks Enter or arrow
events used for these shortcuts, including repeats and release. Ctrl and Shift
themselves can still reach the active app so other shortcuts can work.
The app temporarily releases held Ctrl/Shift modifiers when sending a click
or scroll. Behavior can vary in apps running with elevated privileges.

Settings
--------
Open %LOCALAPPDATA%\Programs\keyMouse\settings.ini in a text editor.
The default file contains:

[Mouse]
MoveStep=6
PollIntervalMs=10

MoveStep sets the relative movement per update. Increase it for faster motion.
PollIntervalMs sets the requested timer interval in milliseconds. A lower
value requests more frequent updates. Windows may deliver ticks less often.
Both settings accept integers from 1 to 1000. Press F12 and start keyMouse
again to load your changes.

Uninstall or reinstall
----------------------
Quit with F12 first. In Windows Settings, open Apps > Installed apps, select
keyMouse, and choose Uninstall. You can also run Uninstall.exe from the
installation folder. The uninstaller keeps settings.ini so a later installation
can reuse your preferences. Delete that file yourself for a complete reset.
Before installing a renamed build, back up settings.ini, quit the previous
app with F12 and uninstall it. Copy the saved settings into the new folder.

Build the installer (for the publisher)
----------------------------------------
Install the .NET 8 or .NET 10 SDK and NSIS 3 on Windows. The example assumes
a project named keyMouse that publishes keyMouse.exe and settings.ini. If the
project file has a different name, use its actual path in the publish command
and set <AssemblyName>keyMouse</AssemblyName> in its PropertyGroup.
The installer checks the same mutex name as the supplied C# application.
If you already published the app, place its complete publish folder beside
installer.nsi. It must contain keyMouse.exe, settings.ini and the runtime files.

If you need to publish the app first, run this from the project folder:
  dotnet publish keyMouse.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o .\publish
Copy the complete publish folder next to installer.nsi if it is elsewhere.

Open MakeNSISW and drag installer.nsi into its window, or right-click
installer.nsi and select "Compile NSIS Script" if that option is available.
The compiler runs the included make-payload.cmd automatically to enumerate
the published files. Keep this helper, README.txt, and LICENSE.txt beside
installer.nsi. The resulting keyMouseSetup.exe appears in the same folder.

Check the finished installer on Windows: launch from Start, try Ctrl+Enter
while editing text (no newline should appear), try scrolling and movement,
then press F12 and uninstall. Confirm that settings.ini is preserved.

License
-------
The keyMouse application and installer scripts are distributed under the
MIT License in LICENSE.txt. Bundled runtime components retain their own terms.
