# Smuggler Run — inventaire de la démo

Ce document recense les ressources visibles dans le projet et ce qu'il reste à vérifier avant une diffusion publique.

## État du prototype

- La scène jouable contient un terrain d'environ 3 km et quatre itinéraires qui se séparent après la portion de départ commune.
- `Assets/Scenes/SampleScene.unity` est déjà activée dans les scènes incluses au build Unity.
- Les sorties des pistes difficiles sont limitées par un compte à rebours; la ville a trois chicanes de police avec une ouverture alternée.
- La sortie est accessible dès la première tentative; le jeu ne force plus un échec scénarisé près de l'arrivée. En cas d'échec réel, l'écran montre la distance restante et le bouton de réessai.
- Les deux pistes de montagne ont des dangers distincts : éboulements physiques (un gros bloc et deux fragments) avec une voie de passage, et épaulement affaissé avec cicatrice stratifiée, fissures irrégulières et éboulis hors de la voie sur la piste difficile. La piste extrême fait 7,5 m de large, reste sans parapets, penche vers l'extérieur des épingles et sanctionne une chute dans le ravin. Sa brèche initiale atteint 8,5 m de profondeur : trop lente, la voiture reste piégée; une réception sans saut abîme fortement le pickup et la cargaison. Plus loin, une brèche d'environ 20 m est traversée par un tablier suspendu en bois de 54 m. Après un passage rapide, les planches tombent en séquence dans le ravin derrière le pickup. Les rochers utilisent les modèles PBR importés et les alertes préviennent avant les événements.
- Les records de temps sont conservés séparément pour les quatre routes. Le meilleur parcours réussi est enregistré localement comme fantôme; H l'affiche ou le masque. Le classement est personnel et local, sans serveur en ligne.
- Le menu pause contient les réglages de volume, qualité, plein écran et sensibilité de direction. La graine quotidienne peut être activée/désactivée; la scène se recharge pour générer le tracé du jour, calculé en UTC. P ouvre le mode photo, déplace la caméra sans HUD, F12 enregistre une capture dans le dossier de données du jeu.
- Les périphériques sont déjà pris en charge par le contrôleur existant (clavier et manette). Les secousses, la poussière, les sons de terrain et l'atelier restent intégrés à la boucle actuelle.
- La carte n'est pas encore une démo de 10 à 15 minutes : il faut allonger ou séquencer les missions après validation du rythme de conduite.

## Ressources avec licence identifiée

| Ressource | Emplacement dans le projet | Crédit / licence |
|---|---|---|
| Modèles de bâtiments Quaternius Downtown City MegaKit | `Assets/Exports/FBX (Unity)/` | CC0 1.0, indiqué dans `Assets/License_Standard.txt`. |
| Texture de gravier Poly Haven « Gravel Road » | `Assets/Textures/GravelRoad/textures/` | CC0 1.0, crédit dans `Assets/Textures/PolyHaven_GravelRoad/CREDITS.txt`. Les cartes 4K albedo et normale alimentent la couche de gravier. |
| Pack « Customizable Rocks and Cliffs PBR — Sample » | `Assets/PolishedSurfaces/System_RockSet_Sample/` | Téléchargement gratuit; licence Unity Asset Store Standard EULA. Prefabs petit, moyen et grand utilisés pour les éboulements et les rochers visibles des brèches. |
| Ciel HDRI « Wasteland Clouds (Pure Sky) » | `Assets/Resources/wasteland_clouds_puresky_2k.hdr` | CC0 1.0, crédit dans `Assets/Resources/HDRI_CREDITS.txt`. |
| Ambiances de moteur, de ville, de gravier, de rochers et battement de cœur | Générées au lancement par les scripts | Aucun fichier audio tiers utilisé pour ces effets générés. |

## Licence à retrouver avant publication

- Le pickup `Assets/Models/2024_ford_f-150_raptor_r.glb` n'a pas de fichier de licence ou de crédit correspondant dans le projet. Retrouver la page d'origine et conserver la preuve d'autorisation commerciale avant de distribuer la démo.
- Aucun modèle de police externe n'est actuellement affecté à `RoadsideExperience.policeVehiclePrefab`. Le jeu utilise donc son modèle procédural. Si un pack de police est ajouté, inscrire ici son nom, son auteur, sa licence et le lien de la page de téléchargement.
- Vérifier aussi la licence de tout asset ajouté manuellement et conserver son texte de licence avec le projet.

## À faire pour préparer une démo publique

1. Vérifier le droit de diffusion du pickup et de chaque asset importé.
2. Importer et affecter un modèle de police si l'apparence procédurale actuelle ne convient pas.
3. Ouvrir `Assets/Scenes/SampleScene.unity` dans Unity 6.6 et vérifier ensemble la continuité et la praticabilité des quatre routes, les chaussées effondrées, les collisions, le pont, le fantôme et le mode photo. La police reste une phase séparée, conformément à la priorité actuelle.
4. Régler le rythme pour viser une session de 10 à 15 minutes, puis enregistrer les captures et la bande-annonce après validation visuelle. Aucune capture de gameplay n'est fabriquée par ce document.
5. Effectuer un build PC et vérifier son lancement sur une configuration représentative.
6. Ajouter une vraie localisation française/anglaise/arabe avec police arabe et mise en forme RTL avant publication; ne pas annoncer ces langues tant qu'elles n'ont pas été intégrées et relues.
