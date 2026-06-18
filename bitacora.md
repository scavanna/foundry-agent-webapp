# Bitacora

> Bitacora consolidada para el contexto AI Search + Foundry cross-tenant: [SCpAI/bitacora.md](../SCpAI/bitacora.md).

## 2026-05-05
- Se crea el repositorio MyMSLearnMCPAgent.
- Se inicializan archivos de documentacion y bitacora.
- Pendiente: definir plan de trabajo tecnico.
- Se decide trabajar directamente en foundry-agent-webapp.
- Se mueven documentacion.md y bitacora.md al repo foundry-agent-webapp.
- Se elimina carpeta MyMSLearnMCPAgent.

## 2026-05-05 - Sesion Foundry Agent Web App
- Se valida login en Azure con tenant Microsoft Non-Production.
- Se fija suscripcion activa: MCAPS-Hybrid-REQ-130312-2025-scavanna (4a618939-1cb2-4e10-8a0c-6944d854106b).
- Se autentica azd correctamente.
- Se crea entorno azd: scavanna-foundry-secops.
- Primer azd up falla en preprovision:
	- Detecta multiples recursos Foundry.
	- En modo no interactivo selecciona recurso incorrecto (aifoundry-mcsa-csu-security).
	- No encuentra agentes y solicita AI_AGENT_ID.
- Se corrige configurando explicitamente recurso y agente objetivo.
- Se reintenta despliegue con azd up.
- Postprovision reporta estado correcto:
	- Client ID: a3eea61b-cbdc-440e-b5c8-687c0dace359.
	- Container App URL: https://ca-web-76veos3l6strm.mangoflower-91937363.eastus.azurecontainerapps.io.
- Validacion de azd show:
	- Reporta "You don't have services defined".
	- Confirmado como comportamiento esperado para este template (infra-only).
- Se detecta bloqueador funcional en prueba de chat:
	- Mensaje observado en UI: "stream timeout".
	- Estado: login y despliegue ok, respuesta de agente incompleta.

## Plan operativo proxima sesion (2026-05-06)
- Ejecutar health check del backend desplegado.
- Extraer logs de Container App para identificar error concreto del stream.
- Probar mismo prompt en Foundry Playground para aislar si la causa es agente/herramientas.
- Fijar AI_AGENT_VERSION=14 y redeploy con azd up.
- Validar respuesta end-to-end y cerrar causa raiz en documentacion.

## 2026-06-06 - Auditoria remota PPAC/Teams y decision de billing
- Se confirma acceso remoto operativo en Power Platform admin center y Teams admin center con sesion de administrador activa.
- En Licensing > Billing Plans se valida el plan `Dialatam03` en estado Enabled, region South America, suscripcion `Diagramatica_VS01`.
- Se abre `Edit` del plan `Dialatam03` y se confirma que `Copilot Studio` aparece como opcion disponible en el selector de meter/producto.
- Durante la edicion se aplican multiples productos en el mismo plan.
- Resultado final observado en la grilla de Billing Plans para `Dialatam03`:
	- Dataverse
	- Power Apps
	- Power Automate
	- Power Pages
	- Copilot Studio
	- Windows 365 for Agents
- Validacion posterior en Licensing > Copilot Studio:
	- `Pay-as-you-go Copilot Credits` muestra `1 Billing plans`.
	- `Total Copilot Credits` aun en `0` (sin consumo reportado al momento de la verificacion).
- Validacion en Teams admin center para app `Diag Edgar` (App ID `5b2d21dc-de1d-402f-95cb-abe97a0a16b8`):
	- Published version: `1.0.1`
	- Supported on: Teams, Outlook, M365
	- `Available to`: `Everyone (org-wide default)`
	- `Installed for`: `No one`
- Decision operativa de la sesion:
	- Mantener temporalmente el plan habilitado para todos los productos.
	- Restriccion fina y segmentacion de planes se posterga para una sesion futura.

