# Documentacion

> Fuente consolidada para el contexto AI Search + Foundry cross-tenant: [SCpAI/documentacion.md](../SCpAI/documentacion.md).

## Objetivo
Este repositorio reune notas y artefactos de aprendizaje para construir y operar un agente MCP.

## Alcance inicial
- Definir arquitectura base.
- Documentar decisiones tecnicas.
- Registrar pasos de implementacion.

## Proximos pasos
- Agregar estructura de carpetas del proyecto.
- Definir stack tecnologico y dependencias.
- Incorporar ejemplos de uso y pruebas.

## Estado actual de implementacion
- Repositorio de trabajo: foundry-agent-webapp.
- Suscripcion activa validada: MCAPS-Hybrid-REQ-130312-2025-scavanna.
- Entorno azd creado: scavanna-foundry-secops.
- Configuracion de recurso Foundry fijada al recurso correcto para evitar seleccion automatica incorrecta.
- Agente objetivo configurado: validacionmcpservermslearn.

## Flujo aplicado para conectar agente existente
1. Login Azure y validacion de suscripcion activa.
2. Login en azd.
3. Creacion y seleccion de entorno azd.
4. Ejecucion de azd up.
5. Correccion de preprovision por seleccion de recurso equivocado cuando hay multiples recursos Foundry.
6. Re-ejecucion de azd up con variables explicitas del recurso/agente.

## Hallazgo importante
- Si existen multiples recursos AIServices, el hook puede tomar el primero en modo no interactivo.
- Mitigacion aplicada: definir AI_FOUNDRY_RESOURCE_NAME, AI_FOUNDRY_RESOURCE_GROUP, AI_AGENT_ENDPOINT y AI_AGENT_ID en el entorno azd.

## Resultado de despliegue observado
- Postprovision completo.
- Entra app configurada correctamente.
- Client ID confirmado: a3eea61b-cbdc-440e-b5c8-687c0dace359.
- URL de Container App reportada:
	https://ca-web-76veos3l6strm.mangoflower-91937363.eastus.azurecontainerapps.io

## Pendientes de validacion final
- Confirmar salida final de azd show.
- Validar endpoint de salud en /api/health.
- Validar login y chat del frontend en la URL publicada.

## Nuevo objetivo operativo (2026-06-06)
- Separar el acceso en dos agentes y dos webapps:
	- Agente `Diagramatica-LegalJuris` + webapp dedicada.
	- Agente `Diagramatica-Edgar` + webapp dedicada.
- Recurso Foundry fijo: `aifoundry-diagramtica`.
- Proyecto fijo: `firstProject`.

## Scripts agregados para doble despliegue
- `deployment/scripts/setup-two-webapps.ps1`
	- Crea/actualiza dos entornos azd:
		- `diagramatica-legal-webapp` con `AI_AGENT_ID=Diagramatica-LegalJuris`
		- `diagramatica-edgar-webapp` con `AI_AGENT_ID=Diagramatica-Edgar`
	- Fija `AI_AGENT_ENDPOINT` a `https://aifoundry-diagramtica.services.ai.azure.com/api/projects/firstProject`
	- Opcional: `-Deploy` para ejecutar `azd up` en ambos.
- `deployment/scripts/deploy-two-webapps.ps1`
	- Wrapper para configurar y desplegar ambos entornos en una sola ejecución.

## Estado de despliegue 2 webapps (2026-06-06)
- Entorno `diagramatica-legal-webapp` desplegado.
	- URL final: `https://ca-web-rghwq3yh4wjxi.salmonforest-283af6d8.eastus.azurecontainerapps.io`
- Entorno `diagramatica-edgar-webapp` desplegado.
	- URL final: `https://ca-web-b5aozwd7s565i.wittymoss-8519a558.eastus.azurecontainerapps.io`

## Estado de Teams post-despliegue
- Los paquetes fueron regenerados con URLs finales usando IDs estables de app:
	- `a8af14e1-b1b6-4556-9c58-a44c0433ffe9` (Diag Legal)
	- `ed3aa9c2-1e29-4112-b26b-b5cd0177f7c3` (Diag Edgar)
- Actualizacion completada en Teams Admin Center para ambas apps.
- Version publicada actual:
	- `Diag Legal`: `1.0.1`
	- `Diag Edgar`: `1.0.1`
- Disponibilidad confirmada: `Everyone (org-wide default)`.

