# Third-party components

Crucible's original work is licensed under [GPL-3.0-or-later](LICENSE). The components and content below retain their upstream licenses and rights. This file is a component summary, not a complete set of notices for every compiled dependency. See the [2026-10-17 license review](docs/license-review.md) for the inspected scope and unresolved distribution requirements.

- **Forge 2.0.15** — [Card-Forge/forge](https://github.com/Card-Forge/forge), GPL-3.0. The original release and notices are retained in the ignored local `vendor/` installation. The source for the app's Forge interop shim is `engine/ForgeBridge.java`, marked GPL-3.0-or-later. Engine/card resources are not replaced with approximate card behavior.
- **Eclipse Temurin Java 17** — [Adoptium](https://adoptium.net/), project-local runtime; the downloaded JDK retains its license and legal files.
- **ASP.NET Core / Blazor / .NET 10** — [dotnet](https://github.com/dotnet), installed SDK/runtime.
- **Microsoft.Data.Sqlite and SQLitePCLRaw** — NuGet dependencies pinned in the C# project and lockfile. Their distributed package license notices apply. SQLite is the underlying local database engine.
- **Svelte and its JavaScript dependencies** — pinned in `src/Crucible.Ui/package-lock.json`. Inspected installed package metadata declares MIT, BSD-3-Clause, ISC, Apache-2.0, or MIT/Apache alternatives. The development dependency Lightning CSS is MPL-2.0. Retain applicable notices for code included in the compiled frontend; a minified bundle does not replace those notices.
- **Tauri, its plugins, and Rust dependencies** — pinned in `src/Crucible.Ui/src-tauri/Cargo.lock`. Most inspected packages offer MIT and/or Apache-2.0; additional declarations include BSD, ISC, Zlib, Unicode-3.0, and MPL-2.0. Preserve each package's actual notices and license choices. MPL source-availability obligations apply to MPL components included in distributed executables; see the review for exact versions.

No card artwork is required by the app. Source account logs and the user's collection are not distributed. Local application startup does not call Forge's desktop `Main` telemetry initialization; the interop shim initializes the game model directly.

The Windows beta includes a clean, dated card-metadata starter catalog with no source decks or statistics (see `resources/starter/manifest.json`) and the GPL-3.0-or-later bridge source. It downloads the unmodified pinned Forge release and a checksum-verified Temurin JDK at setup, retaining their notices. Microsoft Edge WebView2 is an external runtime installed through Microsoft's bootstrapper; Windows-only dialogs and process containment use Tauri plugins and Windows APIs.

The starter catalog's provenance and privacy checks do not establish redistribution rights. Wizards card text/data and any privately supplied source material are excluded from Crucible's GPL grant. No claim is made that Wizards' Fan Content Policy authorizes this simulator or redistribution of that catalog. Crucible is unofficial and is not approved, endorsed, or sponsored by Wizards of the Coast. Portions of the materials used are property of Wizards of the Coast. © Wizards of the Coast LLC.


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
