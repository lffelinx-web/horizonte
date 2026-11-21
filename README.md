# Horizonte

Simulador de construcción de ciudades en 3D para iPhone, hecho en Unity 6 (6000.6.7f1) con URP.

Construyes calles y marcas zonas residenciales, comerciales e industriales. La ciudad crece sola si las zonas tienen calle conectada a la carretera y electricidad. Las viviendas empiezan como casas en un lote y, al crecer, juntan lotes vecinos: apartamentos en 2 × 2, conjunto residencial en 4 × 4 y torre en 3 × 3. Los parques, la policía, la escuela y los edificios de salud suben el atractivo y la satisfacción, y la industria los baja. Cada mes se cobran impuestos y se paga el mantenimiento.

## Estructura

- `Assets/Scripts/Simulation`: reglas de la ciudad en C# puro, sin depender de Unity (mapa, construcción, electricidad, salud, demanda, crecimiento, presupuesto y guardado).
- `Assets/Scripts/Presentation`: vista 3D, cámara, controles táctiles e interfaz.
- `Assets/Editor/ProjectSetup.cs`: configuración del proyecto y compilaciones (menú **Horizonte**).
- `Assets/Editor/BuildingBaker.cs`: prepara los modelos para el juego.
- `Assets/Tests/Editor`: pruebas de la simulación.
- `Assets/Modelos`: los FBX de Blender, con el nombre que usa el juego.

## Modelos de Blender

Una casilla mide **10 × 10 m**. Pon los FBX en `Assets/Modelos` con estos nombres. Mientras falte un modelo, el juego usa uno provisional.

| Edificio | Archivo | Planta en casillas |
| --- | --- | --- |
| Casa (nivel 1) | `residencial_1_a.fbx`, `_b`, `_c`… (casa básica, casa ampliada, dúplex) | 1 × 1 |
| Apartamentos (nivel 2) | `residencial_2_2.fbx` | 2 × 2 |
| Conjunto residencial (nivel 2) | `residencial_2_4.fbx` | 4 × 4 |
| Torre residencial (nivel 3) | `residencial_3_3.fbx` | 3 × 3 |
| Comercial (tienda, oficinas, torre) | `comercial_1.fbx`, `comercial_2.fbx`, `comercial_3.fbx` | 1 × 1 |
| Industrial (taller, fábrica, planta) | `industrial_1.fbx`, `industrial_2.fbx`, `industrial_3.fbx` | 1 × 1 |
| Central eléctrica | `central_electrica.fbx` | 2 × 2 |
| Parque | `parque.fbx` | 1 × 1 |
| Policía | `policia.fbx` | 1 × 1 |
| Escuela | `escuela.fbx` | 2 × 1 (ancho × fondo) |
| Consultorio | `consultorio.fbx` | 2 × 2 |
| Laboratorio médico | `laboratorio.fbx` | 3 × 2 |
| Clínica comunitaria | `clinica.fbx` | 3 × 2 |
| Centro de urgencias | `urgencias.fbx` | 3 × 3 |
| Hospital general | `hospital_general.fbx` | 4 × 4 |
| Hospital especializado | `hospital_especializado.fbx` | 5 × 4 |

El nombre de las zonas es `zona_nivel` y, si el edificio ocupa más de un lote, `zona_nivel_lado` (por ejemplo `residencial_3_3` es una torre de 3 × 3). Para tener variantes, añade una letra al final, por ejemplo `residencial_1_b.fbx`. El juego elige una al azar para cada lote.

Reglas para exportar:

- 1 unidad de Blender = 1 metro, con el modelo a tamaño real y su terreno dentro de la planta (una casa de 10 × 10 m, un hospital de 40 × 36 m en 4 × 4 casillas).
- El origen del objeto va en el **centro de la planta, a nivel del suelo**.
- La fachada principal mira hacia **−Y en Blender**, como en los modelos actuales. El juego gira el edificio para que mire a la calle.
- Los materiales son colores lisos (sin texturas). El vidrio y el metal se reconocen por el nombre del material (vidrio, cristal, aluminio, herrajes, acero…).

**Horizonte > Preparar edificios** (que también corre antes de cada compilación) une cada modelo en una sola pieza con una paleta de colores compartida, genera versiones simplificadas para la distancia y guarda el resultado en `Assets/Resources/Buildings`. La consola muestra las medidas de cada edificio.

## Controles

- Un dedo: construye con la herramienta elegida. En **Explorar**, mueve el mapa y al tocar muestra información de la casilla.
- Dos dedos: mover, pellizcar para acercar o alejar, y girar.
- En el editor: botón izquierdo como un dedo, botón derecho o central para mover, rueda para acercar, Q/E para girar y WASD para desplazarse.

## Comandos

Cierra el Editor de Unity antes de ejecutar estos comandos.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/tests.xml
```

Corre las pruebas de la simulación.

```powershell
powershell -ExecutionPolicy Bypass -File Tools/publish-ios.ps1
```

Genera el proyecto de Xcode y lo publica como release, lo que inicia la compilación de iOS en GitHub Actions.

Una compilación de Windows abierta con `-demo -captura <carpeta>` arma un pueblo de ejemplo, guarda capturas y se cierra. Sirve para revisar el aspecto sin un iPhone. Con `-vitrina -captura <carpeta>` pone todos los modelos en fila frente a una calle y saca una captura de cada uno.

## iOS

- Bundle ID: `com.lfaz.horizonte`. Fastlane lo registra solo la primera vez, pero la app debe existir en App Store Connect con ese Bundle ID.
- La compilación corre en `.github/workflows/ios.yml`. Sin los secrets `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8` y `APPLE_TEAM_ID`, solo compila sin firmar. Con ellos, firma con `fastlane match` (rama `certificados`) y sube a TestFlight.
