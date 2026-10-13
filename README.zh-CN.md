# OyiProtocol

[English](README.md)

OyiEngine（C++，`oyi serve --rpc`）与 OyiEditor（C#）之间的线上协议：一份 FlatBuffers schema
[`schema/oyi_protocol.fbs`](schema/oyi_protocol.fbs)，两边都以 git 子模块引用并锁定到某个 tag。本仓库只放 schema、
它的变更记录，以及证明 C++ 与 C# 绑定能互读字节的 CI。

## 消息

每帧承载一个 `Envelope { channel, message }`，`message` 是以下消息的联合：

| 消息 | 通道 | 方向 | 字段 |
|---|---|---|---|
| `Hello` | Command | 客户端 -> 服务端，第一帧 | `protocol_version`、`capabilities`、`auth`、`client_name` |
| `Welcome` | Command | 服务端 -> 客户端，接受 `Hello` | `protocol_version`、`capabilities`、`engine_version`、`schema_hash`、`session_id` |
| `Request` | Command | 客户端 -> 服务端 | `id`、`command`（必填）、`args[]`、`payload` |
| `Response` | Command | 服务端 -> 客户端，回应 `Request.id` | `id`、`ok`、`error_code`、`message`、`payload` |
| `StreamFrame` | Stream | 服务端 -> 客户端 | `stream`（必填）、`sequence`、`payload` |

命令通道是请求 / 应答、有序；流通道承载快照、日志、profiler 采样，可以丢帧。`Request.command` 背后的命令集和各
payload 的格式由引擎定义，不在本 schema 里。

## 分帧与握手

- 一条 TCP 连接。每帧 = 32 位小端字节数 + 一个 `Envelope` buffer（file identifier `OYIP`）。接收方拒绝超过 16 MiB
  的帧并断开。
- 客户端第一帧必须是 `Hello`。服务端回 `Welcome`；或者回 `Response { id = 0, ok = false }` 后断开：
  - `protocol_version` 与服务端不同 → `error_code = "Unsupported"`；
  - 对端不是回环，且 `auth` 与设备显示的配对令牌不符 → `error_code = "InvalidArgument"`。回环对端可以留空 `auth`。
- 握手之后客户端发 `Request`，按 `id` 匹配 `Response`；其间可能穿插 `StreamFrame`。

## 版本

- `protocol_version`（`Hello` 与 `Welcome` 里）只在不兼容时变化，记录在 schema 头部的 `// protocol_version: N`。
- 兼容改动遵守 FlatBuffers 规则：新字段追加在 table 末尾，新消息追加在 union 末尾，不删除、不调序、不改 id。CI 用
  `flatc --conform` 对上一个 tag 检查。
- tag 为 `vMAJOR.MINOR.PATCH`：MAJOR = `protocol_version`，MINOR = 兼容新增，PATCH = 注释与工具改动。tag 的 MAJOR
  与 schema 头部不一致时 CI 失败。
- 每个 tag 在 [`schema/CHANGELOG.md`](schema/CHANGELOG.md) 有一条记录。

## 使用 schema

以子模块引入本仓库并检出某个 tag，用 **flatc 25.12.19** 从 `schema/oyi_protocol.fbs` 生成代码。

- C++（与 OyiEngine 相同）：`flatc --cpp --scoped-enums --no-includes`，在生成的头文件之前 include
  `<flatbuffers/flatbuffers.h>`。
- C#：`flatc --csharp`。生成的代码调用 `FlatBufferConstants.FLATBUFFERS_25_12_19()`，而 NuGet 上的
  `Google.FlatBuffers` 没有这个方法（它最新的版本比 flatc 25.12.19 旧）。请像
  [`tests/csharp/RoundTrip.csproj`](tests/csharp/RoundTrip.csproj) 那样，从 google/flatbuffers `v25.12.19` 的
  `net/FlatBuffers` 编译运行库。

## 本地验证

```sh
python scripts/verify.py --flatc <flatc 25.12.19> --flatbuffers-root <google/flatbuffers v25.12.19> --cxx <C++20 编译器>
```

脚本检查 flatc 版本与源码约定，对上一个 tag 跑 `flatc --conform`，生成两种绑定，编译 [`tests/cpp`](tests/cpp) 与
[`tests/csharp`](tests/csharp)（.NET 10），并让两个程序互读对方写出的帧。

## 许可

MIT，见 [LICENSE](LICENSE)。
