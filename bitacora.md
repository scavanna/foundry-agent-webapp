# Bitacora

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