## Validacion adicional realizada
- Salida de azd show revisada.
- Resultado observado: el template aparece sin servicios en azd show.
- Conclusión: esperado para este proyecto porque usa patron infra-only en azure.yaml y hooks de despliegue.
- Confirmacion de endpoint publicado en .azure/scavanna-foundry-secops/.env:
	- WEB_ENDPOINT=https://ca-web-76veos3l6strm.mangoflower-91937363.eastus.azurecontainerapps.io

## Bloqueador actual
- En la interfaz web, al enviar mensajes al agente, aparece "stream timeout".
- Estado: infraestructura desplegada y autenticacion funcional, pero la conversacion no completa stream.

## Actualizacion de bloqueo (2026-06-06)
- Causa raiz confirmada en Foundry: los tools MCP de los agentes incluian un header invalido en `AdditionalHeaders`:
	- `Content-Type: application/json`
- Sintoma asociado en Foundry Playground:
	- `Error encountered while enumerating tools from remote server ... Failed to add header 'Content-Type' ...`
- Efecto en la webapp:
	- al fallar la enumeracion de herramientas remotas, el backend no completaba el ciclo esperado de stream y el frontend mostraba `stream timeout`.

## Correccion aplicada
- Se actualizaron los agentes para remover `Content-Type` y dejar solo `Ocp-Apim-Subscription-Key` en cada tool MCP.
- Versiones activas actuales:
	- `Diagramatica-LegalJuris:2`
	- `Diagramatica-Edgar:2`
	- `Diagramatica-Corporativo:2`
- Estado: bloqueo tecnico removido (pendiente validar consulta end-to-end en webapp con un prompt real).

## Estandarizacion de reutilizacion MCP (2026-06-06)
- Se crearon conexiones reutilizables de proyecto (`RemoteTool`) en Foundry:
	- `ema-legislacion`
	- `ema-jurisprudencia`
	- `ema-edgar`
- Los agentes quedaron referenciando estas conexiones mediante `project_connection_id`.
- Versiones activas tras esta estandarizacion:
	- `Diagramatica-LegalJuris:3`
	- `Diagramatica-Edgar:3`
	- `Diagramatica-Corporativo:3`

## Validacion final webapps (2026-06-06)
- Se detecto que ambas Container Apps estaban sirviendo imagen por defecto de ACA (`Hello World`).
- Se forzo redeploy de imagen real en ambos entornos:
	- Legal: `crrghwq3yh4wjxi.azurecr.io/web:deploy-1780791415` -> revision `ca-web-rghwq3yh4wjxi--0000002`
	- Edgar: `crb5aozwd7s565i.azurecr.io/web:deploy-1780791648` -> revision `ca-web-b5aozwd7s565i--0000001`
- Validacion HTTP final en URLs canonicas:
	- `https://ca-web-rghwq3yh4wjxi.salmonforest-283af6d8.eastus.azurecontainerapps.io/api/health` -> `200 {"status":"healthy"}`
	- `https://ca-web-b5aozwd7s565i.wittymoss-8519a558.eastus.azurecontainerapps.io/api/health` -> `200 {"status":"healthy"}`

## Hipotesis de causa (priorizadas)
1. Timeout en herramientas del agente (por ejemplo MCP) o en ejecucion del agente en Foundry.
2. Configuracion del agente sin version fija en runtime (se usa latest de forma implicita).
3. Error de backend al procesar stream SSE (necesita confirmar con logs de Container App).

## Plan recomendado para manana (paso a paso)
1. Verificar salud del backend desplegado.
	- Comando: Invoke-WebRequest "https://ca-web-76veos3l6strm.mangoflower-91937363.eastus.azurecontainerapps.io/api/health"
	- Exito esperado: HTTP 200 y payload de estado.
2. Revisar logs del contenedor para identificar causa exacta del timeout.
	- Comando: az containerapp logs show --name ca-web-76veos3l6strm --resource-group rg-scavanna-foundry-secops --tail 200
	- Exito esperado: ver excepcion concreta, tool timeout o error de stream.
3. Probar el mismo prompt directamente en Foundry Playground con el mismo agente.
	- Exito esperado: determinar si el problema esta en el agente (Foundry) o en la web app.
4. Fijar version explicita del agente y redeploy.
	- Comandos:
		- azd env set AI_AGENT_ID "validacionmcpservermslearn"
		- azd env set AI_AGENT_VERSION "14"
		- azd up
	- Exito esperado: respuesta del chat sin stream timeout.
5. Validar extremo a extremo (login, mensaje, respuesta, uso de tokens).
	- Exito esperado: se completa stream con eventos usage y done.

