🔒 LockStep AI
LockStep AI is a local-first, privacy-centric desktop automation agent designed to run entirely on the CPU. It is built for air-gapped and highly regulated environments (like healthcare and pharma) where cloud AI, GPUs, and admin privileges are strictly forbidden.

By combining a highly compressed 1.58-bit BitNet language model with a deterministic C# Agentic Harness, LockStep AI can understand natural language commands and execute complex UI automations (via Windows UIA) without ever sending a single byte of data to the cloud.

🏗️ Architecture & Philosophy
The core philosophy of LockStep AI is "The LLM is stateless and weak; the C# Harness is the brain."

System 1 (The AI): A 2B parameter BitNet model (ggml-model-i2_s.gguf). It is used exclusively as a routing engine to translate user intent into strict JSON tool calls. It has no memory and no direct control.

System 2 (The Harness): A C# WPF application that holds the memory (SQLite), manages the loop (ReAct pattern), enforces JSON schemas, verifies UI states, and executes the actual keyboard/mouse inputs.

Core Tech Stack
Frontend: C# WPF (.NET 10)

Inference Engine: llama.cpp compiled with custom BitNet kernels (bundled as llama-server.exe)

Communication: Local HTTP (secured via dynamic ports and runtime API keys)

Automation: Windows UI Automation (UIA) & Win32 SendInput

Storage: SQLite (Local chat history & task traceability)

Deployment: Velopack (Zero-admin install to %LOCALAPPDATA%)

💻 Hardware Requirements
Because BitNet relies on specialized C++ CPU kernels, LockStep AI has strict hardware requirements:

CPU: Intel 4th Gen (Haswell) or newer / AMD 2015 (Excavator) or newer.

Instruction Sets: AVX2, BMI2, and FMA are strictly required. (The app will fail-fast and close if these are missing).

RAM: 4 GB minimum (8 GB recommended).

OS: Windows 10 (1909+) or Windows 11, 64-bit.

🚀 Getting Started (Development)
1. Prerequisites
.NET 10 SDK

Visual Studio 2022 (with .NET Desktop Development workload)

Velopack CLI (for packaging)

2. Building the Project
Clone the repository and open LockStepAI.sln in Visual Studio.

bash
git clone https://github.com/your-username/LockStepAI.git
cd LockStepAI
dotnet build
3. Running Locally
To run the app in development, you need to ensure the BitNet folder in your output directory (bin/Debug/net10.0-windows/BitNet) contains:

llama-server.exe and its required .dll files.

ggml-model-i2_s.gguf (The 1.16 GB model).

Note: During the first launch of a published release, the app automatically downloads the 1.16 GB model. However, for local debugging, it is faster to place the model file manually.

📦 Packaging & Deployment (Velopack)
LockStep AI uses Velopack to create a self-updating, zero-admin installer. The model is not included in the installer to keep the download size tiny; it is downloaded on the first launch.

1. Publish the App
Run the following command to create a self-contained, single-file build:

bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
(Ensure that ggml-model-i2_s.gguf is NOT in the ./publish folder).

2. Create the Installer
Use the Velopack CLI (vpk) to generate the installer:

bash
.\bin\Tools\vpk.exe pack --packId LockStepAI --packVersion 0.2.11 --packDir .\publish --mainExe LockStepAI.exe --runtime win-x64 --icon .\Assets\icon.ico --splashImage .\Assets\icon.png --splashProgressColor '#10B981' --shortcuts Desktop,StartMenuRoot --outputDir .\Releases
3. Releasing an Update
To push a new version:

Bump the --packVersion (e.g., 0.2.12).

Run the vpk pack command again.

Upload the contents of the .\Releases folder to your static file host (e.g., GitHub Releases, AWS S3).

The app will automatically check this URL on startup, download the delta update silently, and restart.

📂 Project Structure
LockStepAI/

Views/ - WPF Windows (ChatWindow, SplashWindow, OverlayWindow).

Services/

InferenceService.cs - Manages llama-server.exe (launch, dynamic ports, API keys, health checks, streaming).

AgentService.cs - The C# Harness (Orchestrator, ReAct loop, JSON routing).

UiaService.cs - Windows UI Automation API wrapper.

DatabaseService.cs - SQLite management for chat history and traceability.

Models/ - Data structures (ChatMessage, AgentAction, etc.).

App.xaml.cs - Startup logic, Velopack bootstrapper, model downloader.

LockStepAI.Tests/ - xUnit test project for the C# Harness.

🧠 The Agentic Loop (How it works)
User Input: The user types a prompt (e.g., "Open notepad and type hello").

Intent Routing: InferenceService sends the prompt to llama-server with a strict JSON schema.

Action Generation: BitNet generates a JSON object: {"tool": "open_app", "args": {"app": "notepad"}}.

Execution: The C# Harness parses the JSON and executes the open_app skill.

Verification: The Harness reads the active window via UIA. If successful, it updates its internal memory (SQLite) and loops back to step 2 for the next step.

🛡️ Security Posture
LockStep AI is designed for defense-in-depth:

Localhost Binding: llama-server is strictly bound to 127.0.0.1. It is inaccessible from the local Wi-Fi network.

Dynamic Ports: The server port is randomly selected by the OS at every launch.

Runtime API Keys: A random GUID is generated at startup and passed to the server. The C# HttpClient uses this key, blocking unauthorized local scripts from hijacking the AI.

No Vision: The system does not use screenshots or computer vision, completely eliminating the risk of leaking sensitive on-screen data (like patient records) to the model.

🤝 Contributing
This project is currently a Final Year Project (FYP). For major changes, please open an issue first to discuss what you would like to change. Ensure all C# code follows standard Microsoft naming conventions and passes the xUnit test suite.
