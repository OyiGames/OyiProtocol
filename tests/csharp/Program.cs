// Cross-language round trip for schema/oyi_protocol.fbs, C# side.
//
//   roundtrip write <file>   writes the reference frames
//   roundtrip read <file>    verifies a file written by either language
//   roundtrip check-identifier   a buffer with a foreign file identifier must be rejected
//
// Mirrors tests/cpp/roundtrip.cpp frame for frame.

using System;
using System.Collections.Generic;
using System.IO;
using Google.FlatBuffers;
using oyi.protocol;

namespace Oyi.Protocol.RoundTrip
{
    internal static class Program
    {
        private const uint ProtocolVersion = 1;
        private const int FrameCount = 5;

        private static int failures;

        private static void Check(bool condition, string what)
        {
            if (!condition)
            {
                Console.Error.WriteLine($"FAIL: {what}");
                ++failures;
            }
        }

        private static bool BytesEqual(int length, Func<int, byte> get, byte[] expected)
        {
            if (length != expected.Length)
            {
                return false;
            }
            for (int i = 0; i < length; ++i)
            {
                if (get(i) != expected[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void AppendFrame(List<byte> output, FlatBufferBuilder builder, Offset<Envelope> envelope)
        {
            Envelope.FinishEnvelopeBuffer(builder, envelope);
            byte[] buffer = builder.SizedByteArray();
            uint length = (uint)buffer.Length;
            for (int shift = 0; shift < 32; shift += 8)
            {
                output.Add((byte)((length >> shift) & 0xFF));
            }
            output.AddRange(buffer);
            builder.Clear();
        }

        private static byte[] BuildFrames()
        {
            var output = new List<byte>();
            var builder = new FlatBufferBuilder(256);

            var hello = Hello.CreateHello(builder, ProtocolVersion, 0x8000000000000001UL,
                builder.CreateString(""), builder.CreateString("roundtrip"));
            AppendFrame(output, builder,
                Envelope.CreateEnvelope(builder, Channel.Command, Message.Hello, hello.Value));

            var welcome = Welcome.CreateWelcome(builder, ProtocolVersion, 3, builder.CreateString("0.0.0"),
                0xDEADBEEFCAFEF00DUL, 42);
            AppendFrame(output, builder,
                Envelope.CreateEnvelope(builder, Channel.Command, Message.Welcome, welcome.Value));

            var command = builder.CreateString("world.query");
            var args = Request.CreateArgsVector(builder,
                new[] { builder.CreateString("a"), builder.CreateString("b") });
            var requestPayload = Request.CreatePayloadVector(builder, new byte[] { 1, 2, 3 });
            var request = Request.CreateRequest(builder, 7, command, args, requestPayload);
            AppendFrame(output, builder,
                Envelope.CreateEnvelope(builder, Channel.Command, Message.Request, request.Value));

            var response = Response.CreateResponse(builder, 7, false, builder.CreateString("E_TEST"),
                builder.CreateString("message"));
            AppendFrame(output, builder,
                Envelope.CreateEnvelope(builder, Channel.Command, Message.Response, response.Value));

            var streamName = builder.CreateString("log");
            var streamPayload = StreamFrame.CreatePayloadVector(builder, new byte[] { 255 });
            var stream = StreamFrame.CreateStreamFrame(builder, streamName, 0xFFFFFFFF00000001UL, streamPayload);
            AppendFrame(output, builder,
                Envelope.CreateEnvelope(builder, Channel.Stream, Message.StreamFrame, stream.Value));

            return output.ToArray();
        }

        private static void CheckFrame(int index, Envelope envelope)
        {
            switch (index)
            {
                case 0:
                {
                    bool ok = envelope.Channel == Channel.Command && envelope.MessageType == Message.Hello;
                    Check(ok, "frame 0 is a Command Hello");
                    if (!ok) break;
                    Hello hello = envelope.MessageAsHello();
                    Check(hello.ProtocolVersion == ProtocolVersion, "Hello.protocol_version");
                    Check(hello.Capabilities == 0x8000000000000001UL, "Hello.capabilities");
                    Check(string.IsNullOrEmpty(hello.Auth), "Hello.auth");
                    Check(hello.ClientName == "roundtrip", "Hello.client_name");
                    break;
                }
                case 1:
                {
                    bool ok = envelope.Channel == Channel.Command && envelope.MessageType == Message.Welcome;
                    Check(ok, "frame 1 is a Command Welcome");
                    if (!ok) break;
                    Welcome welcome = envelope.MessageAsWelcome();
                    Check(welcome.ProtocolVersion == ProtocolVersion, "Welcome.protocol_version");
                    Check(welcome.Capabilities == 3, "Welcome.capabilities");
                    Check(welcome.EngineVersion == "0.0.0", "Welcome.engine_version");
                    Check(welcome.SchemaHash == 0xDEADBEEFCAFEF00DUL, "Welcome.schema_hash");
                    Check(welcome.SessionId == 42, "Welcome.session_id");
                    break;
                }
                case 2:
                {
                    bool ok = envelope.Channel == Channel.Command && envelope.MessageType == Message.Request;
                    Check(ok, "frame 2 is a Command Request");
                    if (!ok) break;
                    Request request = envelope.MessageAsRequest();
                    Check(request.Id == 7, "Request.id");
                    Check(request.Command == "world.query", "Request.command");
                    Check(request.ArgsLength == 2 && request.Args(0) == "a" && request.Args(1) == "b", "Request.args");
                    Check(BytesEqual(request.PayloadLength, request.Payload, new byte[] { 1, 2, 3 }), "Request.payload");
                    break;
                }
                case 3:
                {
                    bool ok = envelope.Channel == Channel.Command && envelope.MessageType == Message.Response;
                    Check(ok, "frame 3 is a Command Response");
                    if (!ok) break;
                    Response response = envelope.MessageAsResponse();
                    Check(response.Id == 7, "Response.id");
                    Check(!response.Ok, "Response.ok");
                    Check(response.ErrorCode == "E_TEST", "Response.error_code");
                    Check(response.Message == "message", "Response.message");
                    Check(response.PayloadLength == 0, "Response.payload");
                    break;
                }
                case 4:
                {
                    bool ok = envelope.Channel == Channel.Stream && envelope.MessageType == Message.StreamFrame;
                    Check(ok, "frame 4 is a Stream StreamFrame");
                    if (!ok) break;
                    StreamFrame stream = envelope.MessageAsStreamFrame();
                    Check(stream.Stream == "log", "StreamFrame.stream");
                    Check(stream.Sequence == 0xFFFFFFFF00000001UL, "StreamFrame.sequence");
                    Check(BytesEqual(stream.PayloadLength, stream.Payload, new byte[] { 255 }), "StreamFrame.payload");
                    break;
                }
                default:
                    Check(false, "unexpected extra frame");
                    break;
            }
        }

        private static int Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Check(bytes.Length > 0, "input file is readable and not empty");

            int at = 0;
            int index = 0;
            while (at + 4 <= bytes.Length)
            {
                uint length = (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
                at += 4;
                if (length > (uint)(bytes.Length - at))
                {
                    Check(false, "frame length runs past the end of the file");
                    return 1;
                }
                var frame = new byte[length];
                Array.Copy(bytes, at, frame, 0, (int)length);
                var buffer = new ByteBuffer(frame);
                bool valid = Envelope.EnvelopeBufferHasIdentifier(buffer) && Envelope.VerifyEnvelope(buffer);
                Check(valid, "frame has identifier OYIP and verifies");
                if (valid)
                {
                    CheckFrame(index, Envelope.GetRootAsEnvelope(buffer));
                }
                at += (int)length;
                ++index;
            }
            Check(at == bytes.Length, "no trailing bytes");
            Check(index == FrameCount, "frame count");

            if (failures == 0)
            {
                Console.WriteLine($"csharp: read {index} frames from {path}");
            }
            return failures == 0 ? 0 : 1;
        }

        // Upstream 25.12.19's C# Verifier skipped the file identifier (google/flatbuffers#9211); the
        // vendored runtime comes from OyiGames/flatbuffers, which fixes it. This fails if an unfixed
        // runtime is vendored again.
        private static int CheckIdentifier()
        {
            var builder = new FlatBufferBuilder(64);
            var hello = Hello.CreateHello(builder, ProtocolVersion, 0, builder.CreateString(""),
                builder.CreateString("roundtrip"));
            builder.Finish(Envelope.CreateEnvelope(builder, Channel.Command, Message.Hello, hello.Value).Value, "XXXX");
            var buffer = new ByteBuffer(builder.SizedByteArray());
            Check(!Envelope.EnvelopeBufferHasIdentifier(buffer), "EnvelopeBufferHasIdentifier rejects XXXX");
            Check(!Envelope.VerifyEnvelope(buffer), "VerifyEnvelope rejects XXXX (needs the #9211 fix)");
            if (failures == 0)
            {
                Console.WriteLine("csharp: a foreign file identifier fails verification");
            }
            return failures == 0 ? 0 : 1;
        }

        private static int Write(string path)
        {
            byte[] frames = BuildFrames();
            File.WriteAllBytes(path, frames);
            Console.WriteLine($"csharp: wrote {FrameCount} frames ({frames.Length} bytes) to {path}");
            return 0;
        }

        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "write")
            {
                return Write(args[1]);
            }
            if (args.Length == 2 && args[0] == "read")
            {
                return Read(args[1]);
            }
            if (args.Length == 1 && args[0] == "check-identifier")
            {
                return CheckIdentifier();
            }
            Console.Error.WriteLine("usage: roundtrip write|read <file> | check-identifier");
            return 2;
        }
    }
}