## Comandos de arranque rapido para manana
- cd C:\Users\scavanna\personal\OneDrive\Github\foundry-agent-webapp
- az account show --query "{subscription:name,id:id,tenantId:tenantId}" --output table
- azd env list
- azd env select scavanna-foundry-secops
- azd show

## Criterio de cierre de la siguiente sesion
- Health endpoint estable.
- Chat funcional sin stream timeout.
- Evidencia en logs de ejecucion normal del stream.
- Documentacion y bitacora actualizadas con causa raiz y correccion definitiva.

## Cohorte EDGAR - Paso 4 implementado (2026-06-09)
- Se implemento motor de tablas comparativas en backend para resultados multi-agente.
- Nuevos artefactos:
	- `backend/WebApp.Api/Models/CohortComparisonModels.cs`
	- `backend/WebApp.Api/Services/CohortComparisonService.cs`
- Endpoint agregado:
	- `POST /api/agents/cohort/compare` (autenticado)
- Entrada esperada:
	- Lista de salidas JSON por agente (`agentResponses`) siguiendo el contrato estandar `agent_output_contract.json`.
- Salida generada por el endpoint:
	- `consensusTable`
	- `divergenceTable`
	- `uniqueInsightsTable`
	- `evidenceCoverageTable`
	- `summary` y `warnings`
- Reglas implementadas en esta version:
	- Consenso por hallazgos clave con umbral de mayoria (>= 60%, minimo 2 agentes).
	- Divergencia por posiciones en `tesis_principal` y `recomendacion`.
	- Hallazgos unicos por agente.
	- Cobertura de evidencia por agente (citas, conteos por seccion, confianza).
- Validacion tecnica:
	- `dotnet build` completado en `WebApp.Api` sin errores.

## Cohorte EDGAR - Paso 4 frontend integrado (2026-06-09)
- Se agrego panel en UI para ejecutar comparaciones de cohorte sobre salidas JSON por analista.
- Integracion API:
	- Cliente frontend consume `POST /api/agents/cohort/compare` con autenticacion Bearer.
- Visualizacion incorporada en `AgentChat`:
	- `summary` (consensus/divergence/unique/citations)
	- `consensusTable`
	- `divergenceTable`
	- `uniqueInsightsTable`
	- `evidenceCoverageTable`
	- `warnings`
- Archivos frontend impactados:
	- `frontend/src/types/chat.ts`
	- `frontend/src/services/chatService.ts`
	- `frontend/src/components/AgentChat.tsx`
	- `frontend/src/components/AgentChat.module.css`

## Cohorte EDGAR - Paso 5 sintesis multinivel (2026-06-09)
- Se agrego una sintesis derivada del resultado de comparacion para lectura rapida y auditoria tecnica.
- Niveles expuestos en UI:
	- `Executive`
	- `Analytical`
	- `Technical`
- La sintesis usa como base los resultados de:
	- consenso,
	- divergencia,
	- hallazgos unicos,
	- cobertura de evidencia,
	- warnings de parseo.

## Cohorte EDGAR - Paso 6 persistencia y exportacion (2026-06-09)
- Se persistieron corridas de cohorte en Cosmos DB para mantener trazabilidad por usuario y conversacion.
- Se agrego el endpoint protegido de historial de corridas:
	- `GET /api/agents/cohort/runs`
- La comparacion ahora guarda automaticamente la corrida cuando existe `conversationId`.
- Se agrego exportacion de sesion completa en markdown desde la UI, incluyendo:
	- mensajes de la conversacion,
	- tablas de consenso/divergencia/hallazgos unicos/cobertura,
	- warnings,
	- sintesis multinivel.
- Implementacion:
	- `backend/WebApp.Api/Models/CosmosModels.cs`
	- `backend/WebApp.Api/Services/ConversationRepository.cs`
	- `backend/WebApp.Api/Program.cs`
	- `infra/core/data/cosmos.bicep`
	- `frontend/src/utils/exportConversation.ts`
	- `frontend/src/components/AgentChat.tsx`
- Validacion:
	- `dotnet build backend/WebApp.Api/WebApp.Api.csproj -v minimal` -> OK.
	- `get_errors` sin errores en los archivos tocados.

## Estado actualizado de billing y gobierno (2026-06-06)

### Confirmaciones en Power Platform admin center
- Se verifico en `Licensing > Billing Plans` el plan `Dialatam03` activo y asociado a la suscripcion `Diagramatica_VS01`.
- Se confirmo la disponibilidad de `Copilot Studio` como producto configurable dentro del plan.
- Se dejo el plan `Dialatam03` con alcance amplio (multi-producto):
	- Dataverse
	- Power Apps
	- Power Automate
	- Power Pages
	- Copilot Studio
	- Windows 365 for Agents
