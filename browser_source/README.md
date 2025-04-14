# IRL Speaker Browser Source

## Description

Ever wanted to have your own sound alerts while streaming IRL? This is intended for running in a browser tab on your phone in an app like [Stream Buddy](https://www.streamomation.com/). Opening this in a browser will connect to the Streamer.bot Websocket Server which you designate using the `host` URL parameter. By running an action in Streamer.bot and accompanying node server, you can tell the browser to play a sound from your PC at home.

I also host this page in a Cloudflare Page at [irlspeaker.rondhi.com](https://irlspeaker.rondhi.com/)

## URL Parameters

### Required parameters

- `host` - Point this to your Streamer.bot Websocket server. You need to host the Websocket Server through a tunnel like Cloudflare Tunnels or Tailscale to handle SSL termination.

### Optional parameters

- `soundsHost` - Point this to your node_server. You need to serve the node server through a tunnel like Cloudflare Tunnels or Tailscale. Defaults to your host url and `/irl`
- `port` - Point this to your Streamer.bot Websocket port. Defaults to port `443`, assuming you have something handling SSL termination
- `scheme` - Scheme to use for Streamer.bot Websocket server. Defaults to `wss`
- `endpoint` - Endpoing to use for Streamer.bot Websocket server. Defaults to `/`
- `password` - Password if your Streamer.bot Websocket Server requires authentication
- `timeout` - Set a max amount of time in seconds a sound will play. Defaults to `20`
