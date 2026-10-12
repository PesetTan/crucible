# Third-party components

- **Forge 2.0.15** — [Card-Forge/forge](https://github.com/Card-Forge/forge), GPL-3.0. The original release and notices are retained in the ignored local `vendor/` installation. The source for the app's Forge interop shim is `engine/ForgeBridge.java`, marked GPL-3.0-or-later. Engine/card resources are not replaced with approximate card behavior.
- **Eclipse Temurin Java 17** — [Adoptium](https://adoptium.net/), project-local runtime; the downloaded JDK retains its license and legal files.
- **ASP.NET Core / Blazor / .NET 10** — [dotnet](https://github.com/dotnet), installed SDK/runtime.
- **Microsoft.Data.Sqlite and SQLitePCLRaw** — NuGet dependencies pinned in the C# project and lockfile. Their distributed package license notices apply. SQLite is the underlying local database engine.

No card artwork is required by the app. Source account logs and the user's collection are not distributed. Local application startup does not call Forge's desktop `Main` telemetry initialization; the interop shim initializes the game model directly.