- Se verifico en `Licensing > Copilot Studio` que ya existe `1` billing plan para pay-as-you-go.

### Confirmaciones en Teams admin center
- App validada: `Diag Edgar`.
- App ID: `5b2d21dc-de1d-402f-95cb-abe97a0a16b8`.
- Version publicada: `1.0.1`.
- Disponibilidad: `Everyone (org-wide default)`.
- Instalacion actual: `No one`.

### Decision operativa vigente
- Se mantiene el plan habilitado para todos los productos por rapidez de ejecucion de la POC.
- La restriccion posterior por producto/entorno queda planificada para una siguiente ventana de trabajo.

## Correccion runtime chat (2026-06-07)
- Sintoma reportado en UI Legal: `Failed to get a response after 3 attempts`.
- Evidencia en logs backend:
	- multiples eventos `Unhandled stream update type: StreamingResponseMcp*`
	- cierre de stream con `Stream error: (null)` y `StreamingResponseFailedUpdate`.
- Causa raiz tecnica:
	- el backend no manejaba de forma explicita `StreamingResponseFailedUpdate` y, cuando llegaba `StreamingResponseErrorUpdate` sin mensaje, propagaba un error vacio.
- Correccion aplicada en codigo:
	- archivo: `backend/WebApp.Api/Services/AgentFrameworkService.cs`
	- se agrego manejo explicito de `FailedUpdate`.
	- se agrego `DescribeStreamingUpdate(...)` para extraer detalles utiles de error (message/code/reason/status/details) y evitar `null`.
	- se mejoro el bloque `StreamingResponseErrorUpdate` con fallback descriptivo cuando `Message` viene vacio.
- Despliegue realizado:
	- Legal: imagen `crrghwq3yh4wjxi.azurecr.io/web:deploy-1780792101` -> revision `ca-web-rghwq3yh4wjxi--0000003`.
	- Edgar: imagen `crb5aozwd7s565i.azurecr.io/web:deploy-1780792308` -> revision `ca-web-b5aozwd7s565i--0000002`.
- Validacion posterior:
	- `GET /api/health` Legal = `200`.
	- `GET /api/health` Edgar = `200`.
	- El siguiente paso de validacion funcional requiere prueba de chat real desde UI/Teams para confirmar el detalle concreto del fallo (si reaparece) con el nuevo mensaje enriquecido.

## Cierre de errores de autenticacion y permisos en Edgar (2026-06-07)
- Error 1 corregido: `AADSTS500011` (resource principal no encontrado).
	- Causa: faltaba `identifierUri` en app registration Edgar.
	- Accion: se configuro `api://bcc62fb2-0a94-4db4-9363-497027dacae5`.
- Error 2 corregido: `AADSTS50011` (redirect URI mismatch).
	- Causa: faltaba redirect URI productiva de la webapp Edgar en `spa.redirectUris`.
	- Accion: se agrego `https://ca-web-b5aozwd7s565i.wittymoss-8519a558.eastus.azurecontainerapps.io`.
- Error 3 corregido: `HTTP 403 ForbiddenError` al crear conversacion.
	- Causa: la identidad administrada de Edgar no tenia permiso `Microsoft.MachineLearningServices/workspaces/agents/action` en Foundry Project.
	- Accion: se asigno rol `Foundry User` en scope del proyecto:
		- `/subscriptions/e7368471-6353-4c24-b4a7-61d25fe0f76e/resourceGroups/diagramatica-001/providers/Microsoft.CognitiveServices/accounts/aifoundry-diagramtica/projects/firstProject`
		- Principal Edgar: `a900106a-0933-44ba-9aef-7e8fdbf6c631`
		- Principal Legal (alineacion): `ae09b26f-7d4b-458d-9339-d03a944f4a70`
- Estado final validado:
	- Edgar `/api/health` = `200`.
	- Legal `/api/health` = `200`.
	- Sin errores recientes `Chat stream error`, `Failed to create conversation` ni `HTTP 403` en logs recientes de ambos contenedores.

## Historial de conversaciones persistente con Cosmos DB (2026-06-07)

