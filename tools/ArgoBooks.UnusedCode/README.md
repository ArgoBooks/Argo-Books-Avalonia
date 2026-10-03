# Unused code report

Lists public and internal members that nothing in the solution references, so dead code can be found and deleted deliberately rather than noticed by accident.

## Running it

```powershell
dotnet run --project tools/ArgoBooks.UnusedCode
```

Or build once and run the executable directly:

```powershell
dotnet build tools/ArgoBooks.UnusedCode
tools/ArgoBooks.UnusedCode/bin/Debug/net10.0/argo-unused-code.exe
```

| Option | Description |
|---|---|
| *(none)* | Report methods, constants and properties |
| `--methods` | Methods only |
| `--consts` | Constants only |
| `--props` | Properties only |
| `--raw` | Disable the filters, to inspect what they are hiding |

It always exits 0. This is a report to read, not a gate to fail a build on.

## How it decides

A member is reported when its name appears **exactly once** across every `.cs` and `.axaml` file in the repository: the declaration itself.

A name mentioned in a string literal, an attribute or a XAML binding still counts as a use, so the tool under-reports rather than inventing dead code.

## Why the filters matter

Run it with `--raw` and it reports around 2300 members, of which roughly 97% are alive. Three framework patterns call a member without ever naming it again in source:

1. **xUnit `[Fact]` and `[Theory]` methods**, invoked by the test runner through reflection. Without this filter the test project drowns out everything else.
2. **`[RelayCommand]` methods.** CommunityToolkit generates a `<Name>Command` property and XAML binds to that. It also drops a trailing `Async`, so `SaveFooAsync` is reached as `SaveFooCommand` and a plain name match never connects the two.
3. **Avalonia attached property accessors** (`GetX`/`SetX` alongside `RegisterAttached`), which the XAML compiler calls.

Test files are excluded as a source of declarations but kept as a source of usage, so something exercised only by a test counts as used, not as dead.

Two patterns need no filter at all, which is worth knowing before someone adds one:

- **`[ObservableProperty]`** generates the property from a field, so the property has no declaration in source to match and can never be reported.
- **Avalonia `StyledProperty` accessors** register with `nameof(Value)`, which is a second occurrence of the name.

## Properties on serialized types

Properties are reported in two groups, and the second one must not be deleted on sight.

A property on a type System.Text.Json walks is written to and read from disk whether or not any code touches it. Delete one and it silently disappears from every existing `.argo` file and settings file. So they are listed separately, under **On serialized types**, because an orphaned field is still worth seeing.

Working out which types those are is the only real work in this pass. Checking for a `[JsonPropertyName]` attribute is not enough: a settings class can be serialized whole without carrying a single attribute, and its properties are real saved preferences. So the tool starts from types that are demonstrably serialized, either carrying a Json attribute or named as a serializer's generic argument, and follows their property types outwards until nothing new is reached.

That still misses a type only ever passed to a non-generic `JsonSerializer.Serialize(obj)`, where the type never appears in source. Nothing in the app does that today.

**Never name an app symbol in `Program.cs`.** The tool counts its own source, so a symbol mentioned even in a comment gains a second occurrence and stops being reported.

The filtering is the whole job. Without it the output is unusable, which is easy to mistake for there being nothing to find.

## What it cannot see

Anything reached by reflection over a computed name, through dependency injection, or from outside the repository. **Read each hit before deleting it.**

Overrides and interface implementations are safe by construction: the base or interface declaration is a second occurrence of the name, so they can never be reported.

## Deleting in rounds

Deleting a method can make others unused. If it was the only thing calling a second method, that one now has no callers either, and the tool cannot see it until you run it again. So delete, re-run, and repeat until it reports zero.
