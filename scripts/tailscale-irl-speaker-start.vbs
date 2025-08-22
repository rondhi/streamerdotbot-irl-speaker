Set oShell = CreateObject("WScript.Shell")
oShell.Run "tailscale serve reset;", 0, true
oShell.Run "tailscale serve --bg --set-path /ws 8080", 0, true
oShell.Run "tailscale serve --bg --set-path / 7474", 0, true