### Decision de arquitectura
- Presupuesto restringido: se eligio **Azure Cosmos DB Serverless** (se paga por RU/s consumidas, escala a cero, sin costo base).
- Cuenta: `cosmos-diagramatica-conv` en `eastus2` (el deploy en `eastus` fallaba por falta de capacidad zonal para Serverless).
- Endpoint: `https://cosmos-diagramatica-conv.documents.azure.com:443/`.
- `disableLocalAuth=true`: solo Managed Identity (sin claves de cuenta), control de acceso por RBAC.
- Base de datos `conversationdb` con 3 contenedores:
	- `conversations` (partition key `/userId`).
	- `messages` (partition key `/conversationId`).
	- `audit` (partition key `/userId`, TTL 90 dias).
- Ambas Managed Identities tienen rol "Cosmos DB Built-in Data Contributor"; `COSMOS_ENDPOINT` inyectado como env var en ambas apps.

### Componentes implementados
- Backend:
	- `backend/WebApp.Api/Services/ConversationRepository.cs` (CRUD de conversaciones, mensajes y auditoria).
	- `backend/WebApp.Api/Models/CosmosModels.cs` (documentos + DTOs).
	- `backend/WebApp.Api/Program.cs` (6 endpoints REST + auto-persistencia en `/api/chat/stream`).
	- SDK: `Microsoft.Azure.Cosmos 3.47.0` + `Newtonsoft.Json 13.0.3` (declarados explicitamente).
- Frontend (React + TS):
	- `ConversationSidebar.tsx`, `chatService.ts`, `AgentChat.tsx`, `chat/ChatInput.tsx`.
	- Menu `⋮` -> "Conversation history" para listar/retomar/eliminar conversaciones.

## Correccion de 3 bugs de runtime del chat (2026-06-07)

### Bug 1 - Legal devolvia 404 (falta de ingress)
- Sintoma: "Error 404 - This Container App is stopped or does not exist" pese a `runningStatus=Running`.
- Causa: la Container App de Legal **no tenia ingress configurado**.
- Correccion: `az containerapp ingress enable -n ca-web-rghwq3yh4wjxi -g rg-diagramatica-legal-webapp --type external --target-port 8080 --transport auto`.
- Nota: la app .NET escucha en **8080** (`ASPNETCORE_URLS=http://+:8080`); usar target-port 80 daba `503 connection refused`.

### Bug 2 - Chat rompia en ambas apps (`ttl: null` en Cosmos)
- Sintoma UI: "Failed to get a response after 3 attempts".
- Evidencia: `CosmosException BadRequest (400): The input ttl 'null' is invalid`.
- Causa: `ConversationDocument.Ttl`/`MessageDocument.Ttl` (`int?`) serializaban `"ttl": null`; Cosmos rechaza un `ttl` null explicito (solo acepta entero positivo o `-1`).
- Correccion (`backend/WebApp.Api/Models/CosmosModels.cs`): `[JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]` en ambos modelos -> la propiedad se omite cuando es null.

### Bug 3 - Mensajes de seguimiento fallaban (conversation id Foundry vs Cosmos)
- Sintoma: el PRIMER mensaje respondia, pero el 2do en adelante fallaba.
- Evidencia: `HTTP 400 (ServiceError: bad_request) Invalid conversation id 'b52ae4...', Malformed identifier`.
- Causa: con Cosmos activo, el backend devuelve al frontend el **id de documento Cosmos** (`Guid.NewGuid().ToString("N")`, GUID sin guiones). El frontend lo reenvia en el siguiente mensaje, y el endpoint lo pasaba directo a Foundry como conversation id (Foundry espera su propio formato `conv_...`).
- Correccion (`backend/WebApp.Api/Program.cs`, endpoint `/api/chat/stream`):
	- Nueva conversacion: crear primero la conversacion real en Foundry (`CreateConversationAsync` -> `conv_...`), luego el documento Cosmos guardando ese id en `FoundryConversationId`.
	- Retomar conversacion: buscar el documento con `GetConversationAsync(userId, request.ConversationId)` y usar su `FoundryConversationId` guardado para llamar a Foundry.
	- Conversacion legacy/huerfana sin `FoundryConversationId`: crear una nueva conversacion Foundry como fallback.
	- Sin Cosmos: `request.ConversationId` es directamente el id de Foundry (comportamiento anterior).

### Despliegue de las correcciones
- Imagenes construidas via ACR (`az acr build`) y desplegadas con `az containerapp update`:
	- Legal: `crrghwq3yh4wjxi.azurecr.io/web:convfix-1780798815` -> revision `ca-web-rghwq3yh4wjxi--0000007`.
	- Edgar: `crb5aozwd7s565i.azurecr.io/web:convfix-1780799692` -> revision `ca-web-b5aozwd7s565i--0000006`.
