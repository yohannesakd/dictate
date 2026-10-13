# Contribute to Dictate

This repository is private. Access requires the owner's invitation.

Install the platform tools in the [build guide](portable/README.md).
The [main README](README.md) describes Mac model inputs and signing.
Keep models, signing keys, credentials, build outputs, and personal transcripts outside Git.

Use a topic branch. Describe the behavior change and its verification in the pull request.
Keep shared dictation rules in `portable/Core` and platform adapters in their existing platform modules.
Use the shared interface assets in `desktop/ui` for desktop and Android changes.

Run the shared checks:

```sh
dotnet test portable/Tests/Dictate.Tests.csproj -c Release
npm --prefix desktop ci
npm --prefix desktop test
```

Run the relevant [platform checks](portable/docs/verification.md) when changing recording, recognition, delivery, credentials, or packaging.
Report hardware, driver versions, and verification limits.
Use synthetic or public audio fixtures. Remove credentials, personal transcripts, and machine addresses from submitted logs.
