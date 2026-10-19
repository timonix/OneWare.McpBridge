![image](https://raw.githubusercontent.com/timonix/OneWare.McpBridge/main/Icon.png)

# MCP Bridge

## Overview
This extension runs a local [Model Context Protocol](https://modelcontextprotocol.io) server inside OneWare Studio.
External MCP clients (Claude Code, Claude Desktop, other agents) can then call the same AI tools that OneWare's own
assistant uses.

## Installation
1. Install the extension.
2. Point your MCP client at `http://localhost:3917/mcp` (HTTP transport).
   For Claude Code: `claude mcp add --transport http oneware http://localhost:3917/mcp`

## Usage
- The server starts with OneWare Studio and only listens on localhost.
- **View → Tool Windows → MCP Bridge** shows the server status, connected clients and recent tool calls. You can
  start and stop the server there, and turn tool groups or single tools on and off under **Tools**.
- `readFile`, `editFile` and `runTerminalCommand` are off by default. Turn them on in the MCP Bridge window if you want
  clients to use them.
- The port can be changed in **Settings → Tools → MCP Bridge** (default 3917). Stop and start the server in the
  MCP Bridge window to apply it.

[![Test](https://github.com/timonix/OneWare.McpBridge/actions/workflows/test.yml/badge.svg)](https://github.com/timonix/OneWare.McpBridge/actions/workflows/test.yml)
[![Publish](https://github.com/timonix/OneWare.McpBridge/actions/workflows/publish.yml/badge.svg)](https://github.com/timonix/OneWare.McpBridge/actions/workflows/publish.yml)
