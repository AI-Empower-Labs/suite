# 🚀 AI Empower Labs Suite

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/your-org/your-repo/actions)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

An end-to-end, orchestrated development environment for building AI-powered applications, orchestrated with .NET Aspire.

This suite is designed for demo and development purposes, providing a collection of pre-configured services to accelerate the creation and testing of complex AI workflows.

---

## 📖 Table of Contents

- [🎯 About The Project](#-about-the-project)
- [🛠️ Services Included](#️-services-included)
- [📦 Prerequisites](#-prerequisites)
- [▶️ Getting Started](#️-getting-started)
- [🔧 Configuration](#-configuration)
  - [Viewing Credentials in Aspire](#viewing-credentials-in-aspire)
  - [Customizing Container Images](#customizing-container-images)
  - [Managing User Secrets](#managing-user-secrets)
- [🧩 Troubleshooting](#-troubleshooting)
- [🤝 Contributing](#-contributing)
- [📜 License](#-license)

---

## 🎯 About The Project

The AI Empower Labs Suite uses .NET Aspire to simplify the setup and management of a multi-service development environment. It includes a web application, databases, vector stores, observability tools, and more, all orchestrated to work together seamlessly.

This allows developers to focus on building features rather than spending time on infrastructure configuration.

### ✨ Key Features

- **Orchestrated with .NET Aspire:** For robust and easy-to-manage local development.
- **Pre-configured Services:** Includes a wide range of services for AI application development.
- **Centralized Observability:** Telemetry and logs are collected out-of-the-box.
- **Customizable:** Easily override container images and configurations.

---

## 🛠️ Services Included

The suite orchestrates the following services:

| Service      | Description                                                                                             |
|--------------|---------------------------------------------------------------------------------------------------------|
| **AppHost**      | The main .NET Aspire application that orchestrates all other services.                                    |
| **Postgres**   | Relational database for structured data storage.                                                        |
| **Qdrant**     | A vector database for similarity search and AI applications.                                            |
| **Redis**      | In-memory data store, used for caching and session management.                                          |
| **LibreChat**  | An open-source AI chat platform.                                                                        |
| **Flowise**    | A low-code tool for building and visualizing AI workflows.                                                |
| **Langfuse**   | An open-source observability and analytics platform for LLM applications.                               |
| **ClickHouse** | A fast, open-source, column-oriented database management system.                                        |
| **RustFS**     | A simple file server written in Rust.                                                                   |
| **OpenTelemetry** | Provides observability and telemetry collection for all services.                                      |
| **SearXNG**    | A privacy-respecting, hackable metasearch engine.                                                       |
| **SMTP**       | A simple SMTP server for testing email functionality.                                                   |

---

### Aspire Dashboard

Once running, the Aspire Dashboard provides a complete overview of all services, their logs, and environment variables.

---

## 📦 Prerequisites

- Docker or a compatible container engine (e.g., Docker Desktop, Colima, Rancher Desktop).
- .NET 10 SDK

### Installation

- **Windows (winget):**
  ```powershell
  winget install Microsoft.DotNet.SDK.10
  ```
- **macOS (Homebrew):**
  ```bash
  brew install --cask dotnet-sdk
  ```
- **Linux (Ubuntu example):**
  ```bash
  sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0
  ```

---

## ▶️ Getting Started

1.  Ensure your container engine (Docker) is running.
2.  Clone the repository:
    ```bash
    git clone https://github.com/your-org/your-repo.git
    cd your-repo
    ```
3.  Run the AppHost project:
    ```bash
    dotnet run --project AppHost
    ```
4.  Open the Aspire Dashboard URL printed in the console to monitor the services. It will look like this:
    ```
    info: Aspire.Hosting.DistributedApplication[0]
          Login to the dashboard at https://suite.aiempowerlabs.localhost:17267/login?t=...
    ```

---

## 🔧 Configuration

### Viewing Credentials in Aspire

To find credentials for services like databases or APIs:

1.  Open the **Aspire Dashboard**.
2.  Navigate to a service (e.g., `Postgres`, `LibreChat`).
3.  Select the **Environment** tab.
4.  Here you can view resolved values for variables like `POSTGRES_USER`, `POSTGRES_PASSWORD`, etc.

### Customizing Container Images

You can override default container images using environment variables. This is useful for testing local builds or alternative versions.

**Example:**
```bash
FLOWISE_IMAGE=my-custom-flowise:latest dotnet run --project AppHost
```

| Environment Variable | Overrides                       |
|----------------------|---------------------------------|
| `FLOWISE_IMAGE`      | The Flowise container image     |
| `LIBRECHAT_IMAGE`    | The LibreChat container image   |
| `CLICKHOUSE_IMAGE`   | The ClickHouse container image  |
| `LANGFUSE_IMAGE`     | The Langfuse container image    |
| `RUSTFS_IMAGE`       | The RustFS container image      |
| `POSTGRES_IMAGE`     | The Postgres container image    |
| `QDRANT_IMAGE`       | The Qdrant container image      |

### Managing User Secrets

Use `dotnet user-secrets` to manage development-only secrets securely. Run these commands from a project directory (e.g., `AppHost`).

- **Initialize secrets:**
  ```bash
  dotnet user-secrets init
  ```
- **Set a secret:**
  ```bash
  dotnet user-secrets set "Parameters:ai-empower-labs-api-key" "sk-your-key-here"
  ```
- **List secrets:**
  ```bash
  dotnet user-secrets list
  ```
- **Clear all secrets:**
  ```bash
  dotnet user-secrets clear
  ```

Secrets are stored in your user profile directory, keeping them separate from your code.

---

## 🧩 Troubleshooting

- **Aspire Dashboard not opening:** Check the console output for the correct URL and ensure the port is not blocked.
- **Containers fail to start:**
  - Verify that your container engine (Docker) is running.
  - Check your network connection to ensure images can be pulled.
  - Look at the container logs in the Aspire Dashboard for specific error messages.
- **Missing environment variables:** Use the Dashboard's Environment tab to inspect the effective variables for each service.

---

## 🤝 Contributing

Contributions are what make the open-source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

Please read our [CONTRIBUTING.md](CONTRIBUTING.md) for details on our code of conduct, and the process for submitting pull requests to us.

---

## 📜 License

Distributed under the MIT License. See `LICENSE` for more information.
