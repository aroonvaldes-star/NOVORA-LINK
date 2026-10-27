# Contribuir a NOVORA-LINK

Gracias por el interés en NOVORA-LINK. Este documento explica cómo
reportar problemas, proponer cambios y qué términos se aplican a las
contribuciones.

NOVORA-LINK es un proyecto **propietario**. Aceptar una contribución no
implica abrir el proyecto ni transferir la titularidad del Software.

## 1. Canales

| Propósito | Canal |
|---|---|
| Errores reproducibles | GitHub Issues |
| Propuestas de mejora | GitHub Issues |
| Cambios de código o documentación | Pull Request |
| Vulnerabilidades | **No abra un issue público**; consulte `SECURITY.md` |
| Licencias, marcas o uso comercial | aroonvaldes@gmail.com |

## 2. Antes de enviar un cambio

1. Busque si ya existe un issue o pull request relacionado.
2. Para cambios grandes, abra primero un issue para acordar el alcance.
3. No incluya secretos, tokens, certificados, rutas personales ni datos de
   dispositivos reales.
4. No incluya código de terceros sin verificar que su licencia sea
   compatible con la NLPL v1.1 y sin añadir el aviso correspondiente.
5. No reformatee archivos completos ni mezcle cambios no relacionados en
   un mismo pull request.

## 3. Entorno y compilación

Requisitos:

- .NET SDK 8 y 10.
- Workload de Android para `net10.0-android`.
- Rust estable para `NovoraRelay`.
- Inno Setup 6/7 para el instalador (opcional).

Comandos de referencia:

```powershell
# Windows
dotnet build "src/NOVORA LINK/NLProjectDesktop.csproj" -c Release

# Android (compilación, sin empaquetar)
dotnet build "src/LINK Control/NLProjectAndroid.csproj" -c Release -r android-arm64 `
  -t:Compile -p:AndroidBuildApplicationPackage=false

# Relay Rust
cargo test --release --locked --manifest-path `
  "src/NOVORA LINK/LinkEngine/Network/Native/NovoraRelay/Cargo.toml"

# Suites de pruebas del repositorio
./scripts/Test-NOVORA-CI.ps1 -Configuration Release
```

Las suites son ejecutables de consola. El script omite el harness físico y
el benchmark que requieren entradas externas.

## 4. Estilo y verificación

- Siga la estructura, nomenclatura y patrones del código existente.
- Mantenga activada la nulabilidad (`Nullable`) y evite suprimir avisos sin
  justificación.
- Use nombres con prefijo `NL`, `VE`, `LE` o `ExIn` según el área.
- Ejecute las verificaciones disponibles antes de enviar:

```powershell
./scripts/Verify-EngineNaming.ps1
./scripts/Test-SourceContents.ps1
```

- Actualice la documentación cuando cambie comportamiento, protocolos,
  versiones o dependencias.

## 5. Proceso de pull request

1. Parta de la rama `NOVORA-LINK`.
2. Use una rama descriptiva (`fix/...`, `feat/...`, `docs/...`).
3. Escriba mensajes de commit claros y en imperativo.
4. Firme sus commits con `git commit -s` (Developer Certificate of Origin).
5. Describa el problema, la solución, las pruebas ejecutadas y los riesgos.
6. Atienda la revisión. El Titular puede rechazar o posponer un cambio sin
   obligación de justificarlo.

## 6. Derechos sobre las contribuciones

Enviar una contribución —incluidos pull requests, parches, issues con
código, documentación o diseños— no transfiere su titularidad ni concede
por sí solo al Titular una licencia de explotación más amplia que los
permisos necesarios para alojar y revisar la propuesta en la plataforma
utilizada.

Antes de incorporar material protegido al Software, las partes deberán
formalizar por escrito un acuerdo separado que identifique la contribución,
los derechos patrimoniales autorizados o transmitidos, su alcance,
modalidad, territorio, vigencia y remuneración o participación aplicable,
además de las condiciones de atribución y modificación. No se incorporará
la contribución mientras ese acuerdo no esté firmado por ambas partes.

La autoría y los derechos morales permanecen con la persona autora en los
términos de la ley aplicable; este documento no pretende cederlos ni
renunciar a ellos. Cualquier autorización para modificar una obra deberá
constar en el acuerdo escrito y respetar los derechos inderogables.

La persona que envía material declara que es su autora o que cuenta con
autorización suficiente para proponerlo. Si un empleador, institución,
cliente u otra persona tiene derechos sobre el material, obtenga la
autorización necesaria antes de enviarlo. Un DCO acredita la procedencia
de una contribución; no sustituye el acuerdo escrito sobre derechos de
explotación.

El Titular no está obligado a aceptar, incorporar, mantener ni distribuir
una propuesta. Las aportaciones ya incorporadas y los acuerdos firmados
antes de esta revisión deben analizarse según sus propios términos; este
cambio no los modifica retroactivamente.

## 7. Conducta

Mantenga un trato respetuoso y técnico. No se toleran acosos, amenazas,
discriminación ni la publicación de datos personales de terceros.

## 8. Contacto

- Titular: Aaron Yair Galarza Valdes
- Correo: aroonvaldes@gmail.com
- Repositorio: https://github.com/aroonvaldes-star/NOVORA-LINK