## 2026-06-07 - Historial persistente Cosmos DB + correccion de 3 bugs de chat
- Se implemento historial de conversaciones por usuario con **Azure Cosmos DB Serverless** (`cosmos-diagramatica-conv`, eastus2) por presupuesto restringido.
	- DB `conversationdb` con contenedores `conversations`, `messages`, `audit` (TTL 90d).
	- `disableLocalAuth=true` (solo Managed Identity), `COSMOS_ENDPOINT` inyectado en ambas apps.
	- Backend: `ConversationRepository.cs`, `CosmosModels.cs`, endpoints REST en `Program.cs`. Frontend: sidebar de historial.
- Bug 1 (Legal 404): la Container App no tenia ingress. Se habilito ingress external con target-port 8080 (`az containerapp ingress enable`). Con target-port 80 daba 503.
- Bug 2 (chat rompe en ambas, `ttl: null`): Cosmos rechazaba `"ttl": null` con error 400. Fix: `[JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]` en `CosmosModels.cs`.
- Bug 3 (seguimientos fallan, `Invalid conversation id ... Malformed identifier`): el backend mandaba a Foundry el id de Cosmos en vez del id real `conv_...`. Fix en `Program.cs` endpoint `/api/chat/stream`: al retomar se busca el `FoundryConversationId` guardado en el documento Cosmos; al iniciar se crea primero la conversacion Foundry y se persiste su id.
- Despliegue: imagenes via `az acr build` + `az containerapp update`.
	- Legal: `web:convfix-1780798815` -> revision `--0000007`.
	- Edgar: `web:convfix-1780799692` -> revision `--0000006`.
- Validacion end-to-end OK: logs muestran `conv_...` real y `Completed streaming`; prueba en UI Edgar con conversacion multi-turno (pregunta + seguimiento) respondiendo correctamente; historial persistido en Cosmos.
- Incidencias de tooling durante el despliegue:
	- `az acr build` crasheaba en Windows con `UnicodeEncodeError: '\u2713'` al transmitir logs (cp1252). El build del servidor completaba igual; mitigacion: `PYTHONIOENCODING=utf-8` y verificar la imagen con `az acr repository show-tags`.
	- El terminal integrado no capturaba bien el output de builds largos; se redirigio a archivos de log y a un `deploy-status.txt`.
- Pendiente menor: `StreamingResponseFailedUpdate` intermitente en el 1er intento con tools MCP (el retry lo recupera). Mejorar `DescribeStreamingUpdate` para extraer `Response.Error` anidado.
- Analisis APIM: se evaluo poner un API Management entre webapp y Foundry/Cosmos. Conclusion: no se recomienda ahora (no resuelve el bug, contradice bajo costo). Si fuera necesario en el futuro, empezar con tier Consumption (~$0 base).

## 2026-06-09 - Cohorte EDGAR multi-agente (plan + base tecnica)
- Se relevaron las personalidades en `9AgentesConPersonalidad/` para definir la cohorte de analisis.
- Se detecto duplicidad de variantes y se acordo mantenerlas temporalmente como provisorias:
	- `charlie_munger2_agent.md`
	- `ray_dalio2_agent.md`
- Se renombraron archivos para reflejar esa decision operativa y evitar ambiguedad en orquestacion.
- Se ajusto identificador interno en `charlie_munger2_agent.md` para evitar colision con `charlie_munger_agent.md`.
- Se definio y genero registro canónico de agentes:
	- Archivo: `9AgentesConPersonalidad/agent_cohort_registry.json`.
	- Contenido: 9 analistas + 1 orquestador, estado provisional, grupos de persona, modos de ejecucion, tablas objetivo, formatos de exportacion.
- Se habilito endpoint backend para exponer el registro:
	- `GET /api/agents/cohort` (autenticado).
	- Implementado en `backend/WebApp.Api/Program.cs`.
- Validacion ejecutada:
	- `dotnet build .\\backend\\WebApp.Api\\WebApp.Api.csproj -nologo` -> OK.
	- Warning observado: preexistente y no relacionado con la nueva funcionalidad.

### Plan operativo acordado (siguiente ejecucion)
1. Conectar frontend al endpoint `/api/agents/cohort`.
2. Mostrar selector de agente y modo (individual/cohorte).
3. Implementar contrato de salida estandar por agente para comparacion automatica.
4. Implementar motor de tablas (consenso, divergencia, hallazgos unicos, evidencia).
5. Implementar sintesis final multinivel y persistencia de corridas.
6. Implementar exportadores de conversacion completa en `pdf`, `markdown`, `docx`, `xlsx`.

