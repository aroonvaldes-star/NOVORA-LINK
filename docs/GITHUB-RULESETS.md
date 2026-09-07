# GitHub Rulesets — NOVORA-LINK

Este documento define la protección recomendada para la rama principal y los tags de release de NOVORA-LINK.

## Estado esperado

NOVORA-LINK debe tener dos Rulesets activos:

1. `Protect NOVORA-LINK` — rama principal.
2. `Protect NOVORA release tags` — tags `v*`.

Los archivos fuente de estas reglas están versionados en:

- `rulesets/novora-link-branch.json`
- `rulesets/novora-release-tags.json`

## 1. Rama `NOVORA-LINK`

El Ruleset de rama aplica exclusivamente a:

```text
refs/heads/NOVORA-LINK
```

Protecciones:

- impedir eliminación de la rama;
- impedir force-push;
- exigir que los cambios entren mediante Pull Request;
- exigir que las conversaciones de revisión estén resueltas;
- exigir el status check `build`;
- exigir que la rama del PR esté actualizada con la base antes de merge.

Actualmente el repositorio tiene un único mantenedor principal, por lo que el Ruleset exige PR pero configura `0` aprobaciones obligatorias. Esto evita bloquear al propietario del repositorio sin eliminar el control de CI. Si se incorporan más mantenedores, se recomienda cambiar `required_approving_review_count` a `1` o más y activar revisión de CODEOWNERS.

El check requerido se llama `build` porque el job principal de `.github/workflows/ci.yml` usa ese identificador. GitHub documenta que los Rulesets de status checks usan el nombre del job como contexto para workflows normales.

## 2. Tags de release

El Ruleset de tags aplica a:

```text
refs/tags/v*
```

Protecciones:

- impedir eliminación de tags de versión;
- impedir cambios no fast-forward/reescritura de tags protegidos.

Esto busca que una versión ya publicada no pueda apuntar silenciosamente a otro commit.

## Aplicación automática

Desde PowerShell, con GitHub CLI autenticado como administrador del repositorio:

```powershell
cd "C:\ruta\a\NOVORA-LINK"

pwsh -File .\scripts\Configure-GitHubRulesets.ps1
```

Para simular sin escribir cambios:

```powershell
pwsh -File .\scripts\Configure-GitHubRulesets.ps1 -WhatIf
```

El script es idempotente: si un Ruleset con el mismo nombre y target ya existe, lo actualiza; si no existe, lo crea.

## Permisos requeridos

La REST API de GitHub exige permisos administrativos para crear o modificar Rulesets. Si `gh` utiliza un fine-grained personal access token, el repositorio debe tener:

```text
Repository permissions
└── Administration: Read and write
```

Comprobar autenticación:

```powershell
gh auth status
```

Si hace falta iniciar sesión:

```powershell
gh auth login
```

## Aplicación manual desde GitHub

También pueden importarse los JSON desde la interfaz de GitHub:

```text
Repository
→ Settings
→ Rules
→ Rulesets
→ New ruleset / Import a ruleset
```

GitHub permite importar Rulesets preconstruidos desde JSON.

## Verificación

Después de aplicar las reglas:

```powershell
gh api repos/aroonvaldes-star/NOVORA-LINK/rulesets
```

Deben aparecer ambos Rulesets con:

```text
enforcement = active
```

También puede comprobarse desde:

```text
Repository
→ Settings
→ Rules
→ Rulesets
```

## Política de cambios

Cualquier modificación de estos archivos debe tratarse como un cambio de seguridad/gobernanza:

- realizarse mediante rama separada;
- pasar `NOVORA CI`;
- revisarse antes de volver a ejecutar `Configure-GitHubRulesets.ps1`;
- no reducir protecciones silenciosamente para resolver un fallo de CI.