- Validacion end-to-end:
	- Logs confirman creacion de id real Foundry (`conv_...`) y `Completed streaming for conversation`.
	- Prueba en UI Edgar: conversacion multi-turno funcionando (pregunta inicial + seguimiento en la misma conversacion).
	- Historial persistido correctamente en Cosmos.

### Pendiente menor (no bloqueante)
- `StreamingResponseFailedUpdate` intermitente en el primer intento cuando el agente invoca una herramienta MCP; el retry lo recupera y la respuesta llega.
- `DescribeStreamingUpdate(...)` solo inspecciona propiedades planas (`Message/Error/Code/...`); el motivo real esta anidado en `Response.Error`, por lo que el log no muestra la causa concreta. Mejora futura: extraer ese detalle anidado para diagnosticar por que Foundry corta el primer stream.

## Sobre agregar Azure API Management (APIM) entre WebApp y Foundry/Cosmos (analisis 2026-06-07)
- Pregunta evaluada: tiene sentido un APIM como gateway entre la webapp y Foundry que ademas gestione la relacion con Cosmos.
- Conclusion: **no se recomienda ahora** para este caso.
	- APIM es un gateway de APIs HTTP, no un orquestador de Cosmos; la logica de negocio (mapeo Cosmos-id <-> Foundry-id, persistencia) seguiria en el backend .NET.
	- El bug de conversation id no lo habria evitado APIM.
	- Contradice el objetivo de bajo costo.
- Precios de referencia (USD/mes, EastUS, sin reservas):
	- Consumption: ~$0 + $0.042 / 10k operaciones (1er millon gratis). Unico tier alineado a presupuesto restringido.
	- Basic v2: $150.01/mes. Standard v2 (AI gateway completo): $700/mes. Premium v2: $2,801/mes.
- Cuando si valdria la pena: control de costos de tokens por usuario, cache semantico, gobernanza de varios modelos/agentes. En ese caso empezar con el tier Consumption.

## Cohorte multi-agente EDGAR (actualizacion 2026-06-09)

### Objetivo funcional
- Habilitar una cohorte de 9 agentes analistas con personalidades diferentes para analizar la misma evidencia EDGAR.
- Incorporar un orquestador que ejecute modos competitivo/cooperativo/hibrido.
- Generar comparativas estructuradas (consenso, divergencias, hallazgos unicos, cobertura de evidencia).
- Preparar trazabilidad y exportacion multiformato (pdf, markdown, docx, xlsx) desde una unica interfaz.

### Estado actual del catalogo de personalidades
- Carpeta fuente: `9AgentesConPersonalidad/`.
- Decision operativa: mantener duplicados provisorios y nombrarlos explicitamente:
	- `charlie_munger2_agent.md`
	- `ray_dalio2_agent.md`
- Se conserva `charlie_munger_agent.md` y `ray_dalio_agent.md` como variantes principales.

### Artefactos implementados en esta sesion
- Registro canónico creado: `9AgentesConPersonalidad/agent_cohort_registry.json`.
	- Incluye 9 analistas + 1 orquestador.
	- Define `agentId`, `displayName`, `sourcePromptFile`, `provisional`, `personaGroup`.
	- Define modos de ejecucion y tablas de comparacion objetivo.
- Backend actualizado con endpoint protegido:
	- `GET /api/agents/cohort`
	- Lee y retorna el JSON canónico para frontend/orquestador.
	- Archivo modificado: `backend/WebApp.Api/Program.cs`.
- Validacion tecnica:
	- `dotnet build backend/WebApp.Api/WebApp.Api.csproj -nologo` -> OK.
	- Warning observado es preexistente y no bloqueante para este cambio.

### Plan de trabajo vigente (siguiente fase)
1. Integrar selector de agente en frontend consumiendo `/api/agents/cohort`.
2. Permitir ejecutar agente individual vs cohorte (9 agentes) desde la UI.
3. Implementar contrato unico de salida por agente (JSON comparable).
4. Implementar motor de comparacion (consenso, divergencia, hallazgos unicos, evidencia).
5. Implementar respuesta final multinivel (ejecutivo, analitico, tecnico).
6. Persistir corridas de cohorte y version de prompts para auditoria.
7. Habilitar exportadores de sesion completa en pdf/markdown/docx/xlsx.

### Criterios de cierre de esta linea
- Cohorte ejecutable con 9 agentes y orquestador en la misma webapp.
- Comparativas reproducibles por corrida con trazabilidad de evidencia.
- Historial consultable y exportable en los cuatro formatos objetivo.

