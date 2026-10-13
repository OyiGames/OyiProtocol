# FlatBuffers C# runtime (vendored)

`FlatBuffers/` is `net/FlatBuffers/*.cs` from [google/flatbuffers](https://github.com/google/flatbuffers)
tag `v25.12.19` (commit `7e163021e59cca4f8e1e35a7c828b5c6b7915953`), byte for byte. `LICENSE` is that
repository's Apache-2.0 license, which covers these files.

It is vendored because the C# that flatc 25.12.19 generates calls
`FlatBufferConstants.FLATBUFFERS_25_12_19()`, and the newest `Google.FlatBuffers` NuGet package (25.2.10)
does not have it.

## Use

Import `OyiProtocol.FlatBuffers.props` into the one project that also compiles the generated bindings:

```xml
<Import Project="path/to/OyiProtocol/third_party/flatbuffers-net/OyiProtocol.FlatBuffers.props" />
```

## Update

Only together with flatc: bump `FLATC_VERSION` in `scripts/verify.py` and `FLATBUFFERS_VERSION` in
`.github/workflows/ci.yml`, replace `FlatBuffers/` and `LICENSE` with the files from the new tag (as
committed upstream, LF), and update the tag and commit above. `scripts/verify.py` fails when
`FlatBufferConstants.cs` does not match `FLATC_VERSION`.
