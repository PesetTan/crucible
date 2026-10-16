# Third-party components

- **Forge 2.0.15** — [Card-Forge/forge](https://github.com/Card-Forge/forge), GPL-3.0. The original release and notices are retained in the ignored local `vendor/` installation. The source for the app's Forge interop shim is `engine/ForgeBridge.java`, marked GPL-3.0-or-later. Engine/card resources are not replaced with approximate card behavior.
- **Eclipse Temurin Java 17** — [Adoptium](https://adoptium.net/), project-local runtime; the downloaded JDK retains its license and legal files.
- **ASP.NET Core / Blazor / .NET 10** — [dotnet](https://github.com/dotnet), installed SDK/runtime.
- **Microsoft.Data.Sqlite and SQLitePCLRaw** — NuGet dependencies pinned in the C# project and lockfile. Their distributed package license notices apply. SQLite is the underlying local database engine.

No card artwork is required by the app. Source account logs and the user's collection are not distributed. Local application startup does not call Forge's desktop `Main` telemetry initialization; the interop shim initializes the game model directly.

The Windows beta includes a clean, dated card-metadata/public-deck starter catalog (see `resources/starter/manifest.json`) and the GPL-3.0-or-later bridge source. It downloads the unmodified pinned Forge release and a checksum-verified Temurin JDK at setup, retaining their notices. Microsoft Edge WebView2 is an external runtime installed through Microsoft's bootstrapper; Windows-only dialogs and process containment use Tauri plugins and Windows APIs.

## SharpZipLib 1.4.2

Windows Forge setup bundles [SharpZipLib](https://github.com/icsharpcode/SharpZipLib/tree/v1.4.2) for bzip2 decompression. It is licensed under MIT:

Copyright © 2000-2018 SharpZipLib Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
