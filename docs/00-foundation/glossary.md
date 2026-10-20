# Glosario y equivalencias EasyJob → NEXUS

## §1 Términos

| Término | Significado en NEXUS |
|---|---|
| Mandant | Entidad legal (`LegalEntity`) como frontera de datos. Unidad de aislamiento RLS |
| Party | Directorio universal: organizaciones y personas, internas y externas |
| AssetType | Tipo de material del catálogo (qué existe) |
| AssetItem | Unidad física individual de un tipo `SERIALIZED` |
| Lote (batch) | Conjunto de unidades de un tipo `BATCH` con lote o fecha |
| `tracking_mode` | `BULK`, `BATCH` o `SERIALIZED`; excluyente por tipo |
| Ledger | `StockMovement`, registro append-only del stock |
| StockLocation | Lugar lógico de stock: almacén, obra de proyecto o "en tránsito" |
| Transfer | Movimiento de custodia entre `StockLocation` sin cambio de propiedad |
| Reservation | Compromiso de material con fechas (`SOFT` → `FIRM` → `CONVERTED`/`RELEASED`) |
| ATP | *Available-to-promise*: disponible = existencias + entradas − reservas ponderadas, por ventana de tiempo |
| Job | Unidad operativa ejecutable de un proyecto (alquiler/montaje) |
| Kalkulation | Precálculo formal de un proyecto (`ProjectBudget`) |
| Gate C / Gate D | Aprobaciones humanas de contrato y de entrega de un paso |
| Track | Línea de trabajo de un paso: C, B, S, F, I, M, J |

## §2 Equivalencias EasyJob / adunic

| EasyJob / adunic | NEXUS | Nota |
|---|---|---|
| `Address` (empresas, contactos, direcciones) | `Party` + `Location` | EJ mezcla los tres conceptos |
| `601_Partner` (adunic) | `Party` | |
| `Stock` (almacén EJ) | `Warehouse` → `StockLocation` | |
| `StockType` | `AssetType` | |
| `StockItem` | `AssetItem` | Solo para `SERIALIZED` |
| Cantidad en `StockType` + filas `StockItem` | `tracking_mode` excluyente + ledger | Corrige la doble contabilidad |
| `Job`, `StockType2Job`, `IdJobState` | `Job`, `JobLine`, estado de `Job` | Ver skill `ej-job-states` para el mapeo de estados (paso 7.3) |
| `Subrental` + `Subjob` | `Transfer` / `TransferLine` | |
| TVFs `GetAvbl2Stocks`, `fGetMinAvailableInventory2Stocks`, `GetAvblBreakpoints2Stocks` | `AvailabilityProjection` (ATP) | NEXUS hace explícita la `Reservation` |
| `Job.Opportunity` | `Opportunity` (A4) | |
| `TimeCardUser`, `Freelancer`, persona en `Address` | `Worker` | |
| `081_Personal` (adunic) | `Worker` | |
| `837_MontagePhase` (adunic) | `JobPhase` | |
| `023_Debitoren`, `406_Rechnungen` (adunic) | `Invoice` | |
| `180_Kalk` (adunic) | `ProjectBudget` / controlling | |
| Triggers `trgJobInsertUpdate`, `HPSAvailChange` | Eventos de dominio (A7) | |
