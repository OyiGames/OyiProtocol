// Cross-language round trip for schema/oyi_protocol.fbs, C++ side.
//
//   roundtrip write <file>   writes the reference frames
//   roundtrip read <file>    verifies a file written by either language
//
// The C# side (tests/csharp) writes and checks the same frames, so a file written by one language
// must read back in the other. The bindings are generated with the flags OyiEngine uses
// (--cpp --scoped-enums --no-includes), which is why flatbuffers.h comes first.

#include <flatbuffers/flatbuffers.h>

#include "oyi_protocol_generated.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <string_view>
#include <vector>

namespace proto = oyi::protocol;

namespace {

constexpr uint32_t kProtocolVersion = 1;
constexpr int kFrameCount = 5;

int gFailures = 0;

void Check(bool condition, const char* what) {
    if (!condition) {
        std::fprintf(stderr, "FAIL: %s\n", what);
        ++gFailures;
    }
}

bool Equals(const flatbuffers::String* value, std::string_view expected) {
    return value != nullptr && value->string_view() == expected;
}

bool Equals(const flatbuffers::Vector<uint8_t>* value, const std::vector<uint8_t>& expected) {
    if (expected.empty()) {
        return value == nullptr || value->size() == 0;
    }
    return value != nullptr && value->size() == expected.size() &&
           std::memcmp(value->data(), expected.data(), expected.size()) == 0;
}

void AppendFrame(std::vector<uint8_t>& out, flatbuffers::FlatBufferBuilder& builder,
                 flatbuffers::Offset<proto::Envelope> envelope) {
    proto::FinishEnvelopeBuffer(builder, envelope);
    const auto length = static_cast<uint32_t>(builder.GetSize());
    for (uint32_t shift = 0; shift < 32; shift += 8) {
        out.push_back(static_cast<uint8_t>((length >> shift) & 0xFFu));
    }
    out.insert(out.end(), builder.GetBufferPointer(), builder.GetBufferPointer() + length);
    builder.Clear();
}

std::vector<uint8_t> BuildFrames() {
    std::vector<uint8_t> out;
    flatbuffers::FlatBufferBuilder builder;

    auto hello = proto::CreateHelloDirect(builder, kProtocolVersion, 0x8000000000000001ull, "", "roundtrip");
    AppendFrame(out, builder,
                proto::CreateEnvelope(builder, proto::Channel::Command, proto::Message::Hello, hello.Union()));

    auto welcome = proto::CreateWelcomeDirect(builder, kProtocolVersion, 3, "0.0.0", 0xDEADBEEFCAFEF00Dull, 42);
    AppendFrame(out, builder,
                proto::CreateEnvelope(builder, proto::Channel::Command, proto::Message::Welcome, welcome.Union()));

    const std::vector<flatbuffers::Offset<flatbuffers::String>> args = {builder.CreateString("a"),
                                                                       builder.CreateString("b")};
    const std::vector<uint8_t> requestPayload = {1, 2, 3};
    auto request = proto::CreateRequestDirect(builder, 7, "world.query", &args, &requestPayload);
    AppendFrame(out, builder,
                proto::CreateEnvelope(builder, proto::Channel::Command, proto::Message::Request, request.Union()));

    auto response = proto::CreateResponseDirect(builder, 7, false, "E_TEST", "message");
    AppendFrame(out, builder,
                proto::CreateEnvelope(builder, proto::Channel::Command, proto::Message::Response, response.Union()));

    const std::vector<uint8_t> streamPayload = {255};
    auto stream = proto::CreateStreamFrameDirect(builder, "log", 0xFFFFFFFF00000001ull, &streamPayload);
    AppendFrame(out, builder,
                proto::CreateEnvelope(builder, proto::Channel::Stream, proto::Message::StreamFrame, stream.Union()));

    return out;
}

void CheckFrame(int index, const proto::Envelope& envelope) {
    switch (index) {
    case 0: {
        const auto* hello = envelope.message_as_Hello();
        Check(envelope.channel() == proto::Channel::Command && hello != nullptr, "frame 0 is a Command Hello");
        if (hello != nullptr) {
            Check(hello->protocol_version() == kProtocolVersion, "Hello.protocol_version");
            Check(hello->capabilities() == 0x8000000000000001ull, "Hello.capabilities");
            Check(hello->auth() == nullptr || hello->auth()->size() == 0, "Hello.auth");
            Check(Equals(hello->client_name(), "roundtrip"), "Hello.client_name");
        }
        break;
    }
    case 1: {
        const auto* welcome = envelope.message_as_Welcome();
        Check(envelope.channel() == proto::Channel::Command && welcome != nullptr, "frame 1 is a Command Welcome");
        if (welcome != nullptr) {
            Check(welcome->protocol_version() == kProtocolVersion, "Welcome.protocol_version");
            Check(welcome->capabilities() == 3, "Welcome.capabilities");
            Check(Equals(welcome->engine_version(), "0.0.0"), "Welcome.engine_version");
            Check(welcome->schema_hash() == 0xDEADBEEFCAFEF00Dull, "Welcome.schema_hash");
            Check(welcome->session_id() == 42, "Welcome.session_id");
        }
        break;
    }
    case 2: {
        const auto* request = envelope.message_as_Request();
        Check(envelope.channel() == proto::Channel::Command && request != nullptr, "frame 2 is a Command Request");
        if (request != nullptr) {
            Check(request->id() == 7, "Request.id");
            Check(Equals(request->command(), "world.query"), "Request.command");
            const auto* args = request->args();
            Check(args != nullptr && args->size() == 2 && Equals(args->Get(0), "a") && Equals(args->Get(1), "b"),
                  "Request.args");
            Check(Equals(request->payload(), {1, 2, 3}), "Request.payload");
        }
        break;
    }
    case 3: {
        const auto* response = envelope.message_as_Response();
        Check(envelope.channel() == proto::Channel::Command && response != nullptr, "frame 3 is a Command Response");
        if (response != nullptr) {
            Check(response->id() == 7, "Response.id");
            Check(!response->ok(), "Response.ok");
            Check(Equals(response->error_code(), "E_TEST"), "Response.error_code");
            Check(Equals(response->message(), "message"), "Response.message");
            Check(Equals(response->payload(), {}), "Response.payload");
        }
        break;
    }
    case 4: {
        const auto* stream = envelope.message_as_StreamFrame();
        Check(envelope.channel() == proto::Channel::Stream && stream != nullptr, "frame 4 is a Stream StreamFrame");
        if (stream != nullptr) {
            Check(Equals(stream->stream(), "log"), "StreamFrame.stream");
            Check(stream->sequence() == 0xFFFFFFFF00000001ull, "StreamFrame.sequence");
            Check(Equals(stream->payload(), {255}), "StreamFrame.payload");
        }
        break;
    }
    default:
        Check(false, "unexpected extra frame");
    }
}

int Read(const char* path) {
    std::ifstream file(path, std::ios::binary);
    const std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    Check(!bytes.empty(), "input file is readable and not empty");

    size_t at = 0;
    int index = 0;
    while (at + 4 <= bytes.size()) {
        const uint32_t length = static_cast<uint32_t>(bytes[at]) | (static_cast<uint32_t>(bytes[at + 1]) << 8u) |
                                (static_cast<uint32_t>(bytes[at + 2]) << 16u) |
                                (static_cast<uint32_t>(bytes[at + 3]) << 24u);
        at += 4;
        if (length > bytes.size() - at) {
            Check(false, "frame length runs past the end of the file");
            return 1;
        }
        const uint8_t* buffer = bytes.data() + at;
        flatbuffers::Verifier verifier(buffer, length);
        const bool valid = proto::EnvelopeBufferHasIdentifier(buffer) && proto::VerifyEnvelopeBuffer(verifier);
        Check(valid, "frame has identifier OYIP and verifies");
        if (valid) {
            CheckFrame(index, *proto::GetEnvelope(buffer));
        }
        at += length;
        ++index;
    }
    Check(at == bytes.size(), "no trailing bytes");
    Check(index == kFrameCount, "frame count");

    if (gFailures == 0) {
        std::printf("cpp: read %d frames from %s\n", index, path);
    }
    return gFailures == 0 ? 0 : 1;
}

int Write(const char* path) {
    const std::vector<uint8_t> frames = BuildFrames();
    std::ofstream file(path, std::ios::binary);
    file.write(reinterpret_cast<const char*>(frames.data()), static_cast<std::streamsize>(frames.size()));
    if (!file) {
        std::fprintf(stderr, "FAIL: cannot write %s\n", path);
        return 1;
    }
    std::printf("cpp: wrote %d frames (%zu bytes) to %s\n", kFrameCount, frames.size(), path);
    return 0;
}

} // namespace

int main(int argc, char** argv) {
    if (argc == 3 && std::string_view(argv[1]) == "write") {
        return Write(argv[2]);
    }
    if (argc == 3 && std::string_view(argv[1]) == "read") {
        return Read(argv[2]);
    }
    std::fprintf(stderr, "usage: roundtrip write|read <file>\n");
    return 2;
}