## 2026-06-09 - Cohorte EDGAR (frontend pasos 1 y 2)
- Se implemento consumo de registro de cohorte desde frontend:
	- `GET /api/agents/cohort` (con token Bearer).
	- Archivo: `frontend/src/App.tsx`.
- Se agrego estado de seleccion en UI:
	- `selectedAgentId`
	- `selectedExecutionMode`
- Se implemento barra de seleccion en chat:
	- Archivos: `frontend/src/components/AgentChat.tsx`, `frontend/src/components/AgentChat.module.css`.
	- Controles visibles:
		- Analyst agent
		- Execution mode
- Se conecto el contexto de seleccion al request de stream:
	- Archivo: `frontend/src/services/chatService.ts`.
	- Campos agregados al payload de `/chat/stream`:
		- `selectedAgentId`
		- `executionMode`
- Se agregaron tipos de cohorte:
	- Archivo: `frontend/src/types/chat.ts`.
- Validacion:
	- Sin errores en panel de TypeScript en archivos modificados.
	- `npm run build` no ejecutable en esta sesion porque `npm` no esta disponible.

## 2026-06-09 - Cohorte EDGAR (paso 3: contrato estandar por agente)
- Se implemento contrato estandar de salida comparable para analistas:
	- Archivo nuevo: `9AgentesConPersonalidad/agent_output_contract.json`.
	- Version inicial: `1.0.0`.
	- Campos obligatorios definidos para analisis y comparacion de resultados.
- Se agrego endpoint de lectura del contrato:
	- `GET /api/agents/output-contract`.
	- Implementado en `backend/WebApp.Api/Program.cs`.
- Se integro enforcement del contrato en `/api/chat/stream`:
	- Cuando hay contexto de cohorte (`selectedAgentId` / `executionMode`) y no es respuesta MCP,
	- se enriquece el prompt con instruccion de salida JSON segun contrato.
- Se extendio modelo de request:
	- `backend/WebApp.Api/Models/ChatRequest.cs` con `SelectedAgentId`, `ExecutionMode`, `OutputContractVersion`.
- Se actualizo frontend para enviar version del contrato:
	- `frontend/src/services/chatService.ts` incluye `outputContractVersion: 1.0.0` cuando aplica contexto de cohorte.
- Validacion tecnica:
	- Build backend OK: `dotnet build WebApp.Api.csproj -nologo`.
	- Sin errores de diagnostico en archivos modificados.

## 2026-06-09 - Optimizacion de tokens (cache de recuperacion compartida)
- Se implemento capa de cache para reutilizar evidencia entre agentes de cohorte y evitar retrieval redundante.
- Servicio agregado:
	- `backend/WebApp.Api/Services/SharedRetrievalCacheService.cs`.
	- Estrategia: `userId + query normalizada` con hash y TTL 30 min.
- Integracion principal:
	- `backend/WebApp.Api/Program.cs` en `/api/chat/stream`.
	- Flujo:
		1. Busca evidencia compartida en cache (modo cohorte, no MCP approval).
		2. Si hay hit, inyecta `SHARED_EVIDENCE_PACKAGE` + directiva de evitar retrieval/search.
		3. Si hay miss, tras el stream construye paquete de evidencia (resumen + citas) y lo guarda.
- Observabilidad:
	- SSE nuevo `retrievalCache` (hit/miss).
	- Endpoint nuevo `GET /api/agents/retrieval-cache/stats`.
- Validacion:
	- Build backend exitoso despues de ajustes.
	- Sin errores de diagnostico en archivos modificados.

## 2026-06-09 - Cohorte EDGAR (paso 4: motor de tablas comparativas)
- Se implemento backend de comparacion estructurada para consolidar salidas de multiples analistas.
- Nuevos archivos:
	- `backend/WebApp.Api/Models/CohortComparisonModels.cs`.
	- `backend/WebApp.Api/Services/CohortComparisonService.cs`.
- Endpoint nuevo en API:
	- `POST /api/agents/cohort/compare` (autenticado, registrado en `Program.cs`).
