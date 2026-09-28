# Architecture Overview

Argo Books is a desktop accounting app for Windows, macOS and Linux, built with .NET, with an
Android companion app for capturing receipts away from the desk.

## Technology Stack

![Tech Stack](diagrams/architecture/tech-stack.svg)

| Layer | Technology | Description |
|-------|------------|-------------|
| **Platform** | [.NET 10](https://dotnet.microsoft.com/en-us/) | Core runtime and framework |
| **UI Framework** | [Avalonia](https://avaloniaui.net/) | Cross-platform XAML-based UI |
| **Charts** | [LiveCharts2](https://livecharts.dev/) | Interactive data visualization |
| **Rendering** | [SkiaSharp](https://github.com/mono/SkiaSharp) | 2D graphics engine |

## MVVM Architecture

The app uses the [Model-View-ViewModel (MVVM)](https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern/) pattern, which keeps the screens, their logic and the data apart.

![MVVM Pattern](diagrams/architecture/mvvm.svg)

- **View**: the XAML screens and controls
- **ViewModel**: what a screen shows and what its buttons do
- **Model**: the business data, such as customers, invoices and expenses

## Project Contents

| Project | Contents |
|---------|----------|
| **ArgoBooks** | Views, ViewModels, Controls, Modals, UI Services |
| **ArgoBooks.Core** | Models, Business Services, Data, Platform |
| **ArgoBooks.Shared** | Code shared between desktop and mobile: Security (encryption, key derivation), Sync, Receipts, Telemetry |
| **ArgoBooks.Desktop** | Desktop entry point (Windows/macOS/Linux) |
| **ArgoBooks.Mobile** | Android companion app: receipt capture, scan review, read-only snapshot viewing |
| **ArgoBooks.Tests** | Unit tests (xUnit) |

`ArgoBooks.Shared` is referenced by `ArgoBooks.Core`, and its types use the `ArgoBooks.Core.*`
namespaces even though they sit in a separate project. Anything the phone and the desktop both
need, especially the encryption used on company files, goes there rather than in Core.

The developer tools in `tools/` are deliberately **outside** the solution, so the app can't depend
on them and they are never built into or shipped with a release.

## Design principles

- **Business logic lives in services**, not in views or view models.
- **One codebase** runs on Windows, macOS, Linux and Android.
- **One shared instance of each app-wide service**, held as a static property on `App`. There is no dependency injection container.
- **Compiled bindings** are on by default, so bindings are checked when the app is built and run faster.
- **Company data is a file, loaded fully into memory.** See [Data Storage](DataStorage.md).
