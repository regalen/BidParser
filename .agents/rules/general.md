---
description: "General status, technology stack, and architecture of the BidParser project"
alwaysApply: true
---
# BidParser - General Rules & Project Status

This rule provides universal context regarding the BidParser project status, technology stack, directory layout, MVP scope, and common development commands.

## 1. Project Status & Architecture

BidParser is an internal web application for sales operations. Users upload a supplier quote (PDF or XLSX), the app extracts and validates the line items, and the user reviews them before a standardized XLSX is exported.

The backend has been re-platformed from Python/FastAPI to **ASP.NET Core 10**. The frontend is a **React/Vite/TypeScript** single-page application. The app is deployed as a single Docker container via `docker-compose` on an internal server.

### Workspace Directory Layout

- `src/BidParser.Api/` — ASP.NET Core 10 Minimal API app.
- `src/BidParser.Domain/` — Core entities, domain models, abstractions, and centralized constants.
- `src/BidParser.Infrastructure/` — Persistence layer (EF Core + SQLite), WAL connection interceptor, and background retention jobs.
- `src/BidParser.Parsing/` — Extraction engines (PdfPig/ClosedXML), cleanup pipelines, registry.
- `src/BidParser.Output/` — Excel writer for standardized template exports.
- `tests/BidParser.Parsing.Tests/` — Parser correctness and regression tests against golden outputs.
- `tests/BidParser.Api.Tests/` — WebApplicationFactory integration tests for auth, security, APIs.
- `frontend/` — React/Vite/TypeScript frontend under the ProductLens visual language.
- `samples/inputs/` — Real supplier quote files used for parsing test inputs.
- `samples/outputs/` — Golden `_parsed.xlsx` fixtures.

---

## 2. MVP Scope & Guardrails

These product decisions are locked. Anything outside these is considered out of scope:
- **Single Vendor**: Nutanix only. (Other vendors like Dell or Lenovo may be added in the future).
- **Single-File Upload**: One file per parse request.
- **Auto-Download Flow**: Upload & parse → progress panel → auto-download of generated `_parsed.xlsx`.
- **Remembered Defaults**: Last-used vendor, FX rate, and margin are saved per-user and pre-filled.
- **Out of Scope**: Multi-file batch upload, CSV vendor formats, approval review gate in UI, email alerts, SSO.

---

## 3. Working with the User

- Confirm extraction accuracy in chat (render a table) **before** writing scaffolding code for a new format.
- Output field mapping for `samples/template/ANZ-GENERIC_ForeignUplift.xlsx` is locked in `docs/output_mapping.md`. Never re-derive cell positions from the template directly; read the spec.

---

## 4. Development Commands

Always run these commands from the repository root:

- **Run all tests**: `dotnet test BidParser.sln`
- **Run local backend**: `dotnet run --project src/BidParser.Api`
- **Run local frontend**: `cd frontend && npm run dev`
- **Build frontend**: `cd frontend && npm run build`
- **Run production container**: `docker compose up -d`
- **Build production image**: `docker compose build`
