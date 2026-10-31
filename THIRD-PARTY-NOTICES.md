# 第三方组件与字体

ATTM 的程序代码没有第三方 NuGet 依赖，但打包时会带上下列资源与运行时。

---

## 1. Lekton（编辑器字体）

- 文件：`assets/fonts/Lekton-Regular.ttf`、`assets/fonts/Lekton-Bold.ttf`
- 许可：SIL Open Font License 1.1（`assets/fonts/Lekton-OFL.txt`）
- 来源：https://fonts.google.com/specimen/Lekton
- 允许：自由使用、修改、再分发（含商用），但字体文件本身不能单独售卖，
  修改后的版本不能使用保留字体名 "Lekton"。

---

## 2. Material Symbols Sharp（图标字体）

- 文件：`assets/fonts/MaterialSymbolsSharp.ttf`
- 许可：Apache License 2.0
- 来源：https://fonts.google.com/icons
- 允许：自由使用、修改、再分发，需保留版权与许可声明。

---

## 3. .NET 运行时（自包含发布时）

- 组件：.NET 10 Desktop Runtime（`Microsoft.NETCore.App`、`Microsoft.WindowsDesktop.App`）
- 许可：MIT
- 来源：https://github.com/dotnet/runtime
- 自包含单文件发布时会被嵌入 `ATTM.exe`，体积也因此较大。

---

## 4. Windows 系统组件

ATTM 通过 P/Invoke 调用 `winmm.dll`（MIDI 输出）、`dwmapi.dll`（窗口标题栏配色）。
这些是 Windows 自带组件，不随程序分发，使用它们不涉及再分发授权。

---

## 5. 未使用的常见库（说明）

为了体积与可控性，本项目**故意没有**使用：

- AvalonEdit（编辑器）
- NAudio / DryWetMIDI / Melanchall（MIDI）
- CommunityToolkit.Mvvm

所以在许可上只需要关注上面三项。
