# Lichtbak

All the pictures you open, together in one window with thumbnails in a sidebar, like
Preview on the Mac. For Windows 10 and 11.

On a Mac you can select twenty photos, open them, and flip through exactly those twenty in
one Preview window. Windows opens a separate window for each one, or shows the whole folder
instead of your selection. Lichtbak (Dutch for *light box*) fixes that.

## Download

Grab `Lichtbak-x.y.z-setup.exe` from [Releases](../../releases/latest) and run it. It
installs for you only, so no administrator rights are needed. Afterwards the setup offers to
make Lichtbak your default app for pictures.

Windows may warn that the file comes from an unknown publisher, because it is not signed.
Choose *More info* > *Run anyway*.

## How it works

- Select pictures in Explorer and choose **Open with > Lichtbak**. If Lichtbak is your
  default app, **Enter** or a double-click works too. In the classic right-click menu
  (*Show more options*) you will also find **Open in Lichtbak**.
- Everything you open ends up in **one window**. Pictures you open later are added to the
  same window. A selection is sorted by name, the way Explorer sorts it (photo2 before photo10).
  Prefer a window per picture? Right-click > **Open each picture in its own window**.
- **Sidebar:** click a thumbnail to show it. Drag thumbnails to change the order, or drag one
  into another app or folder to copy the file. The button in the top-left corner, or **Tab**,
  shows or hides the sidebar.
- **Browse** with the arrow keys, Page Up/Down, Home/End, or the mouse wheel.
- **Zoom** with Ctrl + mouse wheel or a touchpad pinch, Ctrl+plus/minus, **Ctrl+0** to fit
  the window and **Ctrl+1** for actual size. Double-click switches between the two. When zoomed
  in, drag or scroll to move around.
- **F3** in Lichtbak, or **Win+F3** from anywhere, shows all Lichtbak windows as tiles on a
  dark screen, like App Exposé on the Mac: no other apps and no row of desktops as in Task
  View. Click a tile, or use the arrow keys and Enter, to bring that window forward.
- **F11** for full screen, **Esc** to leave it.
- **Delete** removes a picture from the list (the file stays where it is).
  **Ctrl+Delete** moves the file to the Recycle Bin.
- **Ctrl+C** copies the picture, **Ctrl+O** adds pictures, and you can drop files or folders
  onto the window.
- Right-click for *Show in Explorer*, *Open with…* and the commands above.
- The interface is in English, or in Dutch when Windows is.

Formats: JPEG, PNG, GIF, BMP, TIFF, ICO and JPEG XR. WebP, HEIC and AVIF also work if the
matching Windows extension is installed (Lichtbak decodes through the same codecs as
Windows itself). Camera rotation (EXIF) is applied.

## Uninstall

Settings > Apps > Installed apps > Lichtbak > Uninstall. Or run
`%LOCALAPPDATA%\Programs\Lichtbak\Lichtbak.exe --uninstall`.

## Building

No SDK needed. `build.cmd` compiles with the C# compiler that ships with the .NET Framework
on every Windows machine. The result is `bin\Lichtbak.exe` and `bin\Lichtbak-setup.exe`,
which are the same file: named `-setup`, the exe opens the install window.

Command line:

| | |
|---|---|
| `Lichtbak.exe file…` | open files (handed to the running window if there is one) |
| `Lichtbak.exe --install` | install silently for the current user |
| `Lichtbak.exe --uninstall` | remove again |
| `Lichtbak.exe --selftest outdir file…` | open files off-screen, render the window to PNGs and write a report (test pictures: `tools\MakeSamples.cs`) |

### How a selection ends up in one window

Explorer starts the program registered for a verb once **per selected file**. Lichtbak keeps
one process. The first one owns a mutex (`Local\Lichtbak.Instance`) and listens on a named
pipe. Every later process sends its file over the pipe and exits. Files that arrive within
1.5 s of each other count as one batch, which is sorted by name and shown from its first
picture. The verbs are registered with `MultiSelectModel=Player`, so Explorer keeps offering
them for selections of more than 15 files.

Settings and a log live in `%APPDATA%\Lichtbak`.

## License

MIT
