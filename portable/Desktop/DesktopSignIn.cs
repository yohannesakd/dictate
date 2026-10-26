using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dictate.Core;

namespace Dictate.Desktop;

public static class DesktopSignIn
{
    public static async Task SignInAsync(ChatGptAuth auth, CancellationToken stopped = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopped);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var cancellation = timeout.Token;
        var listener = new TcpListener(IPAddress.Loopback, 1455);
        listener.Start();
        try
        {
            var signIn = ChatGptAuth.Begin();
            Process.Start(new ProcessStartInfo(signIn.Url) { UseShellExecute = true });
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                var request = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
                if (request is null || request.Length > 65536) continue;
                var path = request.Split(' ').ElementAtOrDefault(1);
                if (path is null || !path.StartsWith("/auth/callback?", StringComparison.Ordinal)) continue;
                await auth.CompleteAsync(signIn, "http://localhost:1455" + path, cancellation).ConfigureAwait(false);
                var body = "Dictate is signed in. You can close this window.";
                var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, cancellation).ConfigureAwait(false);
                return;
            }
        }
        finally { listener.Stop(); }
    }
}
