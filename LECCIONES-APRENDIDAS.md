# Lecciones Aprendidas

## 2026-06-06 - Billing Copilot Studio en PPAC
- El plan de billing en PPAC puede quedar con varios productos a la vez; no es exclusivamente de un solo producto en esta experiencia.
- Habilitar `Copilot Studio` en un billing plan se refleja en `Licensing > Copilot Studio` como incremento de `Billing plans` (aunque el consumo siga en cero).
- Que exista `Billing plans: 1` no implica consumo inmediato; el costo aparece cuando hay uso real.

## Gobierno y costo
- Habilitar un plan para muchos productos acelera pruebas, pero aumenta el alcance de facturacion potencial.
- Para POC rapida: habilitacion amplia puede ser valida.
- Para operacion controlada: conviene segmentar por producto y por entorno en planes separados.

## Operacion remota
- Con sesion admin activa en navegador compartido se puede auditar y operar PPAC/Teams de punta a punta de forma remota.
- Los cambios de disponibilidad/publicacion en Teams pueden verificarse en `Manage app` sin depender de scripts locales.

## Teams - publicacion de app
- `Available to: Everyone (org-wide default)` no implica instalacion automatica; solo habilita disponibilidad.
- `Installed for: No one` confirma que aun no hay push de instalacion masiva.

## Proxima mejora recomendada
- Crear al menos dos planes dedicados:
  - Plan A: Copilot Studio (POC agentes)
  - Plan B: Dataverse/otros productos segun necesidad
- Documentar responsable, entorno objetivo y politica de apagado para cada plan.

## 2026-06-07 - Historial persistente con Cosmos DB y bugs de chat

### Cosmos DB Serverless para presupuesto restringido
- Cosmos DB **Serverless** es la opcion mas economica cuando el trafico es bajo/esporadico: se paga por RU/s consumidas y no hay costo base (escala a cero).
- Cosmos Serverless puede fallar el aprovisionamiento por falta de capacidad zonal en una region; si pasa, probar otra region (en este caso `eastus` fallo y `eastus2` funciono).
- Con `disableLocalAuth=true` el acceso es solo por Managed Identity + RBAC (rol "Cosmos DB Built-in Data Contributor"); evita manejar claves de cuenta.

### Cosmos rechaza `ttl: null` explicito
- Una propiedad `ttl` con valor `null` explicito en el documento produce `400 BadRequest: The input ttl 'null' is invalid`. Cosmos solo acepta un entero positivo o `-1` (nunca expira), o que la propiedad **no exista**.
- Con Newtonsoft.Json: usar `[JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]` para omitir la propiedad cuando es null, en vez de serializarla como `null`.

### No confundir el id interno con el id del proveedor (Foundry)
- Cuando se agrega una capa de persistencia propia (Cosmos) sobre un servicio externo (Foundry), aparecen **dos espacios de IDs**: el id de documento interno y el id de conversacion del proveedor.
- Error tipico: devolver al frontend el id interno y luego reenviarlo al proveedor, que lo rechaza por formato (`Invalid conversation id ... Malformed identifier`).
- Regla: guardar el id del proveedor en el documento (campo `FoundryConversationId`) y resolverlo siempre antes de llamar al proveedor. El id interno solo viaja entre frontend y backend.
- El primer mensaje funcionaba (se creaba la conversacion del proveedor en el momento), por eso el bug solo se manifestaba en los mensajes de seguimiento: util como sintoma diagnostico.

### Container Apps: running != accesible
- `runningStatus=Running` NO garantiza que la app responda por HTTP. Si falta el ingress, se obtiene `404 - This Container App is stopped or does not exist`.
- Verificar siempre: ingress habilitado (external), FQDN asignado, y que `target-port` coincide con el puerto real de la app (aqui 8080, no 80). Un target-port equivocado da `503 connection refused`.

### Tooling de despliegue en Windows
- `az acr build` puede crashear en Windows con `UnicodeEncodeError` al transmitir logs con caracteres como `✓` (cp1252). El build del lado servidor suele completar igual; setear `PYTHONIOENCODING=utf-8` y verificar la imagen con `az acr repository show-tags`.
- Para builds largos, redirigir el output a archivos de log y a un archivo de estado en vez de depender del scroll del terminal.
- En `az acr build`, un `--build-arg` cuyo valor contiene `;` (p.ej. una connection string de App Insights) rompe el shell del agente ACR; pasar esos valores por secreto/env o evitarlos en build-args.

### APIM como gateway: cuando NO
- Un API Management no "gestiona" Cosmos: es un gateway de APIs HTTP. La logica de negocio y la persistencia siguen en el backend.
- No agregar APIM solo "por si acaso": suma costo (Basic v2 $150/mes, Standard v2 $700/mes) y no resuelve problemas de logica.
- Se justifica cuando se necesita gobernanza real: rate-limiting de tokens por usuario, cache semantico, content safety, balanceo entre modelos. En presupuesto restringido, empezar por el tier Consumption (~$0 base).

## 2026-06-09 - Incidente Edgar: JsonDocument descartado en endpoints

### Hallazgo tecnico
- Devolver `Results.Json(document.RootElement)` dentro de un `using var document = JsonDocument.Parse(...)` puede fallar en runtime con `ObjectDisposedException` cuando ASP.NET serializa la respuesta fuera del scope.
- El error se manifesto en produccion como:
  - `Error fetching cohort registry: HTTP 500`
  - Banner UI: `Failed to get a response after 3 attempts`.

### Correccion patron
- Clonar siempre el `RootElement` antes de retornarlo:
  - `Results.Json(document.RootElement.Clone())`.
- Aplicado en:
  - `/api/agents/cohort`
  - `/api/agents/output-contract`.

### Leccion operativa
- Aunque la app responda `Healthy`, errores de serializacion en endpoints secundarios pueden degradar la experiencia completa del chat/orquestacion.
- Para cierre real de incidente, validar siempre con:
  1. revision activa y saludable,
  2. carga de UI sin errores de cohorte,
  3. envio de mensaje real con respuesta exitosa.

## 2026-06-09 - Build frontend y variables VITE obligatorias

### Hallazgo
- Una imagen puede quedar `Healthy/Running` en Container Apps y aun asi romper la UI en runtime si se construyo sin variables `VITE_*` requeridas.
- Sintoma observado en browser:
  - `VITE_ENTRA_SPA_CLIENT_ID is not set`.

### Leccion tecnica
- El frontend (Vite) requiere inyectar variables en build-time, no en runtime.
- Para este proyecto, en builds ACR del webapp se deben pasar al menos:
  - `ENTRA_SPA_CLIENT_ID`
  - `ENTRA_TENANT_ID`
  - `ENTRA_BACKEND_CLIENT_ID` (aunque venga vacio en este entorno).

### Practica recomendada
- Despues de cada deploy, ejecutar smoke test browser rapido (no solo `/api/health`).
- Si falla UI por `VITE_*`, hacer rollback rapido a ultima imagen estable y redeploy con build args correctos.

## 2026-06-09 - UX Cohorte: manual vs automatica

### Hallazgo
- El mensaje `Provide at least one analyst JSON output to run comparison.` no indica fallo del backend nuevo.
- Indica uso del flujo manual (`Run Comparison`) sin datos pegados.

### Leccion de producto
- Debe reforzarse en UI/operacion la distincion:
  - `Run 9 Analysts` = flujo automatico.
  - `Run Comparison` = flujo manual.
