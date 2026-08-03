# Security policy

Telemetry Loom currently listens only on localhost and does not support remote access or authentication.

Localhost-only binding is a product safety boundary, not permission to expose the service remotely. Remote binding is unsupported until authentication and a deliberate threat model exist.

Do not commit credentials, tokens, private keys, environment files containing secrets, or unreviewed hardware captures. Fixtures can disclose hardware topology even when they contain no credentials.

Do not open a public issue for a vulnerability that could expose data or execute code. Report it privately through GitHub's security advisory interface for this repository.

Only the latest release and the current `main` branch receive security fixes during early development.
