# Legal — NOVORA-LINK

**Versión del paquete legal:** 1.0  
**Vigente desde:** 21 de octubre de 2026  
**Titular:** Aaron Yair Galarza Valdes

Esta carpeta reúne los documentos legales que acompañan a NOVORA-LINK y
que se incluyen en el instalador.

## 1. Índice

| Documento | Contenido | Idioma |
|---|---|---|
| [`../LICENSE`](../LICENSE) | NLPL v1.1, texto canónico | Español |
| [`../LICENSE.en.md`](../LICENSE.en.md) | Traducción de referencia | Inglés |
| [`NLLegalLicense.txt`](NLLegalLicense.txt) | Copia de la NLPL para el instalador | Español |
| [`NLLegalCopyright.md`](NLLegalCopyright.md) | Titularidad y alcance del copyright | Español |
| [`NLLegalEULA.txt`](NLLegalEULA.txt) | Contrato de usuario final | Español |
| [`NLLegalEULA.en.txt`](NLLegalEULA.en.txt) | Traducción del EULA | Inglés |
| [`NLLegalPrivacy.md`](NLLegalPrivacy.md) | Referencia al aviso de privacidad | Español |
| [`NLLegalTrademarks.md`](NLLegalTrademarks.md) | Referencia a las reglas de marcas | Español |
| [`NLLegalThirdPartyNotices.md`](NLLegalThirdPartyNotices.md) | Inventario y avisos de terceros | Español |
| [`NLLegalAcknowledgements.md`](NLLegalAcknowledgements.md) | Agradecimientos y procedencia | Español |
| [`LICENSES/`](LICENSES/) | Textos íntegros de licencias de terceros | Inglés |

Documentos en la raíz del repositorio:

- [`../NOTICE`](../NOTICE) — aviso de atribución bilingüe.
- [`../PRIVACY.md`](../PRIVACY.md) — aviso de privacidad completo.
- [`../TRADEMARKS.md`](../TRADEMARKS.md) — reglas de marcas.
- [`../CONTRIBUTING.md`](../CONTRIBUTING.md) — contribuciones y licencia
  de aportes.
- [`../SECURITY.md`](../SECURITY.md) — política de seguridad.

## 2. Jerarquía

1. **Código fuente y modificaciones:** NLPL v1.1 (`../LICENSE`).
2. **Software instalado:** EULA (`NLLegalEULA.txt`).
3. **Datos personales:** `../PRIVACY.md`.
4. **Marcas:** `../TRADEMARKS.md`.
5. **Componentes de terceros:** sus propias licencias.

En caso de conflicto entre el EULA y la NLPL, el EULA prevalece para el uso
del binario instalado y la NLPL para el código fuente.

## 3. Licencias de terceros incluidas

- [`LICENSES/Apache-2.0.txt`](LICENSES/Apache-2.0.txt)
- [`LICENSES/MIT.txt`](LICENSES/MIT.txt)
- [`LICENSES/BSD-2-Clause-dav1d.txt`](LICENSES/BSD-2-Clause-dav1d.txt)
- [`LICENSES/Zlib.txt`](LICENSES/Zlib.txt)
- [`LICENSES/LGPL-3.0.txt`](LICENSES/LGPL-3.0.txt)

## 4. Mantenimiento

Al cambiar la licencia, el EULA o el inventario de terceros:

1. Actualice la versión y la fecha en el documento correspondiente.
2. Mantenga `../LICENSE` y `NLLegalLicense.txt` idénticos.
3. Actualice `NLLegalThirdPartyNotices.md` si cambian dependencias.
4. Verifique que el instalador incluya la carpeta `Legal/`.
5. Registre el cambio en el historial de Git.

## 5. Estado

Estos documentos fueron preparados a partir del inventario técnico del
repositorio y de las licencias declaradas por los proyectos de terceros.
**Se recomienda su revisión por un abogado con experiencia en propiedad
intelectual y protección de datos en México antes de una release estable o
de una distribución comercial.**

## 6. Contacto

- Correo: aroonvaldes@gmail.com
- Repositorio: https://github.com/aroonvaldes-star/NOVORA-LINK
