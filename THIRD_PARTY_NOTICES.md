# Third-party notices

Vela includes code adapted from the following projects.

## OpenTaskbar

https://github.com/alztrk/opentaskbar

Used in `src/Vela/Shell/JumpLists.cs` (ported: native jump list reader and Shell COM helper),
`src/Vela/Services/GpuThermal.cs` (GPU temperature from WDDM adapter data, NVML fallback),
`src/Vela/Connectors/CursorConnector.cs` (Cursor browser sign-in and plan usage) and the Aero Peek / middle-click close in
`src/Vela/Views/WindowPreview.cs`; also the reference design for the live DWM window previews and the smart auto-hide in
`src/Vela/Views/DockWindow.xaml.cs`.

```
MIT License

Copyright (c) 2026 OpenTaskbar contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```


## WacOS (MIT-licensed snapshot)

https://github.com/renzyxx/WacOS at commit `2beced5` (2026-11-20), the revision the author published under the MIT License
(later revisions use other licenses; nothing from them is used).

Used in `src/Vela/Theming/LiquidGlass.cs` (Snell's-law lens profile, specular rim and the glass constants from
`src/shell/backdrop.cpp`) and `src/Vela/Services/GameMode.cs` (game detection from `src/shell/jogo.cpp`).

```
MIT License

Copyright (c) 2026 renzyxx

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Inter (Inter Display)

https://github.com/rsms/inter — Copyright (c) 2016 The Inter Project Authors.
Bundled as `src/Vela/Assets/Fonts/InterDisplay-*.ttf` (embedded in Vela.exe) under the SIL Open Font License 1.1;
the full licence text is `src/Vela/Assets/Fonts/OFL.txt` and is copied next to Vela.exe in release builds.
