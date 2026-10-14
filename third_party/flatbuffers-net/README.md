# FlatBuffers C# runtime (vendored)

`FlatBuffers/` is `net/FlatBuffers/*.cs` from [OyiGames/flatbuffers](https://github.com/OyiGames/flatbuffers)
tag `v25.12.19-oyi.1` (commit `1f9c67d789a2f33021022d83b524c83198b1764b`), byte for byte. That fork is
upstream google/flatbuffers `v25.12.19` plus fixes; see its `oyi/README.md`. `LICENSE` is the Apache-2.0
license that covers these files.

It is vendored for two reasons. The C# that flatc 25.12.19 generates calls
`FlatBufferConstants.FLATBUFFERS_25_12_19()`, which the newest `Google.FlatBuffers` NuGet package (25.2.10)
does not have. And the fork fixes the Verifier's file identifier check
([google/flatbuffers#9211](https://github.com/google/flatbuffers/pull/9211)): upstream 25.12.19 accepts a
wrong or missing identifier, and throws on `nested_flatbuffer` fields. `tests/csharp`
(`roundtrip check-identifier`) fails if a runtime without that fix is vendored again.

## Use

Import `OyiProtocol.FlatBuffers.props` into the one project that also compiles the generated bindings:

```xml
<Import Project="path/to/OyiProtocol/third_party/flatbuffers-net/OyiProtocol.FlatBuffers.props" />
```

## Update

Only together with flatc. Bump `FLATC_VERSION` in `scripts/verify.py` and `FLATBUFFERS_TAG` in
`.github/workflows/ci.yml`. Replace `FlatBuffers/` and `LICENSE` with the files at the new fork tag (use
`git show <tag>:net/FlatBuffers/<file>` to keep LF endings), and update the tag and commit above.
`scripts/verify.py` fails when `FlatBufferConstants.cs` does not match `FLATC_VERSION`.