- Capacidades entregadas en esta iteracion:
	- Tabla de consenso (`consensusTable`) basada en `hallazgos_clave` normalizados con umbral de mayoria.
	- Tabla de divergencia (`divergenceTable`) por dimension (`tesis_principal`, `recomendacion`).
	- Tabla de hallazgos unicos (`uniqueInsightsTable`) por agente.
	- Tabla de cobertura de evidencia (`evidenceCoverageTable`) con citas, conteos por seccion y confianza.
	- Resumen agregado (`summary`) y alertas de parseo (`warnings`) por salida invalida.
- Estado de validacion:
	- `dotnet build WebApp.Api.csproj -nologo` ejecutado con resultado exitoso.
	- Sin errores de diagnostico en archivos nuevos/modificados.

## 2026-06-09 - Cohorte EDGAR (paso 4 frontend: render de tablas)
- Se integro en frontend el uso del endpoint `POST /api/agents/cohort/compare`.
- Cambios principales:
	- `frontend/src/types/chat.ts`: tipos de request/response para comparacion de cohorte.
	- `frontend/src/services/chatService.ts`: metodo `compareCohortOutputs(...)` con token Bearer.
	- `frontend/src/components/AgentChat.tsx`: panel `Cohort Comparison Engine` con:
		- captura de salidas JSON por agente,
		- ejecucion de comparacion,
		- render de tablas: consenso, divergencia, hallazgos unicos y cobertura de evidencia,
		- resumen y warnings.
	- `frontend/src/components/AgentChat.module.css`: estilos para panel, tarjetas y tablas.
- Validacion:
	- Sin errores de diagnostico TypeScript/CSS en archivos modificados.

## 2026-06-09 - Cohorte EDGAR (paso 5: sintesis multinivel)
- Se agrego una sintesis final derivada del resultado de comparacion de cohorte.
- Niveles generados en UI:
	- `Executive`: lectura de alto nivel para decision rapida.
	- `Analytical`: resumen de consenso, divergencias, warnings y cobertura.
	- `Technical`: totales, contract version, modo de ejecucion y confianza media.
- Implementacion:
	- `frontend/src/components/AgentChat.tsx`
	- `frontend/src/components/AgentChat.module.css`
- Estado de validacion:
	- Pendiente validacion final de errores TypeScript/CSS tras el ultimo ajuste.

## 2026-06-09 - Cohorte EDGAR (paso 6: persistencia y exportacion)
- Se agrego persistencia de corridas de cohorte en Cosmos DB con container `cohortRuns`.
- Se incorporo el endpoint `GET /api/agents/cohort/runs` para consultar historial de corridas.
- La corrida se auto-guarda al ejecutar la comparacion cuando hay `conversationId`.
- Se agrego exportacion de sesion completa en markdown desde la UI de comparacion.
- Archivos tocados:
	- `backend/WebApp.Api/Models/CosmosModels.cs`
	- `backend/WebApp.Api/Services/ConversationRepository.cs`
	- `backend/WebApp.Api/Program.cs`
	- `infra/core/data/cosmos.bicep`
	- `frontend/src/utils/exportConversation.ts`
	- `frontend/src/components/AgentChat.tsx`
- Validacion:
	- `dotnet build WebApp.Api.csproj -v minimal` -> OK.
	- `get_errors` sin hallazgos en backend, frontend e infraestructura editados.

## 2026-06-09 - Cohorte EDGAR (paso 6: despliegue y smoke test runtime)
- Hallazgo de infraestructura: `azd provision` de esta plantilla no aplica `infra/core/data/cosmos.bicep` en el entorno Edgar/Legal actual.
- Accion aplicada en produccion: creacion dirigida de container Cosmos `cohortRuns` en cuenta `cosmos-diagramatica-conv`, DB `conversations`, pk `/userId`.
- Verificacion de infraestructura: listado de containers confirma `cohortRuns` junto a `messages`, `audit` y `conversations`.
- Despliegue de aplicacion: `azd deploy --no-prompt` en entorno `diagramatica-edgar-webapp` completado con exito.
- Smoke test endpoint:
	- Antes del deploy devolvia HTML (fallback SPA).
	- Despues del deploy devuelve `401 Unauthorized` en `GET /api/agents/cohort/runs` sin token, confirmando que el endpoint backend existe y exige autenticacion.

