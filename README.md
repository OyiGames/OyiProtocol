# OyiProtocol

[中文](README.zh-CN.md)

The wire protocol between OyiEngine (C++, `oyi serve --rpc`) and OyiEditor (C#): one FlatBuffers
schema, [`schema/oyi_protocol.fbs`](schema/oyi_protocol.fbs), that both sides consume as a git
submodule pinned to a tag. This repository holds only the schema, its changelog and the CI that
proves the C++ and C# bindings read each other's bytes.

## Messages

Every frame carries one `Envelope { channel, message }`. `message` is a union of:

| Message | Channel | Direction | Fields |
|---|---|---|---|
| `Hello` | Command | client -> server, first frame | `protocol_version`, `capabilities`, `auth`, `client_name` |
| `Welcome` | Command | server -> client, accepts a `Hello` | `protocol_version`, `capabilities`, `engine_version`, `schema_hash`, `session_id` |
| `Request` | Command | client -> server | `id`, `command` (required), `args[]`, `payload` |
| `Response` | Command | server -> client, answers `Request.id` | `id`, `ok`, `error_code`, `message`, `payload` |
| `StreamFrame` | Stream | server -> client | `stream` (required), `sequence`, `payload` |

`Welcome.schema_hash` fingerprints the engine build's reflected component layouts (the `schema` in
`oyi --version --json`), not this schema. Equal values mean component snapshots are binary
compatible. The engine computes it from its C++ headers at build time, so clients compare it with a
value they already know; they cannot recompute it.

The command channel is request/response and ordered. The stream channel carries snapshots, logs and
profiler samples and may drop frames. The command set behind `Request.command` and the payload
formats are defined by the engine, not by this schema.

## Framing and handshake

- One TCP connection. Each frame is a 32-bit little-endian byte count followed by one `Envelope`
  buffer with file identifier `OYIP`. Receivers reject frames larger than 16 MiB and close the
  connection.
- The first frame from the client must be a `Hello`. The server answers with a `Welcome`, or with a
  `Response { id = 0, ok = false }` and then closes:
  - `error_code = "Unsupported"` when `protocol_version` differs from the server's;
  - `error_code = "InvalidArgument"` when the peer is not loopback and `auth` does not match the
    pairing token the device displays. Loopback peers may leave `auth` empty.
- After the handshake the client sends `Request`s and matches `Response`s by `id`; `StreamFrame`s
  may arrive between them.

## Versions

- `protocol_version` (in `Hello` and `Welcome`) changes only on an incompatible change. It is
  recorded in the schema header as `// protocol_version: N`.
- Compatible changes follow FlatBuffers' rules: new fields are appended at the end of a table, new
  union members at the end of the union, nothing is removed or reordered, ids never change. CI runs
  `flatc --conform` against the previous tag to enforce this.
- Tags are `vMAJOR.MINOR.PATCH`: MAJOR = `protocol_version`, MINOR = compatible additions, PATCH =
  comments and tooling. CI fails a tag whose MAJOR differs from the schema header.
- Every tag has an entry in [`schema/CHANGELOG.md`](schema/CHANGELOG.md).

## Consuming the schema

Add the repository as a submodule and check out a tag; generate code from
`schema/oyi_protocol.fbs` with **flatc 25.12.19**.

- C++ (as OyiEngine does): `flatc --cpp --scoped-enums --no-includes`, include
  `<flatbuffers/flatbuffers.h>` before the generated header.
- C#: `flatc --csharp`, and import
  [`third_party/flatbuffers-net/OyiProtocol.FlatBuffers.props`](third_party/flatbuffers-net) into the
  same project to compile the vendored runtime (google/flatbuffers `v25.12.19`, `net/FlatBuffers`).
  The `Google.FlatBuffers` NuGet package does not work: the generated code calls
  `FlatBufferConstants.FLATBUFFERS_25_12_19()`, and the newest package (25.2.10) does not have it.
  [`tests/csharp/RoundTrip.csproj`](tests/csharp/RoundTrip.csproj) is a working example.

## Verifying locally

```sh
python scripts/verify.py --flatc <flatc 25.12.19> --flatbuffers-root <google/flatbuffers at v25.12.19, for include/> --cxx <C++20 compiler>
```

The script checks the flatc version, source conventions and the vendored runtime's version, runs `flatc --conform` against the
previous tag, generates both bindings, builds [`tests/cpp`](tests/cpp) and
[`tests/csharp`](tests/csharp) (.NET 10), and has each program read the frames the other wrote.

## License

MIT, see [LICENSE](LICENSE). `third_party/flatbuffers-net` is Apache-2.0, see its
[LICENSE](third_party/flatbuffers-net/LICENSE).
