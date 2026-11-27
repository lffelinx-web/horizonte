# Horizonte

Simulador de construcción de ciudades en 3D para iPhone, hecho en Unity 6 (6000.6.7f1) con URP.

Trazas calles y colocas cada edificio junto a ellas: viviendas, comercios, industrias, energía, agua, seguridad, educación, salud, transporte y parques, cada uno con su precio. Las viviendas se llenan de habitantes si tienen calle conectada a la carretera, electricidad y empleos cerca; los servicios cubren un radio y suben la satisfacción. La electricidad llega a un radio alrededor de las centrales, y las subestaciones la llevan más lejos.

Un día dura 8 minutos, con amanecer, atardecer y noche; los impuestos y el mantenimiento se cobran cada día a las 6:00, y las obras tardan horas (unas 3 una casa, 8 una torre). El mapa tiene montañas al noroeste, el río, una reserva forestal cercada al noreste, tierras fértiles al sureste y una carretera que lo cruza. Las casas y algunos edificios usan madera: una caseta forestal en la entrada de la reserva manda a su leñador a talar los árboles que marques con **Talar**, y el aserradero tala solo; los árboles vuelven a crecer.

## Estructura

- `Assets/Scripts/Simulation`: reglas de la ciudad en C# puro, sin depender de Unity (mapa, construcción, electricidad, salud, demanda, crecimiento, presupuesto y guardado).
- `Assets/Scripts/Presentation`: vista 3D, cámara, controles táctiles e interfaz.
- `Assets/Editor/ProjectSetup.cs`: configuración del proyecto y compilaciones (menú **Horizonte**).
- `Assets/Editor/BuildingBaker.cs`: prepara los modelos para el juego.
- `Assets/Tests/Editor`: pruebas de la simulación.
- `Assets/Modelos`: los FBX de Blender, con el nombre que usa el juego.

## Modelos de Blender

Una casilla mide **10 × 10 m**. Cada edificio del juego tiene un nombre de archivo fijo; pon el FBX en `Assets/Modelos` con ese nombre y reemplaza al edificio provisional. La lista completa, con tamaño en casillas, precio y efectos, está en `Assets/Scripts/Simulation/Catalog.cs` (el primer texto de cada línea es el nombre del archivo).

| Categoría | Archivos (sin `.fbx`) |
| --- | --- |
| Transporte | `terminal_autobuses`, `estacion_tren`, `estacion_metro`, `centro_logistico`, `aeropuerto` (calle, avenida y puente se dibujan solos) |
| Vivienda | `casa_basica`, `casa_ampliada`, `duplex`, `apartamentos`, `conjunto_residencial`, `torre_residencial` |
| Comercio | `tienda_abarrotes`, `mercado_municipal`, `supermercado`, `restaurante`, `centro_comercial`, `edificio_oficinas`, `torre_corporativa`, `centro_financiero` |
| Industria | `caseta_forestal`, `granja_agricola`, `procesadora_alimentos`, `aserradero`, `fabrica_materiales`, `planta_metalurgica`, `fabrica_bienes`, `planta_electronica`, `industria_automatizada` |
| Energía | `central_termoelectrica`, `subestacion`, `planta_solar`, `parque_eolico`, `central_hidroelectrica`, `sistema_baterias`, `central_nuclear`, `red_inteligente` |
| Agua y residuos | `estacion_bombeo`, `planta_potabilizadora`, `tanque_agua`, `control_alcantarillado`, `planta_tratamiento`, `recoleccion_basura`, `planta_reciclaje`, `valorizacion_energetica` |
| Seguridad y gobierno | `puesto_policia`, `comisaria`, `estacion_bomberos`, `proteccion_civil`, `central_emergencias`, `gestion_riesgos`, `ayuntamiento`, `centro_administrativo` |
| Educación | `guarderia`, `escuela_primaria`, `escuela_secundaria`, `instituto_tecnico`, `universidad`, `biblioteca`, `centro_investigacion` |
| Salud | `consultorio`, `clinica`, `urgencias`, `hospital_general`, `hospital_especializado`, `laboratorio` |
| Parques, cultura y futuro | `parque_vecinal`, `plaza_publica`, `centro_deportivo`, `museo`, `teatro`, `estadio`, `granja_vertical`, `residencial_inteligente`, `fabrica_robotizada`, `gestion_urbana` |

