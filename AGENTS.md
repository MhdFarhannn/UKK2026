# UKK2026 Project Rules

## Immutable Database Contract

- `query.sql` is the canonical MySQL schema and seed data supplied by the project owner.
- Never edit, reformat, rename, replace, regenerate, or delete `query.sql`.
- Its SHA-256 checksum is `7a691b8f80c4a8fda0a335124d512c65762a1dd6d242a2d9cf2d236aa03d8f8d`.
- Read `query.sql` before database-related work and use its exact table names, columns, constraints, relationships, and seeded status values.
- Adapt application code to the schema. Do not adapt the schema to application code.
- If a requested feature conflicts with the schema, report the conflict instead of changing `query.sql`.