## 2026-06-09 - Hardening backend CohortRuns (auto-ensure)
- Se robustecio la inicializacion de Cosmos en `ConversationRepository` para crear `cohortRuns` automaticamente si no existe.
- Cambio aplicado: `CreateContainerIfNotExistsAsync` durante `InitializeAsync` con partition key `/userId`.
- Objetivo: evitar fallos de persistencia por drift de infraestructura entre codigo e IaC.
- Validacion: `dotnet build backend/WebApp.Api/WebApp.Api.csproj -nologo` completado en exito.

## 2026-06-09 - Exportacion multiformato de sesion (Markdown/Word/Excel/PDF)
- Se extendio la exportacion de sesion completa en la UI de Cohort Comparison.
- Nuevos formatos habilitados desde la misma fila de acciones:
	- Markdown (`.md`)
	- Word compatible (`.doc`)
	- Excel compatible (`.xls`)
	- PDF via flujo de impresion del navegador (`window.print`)
- Implementacion:
	- `frontend/src/utils/exportConversation.ts`
	- `frontend/src/components/AgentChat.tsx`
- Estado de validacion local:
	- `get_errors` sin hallazgos en archivos frontend modificados.

## 2026-06-09 - Incidente produccion Edgar (error de reintentos) cerrado
- Reporte de usuario: la webapp Edgar seguia mostrando `Failed to get a response after 3 attempts`.
- Diagnostico en logs de backend (revisiones previas):
	- `ObjectDisposedException: Cannot access a disposed object. Object name: 'JsonDocument'`.
	- El fallo ocurria en endpoints de cohorte al serializar `document.RootElement` fuera de su scope.
- Correccion aplicada en codigo:
	- `backend/WebApp.Api/Program.cs`
		- `Results.Json(document.RootElement)` -> `Results.Json(document.RootElement.Clone())`
		- Endpoints corregidos: `/api/agents/cohort` y `/api/agents/output-contract`.
- Despliegue de correccion:
	- Build ACR exitoso: `web:deploy-1781043001` (run `cad` exitoso).
	- Update Container App Edgar exitoso a nueva imagen.
	- Revision nueva creada: `ca-web-b5aozwd7s565i--0000011`.
- Validacion de cierre:
	- Revision `0000011` saludable y en ejecucion.
	- UI carga completa de cohorte sin 500.
	- Chat de prueba enviado y respondido correctamente (sin banner de reintentos).

## 2026-06-09 - Cohorte automatica (9 analistas) + concurrencia limitada
- Implementacion backend:
	- endpoint nuevo `POST /api/agents/cohort/run`.
	- salida con `agentResponses`, `comparison`, `errors`.
- Implementacion frontend:
	- boton `Run 9 Analysts` agregado en panel de tablas.
	- flujo automatico autocompleta JSONs y dispara comparacion/sintesis.
	- flujo manual `Run Comparison` se conserva.
- Concurrencia:
	- ejecucion paralela con `SemaphoreSlim`.
	- limite configurable por `COHORT_RUN_MAX_CONCURRENCY` (default 3).

## 2026-06-09 - Despliegue, rollback y redeploy corregido
- Build y deploy inicial de cambios de concurrencia: imagen `deploy-1781044982`.
- Hallazgo en smoke test browser: error frontend `VITE_ENTRA_SPA_CLIENT_ID is not set`.
- Accion de contingencia:
	- rollback inmediato a `deploy-1781043001` para restablecer servicio.
- Correccion de pipeline de build:
	- ACR build con argumentos `ENTRA_SPA_CLIENT_ID`, `ENTRA_TENANT_ID`, `ENTRA_BACKEND_CLIENT_ID`.
- Build corregido exitoso: `deploy-1781046100`.
- Estado final de produccion verificado:
	- `ca-web-b5aozwd7s565i--0000015` -> `Healthy` + `Running`.
	- imagen activa: `crb5aozwd7s565i.azurecr.io/web:deploy-1781046100`.

## 2026-06-09 - Observacion de uso en UI (post-deploy)
- Si aparece `Provide at least one analyst JSON output to run comparison.`:
	- corresponde al boton manual `Run Comparison` sin inputs pegados.
	- para ejecucion automatica de la cohorte se debe usar `Run 9 Analysts`.
