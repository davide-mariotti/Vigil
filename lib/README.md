# Libraries inside Vigil.exe

`build.bat` packs these files into the exe as resources. They are the .NET Framework 4.7.2 builds from NuGet (x64 for LibreHardwareMonitorLib), unchanged.

| File | Version | License | Source |
|------|---------|---------|--------|
| LibreHardwareMonitorLib.dll | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit.dll | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD.dll | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| BlackSharp.Core.dll | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| HidSharp.dll | 2.6.4 | Apache-2.0 ([HidSharp-LICENSE.txt](HidSharp-LICENSE.txt)) | https://software.seekye.com/hidsharp |
| System.Memory.dll | 4.6.3 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Buffers.dll | 4.6.1 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Numerics.Vectors.dll | 4.6.1 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Runtime.CompilerServices.Unsafe.dll | 6.1.2 | MIT | https://github.com/dotnet/maintenance-packages |
| Microsoft.Web.WebView2.Core.dll, Microsoft.Web.WebView2.WinForms.dll | 1.0.4314.35 | BSD ([WebView2-LICENSE.txt](WebView2-LICENSE.txt)) | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| WebView2Loader.dll (native, x64) | 1.0.4314.35 | BSD ([WebView2-LICENSE.txt](WebView2-LICENSE.txt)) | same package, `runtimes/win-x64/native`. Written to `%LOCALAPPDATA%\Vigil\` at the first start, because Windows can't load a native DLL from memory |

The window's fonts sit in `ui/fonts/`: Geist and Geist Mono (SIL OFL 1.1, [Geist-LICENSE.txt](../ui/fonts/Geist-LICENSE.txt)) and Instrument Serif italic (SIL OFL 1.1, [InstrumentSerif-LICENSE.txt](../ui/fonts/InstrumentSerif-LICENSE.txt)), the latin subsets from Fontsource 5.3.0, the same ones as davide-mariotti.github.io. The icons in `ui/icons.js` are Lucide 1.74.0 (ISC).

The MPL-2.0 text: https://www.mozilla.org/MPL/2.0/

To update one: download the package from nuget.org, take the DLL from `lib/net472` (or `runtimes/win-x64/lib/net472` for LibreHardwareMonitorLib, `lib/net462` or `lib/netstandard2.0` for the others), replace it here and run `build.bat`.