Si el modelo trae otro tamaño, cambia su ancho y fondo en `Catalog.cs` para que coincidan. Para tener variantes, añade una letra al final, por ejemplo `casa_basica_b.fbx`; el juego elige una al azar.

### Guía de modelado

**Tamaño.** El terreno ocupa casillas completas y nada se sale de él (ni aleros ni árboles); deja 0.3 m libres por lado para el edificio.

| Casillas | Forma | Terreno | Vértices como máximo |
| --- | --- | --- | --- |
| 1 | 1 × 1 | 10 × 10 m | 15 000 |
| 2 | 2 × 1 o 1 × 2 | 20 × 10 m o 10 × 20 m | 25 000 |
| 4 | 2 × 2 | 20 × 20 m | 40 000 |
| 6–9 | 3 × 2 o 3 × 3 | 30 × 20 m o 30 × 30 m | 60 000–90 000 |

La fachada principal es el lado del ancho (eje X de Blender). Los edificios rectangulares se acuestan solos para dar a calles que van de norte a sur.

**En Blender.**

- 1 unidad = 1 metro, escala 1, con escala y rotación aplicadas (Ctrl+A → All Transforms).
- El origen va en el **centro del terreno, a nivel del suelo**.
- La fachada principal mira hacia **−Y** (lo que se ve en la vista Frontal, tecla 1 del teclado numérico).
- Base del terreno de 0.15 m de alto como máximo, sin pedestales.
- Unos 3 m por piso en vivienda (planta baja de 3.2 m).

**Color, para que la ciudad se vea viva.**

- El juego convierte cada material en un color liso: de las texturas solo queda su tono promedio y el relieve se pierde. Usa colores claros y definidos.
- La pared principal clara (material con *estuco*, *aplanado*, *pintura*, *muro* o *pared* en el nombre) se repinta en el juego con un color por edificio en viviendas y comercios: terracota, mostaza, azul, menta, rosa o durazno. Modélala en un tono claro y neutro (marfil o blanco).
- Los acentos conservan su color: madera, cantera, ladrillo, herrería, terracota. Úsalos para que cada edificio tenga carácter.
- Vidrio oscuro y azulado; el juego lo oscurece si viene claro. Nómbralo *vidrio*, *cristal* o *ventana*.
- Metal con *metal*, *aluminio*, *acero*, *herraje* o *latón* en el nombre.
- Las lámparas e interiores encendidos se nombran *lámpara*, *luz* o *iluminado*, y brillan con luz propia.
- El pasto del lote (*césped* o *pasto*) toma el verde del mapa para que el terreno no se note.
- Evita que todo sea del mismo tono: variedad de colores entre azotea, muros, acentos y jardín.

**Peso.** Las rejas, los arbustos y las barandas con mucho detalle son lo que más pesa: mejor rejas planas y árboles sencillos.

**Personas y coches.** En la calle hay personas que salen de los edificios y un coche por cada casa con cochera, con figuras provisionales. Para usar los tuyos, pon en `Assets/Modelos` archivos que empiecen con `persona` (por ejemplo `persona_a.fbx`, `persona_b.fbx`) o con `coche`. Las personas miden unos 1.7 m y tienen de 300 a 800 vértices; los coches, unos 4.2 m de largo y unos 1 500 vértices. En ambos, el origen va en el suelo, al centro, y el frente mira hacia −Y. Dónde se estaciona el coche de cada casa está en `Garages`, en `Assets/Scripts/Presentation/Traffic.cs`; hoy solo la casa ampliada tiene cochera.

**Modelos pendientes.** Los modelos que pasan del límite de vértices esperan en la carpeta `Modelos pendientes`, fuera del juego. Mientras tanto, esos edificios usan su forma provisional.

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
