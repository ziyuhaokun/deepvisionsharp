# Application Ports

Infrastructure (and a few Shell UI adapters) implement these interfaces.

## CQRS vs Port (dual channel)

| Channel | Entry | Pipeline |
| --- | --- | --- |
| **A CQRS** | `IMediator.Send` | Logging / Validation / Performance |
| **B Port** | DI inject interface | Caller-owned logging & validation |

- Prefer **A** for one-shot business writes (CRUD, start-training façade, import).
- Prefer **B** for long sessions, process hosts, high-frequency labeling load/save, thumbnails.
- Shell must not reference Infrastructure concrete types; inject Ports only.

Ports in this folder are primarily **B** (or support handlers in A).  
Workflow contracts living next to features (e.g. `IAnnotationWorkflowService`,
`ITrainingJobLifecycleService`) follow the same dual-channel rules.
