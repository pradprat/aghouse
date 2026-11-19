# Contributing to aghouse

Bug reports, ideas and pull requests are welcome.

## Reporting

- **Bugs and ideas:** open an issue using one of the templates. Include your aghouse version (`aghouse ping`) and Windows version.
- **Security problems:** report them privately, as [SECURITY.md](SECURITY.md) describes.

## Building

You need Windows 10 1809 or later and the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`). Run these from the repo root:

```powershell
dotnet build
dotnet run --project src/AgHouse.App
dotnet test
```

aghouse runs one instance per Windows user, so quit an installed aghouse before you run a build from source.

## Pull requests

- Keep a PR to one change, and say in its description what it changes and how you tried it.
- `dotnet test` must pass. CI runs the same build and tests on Windows.
- Logic that doesn't need the UI (parsers, models, the CLI protocol) goes in `src/AgHouse.Core` and gets unit tests in `tests/`.
- UI follows [PRODUCT.md](PRODUCT.md) and [DESIGN.md](DESIGN.md): native Fluent controls, keyboard first, and nothing drawn over a terminal pane.
- From 1.0, the CLI, `settings.json` keys and the agent integrations stay compatible until 2.0. Adding things is fine. Renaming or removing them needs an issue first.
- Update README.md when a feature, shortcut or command changes, and add a line to CHANGELOG.md.

By contributing, you agree that your work is licensed under this repository's [LICENSE](LICENSE).
