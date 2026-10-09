# App icon

`TaskWidget-1024.png` is the master icon. `src/TaskWidget.Shell/Assets/TaskWidget.ico` is built from it, and the app, both windows and the installer use that file.

The `.ico` holds 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 px. Sizes 32 and up are the master, scaled down. At 16, 20 and 24 px the scaled ring and checkmark blur, so those three sizes come from `override-*.png`: 16 px is the checkmark alone, and 20 and 24 px redraw the ring, checkmark and dot with thicker strokes. `draw-small.ps1` draws them.

To rebuild after changing the master, run these in Windows PowerShell 5.1, which has System.Drawing:

```
powershell -ExecutionPolicy Bypass -File assets/icon/draw-small.ps1 -OutDir assets/icon
```

```
powershell -ExecutionPolicy Bypass -File assets/icon/make-ico.ps1 -Source assets/icon/TaskWidget-1024.png -OutIco src/TaskWidget.Shell/Assets/TaskWidget.ico -OutPng assets/icon/TaskWidget-1024.png -PreviewDir assets/icon
```

`draw-small.ps1` writes `small-a-*` (ring) and `small-b-*` (checkmark only). Copy the ones you want over `override-*.png` before running `make-ico.ps1`. `make-ico.ps1` writes `final-*.png` previews, which git ignores.
