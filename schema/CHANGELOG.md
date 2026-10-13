# Changelog

Message additions and removals per tag, newest first. Tag rules are in the README.

## v1.0.1 (protocol_version 1)

No wire change. The FlatBuffers C# runtime (google/flatbuffers v25.12.19) is vendored in
`third_party/flatbuffers-net` with an MSBuild props file for consumers. `Welcome.schema_hash` is now
documented: a fingerprint of the engine build's component layouts, compared but never recomputed by
clients.

## v1.0.0 (protocol_version 1)

First release. The schema moved here unchanged from OyiEngine (`protocol/oyi_protocol.fbs`,
OyiEngine PR #22); only the header comment changed.

- `Envelope { channel, message }`, file identifier `OYIP`.
- `Channel`: `Command`, `Stream`.
- `Message`: `Hello`, `Welcome`, `Request`, `Response`, `StreamFrame`.
