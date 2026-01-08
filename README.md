## 🚀 AI Empower Labs Suite — Setup & Operations (for demo use not for production)

This repository uses .NET Aspire to orchestrate an end-to-end development environment with multiple services (AppHost, Web app, databases, vectors, observability, etc.).

Below you'll find installation instructions, first-run secrets, how to view/override environment variables in Aspire, image customization, and tips for using `dotnet user-secrets`.

---

## 📦 Prerequisites

- Docker or container engine running (Docker Desktop/colima/Rancher Desktop). Ensure it’s running before you start.
- .NET SDK 10 (net10.0)
  - Windows (winget):
    ```powershell
    winget install Microsoft.DotNet.SDK.10
    ```
  - macOS (Homebrew):
    ```bash
    brew install --cask dotnet-sdk
    ```
  - Linux (Ubuntu example):
    ```bash
    sudo apt-get update
    sudo apt-get install -y dotnet-sdk-10.0
    ```

Optional but recommended:
- Make sure ports 17267 (Aspire Dashboard), 4317/4318 (OTLP), 9090+ etc. are not blocked by other apps.

---

## ▶️ Running the Suite

From the solution root:

```bash
dotnet run --project AppHost
```

Once running, open the Aspire Dashboard (it will print the URL in the console), like

```bash
info: Aspire.Hosting.DistributedApplication[0]
      Login to the dashboard at https://suite.aiempowerlabs.localhost:17267/login?t=077b2dec3554ce102825575ac3d5e546
```

---

## 🧭 Viewing and Resolving Usernames/Passwords via Aspire

To resolve credentials, use the Aspire Dashboard to discover and resolve them:

1. Open Dashboard → choose a service (e.g., Postgres, LibreChat, Flowise, RustFS, Qdrant).
2. Open the Environment (or Parameters) tab.
3. Look for variables like:
   - `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
   - `OPENAI_API_KEY`, `AEL_STUDIO_API_KEY` (or hierarchical equivalents via parameters)
4. Provide or override values if needed. You can set them via:
   - Environment variables before `dotnet run`
   - `.env` file in the solution root
   - `dotnet user-secrets` for parameters/secrets consumed by .NET services

Connection strings and computed values are often injected by Aspire at runtime. If something looks empty, ensure Docker is running and the dependent resources are healthy.

---

## 🎨 Customizing Container Images

You can override the default container images used by this application through environment variables. This is useful for testing local builds or using alternative image sources.

### Available Environment Variables

- `FLOWISE_IMAGE` — override the Flowise container image
- `LIBRECHAT_IMAGE` — override the LibreChat container image
- `CLICKHOUSE_IMAGE` - overide the clickhouse container image
- `LANGFUSE_IMAGE` - overide the langfuse container image
- `RUSTFS_IMAGE` - overide the rustfs container image
- `POSTGRES_IMAGE` - overide the postgres container image
- `QDRANT_IMAGE` - overide the qdrant container image

### Usage Example

To use a locally built Flowise image instead of the default:

```bash
FLOWISE_IMAGE=flowise:local dotnet run --project AppHost
```

This tells Aspire to use your local `flowise:local` image instead of pulling the default image from the registry.

### Tips

- Set these variables before running `dotnet run`
- Use custom tags to test different versions: `FLOWISE_IMAGE=flowise:experimental`
- Point to different registries if needed: `FLOWISE_IMAGE=docker.io/myorg/flowise:latest`

---

## 🔧 Using dotnet user-secrets (view, update, reset)

`dotnet user-secrets` securely stores development-only secrets on your machine. Run these commands from the project directory that reads the secrets (`AppHost`, `WebApplication1`, etc.).

- Initialize (once per project):
  ```bash
  dotnet user-secrets init
  ```

- Set/update a secret:
  ```bash
  dotnet user-secrets set "Section__Key" "value"
  # example
  dotnet user-secrets set "OpenAI__ApiKey" "sk-..."
  ```

- List current secrets:
  ```bash
  dotnet user-secrets list
  ```

- Remove a single secret:
  ```bash
  dotnet user-secrets remove "Section__Key"
  ```

- Reset/clear all secrets for this project:
  ```bash
  dotnet user-secrets clear
  ```

Where are they stored?
- Windows: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>`
- macOS/Linux: `~/.microsoft/usersecrets/<user_secrets_id>`

---

## 🧩 Troubleshooting

- Aspire Dashboard not opening: check console output for the Dashboard URL, ensure port 18888 is free.
- Containers not starting: verify Docker is running and you have network access to pull images.
- Missing env vars in services: use Dashboard → Environment tab to inspect effective variables; override via `.env`, shell exports, or user-secrets.
- OTLP/Telemetry warnings: ensure the OpenTelemetry collector resource is healthy in Dashboard.

---

## 📚 Useful Commands

```bash
# Build solution
dotnet build

# Run only the AppHost
dotnet run --project AppHost
```
