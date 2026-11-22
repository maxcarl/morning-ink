using System.Net;
using System.Text;
using System.Text.Json;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Ai;

/// <summary>Answers like an OpenAI-compatible chat endpoint (Ollama's /v1) and keeps every request body.</summary>
public sealed class FakeChatServer : IDisposable
{
    private readonly LocalServer _server;

    public FakeChatServer(Func<string, string?>? answer = null)
    {
        _server = new LocalServer(async context =>
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            Bodies.Add(body);
            var reply = (answer ?? (_ => "Kurz gesagt: Das Wichtigste in zwei Sätzen."))(body);
            if (reply is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }

            context.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-1",
                @object = "chat.completion",
                created = 1_700_000_000,
                model = "qwen2.5:7b",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = reply }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 10, completion_tokens = 10, total_tokens = 20 },
            });
            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
        });
    }

    public List<string> Bodies { get; } = [];

    public int Port => _server.Port;

    /// <summary>The base address as entered in the settings, without "/v1".</summary>
    public string Endpoint => $"http://127.0.0.1:{_server.Port}";

    public void Dispose() => _server.Dispose();
}