### Avance de implementacion (2026-06-09 - pasos 1 y 2)
- Paso 1 completado: frontend conectado al endpoint `GET /api/agents/cohort`.
	- Archivo: `frontend/src/App.tsx`.
	- Se agrego carga autenticada de registro de cohorte y manejo de fallback.
- Paso 2 completado: selector de agente y modo de ejecucion visible en UI.
	- Archivos: `frontend/src/components/AgentChat.tsx` y `frontend/src/components/AgentChat.module.css`.
	- Se agregaron controles para:
		- `Analyst agent`
		- `Execution mode`
	- Se muestran agentes provisorios como tales (ej. `munger2`, `dalio2`).
- Conexion tecnica al flujo de chat:
	- Archivo: `frontend/src/services/chatService.ts`.
	- El request body de `/chat/stream` ahora incluye contexto:
		- `selectedAgentId`
		- `executionMode`
- Tipado agregado para cohorte:
	- Archivo: `frontend/src/types/chat.ts` (`ICohortRegistry`, `ICohortAgentSummary`, `ICohortExecutionMode`).

### Validacion tecnica de este avance
- Validacion de errores TypeScript desde workspace en archivos modificados: sin errores.
- Nota de entorno local: no fue posible ejecutar `npm run build` porque `npm` no esta instalado/disponible en la sesion actual.

### Avance de implementacion (2026-06-09 - paso 3)
- Paso 3 completado: contrato de salida estandar por agente implementado en backend y conectado al flujo de chat.

#### Artefactos agregados
- Contrato JSON canónico:
	- `9AgentesConPersonalidad/agent_output_contract.json`
	- Version: `1.0.0`
	- Campos obligatorios:
		- `tesis_principal`
		- `hallazgos_clave`
		- `riesgos`
		- `oportunidades`
		- `supuestos`
		- `confianza_0_100`
		- `evidencia_edgar_citada`
		- `recomendacion`

#### Backend
- Endpoint nuevo para consumo del contrato:
	- `GET /api/agents/output-contract` (autenticado)
	- Archivo: `backend/WebApp.Api/Program.cs`
- Integracion en runtime del stream (`/api/chat/stream`):
	- si llega contexto de cohorte (`selectedAgentId`/`executionMode`) y no es flujo MCP approval,
	- se inyecta instruccion de salida estructurada con el contrato JSON,
	- se exige respuesta en JSON plano comparable.

#### Modelos/payload
- `backend/WebApp.Api/Models/ChatRequest.cs` extendido con:
	- `SelectedAgentId`
	- `ExecutionMode`
	- `OutputContractVersion`
- `frontend/src/services/chatService.ts` actualizado para enviar:
	- `selectedAgentId`
	- `executionMode`
	- `outputContractVersion` (1.0.0)

#### Validacion
- `dotnet build` backend OK luego de cambios.
- Diagnostico del workspace sin errores en archivos modificados.

### Optimizacion de tokens (2026-06-09 - recuperacion compartida + cache)
- Se implemento capa de recuperacion compartida para cohorte con objetivo de reducir consultas redundantes al indice/herramientas.

#### Diseno aplicado
- En modo cohorte, el backend intenta resolver evidencia compartida desde cache por `usuario + pregunta normalizada`.
- Si hay `cache hit`:
	- se inyecta `SHARED_EVIDENCE_PACKAGE` en el contexto del prompt.
	- se instruye al agente evitar retrieval/search salvo insuficiencia de evidencia.
- Si hay `cache miss`:
	- se ejecuta el flujo normal.
	- al finalizar stream se construye y persiste paquete de evidencia (resumen + citas) para reutilizacion.

#### Componentes nuevos/modificados
- Servicio nuevo: `backend/WebApp.Api/Services/SharedRetrievalCacheService.cs`
	- TTL por defecto: 30 minutos.
	- clave hash: `userId + query normalizada`.
- `backend/WebApp.Api/Program.cs`
	- `POST /api/chat/stream`: lectura/escritura de cache compartida.
	- Evento SSE nuevo: `retrievalCache` (hit/miss + metadata).
	- Endpoint nuevo: `GET /api/agents/retrieval-cache/stats`.
	- Enriquecimiento de prompt con evidencia compartida cuando aplica.

#### Resultado esperado de costo
- Menos llamadas repetidas de retrieval en corridas de cohorte para preguntas equivalentes.
- Reduccion de tokens al reutilizar evidencia ya recuperada/citada.
- Mejor latencia percibida en ejecuciones consecutivas por misma consulta base.

