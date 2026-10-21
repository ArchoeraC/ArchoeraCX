# Archoera vault (adapted)

Rust credential vault adapted from the ArchoeraMusic `vault-rs` implementation
(AGPL-3.0-or-later, same series). ArchoeraCX exposes only the passwordless
`crypto` / `file` modes from the launcher:

- `crypto`: master key in the OS secure store (DPAPI / Keychain / libsecret);
  falls back to a user-only `secret.key` when the desktop keyring is unavailable.
- `file`: explicit user-only `secret.key` file, used for headless/dev setups.

Passphrase / device-slot modes present in the upstream source are not exposed by
the ArchoeraCX UI or C# store. The vault is used only for account tokens; account
metadata stays in `accounts.json`.

Build: `app/tool/build-vault.sh` (or `core/vault-rs/build.sh`).
