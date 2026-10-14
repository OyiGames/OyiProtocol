# Changelog

Message additions and removals per tag, newest first. Version rules are in the README: while the
protocol is 0.x, tags are `v0.<protocol_version>.<PATCH>`.

## v0.1.2 (protocol_version 1)

No wire change. FlatBuffers now comes from [OyiGames/flatbuffers](https://github.com/OyiGames/flatbuffers)
`v25.12.19-oyi.1` (upstream 25.12.19 plus fixes): CI uses its flatc and C++ headers, and the vendored C#
runtime includes the Verifier file identifier fix (google/flatbuffers#9211). `Envelope.VerifyEnvelope`
now rejects a foreign identifier on its own; calling `EnvelopeBufferHasIdentifier` as well is no longer
required but still harmless.

## v0.1.1 (protocol_version 1)

No wire change. The vendored C# `Verifier` ignores the file identifier (upstream bug, documented in
`third_party/flatbuffers-net/README.md`): C# readers must also call
`Envelope.EnvelopeBufferHasIdentifier`. Both round-trip programs now check that a buffer with a
foreign identifier is rejected.

## v0.1.0 (protocol_version 1)

First release.

- `Envelope { channel, message }`, file identifier `OYIP`.
- `Channel`: `Command`, `Stream`.
- `Message`: `Hello`, `Welcome`, `Request`, `Response`, `StreamFrame`.
- `Welcome.schema_hash` is documented as a fingerprint of the engine build's component layouts,
  compared but never recomputed by clients.
- The FlatBuffers C# runtime (google/flatbuffers v25.12.19) is vendored in
  `third_party/flatbuffers-net`.

The schema moved here from OyiEngine (`protocol/oyi_protocol.fbs`, OyiEngine PR #22); the wire format
is unchanged. Tags `v1.0.0` and `v1.0.1` were published briefly on 2026-10-13 and withdrawn when the
protocol was put on 0.x; commit `f75853b` (formerly `v1.0.0`) has the same wire format as v0.1.0.
