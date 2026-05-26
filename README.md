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
