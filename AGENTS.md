# Repository Guidelines

## Project Structure & Module Organization

This repository is currently an empty project scaffold. As implementation begins, keep the top level limited to project configuration and documentation. Use clear, conventional directories:

- `src/` for application source code, organized by feature or module.
- `tests/` for automated tests that mirror the relevant `src/` paths.
- `assets/` for static files such as images, icons, or sample QR payloads.
- `docs/` for architecture notes, setup instructions, and API documentation.

Avoid mixing generated output, credentials, or local environment files with source code. Add generated directories to `.gitignore`.

## Build, Test, and Development Commands

No build system or runtime has been selected. When one is introduced, document the canonical commands in `README.md` and keep them reproducible from a clean checkout. At minimum, provide commands for:

- installing dependencies;
- running the application locally;
- formatting/linting;
- running the full test suite.

Prefer a single documented entry point (for example, `npm test`, `make test`, or `dotnet test`) over ad-hoc commands.

## Coding Style & Naming Conventions

Follow the formatter and linter for the chosen language; commit formatted code only. Use 2 spaces for JSON, YAML, and Markdown indentation. Name files and directories consistently with the ecosystem (for example, `kebab-case` for web assets and `PascalCase` for C# types). Choose descriptive names: `qr-payment-parser` is preferable to `utils`.

Keep modules focused, avoid unexplained constants, and do not commit secrets. Store local configuration in ignored files such as `.env.local`; provide safe keys and defaults in `.env.example`.

## Testing Guidelines

Add tests alongside each feature and cover valid input, invalid input, and important edge cases. Use names that state the behavior, such as `rejects an expired QR payload`. Tests must be deterministic and must not rely on live banking systems or credentials. Run formatting, linting, and the full suite before opening a pull request.

## Commit & Pull Request Guidelines

There is no commit history yet, so establish concise imperative commit subjects, optionally using Conventional Commit prefixes: `feat: add QR payload validation` or `fix: reject missing currency code`.

Pull requests should explain the change, identify related issues, list validation performed, and include screenshots or sample request/response output for user-visible behavior. Keep changes narrowly scoped and request review before merging.
