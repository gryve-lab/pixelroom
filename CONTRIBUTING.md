# Contributing

Please open an issue before starting a substantial change, especially one that changes sharing permissions, account behavior, or file operations.

## Development

1. Clone the repository on Windows.
2. Run `build.ps1` to build the application.
3. Run `test.ps1` before submitting a change.
4. Check both Gray and Dark themes when changing the interface, including the minimum window size.

Keep compatibility with .NET Framework 4.8 and the compiler used by `build.ps1`. Keep the application usable in both 32-bit and 64-bit processes.

## Tests and credentials

Use the fixtures in `tests/` for automated API tests. Do not add real API keys, account profiles, private file links, or user metadata to the repository. Generated builds and test profiles are ignored by Git.

Changes that affect file operations should verify failure and cancellation behavior as well as the successful operation. Changes to account handling should also check that late responses cannot restore or display data from a disconnected account.

## Pull requests

Explain the concrete problem, resulting behavior, and the checks you ran. Clearly distinguish local organization features from changes synchronized with PixelDrain.
