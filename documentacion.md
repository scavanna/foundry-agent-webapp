# Documentacion

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

## Validacion adicional realizada
- Salida de azd show revisada.
- Resultado observado: el template aparece sin servicios en azd show.
- Conclusión: esperado para este proyecto porque usa patron infra-only en azure.yaml y hooks de despliegue.
- Confirmacion de endpoint publicado en .azure/scavanna-foundry-secops/.env:
	- WEB_ENDPOINT=https://ca-web-76veos3l6strm.mangoflower-91937363.eastus.azurecontainerapps.io

## Bloqueador actual
- En la interfaz web, al enviar mensajes al agente, aparece "stream timeout".
- Estado: infraestructura desplegada y autenticacion funcional, pero la conversacion no completa stream.

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
