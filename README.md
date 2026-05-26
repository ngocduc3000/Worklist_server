# Worklist Server

A robust backend server designed to manage institutional workflows, task allocations, and automated worklists. Integrated with LightRAG and Ollama, this project provides an advanced Retrieval-Augmented Generation (RAG) environment leveraging local AI models with full containerization, data persistence, and isolation of core services.

---

## 🚀 Features

- **Automated Worklist Management:** Efficiently tracks, queues, and allocates tasks.
- **Dockerized Architecture:** Pre-configured with Docker and Docker Compose for instant environment replication.
- **Local AI Embedding & Inference:** Fully integrated with Ollama to run lightweight, high-performance open-source models locally.
- **Data Persistence:** Integrated volume mapping to ensure application states, documents, and AI model weights are preserved across container restarts.
- **Production Ready:** Includes automated restart policies (`unless-stopped`) and clean dependency management between services.

---

## 🛠️ Prerequisites

Before setup, ensure you have the following components installed on your host system:
- **Operating System:** Linux (Ubuntu 20.04/22.04 LTS recommended), macOS, or Windows 11 (with WSL2).
- **Docker:** `v20.10.x` or higher.
- **Docker Compose:** `v2.x.x` or higher.

---

## 📦 Installation & Setup

Follow these step-by-step instructions to get your development or production environment running:

### 1. Clone the Repository
```bash
git clone [https://github.com/ngocduc3000/Worklist_server.git](https://github.com/ngocduc3000/Worklist_server.git)
cd Worklist_server
2. Configure Environment Variables
Copy the template environment file to create your active configuration:

Bash
cp .env.example .env
Note: Open the .env file with your preferred text editor (e.g., nano .env) to customize settings such as internal ports, host bindings, and model names before launching.

3. Deploy using Docker Compose
Build, pull required images, and launch all backend services in detached (background) mode:

Bash
docker compose up -d
4. Verify Service Status
Check if all containers are running successfully and healthy:

Bash
docker ps
You should see your active containers (lightrag-service and ollama-service) listed with an Up status.

📂 Project Structure
Plaintext
Worklist_server/
├── data/                    # Persistent storage volumes mapped to host
│   ├── inputs/              # Raw source files or incoming task documents
│   ├── prompts/             # System instruction configurations
│   ├── rag_storage/         # Internal database index and system runstates
│   └── ollama_storage/      # Local weights and manifests for downloaded AI models
├── .env.example             # Template for required environment configuration variables
├── Dockerfile               # Production container blueprint for the core application
├── docker-compose.yml       # Multi-container service orchestration specification
└── README.md                # Project documentation and guide
🛠️ Maintenance & Troubleshooting
Viewing Real-time Service Logs
To monitor live execution logs or troubleshoot connection states between services, run:

Bash
docker compose logs -f
Checking Active Local Models
To verify which models are successfully loaded into your isolated environment, execute:

Bash
docker exec -it ollama-service ollama list
Pulling New Models Manually
If your application requires a specific embedding or LLM architecture (e.g., nomic-embed-text-v2-moe), force an internal download via:

Bash
docker exec -it ollama-service ollama pull nomic-embed-text-v2-moe
Stopping the Server
To halt all active containers safely without erasing your stored persistent indexes or data volumes:

Bash
docker compose down
Wiping Cache and Volume Storage (Hard Reset)
To reset the system entirely and remove all downloaded models along with database storages:

Bash
docker compose down -v
🤝 Contributing
Contributions are what make the open-source community such an amazing place to learn, inspire, and create. Any contributions you make are greatly appreciated.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".

Fork the Project

Create your Feature Branch (git checkout -b feature/AmazingFeature)

Commit your Changes (git commit -m 'Add some AmazingFeature')

Push to the Branch (git push origin feature/AmazingFeature)

Open a Pull Request

📄 License
Distributed under the MIT License. See LICENSE file for more information.

✉️ Contact
Nguyễn Ngọc Đức - GitHub Profile

Project Link: https://github.com/ngocduc3000/Worklist_server
