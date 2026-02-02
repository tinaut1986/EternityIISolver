# Eternity II Distributed Solver

Este proyecto es un sistema distribuido diseñado para resolver el puzzle **Eternity II** utilizando una arquitectura de Servidor-Trabajador (Server-Worker).

## Tabla de Contenidos
1. [Estructura del Proyecto](#estructura-del-proyecto)
2. [Requisitos Previos](#requisitos-previos)
3. [Configuración del Servidor](#configuración-del-servidor)
4. [Configuración del Worker](#configuración-del-worker)
5. [Dashboard Web](#dashboard-web)
6. [Arquitectura y Lógica del Sistema](#arquitectura-y-lógica-del-sistema)
7. [Formato de los Datos](#formato-de-los-datos)
8. [Documentación Técnica](#documentación-técnica)
9. [Troubleshooting](#troubleshooting)

---

## Estructura del Proyecto

```
Eternity II/
├── EternityServer/          # API REST (ASP.NET Core 9)
│   ├── Controllers/         # Endpoints de la API
│   ├── Data/               # Entity Framework DbContext
│   ├── Models/             # Modelos de base de datos (Job, Solution)
│   ├── Services/           # Servicios de fondo (ZombieJobCleaner)
│   └── wwwroot/            # Dashboard web estático
├── EternityWorker/          # Aplicación de consola (solver)
│   └── Services/           # SolverService, ApiClient
├── EternityShared/          # Biblioteca compartida
│   ├── Dtos/               # Data Transfer Objects para la API
│   └── Game/               # Lógica del puzzle (Board, Piece, etc.)
├── eternity2_256.csv        # Definición de las 256 piezas
└── eternity2_256_all_hints.csv  # Pistas oficiales (5 piezas fijas)
```

---

## Requisitos Previos

- **.NET 9 SDK**: Necesario para compilar y ejecutar localmente.
- **MySQL / MariaDB**: Para almacenar el estado de los trabajos y las mejores soluciones.
- **Docker** (Opcional): Para desplegar el servidor en entornos como Linux.

---

## Configuración del Servidor

### 1. Archivos de Configuración
Los archivos `appsettings.json` contienen credenciales y están excluidos del repositorio. 
Copia la plantilla y configura tus valores:

```bash
# Windows
copy EternityServer\appsettings.template.json EternityServer\appsettings.json

# Linux/Mac
cp EternityServer/appsettings.template.json EternityServer/appsettings.json
```

Edita `appsettings.json` con tus datos:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=TU_IP;Database=eternity_db;User=TU_USUARIO;Password=TU_PASSWORD;"
}
```

### 2. Base de Datos
Crea una base de datos en MySQL/MariaDB llamada `eternity_db`:
```sql
CREATE DATABASE eternity_db;
GRANT ALL PRIVILEGES ON eternity_db.* TO 'eternity_worker'@'%' IDENTIFIED BY 'tu_password';
FLUSH PRIVILEGES;
```

### 3. Ejecución local (Windows/Linux)
```bash
cd EternityServer
dotnet run
```
El servidor aplicará automáticamente las migraciones de Entity Framework al iniciar y sembrará el primer trabajo si la BD está vacía.

### 3. Ejecución con Docker
```bash
docker compose up -d --build
```

El servidor expone el puerto `8080` por defecto y se conecta a MariaDB a través de la red Docker.

---

## Configuración del Worker

### Archivos Necesarios
El Worker requiere el archivo `eternity2_256.csv` en la misma carpeta que el ejecutable.

**Nota:** La primera línea del CSV (`16,16,5,17`) son metadatos y se ignora automáticamente.

### Configuración del Servidor de Destino
Copia la plantilla y configura la URL del servidor:

```bash
# Windows
copy EternityWorker\appsettings.template.json EternityWorker\appsettings.json

# Linux/Mac
cp EternityWorker/appsettings.template.json EternityWorker/appsettings.json
```

Edita `EternityWorker/appsettings.json`:
```json
{
  "ServerUrl": "http://192.168.1.101:5210"
}
```

### Ejecución
```bash
cd EternityWorker
dotnet run
```

Al iniciar, el Worker pregunta por el modo de carga:
1. **Background (15%)** - Usa pocos núcleos, para no interferir con otras tareas.
2. **Balanced (50%)** - Usa la mitad de los núcleos disponibles.
3. **Turbo (100%)** - Usa todos los núcleos para máxima velocidad.

---

## Dashboard Web

Accede al dashboard en: `http://TU_SERVIDOR:PUERTO/`

El dashboard muestra:
- **Best Depth**: La profundidad máxima alcanzada (X/256 piezas colocadas).
- **Active Workers**: Número de workers activos en este momento.
- **Tablero Visual**: Representación gráfica del mejor tablero encontrado hasta ahora.

---

## Arquitectura y Lógica del Sistema

### Flujo General

```
┌─────────────────────────────────────────────────────────────────┐
│                         SERVIDOR                                │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────────────┐  │
│  │   Base de   │◄───│  JobsContr. │◄───│  Dashboard Web      │  │
│  │   Datos     │    │  (API)      │    │  (index.html)       │  │
│  └─────────────┘    └──────┬──────┘    └─────────────────────┘  │
│                            │                                     │
└────────────────────────────┼─────────────────────────────────────┘
                             │ HTTP
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
   ┌─────────┐          ┌─────────┐          ┌─────────┐
   │ Worker1 │          │ Worker2 │          │ Worker3 │
   │ (4 CPU) │          │ (8 CPU) │          │ (2 CPU) │
   └─────────┘          └─────────┘          └─────────┘
```

### Ciclo de Vida de un Job

1. **SEED**: Al arrancar, el servidor crea el Job 1 (raíz) con solo los 5 hints.
2. **REQUEST**: Un Worker pide trabajo → El servidor le asigna un Job pendiente.
3. **SOLVE**: El Worker explora el árbol de combinaciones usando backtracking.
4. **RESULT**: Tras el tiempo límite, el Worker reporta:
   - `SPLIT`: Se crean subjobs para cada "primera decisión" posible.
   - `NO_SOLUTION_FOUND`: La rama explorada no tiene solución.
   - `SOLUTION_FOUND`: ¡Eureka! Se encontró la solución.

### Sistema de Split (División de Trabajo)

Cuando el tiempo se agota, el Worker genera **subjobs** para distribuir el trabajo:

```
Job 1 (raíz, 5 hints)
    │
    ├── [60s] → SPLIT
    │
    ├── Job 2 (hint + pieza A en [0,0])
    ├── Job 3 (hint + pieza B en [0,0])
    ├── Job 4 (hint + pieza C en [0,0])
    └── Job 5 (hint + pieza D en [0,0])
         │
         ├── [10min] → SPLIT
         │
         ├── Job 6 (hint + D + pieza X en [0,1])
         ├── Job 7 (hint + D + pieza Y en [0,1])
         └── ...
```

### Tiempo Límite Dinámico

El servidor asigna el tiempo límite según la carga actual:

| Jobs Pendientes | Tiempo | Razón |
|-----------------|--------|-------|
| < 4             | 60s    | Pocos jobs → divide rápido para crear paralelismo |
| 4 - 19          | 5min   | Carga moderada → equilibrio |
| ≥ 20            | 10min  | Mucho trabajo → exploración profunda |

### Validación y Quorum

Para evitar errores (bugs, hardware defectuoso), cada rama se valida así:
1. El primer Worker que completa una rama reporta `NodesVisited` y `LeafChecksum`.
2. Un segundo Worker repite el trabajo y compara los valores.
3. Si coinciden → la rama se marca como "Verificada".
4. Si hay conflicto → la rama se reinicia para una tercera opinión.

### Zombie Job Cleaner

Un servicio de fondo detecta jobs "zombies" (asignados pero sin heartbeat):
- Si un Worker muere o se desconecta, su job vuelve a `Pending` tras unos minutos.
- Esto garantiza que ningún trabajo se pierda.

---

## Formato de los Datos

### Piezas (eternity2_256.csv)

Cada línea: `ID,E,S,W,N`
- **ID**: Número de la pieza (1-256).
- **E, S, W, N**: Colores de los bordes (Este, Sur, Oeste, Norte).
- **Color 0**: Borde del tablero (solo válido en piezas de perímetro).

Ejemplo: `139,14,8,8,9` → Pieza 139 con Este=14, Sur=8, Oeste=8, Norte=9.

### Hints (eternity2_256_all_hints.csv)

Cada línea: `Row,Col,PieceID,Rotation`
- **Row, Col**: Posición en el tablero (0-15).
- **PieceID**: ID de la pieza a colocar.
- **Rotation**: Giros de 90° en sentido horario (0-3).

Ejemplo: `8,7,139,3` → Pieza 139 en [8,7] rotada 3 veces.

### Serialización del Tablero (Binario)

Cada pieza colocada se serializa como 4 bytes:
```
[Position (1 byte)] [PieceID High (1 byte)] [PieceID Low (1 byte)] [Rotation (1 byte)]
```
Un tablero completo (256 piezas) ocupa 1024 bytes.

---

## Documentación Técnica

### Clases Principales

#### `Board` (EternityShared/Game/Board.cs)
Representa el tablero de 16x16. Métodos clave:
- `TryPlace(row, col, piece, rotation)`: Intenta colocar una pieza. Valida bordes y vecinos.
- `IsValidPlacement()`: Comprueba que la pieza encaje con vecinos y respete las reglas de borde.
- `GetPieceId(row, col)`: Obtiene el ID de la pieza en una posición.

#### `Piece` (EternityShared/Game/Piece.cs)
```csharp
public record struct Piece(int Id, byte Top, byte Right, byte Bottom, byte Left)
{
    public Piece Rotate(int count); // Gira la pieza N veces (sentido horario)
}
```

#### `SolverService` (EternityWorker/Services/SolverService.cs)
El corazón del algoritmo. Usa **backtracking síncrono** para explorar combinaciones:
```csharp
private bool BacktrackSync(int r, int c, CancellationToken ct)
{
    foreach (piece in availablePieces)
        foreach (rotation in 0..3)
            if (board.TryPlace(r, c, piece, rotation))
                if (BacktrackSync(nextPosition)) return true;
                board.Remove(r, c);
    return false;
}
```

### Reglas de Validación del Tablero

1. **Bordes Exteriores**: Las piezas en el perímetro DEBEN tener color 0 en el lado que da al borde.
2. **Interior**: Las piezas interiores NO pueden tener color 0 en ningún lado.
3. **Vecinos**: Los lados adyacentes de dos piezas deben tener el mismo color.

### API Endpoints

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/api/jobs/request-job` | Solicitar un job pendiente |
| POST | `/api/jobs/heartbeat` | Enviar latido (mantener job activo) |
| POST | `/api/jobs/report-success` | Reportar finalización (solución o callejón) |
| POST | `/api/jobs/report-split` | Reportar división en subjobs |
| GET | `/api/jobs/stats` | Obtener estadísticas para el dashboard |
| GET | `/api/jobs/pieces` | Obtener lista de piezas (para el visor) |

---

## Troubleshooting

### "No jobs available"
- **Causa**: No hay jobs pendientes o el servidor no es alcanzable.
- **Solución**: Verifica que el servidor esté corriendo y que la BD tenga al menos 1 job.

### El Worker "revienta" con "Key already exists"
- **Causa**: El CSV de piezas tiene una línea de metadatos que se está parseando como pieza.
- **Solución**: Asegúrate de usar la versión actualizada de `PieceLoader.cs` que ignora la primera línea.

### Los jobs se quedan en "Assigned" para siempre
- **Causa**: Un Worker murió sin reportar.
- **Solución**: El `ZombieJobCleaner` debería liberar estos jobs automáticamente. Verifica que el servicio esté activo.

### El dashboard no muestra el tablero
- **Causa**: El campo `BestBoardState` está vacío.
- **Solución**: Asegúrate de que el servidor guarde `BestBoardState` tanto en `ReportSuccess` como en `ReportSplit`.

---

## Notas de Infraestructura (Beelink/Linux)

1. **Permisos de DB**:
   ```sql
   GRANT ALL PRIVILEGES ON eternity_db.* TO 'eternity_worker'@'%' IDENTIFIED BY 'eternity_worker';
   ```

2. **Nombres de Archivos**: Linux es sensible a mayúsculas. Usar nombres exactos.

3. **Red Docker**: El servidor necesita estar en la misma red que MariaDB:
   ```yaml
   networks:
     - shared_database_network
   ```

---

## Licencia

Este proyecto es de uso personal/educativo. El puzzle Eternity II es propiedad de Christopher Monckton.
