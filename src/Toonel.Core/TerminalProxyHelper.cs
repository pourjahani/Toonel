namespace Toonel.Core;

/// <summary>
/// Generates ready-to-paste proxy environment snippets for terminals, for use when
/// running in system-proxy mode (TUN mode needs no setup — it captures terminals too).
/// </summary>
public static class TerminalProxyHelper
{
    public static string PowerShellSnippet(int socksPort, int httpPort) => $"""
        # Toonel terminal proxy (paste into PowerShell)
        $env:HTTP_PROXY  = "http://127.0.0.1:{httpPort}"
        $env:HTTPS_PROXY = "http://127.0.0.1:{httpPort}"
        $env:ALL_PROXY   = "socks5://127.0.0.1:{socksPort}"
        $env:NO_PROXY    = "localhost,127.0.0.1,::1,*.local,10.*,192.168.*"
        # git / npm / pip one-liners:
        # git config --global http.proxy http://127.0.0.1:{httpPort}
        # npm  config set proxy  http://127.0.0.1:{httpPort}
        # npm  config set https-proxy http://127.0.0.1:{httpPort}
        # pip  config set global.proxy http://127.0.0.1:{httpPort}
        """;

    public static string CmdSnippet(int socksPort, int httpPort) => $"""
        :: Toonel terminal proxy (paste into cmd)
        set HTTP_PROXY=http://127.0.0.1:{httpPort}
        set HTTPS_PROXY=http://127.0.0.1:{httpPort}
        set ALL_PROXY=socks5://127.0.0.1:{socksPort}
        set NO_PROXY=localhost,127.0.0.1,::1
        """;

    public static string ClearSnippet => """
        # Remove terminal proxy (PowerShell)
        $env:HTTP_PROXY=''; $env:HTTPS_PROXY=''; $env:ALL_PROXY=''; $env:NO_PROXY=''
        """;
}
