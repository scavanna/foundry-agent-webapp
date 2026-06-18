# Teams rollout - 2 apps separadas (org-wide)

Este directorio documenta el despliegue de dos apps de Teams separadas:
- Diagramatica Legal/Juris
- Diagramatica Edgar

## 1) Generar paquetes de app

Ejecuta el script desde la raiz del repo con las URLs finales de ambas webapps:

```powershell
pwsh -NoProfile -File .\deployment\scripts\generate-teams-app-packages.ps1 \
  -LegalAppUrl "https://<legal-webapp-url>" \
  -EdgarAppUrl "https://<edgar-webapp-url>" \
  -AppPackageVersion "1.0.0"
```

Se generan dos paquetes en `teams/packages/`:
- `teamsapp-legal-juris.zip`
- `teamsapp-edgar.zip`

## 2) Publicar en Teams Admin Center

1. Ir a Teams Admin Center.
2. Teams apps -> Manage apps.
3. Upload new app -> Upload.
4. Subir ambos zip.
5. Verificar que ambas apps queden en estado Allowed.

## 2.1) Actualizar apps ya existentes (in-place)

Si las apps ya existen en el catalogo (mismo app ID), no usar "Upload new app".

1. Ir a Teams apps -> Manage apps.
2. Buscar `Diag Legal` y abrir detalle.
3. En `New version`, usar `Upload file` y subir `teamsapp-legal-juris.zip`.
4. Repetir para `Diag Edgar` con `teamsapp-edgar.zip`.

Regla obligatoria:
- En cada actualizacion, incrementar `-AppPackageVersion` (por ejemplo, de `1.0.1` a `1.0.2`).
- Si se sube la misma version de manifiesto, Teams devuelve error `409` y rechaza el update.

## 3) Hacerlas disponibles para todos los usuarios

Aplicar en el policy global de la organizacion:
- Teams apps -> Permission policies -> Global (Org-wide default)
  - Confirmar que custom apps esten permitidas.
  - Confirmar que ambas apps de Diagramatica esten permitidas.

Opcional para adopcion inmediata:
- Teams apps -> Setup policies -> Global (Org-wide default)
  - Add installed apps: agregar ambas apps.
  - Add pinned apps: agregar ambas apps si se desea fijarlas en barra lateral.

## 4) Validacion

Con un usuario comun (no admin):
1. Abrir Teams.
2. Buscar "Diag Legal" y "Diag Edgar".
3. Abrir ambas y validar carga de la webapp.

## Notas

- Las dos apps son separadas por diseno (1 app = 1 agente).
- Este modelo alinea gobierno, telemetria y ownership por dominio.