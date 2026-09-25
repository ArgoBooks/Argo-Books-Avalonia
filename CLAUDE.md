# Argo Books: Claude Code Guide

## Workflow Rules

- **Do NOT build or run tests after every change.** Only build/test when explicitly asked.
- **Do NOT amend commits or force push** unless explicitly told to. Always create new commits.
- **Do NOT update the language files** in `tools/ArgoBooks.Translations/languages/`.
- **Do NOT commit plan or spec markdown files** (e.g. anything under `docs/superpowers/`). These are local planning artifacts; keep them untracked.

## Project Overview

Argo Books is a cross-platform desktop accounting application built with C# .NET 10 and Avalonia UI. It provides receipt scanning, invoicing, expense/revenue tracking, predictive analytics, inventory management, rental management, customer management, and more. Data is stored locally in encrypted `.argo` files. Available for Windows, macOS, and Linux.

## Build & Run Commands

- **Build:** `dotnet build ArgoBooks.sln`
- **Run desktop (Windows):** `dotnet run --project ArgoBooks.Desktop -f net10.0-windows10.0.17763.0`
- **Run desktop (cross-platform):** `dotnet run --project ArgoBooks.Desktop -f net10.0`
- **Run all tests:** `dotnet test ArgoBooks.Tests`
- **Run single test:** `dotnet test ArgoBooks.Tests --filter "FullyQualifiedName~TestClassName.TestMethodName"`
- **Run test category:** `dotnet test ArgoBooks.Tests --filter "FullyQualifiedName~Namespace"`

Requires .NET 10 SDK.

## Architecture

**MVVM pattern** with Avalonia UI. CommunityToolkit.Mvvm provides `[ObservableProperty]`, `[RelayCommand]`, and other source generators.

### Projects

| Project | Role |
|---------|------|
| **ArgoBooks** | UI layer: Views (.axaml), ViewModels, Controls, UI Services |
| **ArgoBooks.Core** | Business logic: Models, Services, Data, Security, Platform abstraction |
| **ArgoBooks.Shared** | Code shared with mobile: encryption, receipt scanning |
| **ArgoBooks.Desktop** | Entry point for Windows/macOS/Linux |
| **ArgoBooks.Mobile** | Android entry point |
| **ArgoBooks.Tests** | xUnit tests (references both ArgoBooks and ArgoBooks.Core) |

**Dependency flow:**

```
Desktop -> ArgoBooks -> ArgoBooks.Core -> ArgoBooks.Shared
Mobile  -> ArgoBooks.Shared
```

### Tools

Developer tools live in `tools/`, separate from the app. Each has a README explaining what it does.

| Tool | Role |
|------|------|
| **ArgoBooks.Translations** | Generates the language files via Azure Translator |
| **ArgoBooks.Recovery** | Opens a company file when the password is lost |
| **ArgoBooks.UnusedCode** | Reports members that nothing references |

### Data Storage

File-based. Company data lives in encrypted `.argo` files (AES-256). `CompanyManager` orchestrates file lifecycle: load, save, encryption. All data is held in memory for performance and security.

### Key Patterns

- **Singleton services** via `Microsoft.Extensions.DependencyInjection`
- **Compiled bindings** enabled by default (`AvaloniaUseCompiledBindingsByDefault=true`)
- **Platform abstraction** via `PlatformServiceFactory` with platform-specific implementations (Windows Hello, etc.)
- **Central package versioning** in `Directory.Packages.props`; app version in `Directory.Build.props`
- **Conditional compilation:** `WINDOWS` constant defined when targeting `net10.0-windows`
- **Web views are cross-platform.** `Avalonia.Controls.WebView` backs the invoice preview and PDF
  rendering on every desktop. On macOS it registers no `WKScriptMessageHandler`, so nothing the page
  posts arrives; both callers answer through `WebViewOutbox` instead, and new page code must post via
  `window.__argoPost` rather than `postMessage` directly

### Service Layer

Most business services are in `ArgoBooks.Core/Services/`: `CompanyManager` (file lifecycle), `SpreadsheetImportService`/`SpreadsheetExportService` (Excel/CSV), `GeminiService` (AI matching), `InsightsService` (analytics), `ReportRenderer` (PDF via QuestPDF), `LicenseService`, `PaymentPortalService`.

Anything mobile also needs lives in `ArgoBooks.Shared` instead: `EncryptionService` (AES-256) and `GeminiReceiptScannerService` (OCR).

UI services in `ArgoBooks/Services/` handle navigation, theming, localization, undo/redo, modals, and chart loading.

### Models (ArgoBooks.Core/Models/)

Organized by domain: `Entities/`, `Transactions/`, `Invoices/`, `Inventory/`, `Rentals/`, `Reports/`, `Charts/`, `AI/`, `Portal/`, `Insights/`.

## Testing

- **Framework:** xUnit with Coverlet for coverage
- **Test data:** Excel/CSV files in `TestData/` at the solution root
- Tests mirror source structure: `Converters/`, `Data/`, `Models/`, `Services/`, `Utilities/`, `Validation/`, `ViewModels/`, etc.

## Multi-Target Builds

ArgoBooks, ArgoBooks.Core and ArgoBooks.Desktop target both `net10.0` and `net10.0-windows10.0.17763.0`. ArgoBooks.Shared and ArgoBooks.Tests are `net10.0` only, and ArgoBooks.Mobile is `net10.0-android`. Windows-specific code (Windows Hello, DPAPI) is gated behind the `WINDOWS` compilation constant or target framework conditions in csproj files. The web view is not Windows-specific: it uses WebView2 there and WKWebView on macOS.
