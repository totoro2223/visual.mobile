/*{
    "DESCRIPTION": "Carrés animés avec transitions aléatoires du noir au blanc",
    "CREDIT": "phiphi",
    "ISFVSN": "2",
    "CATEGORIES": ["Generator"],
    "INPUTS": [
        {
            "NAME": "gridSize",
            "TYPE": "float",
            "MIN": 2.0,
            "MAX": 50.0,
            "DEFAULT": 10.0,
            "LABEL": "Nombre de carrés"
        },
        {
            "NAME": "speed",
            "TYPE": "float",
            "MIN": 0.01,
            "MAX": 5.0,
            "DEFAULT": 0.2,
            "LABEL": "Vitesse d'animation"
        },
        {
            "NAME": "randomness",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 10.0,
            "DEFAULT": 5.0,
            "LABEL": "Variation aléatoire"
        },
        {
            "NAME": "blackAmount",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.7,
            "LABEL": "Quantité de carrés noirs"
        },
        {
            "NAME": "transparencyWidth",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.3,
            "LABEL": "Largeur zone transparente 1"
        },
        {
            "NAME": "transparency2Width",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.0,
            "LABEL": "Largeur zone transparente 2"
        },
        {
            "NAME": "transparencyPosX",
            "TYPE": "float",
            "MIN": 0.0,
            "MAX": 1.0,
            "DEFAULT": 0.5,
            "LABEL": "Position X des zones transparentes"
        },
        {
            "NAME": "whiteColor",
            "TYPE": "color",
            "DEFAULT": [1.0, 1.0, 1.0, 1.0],
            "LABEL": "Couleur des carrés blancs"
        },
        {
            "NAME": "blackColor",
            "TYPE": "color",
            "DEFAULT": [0.0, 0.0, 0.0, 1.0],
            "LABEL": "Couleur des carrés noirs"
        }
    ]
}*/

// Fonction de hash pour générer des valeurs pseudo-aléatoires
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void main() {
    vec2 uv = isf_FragNormCoord;
    
    // Calculer le ratio d'aspect pour des carrés avec arrêtes égales
    float aspect = RENDERSIZE.x / RENDERSIZE.y;
    
    // Ajuster gridSize pour avoir un nombre entier de carrés complets
    float adjustedGridSizeX = floor(gridSize * aspect);
    float adjustedGridSizeY = floor(gridSize);
    
    // S'assurer qu'il y a au moins 1 carré
    adjustedGridSizeX = max(adjustedGridSizeX, 1.0);
    adjustedGridSizeY = max(adjustedGridSizeY, 1.0);
    
    // Calculer les coordonnées de grille ajustées
    vec2 gridUV = floor(vec2(uv.x * adjustedGridSizeX, uv.y * adjustedGridSizeY));
    
    // Calculer la position du carré en coordonnées normalisées
    float squareWidth = 1.0 / adjustedGridSizeX;
    float squareLeft = gridUV.x * squareWidth;
    float squareRight = (gridUV.x + 1.0) * squareWidth;
    float squareCenter = (squareLeft + squareRight) * 0.5;
    
    // Vérifier si le centre de ce carré est dans la zone de transparence 1
    float halfWidth = transparencyWidth * 0.5;
    float leftEdge = transparencyPosX - halfWidth;
    float rightEdge = transparencyPosX + halfWidth;
    
    if (squareCenter >= leftEdge && squareCenter <= rightEdge) {
        // Zone transparente - ce carré entier devient transparent
        gl_FragColor = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    
    // Générer une valeur aléatoire unique pour chaque carré
    float randomValue = hash(gridUV);
    
    // Tous les carrés s'animent avec un timing aléatoire
    float timeRandomValue = hash(gridUV + vec2(123.45, 678.90));
    
    // Créer une animation cyclique avec un décalage aléatoire
    float timeOffset = timeRandomValue * randomness;
    float cycle = fract((TIME * speed + timeOffset) * 0.2);
    
    // Transition asymétrique contrôlée par blackAmount
    // Plus blackAmount est élevé, plus le carré passe de temps en noir
    float brightness;
    float whitePhase = 1.0 - blackAmount; // Pourcentage du cycle passé en blanc
    
    if (cycle < whitePhase) {
        // Phase blanche: montée puis descente
        float localCycle = cycle / whitePhase;
        if (localCycle < 0.5) {
            brightness = localCycle * 2.0;
        } else {
            brightness = (1.0 - localCycle) * 2.0;
        }
    } else {
        // Phase noire: reste noir
        brightness = 0.0;
    }
    
    // Vérifier si le carré est actuellement en phase noire ET dans la zone 2
    if (brightness < 0.01) {
        // Le carré est noir, vérifier la deuxième zone de transparence
        float halfWidth2 = transparency2Width * 0.5;
        float leftEdge2 = transparencyPosX - halfWidth2;
        float rightEdge2 = transparencyPosX + halfWidth2;
        
        if (squareCenter >= leftEdge2 && squareCenter <= rightEdge2) {
            // Carré noir dans la zone 2 = transparent
            gl_FragColor = vec4(0.0, 0.0, 0.0, 0.0);
            return;
        }
    }
    
    // Appliquer une courbe pour une transition plus douce
    brightness = smoothstep(0.0, 1.0, brightness);
    
    // Interpoler entre les deux couleurs
    vec3 color = mix(blackColor.rgb, whiteColor.rgb, brightness);
    
    gl_FragColor = vec4(color, 1.0);
}