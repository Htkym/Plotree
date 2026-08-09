# Contributing to Plotree

Thank you for contributing to Plotree. Please keep each pull request focused on one problem or feature.

## Before you start

- Create a branch from the latest `main`.
- Do not include `.plotree` files with private story material, generated build output, or signing certificates.
- Use English for code identifiers and commit messages. User-facing application text must be localized in both English and Japanese.

## Development workflow

1. Make the smallest complete change.
2. Add or update tests when behavior changes.
3. Run the test project:

   ```powershell
   dotnet test .\tests\Plotree.Tests\Plotree.Tests.csproj -p:Platform=x64
   ```

4. Open a pull request using the provided template.

For interactive WinUI testing, use the repository's BuildAndRun workflow rather than starting the executable directly.

## Pull request expectations

- Explain the user-facing outcome and the implementation approach.
- Include screenshots for visual changes.
- Keep `Strings\en-US\Resources.resw` and `Strings\ja-JP\Resources.resw` aligned when adding or changing UI text.
- Preserve existing automation IDs unless a stable replacement is needed.
- Update the README or user manuals when behavior, setup, or workflow changes.

## Reviews

Address review comments with follow-up commits or a clear explanation. Merge only after the required CI checks have completed successfully.
