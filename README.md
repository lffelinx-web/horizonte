# Horizonte

Simulador de construcción de ciudades en 3D para iPhone, hecho en Unity 6 (6000.6.7f1) con URP.

Construyes calles y marcas zonas residenciales, comerciales e industriales. La ciudad crece sola si las zonas tienen calle conectada a la carretera y electricidad. Los parques, la policía y la escuela suben el atractivo de los barrios, y la industria lo baja. Cada mes se cobran impuestos y se paga el mantenimiento.

## Estructura

- `Assets/Scripts/Simulation`: reglas de la ciudad en C# puro, sin depender de Unity (mapa, construcción, electricidad, demanda, crecimiento, presupuesto y guardado).
- `Assets/Scripts/Presentation`: vista 3D, cámara, controles táctiles e interfaz.
- `Assets/Editor/ProjectSetup.cs`: configuración del proyecto y compilaciones (menú **Horizonte**).
- `Assets/Tests/Editor`: pruebas de la simulación.
- `Assets/Resources/Buildings`: aquí van los modelos FBX de Blender.

## Modelos de Blender

Pon los FBX en `Assets/Resources/Buildings` con estos nombres. Mientras falte un modelo, el juego usa uno provisional.

| Edificio | Archivo | Planta |
| --- | --- | --- |
| Residencial (casas, departamentos, torre) | `residencial_1.fbx`, `residencial_2.fbx`, `residencial_3.fbx` | 1 × 1 casillas |
| Comercial (tienda, oficinas, rascacielos) | `comercial_1.fbx`, `comercial_2.fbx`, `comercial_3.fbx` | 1 × 1 |
| Industrial (taller, fábrica, planta) | `industrial_1.fbx`, `industrial_2.fbx`, `industrial_3.fbx` | 1 × 1 |
| Central eléctrica | `central_electrica.fbx` | 2 × 2 |
| Parque | `parque.fbx` | 1 × 1 |
| Policía | `policia.fbx` | 1 × 1 |
| Escuela | `escuela.fbx` | 2 × 1 (ancho × fondo) |

Para tener variantes, añade una letra al final, por ejemplo `residencial_1_b.fbx` o `residencial_1_c.fbx`. El juego elige una al azar para cada lote.

Reglas para exportar:

- 1 unidad de Blender = 1 metro. Una casilla mide **8 × 8 m**, y la calle ocupa 1.3 m de banqueta en cada orilla, así que deja el edificio dentro de unos 7 × 7 m.
- El origen del objeto va en el **centro de la planta, a nivel del suelo**.
- La fachada principal mira hacia **−Y en Blender** (hacia el frente en la vista frontal, tecla 1). El juego gira el edificio para que mire a la calle.
- En el exportador FBX: *Apply Scalings: FBX All*, *Forward: −Z Forward*, *Up: Y Up* y *Apply Transform* activado.

Al importar, la consola de Unity muestra las medidas de cada modelo para comprobar que caben en su casilla.

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

Una compilación de Windows abierta con `-demo -captura <carpeta>` arma un pueblo de ejemplo, guarda capturas y se cierra. Sirve para revisar el aspecto sin un iPhone.

## iOS

- Bundle ID: `com.lfaz.horizonte`. Fastlane lo registra solo la primera vez, pero la app debe existir en App Store Connect con ese Bundle ID.
- La compilación corre en `.github/workflows/ios.yml`. Sin los secrets `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_KEY_P8` y `APPLE_TEAM_ID`, solo compila sin firmar. Con ellos, firma con `fastlane match` (rama `certificados`) y sube a TestFlight.