#### Validacion tecnica
- Build backend exitoso tras cambios.
- Sin errores de diagnostico en `Program.cs`, `SharedRetrievalCacheService.cs` y `ChatRequest.cs`.

## Cierre incidente Edgar webapp (2026-06-09)
- Sintoma reportado en produccion: banner UI `Failed to get a response after 3 attempts` y fallo en carga de registro de cohorte.

### Causas raiz confirmadas
1. `FileNotFoundException` por recursos de cohorte no presentes en runtime (`9AgentesConPersonalidad`).
2. `ObjectDisposedException: JsonDocument` en endpoints de cohorte al devolver `document.RootElement` fuera del scope `using`.

### Correcciones aplicadas
- `deployment/docker/frontend.Dockerfile`:
	- Se copio `9AgentesConPersonalidad` al contenedor runtime.
- `backend/WebApp.Api/Program.cs`:
	- Resolucion robusta de rutas con `ResolveSupportFilePath(...)` para archivos de soporte.
	- Correccion de serializacion en endpoints:
		- `GET /api/agents/cohort`
		- `GET /api/agents/output-contract`
	- Cambio clave: `Results.Json(document.RootElement.Clone())` para evitar uso de `JsonDocument` descartado.

### Despliegue de hotfix
- Imagen publicada en ACR Edgar:
	- `crb5aozwd7s565i.azurecr.io/web:deploy-1781043001`
- Container App actualizada:
	- `ca-web-b5aozwd7s565i` (rg `rg-diagramatica-edgar-webapp`)
	- Revision nueva: `ca-web-b5aozwd7s565i--0000011`

### Validacion de cierre
- Infra/rollout:
	- Revision `0000011` en estado `Healthy` y `Running`.
- UI:
	- Selector de cohorte carga correctamente con los 9 analistas.
	- Prueba de chat realizada en vivo: respuesta recibida sin banner de reintentos.
- Logs:
	- Sin nuevas excepciones `ObjectDisposedException: JsonDocument` en revision `0000011`.

## Avance adicional (2026-06-09 noche) - Cohorte automatica 9 analistas + concurrencia controlada

### Objetivo funcional
- Eliminar el paso manual de copy/paste de JSON por analista para construir la comparativa.
- Habilitar ejecucion automatica de los 9 analistas con un solo boton y reutilizar el motor de comparacion existente.

### Backend
- Nuevo endpoint: `POST /api/agents/cohort/run`
	- Ejecuta analistas activos del `agent_cohort_registry.json`.
	- Retorna:
		- respuestas por analista (`agentResponses`),
		- comparacion consolidada (`comparison`),
		- errores por analista (`errors`).
- Concurrencia limitada implementada en ejecucion de cohorte:
	- Ejecucion paralela con `SemaphoreSlim`.
	- Limite configurable por variable `COHORT_RUN_MAX_CONCURRENCY`.
	- Fallback por defecto: `3`.
	- Clamp de seguridad: minimo `1`, maximo `12`, y nunca mayor al numero de analistas activos.

### Frontend
- UI de Cohort Comparison ampliada:
	- Boton nuevo `Run 9 Analysts` (flujo automatico).
	- Boton existente `Run Comparison` se mantiene (flujo manual, backward compatible).
- El flujo automatico:
	- toma `comparisonQuery` o ultimo mensaje de usuario,
	- llama `runCohortAndCompare`,
	- autocompleta textareas JSON,
	- muestra tablas/sintesis sin pegado manual,
	- muestra warnings de autorun por analista cuando aplica.

### Despliegues y estabilizacion
- Imagen desplegada inicialmente para concurrencia: `deploy-1781044982`.
- Durante smoke test se detecto regresion frontend en runtime:
	- `VITE_ENTRA_SPA_CLIENT_ID is not set`.
- Mitigacion aplicada:
	- rollback temporal a imagen estable (`deploy-1781043001`) para restaurar disponibilidad.
- Redeploy corregido con build args de Entra en ACR:
	- `ENTRA_SPA_CLIENT_ID`
	- `ENTRA_TENANT_ID`
	- `ENTRA_BACKEND_CLIENT_ID`
- Estado final confirmado:
	- revision activa: `ca-web-b5aozwd7s565i--0000015`
	- health: `Healthy`
	- running: `Running`
	- image: `crb5aozwd7s565i.azurecr.io/web:deploy-1781046100`

### Nota operativa para uso de UI
- Si un usuario ve `Provide at least one analyst JSON output to run comparison.`
	- esta ejecutando el flujo manual (`Run Comparison`) sin entradas.
	- para el flujo automatico debe usar `Run 9 Analysts`.
